using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api;

public sealed class SqlFinanceStore(string connectionString)
{
    private const string MemberSelect = """
        SELECT c.CustomerId AS Id, c.CustomerNo AS MemberNo, c.FullName,
               c.PrimaryPhone AS Phone, COALESCE(a.City, a.Line1, N'') AS Village,
               COALESCE(g.GroupName, N'') AS GroupName, b.BranchName AS Branch,
               CASE c.Status WHEN 'ACTIVE' THEN 'Active' WHEN 'INACTIVE' THEN 'Inactive'
                    WHEN 'BLACKLISTED' THEN 'Blacklisted' ELSE 'Deceased' END AS Status,
               CAST(c.RegistrationDate AS datetime2) AS JoinedOn,
               CAST(c.DateOfBirth AS datetime2) AS DateOfBirth, c.GuardianName,
               c.MonthlyIncome,
               CASE c.KycStatus WHEN 'VERIFIED' THEN 'Verified' WHEN 'REJECTED' THEN 'Rejected'
                    ELSE 'Pending' END AS KycStatus
          FROM cust.Customer c
          JOIN ref.Branch b ON b.BranchId = c.BranchId
          OUTER APPLY (
              SELECT TOP (1) ca.City, ca.Line1
                FROM cust.CustomerAddress ca
               WHERE ca.CustomerId = c.CustomerId AND ca.IsDeleted = 0 AND ca.IsPrimary = 1
          ) a
          OUTER APPLY (
              SELECT TOP (1) cg.GroupName
                FROM cust.GroupMember gm
                JOIN cust.CustomerGroup cg ON cg.GroupId = gm.GroupId
               WHERE gm.CustomerId = c.CustomerId AND gm.LeftOn IS NULL AND cg.Status = 'ACTIVE'
               ORDER BY gm.JoinedOn DESC
          ) g
         WHERE c.IsDeleted = 0
        """;

    private const string LoanSelect = """
        SELECT l.LoanId AS Id, l.LoanAccountNo AS LoanNo, l.CustomerId AS MemberId,
               c.FullName AS MemberName, p.ProductName AS Product, l.PrincipalAmount AS SanctionedAmount,
               l.PrincipalOutstanding + l.InterestOutstanding + l.FeesOutstanding + l.PenaltyOutstanding AS Outstanding,
               l.InstallmentAmount AS Installment, CAST(l.NextDueDate AS datetime2) AS NextDue,
               COALESCE(e.EmployeeName, N'Unassigned') AS RelationshipManager,
               CASE l.Status WHEN 'ACTIVE' THEN 'Active' ELSE 'Closed' END AS Status
          FROM lon.Loan l
          JOIN cust.Customer c ON c.CustomerId = l.CustomerId
          JOIN lon.LoanProduct p ON p.ProductId = l.ProductId
          LEFT JOIN sec.Employee e ON e.EmployeeId = l.EmployeeId
         WHERE l.Status = 'ACTIVE'
        """;

    private readonly string connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("A SQL Server connection string is required.", nameof(connectionString))
        : connectionString;

    public async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
    }

    public async Task<BranchInfo> GetDefaultBranchAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GetDefaultBranchAsync(connection, null, cancellationToken);
    }

    public async Task<IReadOnlyList<LoanProductOption>> GetLoanProductsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ProductId AS Id, ProductCode AS Code, ProductName AS Name, MinAmount,
                   MaxAmount, MinInstallments, MaxInstallments, Frequency
              FROM lon.LoanProduct
             WHERE IsActive = 1 AND Frequency = 'MONTHLY'
             ORDER BY ProductName;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var products = await connection.QueryAsync<LoanProductOption>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return products.AsList();
    }

    public async Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var branch = await GetDefaultBranchAsync(connection, null, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var todayValue = ToSqlDate(today);
        var monthStart = ToSqlDate(new DateOnly(today.Year, today.Month, 1));

        var totals = await connection.QuerySingleAsync<DashboardTotals>(
            new CommandDefinition("""
                SELECT
                    (SELECT COUNT(*) FROM cust.Customer WHERE BranchId = @BranchId AND IsDeleted = 0 AND Status = 'ACTIVE') AS ActiveMembers,
                    (SELECT COUNT(*) FROM lon.Loan WHERE BranchId = @BranchId AND Status = 'ACTIVE') AS ActiveLoans,
                    (SELECT COALESCE(SUM(PrincipalOutstanding + InterestOutstanding + FeesOutstanding + PenaltyOutstanding), 0)
                       FROM lon.Loan WHERE BranchId = @BranchId AND Status = 'ACTIVE') AS PortfolioOutstanding,
                    (SELECT COUNT(*) FROM lon.Loan WHERE BranchId = @BranchId AND Status = 'ACTIVE' AND NextDueDate < @Today) AS OverdueAccounts,
                    (SELECT COALESCE(SUM(Amount), 0) FROM col.Payment p JOIN lon.Loan l ON l.LoanId = p.LoanId
                      WHERE l.BranchId = @BranchId AND p.Status = 'POSTED' AND p.PaymentDate >= @MonthStart AND p.PaymentDate <= @Today) AS CollectionsThisMonth;
                """, new { branch.Id, Today = todayValue, MonthStart = monthStart }, cancellationToken: cancellationToken));

        var daily = await connection.QueryAsync<DailyCollectionRow>(
            new CommandDefinition("""
                SELECT p.PaymentDate AS PaidOn, SUM(p.Amount) AS Amount
                  FROM col.Payment p
                  JOIN lon.Loan l ON l.LoanId = p.LoanId
                 WHERE l.BranchId = @BranchId AND p.Status = 'POSTED'
                   AND p.PaymentDate >= @From AND p.PaymentDate <= @Today
                 GROUP BY p.PaymentDate;
                """, new { branch.Id, From = ToSqlDate(today.AddDays(-6)), Today = todayValue }, cancellationToken: cancellationToken));

        var dailyMap = daily.ToDictionary(row => DateOnly.FromDateTime(row.PaidOn), row => row.Amount);
        var trend = Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(offset - 6))
            .Select(date => new DashboardCollectionDay(date.ToString("ddd", CultureInfo.InvariantCulture), dailyMap.GetValueOrDefault(date)))
            .ToArray();

        var dueLoans = await connection.QueryAsync<Loan>(
            new CommandDefinition(LoanSelect + """
                 AND l.BranchId = @BranchId AND l.NextDueDate <= @Today
                 ORDER BY l.NextDueDate;
                """, new { branch.Id, Today = todayValue }, cancellationToken: cancellationToken));

        return new DashboardSummary(
            totals.ActiveMembers,
            totals.ActiveLoans,
            totals.PortfolioOutstanding,
            totals.CollectionsThisMonth,
            totals.OverdueAccounts,
            null,
            totals.CollectionsThisMonth,
            trend,
            dueLoans.AsList());
    }

    public async Task<IReadOnlyList<Member>> GetMembersAsync(string? search, CancellationToken cancellationToken)
    {
        const string sql = MemberSelect + """
             AND (@Search IS NULL OR c.CustomerNo = @Search OR c.PrimaryPhone LIKE '%' + @Search + '%'
                  OR c.FullName LIKE '%' + @Search + '%' OR a.City LIKE '%' + @Search + '%'
                  OR a.Line1 LIKE '%' + @Search + '%')
             ORDER BY c.CustomerId DESC;
            """;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var members = await connection.QueryAsync<Member>(
            new CommandDefinition(sql, new { Search = NullIfWhiteSpace(search) }, cancellationToken: cancellationToken));
        return members.AsList();
    }

    public async Task<Member> AddMemberAsync(CreateMemberRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var branch = await GetDefaultBranchAsync(connection, transaction, cancellationToken);
            var (firstName, lastName) = SplitName(request.FullName);
            var kycStatus = request.KycStatus?.Trim().ToUpperInvariant() switch
            {
                "VERIFIED" => "VERIFIED",
                "REJECTED" => "REJECTED",
                _ => "PENDING"
            };

            var customer = await connection.QuerySingleAsync<CreatedCustomer>(
                new CommandDefinition("""
                    DECLARE @seq BIGINT = NEXT VALUE FOR ref.SeqCustomerNo;
                    DECLARE @customerNo VARCHAR(20) = 'C' + RIGHT('0000000' + CONVERT(VARCHAR(10), @seq), 7);
                    INSERT cust.Customer(CustomerNo, FirstName, LastName, DateOfBirth, GuardianName,
                                         PrimaryPhone, MonthlyIncome, KycStatus, BranchId)
                    OUTPUT INSERTED.CustomerId AS Id, INSERTED.CustomerNo AS MemberNo
                    VALUES (@customerNo, @FirstName, @LastName, @DateOfBirth, @GuardianName,
                            @Phone, @MonthlyIncome, @KycStatus, @BranchId);
                    """, new
                {
                    FirstName = firstName,
                    LastName = lastName,
                    request.DateOfBirth,
                    GuardianName = NullIfWhiteSpace(request.GuardianName),
                    Phone = request.Phone.Trim(),
                    request.MonthlyIncome,
                    KycStatus = kycStatus,
                    branch.Id
                }, transaction, cancellationToken: cancellationToken));

            if (!string.IsNullOrWhiteSpace(request.Village))
            {
                var village = request.Village.Trim();
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT cust.CustomerAddress(CustomerId, AddressType, Line1, City, IsPrimary)
                    VALUES (@CustomerId, 'RESIDENTIAL', @Village, @Village, 1);
                    """, new { CustomerId = customer.Id, Village = village }, transaction, cancellationToken: cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(request.GroupName))
            {
                var groupId = await connection.QuerySingleOrDefaultAsync<long?>(
                    new CommandDefinition("""
                        SELECT TOP (1) GroupId
                          FROM cust.CustomerGroup WITH (UPDLOCK, HOLDLOCK)
                         WHERE BranchId = @BranchId AND GroupName = @GroupName AND Status = 'ACTIVE'
                         ORDER BY GroupId;
                        """, new { BranchId = branch.Id, GroupName = request.GroupName.Trim() }, transaction, cancellationToken: cancellationToken));

                if (groupId is null)
                {
                    groupId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                        INSERT cust.CustomerGroup(GroupNo, GroupName, BranchId)
                        OUTPUT INSERTED.GroupId
                        VALUES (@GroupNo, @GroupName, @BranchId);
                        """, new
                    {
                        GroupNo = "CG" + Guid.NewGuid().ToString("N")[..18].ToUpperInvariant(),
                        GroupName = request.GroupName.Trim(),
                        BranchId = branch.Id
                    }, transaction, cancellationToken: cancellationToken));
                }

                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT cust.GroupMember(GroupId, CustomerId) VALUES (@GroupId, @CustomerId);
                    """, new { GroupId = groupId, CustomerId = customer.Id }, transaction, cancellationToken: cancellationToken));
            }

            var auditValues = JsonSerializer.Serialize(new
            {
                customer.MemberNo,
                request.FullName,
                request.Phone,
                request.Village,
                request.GroupName,
                request.DateOfBirth,
                request.MonthlyIncome,
                KycStatus = kycStatus
            });
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT aud.AuditLog(TableName, RecordId, Action, NewValues)
                VALUES ('cust.Customer', @RecordId, 'INSERT', @NewValues);
                """, new { RecordId = customer.Id.ToString(CultureInfo.InvariantCulture), NewValues = auditValues }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return await GetMemberByIdAsync(connection, customer.Id, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<LoanApplication>> GetLoanApplicationsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT a.ApplicationId AS Id, a.ApplicationNo, a.CustomerId AS MemberId, c.FullName AS MemberName,
                   p.ProductName AS Product, a.RequestedAmount AS AmountRequested,
                   a.RequestedInstallments AS TenureMonths, CAST(a.ApplicationDate AS datetime2) AS AppliedOn,
                   COALESCE(a.PurposeDescription, '') AS Purpose,
                   CASE a.Status WHEN 'SUBMITTED' THEN 'Awaiting review' WHEN 'UNDER_REVIEW' THEN 'Awaiting review'
                        WHEN 'APPROVED' THEN 'Approved' WHEN 'DISBURSED' THEN 'Disbursed'
                        WHEN 'REJECTED' THEN 'Declined' ELSE a.Status END AS Status,
                   COALESCE(e.EmployeeName, N'Unassigned') AS RelationshipManager
              FROM lon.LoanApplication a
              JOIN cust.Customer c ON c.CustomerId = a.CustomerId
              JOIN lon.LoanProduct p ON p.ProductId = a.ProductId
              LEFT JOIN sec.Employee e ON e.EmployeeId = a.EmployeeId
             ORDER BY a.ApplicationDate DESC, a.ApplicationId DESC;
            """;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var applications = await connection.QueryAsync<LoanApplication>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return applications.AsList();
    }

    public async Task<IReadOnlyList<Loan>> GetLoanAccountsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var loans = await connection.QueryAsync<Loan>(
            new CommandDefinition(LoanSelect + " ORDER BY l.NextDueDate, l.LoanId;", cancellationToken: cancellationToken));
        return loans.AsList();
    }

    public async Task<LoanApplication?> AddLoanAsync(CreateLoanRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var product = await connection.QuerySingleOrDefaultAsync<ProductRow>(
                new CommandDefinition("""
                    SELECT ProductId AS Id, ProductName AS Name, InterestRatePlanId, InterestMethod, Frequency,
                           MinAmount, MaxAmount, MinInstallments, MaxInstallments, DefaultRatePct
                      FROM lon.LoanProduct
                     WHERE IsActive = 1 AND Frequency = 'MONTHLY'
                       AND (ProductName = @Product OR ProductCode = @Product);
                    """, new { Product = request.Product.Trim() }, transaction, cancellationToken: cancellationToken));
            if (product is null || request.Amount < product.MinAmount || request.Amount > product.MaxAmount ||
                request.TenureMonths < product.MinInstallments || request.TenureMonths > product.MaxInstallments)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var customer = await connection.QuerySingleOrDefaultAsync<ApplicationMember>(
                new CommandDefinition("""
                    SELECT CustomerId AS Id, BranchId, MonthlyIncome,
                           (SELECT TOP (1) GroupId FROM cust.GroupMember gm
                             WHERE gm.CustomerId = c.CustomerId AND gm.LeftOn IS NULL ORDER BY gm.JoinedOn DESC) AS GroupId
                      FROM cust.Customer c
                     WHERE CustomerId = @MemberId AND IsDeleted = 0 AND Status = 'ACTIVE';
                    """, new { request.MemberId }, transaction, cancellationToken: cancellationToken));
            if (customer is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var applicationNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
                DECLARE @seq BIGINT = NEXT VALUE FOR ref.SeqApplicationNo;
                SELECT 'A' + RIGHT('0000000' + CONVERT(VARCHAR(10), @seq), 7);
                """, transaction: transaction, cancellationToken: cancellationToken));

            var applicationId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT lon.LoanApplication(ApplicationNo, CustomerId, ProductId, InterestRatePlanId, BranchId, GroupId,
                    ApplicationDate, RequestedAmount, RequestedInstallments, TermYears, TermMonths,
                    Frequency, InterestMethod, InterestRatePct, MonthlyIncome, PurposeDescription, Status)
                OUTPUT INSERTED.ApplicationId
                VALUES (@ApplicationNo, @CustomerId, @ProductId, @InterestRatePlanId, @BranchId, @GroupId,
                    CAST(SYSUTCDATETIME() AS date), @Amount, @Installments, 0, @TermMonths,
                    @Frequency, @InterestMethod, @Rate, @MonthlyIncome, @Purpose, 'SUBMITTED');
                """, new
            {
                ApplicationNo = applicationNo,
                CustomerId = customer.Id,
                ProductId = product.Id,
                product.InterestRatePlanId,
                customer.BranchId,
                customer.GroupId,
                request.Amount,
                Installments = request.TenureMonths,
                TermMonths = request.TenureMonths,
                product.Frequency,
                product.InterestMethod,
                Rate = product.DefaultRatePct,
                customer.MonthlyIncome,
                Purpose = NullIfWhiteSpace(request.Purpose)
            }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT lon.ApplicationStatusHistory(ApplicationId, FromStatus, ToStatus, Comments)
                VALUES (@ApplicationId, NULL, 'SUBMITTED', N'Application submitted from web workspace.');
                """, new { ApplicationId = applicationId }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return (await GetLoanApplicationsAsync(cancellationToken))
                .Single(application => application.Id == applicationId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> DecideLoanAsync(string applicationNo, bool approved, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var application = await connection.QuerySingleOrDefaultAsync<ApprovalRow>(
                new CommandDefinition("""
                    SELECT ApplicationId AS Id, Status, RequestedAmount, RequestedInstallments, InterestRatePct
                      FROM lon.LoanApplication WITH (UPDLOCK, ROWLOCK)
                     WHERE ApplicationNo = @ApplicationNo;
                    """, new { ApplicationNo = applicationNo }, transaction, cancellationToken: cancellationToken));
            if (application is null || application.Status is not ("SUBMITTED" or "UNDER_REVIEW"))
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            var newStatus = approved ? "APPROVED" : "REJECTED";
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE lon.LoanApplication
                   SET Status = @NewStatus,
                       ApprovedAmount = CASE WHEN @Approved = 1 THEN RequestedAmount ELSE NULL END,
                       ApprovedInstallments = CASE WHEN @Approved = 1 THEN RequestedInstallments ELSE NULL END,
                       ApprovedRatePct = CASE WHEN @Approved = 1 THEN InterestRatePct ELSE NULL END,
                       ApprovedAt = CASE WHEN @Approved = 1 THEN SYSUTCDATETIME() ELSE NULL END,
                       UpdatedAt = SYSUTCDATETIME()
                 WHERE ApplicationId = @Id;
                INSERT lon.ApplicationStatusHistory(ApplicationId, FromStatus, ToStatus, Comments)
                VALUES (@Id, @OldStatus, @NewStatus, N'Decision recorded from web workspace.');
                """, new
            {
                NewStatus = newStatus,
                Approved = approved,
                application.Id,
                OldStatus = application.Status
            }, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Loan?> DisburseLoanAsync(string applicationNo, DisburseLoanRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var applicationId = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("""
                SELECT ApplicationId FROM lon.LoanApplication
                 WHERE ApplicationNo = @ApplicationNo AND Status = 'APPROVED';
                """, new { ApplicationNo = applicationNo }, cancellationToken: cancellationToken));
        if (applicationId is null)
        {
            return null;
        }

        var parameters = new DynamicParameters();
        parameters.Add("ApplicationId", applicationId.Value, DbType.Int64);
        parameters.Add("DisbursementDate", ToSqlDate(request.DisbursementDate), DbType.Date);
        parameters.Add("FirstInstallmentDate", ToSqlDate(request.FirstInstallmentDate), DbType.Date);
        parameters.Add("Mode", ToDatabaseMode(request.Mode), DbType.AnsiString, size: 20);
        parameters.Add("Reference", NullIfWhiteSpace(request.Reference), DbType.String, size: 60);
        parameters.Add("UserId", null, DbType.Int32);
        parameters.Add("LoanId", dbType: DbType.Int64, direction: ParameterDirection.Output);
        await connection.ExecuteAsync(new CommandDefinition(
            "lon.usp_Loan_Disburse", parameters, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));

        var loanId = parameters.Get<long>("LoanId");
        return await connection.QuerySingleAsync<Loan>(
            new CommandDefinition(LoanSelect + " AND l.LoanId = @LoanId;", new { LoanId = loanId }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<Collection>> GetCollectionsAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.PaymentId AS Id, p.ReceiptNo, l.LoanAccountNo AS LoanNo, c.FullName AS MemberName,
                   CAST(p.PaymentDate AS datetime2) AS PaidOn, p.Amount,
                   CASE p.Mode WHEN 'BANK_TRANSFER' THEN 'Bank transfer' ELSE
                        CONCAT(UPPER(LEFT(p.Mode, 1)), LOWER(SUBSTRING(p.Mode, 2, 30))) END AS Mode,
                   COALESCE(p.Reference, p.TransactionNo, p.ChequeNo, '') AS Reference
              FROM col.Payment p
              JOIN lon.Loan l ON l.LoanId = p.LoanId
              JOIN cust.Customer c ON c.CustomerId = p.CustomerId
             WHERE p.Status = 'POSTED'
             ORDER BY p.PaymentDate DESC, p.PaymentId DESC;
            """;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var payments = await connection.QueryAsync<Collection>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));
        return payments.AsList();
    }

    public async Task<Collection?> RecordCollectionAsync(CreateCollectionRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var idempotencyKey = request.IdempotencyKey ?? Guid.NewGuid();
            var existing = await connection.QuerySingleOrDefaultAsync<Collection>(
                new CommandDefinition("""
                    SELECT p.PaymentId AS Id, p.ReceiptNo, l.LoanAccountNo AS LoanNo, c.FullName AS MemberName,
                           CAST(p.PaymentDate AS datetime2) AS PaidOn, p.Amount, p.Mode,
                           COALESCE(p.Reference, p.TransactionNo, p.ChequeNo, '') AS Reference
                      FROM col.Payment p
                      JOIN lon.Loan l ON l.LoanId = p.LoanId
                      JOIN cust.Customer c ON c.CustomerId = p.CustomerId
                     WHERE p.IdempotencyKey = @IdempotencyKey;
                    """, new { IdempotencyKey = idempotencyKey }, transaction, cancellationToken: cancellationToken));
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }

            var loan = await connection.QuerySingleOrDefaultAsync<PaymentLoan>(
                new CommandDefinition("""
                    SELECT LoanId AS Id, CustomerId, BranchId, Status,
                           PrincipalOutstanding + InterestOutstanding + FeesOutstanding + PenaltyOutstanding AS Outstanding,
                           c.FullName AS MemberName
                      FROM lon.Loan l
                      JOIN cust.Customer c ON c.CustomerId = l.CustomerId
                     WHERE l.LoanAccountNo = @LoanNo;
                    """, new { request.LoanNo }, transaction, cancellationToken: cancellationToken));
            if (loan is null || loan.Status != "ACTIVE" || request.Amount > loan.Outstanding)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            var parameters = new DynamicParameters();
            parameters.Add("LoanId", loan.Id, DbType.Int64);
            parameters.Add("PaymentDate", ToSqlDate(request.PaidOn), DbType.Date);
            parameters.Add("Amount", request.Amount, DbType.Decimal);
            parameters.Add("Mode", ToDatabaseMode(request.Mode), DbType.AnsiString, size: 20);
            parameters.Add("Reference", NullIfWhiteSpace(request.Reference), DbType.String, size: 60);
            parameters.Add("IdempotencyKey", idempotencyKey, DbType.Guid);
            parameters.Add("UserId", null, DbType.Int32);
            parameters.Add("PaymentId", dbType: DbType.Int64, direction: ParameterDirection.Output);
            await connection.ExecuteAsync(new CommandDefinition(
                "col.usp_PostPayment", parameters, transaction, commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken));

            var paymentId = parameters.Get<long>("PaymentId");
            var payment = await connection.QuerySingleAsync<Collection>(
                new CommandDefinition("""
                    SELECT PaymentId AS Id, ReceiptNo, @LoanNo AS LoanNo, @MemberName AS MemberName,
                           CAST(PaymentDate AS datetime2) AS PaidOn, Amount,
                           CASE Mode WHEN 'BANK_TRANSFER' THEN 'Bank transfer' ELSE
                                CONCAT(UPPER(LEFT(Mode, 1)), LOWER(SUBSTRING(Mode, 2, 30))) END AS Mode,
                           COALESCE(Reference, TransactionNo, ChequeNo, '') AS Reference
                      FROM col.Payment WHERE PaymentId = @PaymentId;
                    """, new { PaymentId = paymentId, request.LoanNo, loan.MemberName }, transaction, cancellationToken: cancellationToken));

            await PostCollectionJournalAsync(connection, transaction, paymentId, loan, request, payment.ReceiptNo, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return payment;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReportData> PostVoucherAsync(CreateVoucherRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var branch = await GetDefaultBranchAsync(connection, transaction, cancellationToken);
            var ledgers = await connection.QueryAsync<LedgerIdRow>(new CommandDefinition("""
                SELECT LedgerId AS Id, LedgerName AS Name
                  FROM gl.Ledger
                 WHERE IsActive = 1 AND IsGroupLedger = 0
                   AND LedgerName IN (@DebitLedger, @CreditLedger);
                """, new { request.DebitLedger, request.CreditLedger }, transaction, cancellationToken: cancellationToken));
            var ledgerIds = ledgers.ToDictionary(row => row.Name, row => row.Id, StringComparer.OrdinalIgnoreCase);
            if (request.DebitLedger.Equals(request.CreditLedger, StringComparison.OrdinalIgnoreCase) ||
                !ledgerIds.TryGetValue(request.DebitLedger, out var debitLedgerId) ||
                !ledgerIds.TryGetValue(request.CreditLedger, out var creditLedgerId))
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new InvalidOperationException("Choose two different active ledger accounts.");
            }

            var voucherNo = $"JV-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..29];
            var journalId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT gl.JournalEntry(VoucherNo, VoucherType, VoucherDate, BranchId, PaymentMode, TransactionNo, Remarks)
                OUTPUT INSERTED.JournalEntryId
                VALUES (@VoucherNo, 'JOURNAL', @VoucherDate, @BranchId, @Mode, @Reference, @Narration);
                """, new
            {
                VoucherNo = voucherNo,
                VoucherDate = ToSqlDate(request.VoucherDate),
                BranchId = branch.Id,
                Mode = ToDatabaseMode(request.Mode),
                Reference = NullIfWhiteSpace(request.Reference),
                Narration = request.Narration.Trim()
            }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT gl.JournalLine(JournalEntryId, LineNo, LedgerId, Particulars, DebitAmount, CreditAmount)
                VALUES (@JournalId, 1, @DebitLedgerId, @Narration, @Amount, 0),
                       (@JournalId, 2, @CreditLedgerId, @Narration, 0, @Amount);
                EXEC gl.usp_PostJournalEntry @JournalEntryId = @JournalId, @UserId = NULL;
                """, new { JournalId = journalId, DebitLedgerId = debitLedgerId, CreditLedgerId = creditLedgerId,
                    Narration = request.Narration.Trim(), request.Amount }, transaction, cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            return await BuildReportAsync("cashbook-detail", request.VoucherDate, request.VoucherDate, branch.Name, cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ReportData?> BuildReportAsync(
        string type,
        DateOnly from,
        DateOnly to,
        string? branchName,
        CancellationToken cancellationToken)
    {
        var definition = ReportCatalog.All.SingleOrDefault(item => item.Id == type);
        if (definition is null)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var branch = await GetBranchAsync(connection, branchName, cancellationToken);
        var reportRows = await FinanceReportBuilder.BuildAsync(connection, type, from, to, branch, cancellationToken);
        return new ReportData(
            type,
            definition.Title,
            $"Dhan Jagrati Microfinance Ltd · {definition.Category}",
            branch.Name,
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset.Now.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture),
            reportRows.Metrics,
            reportRows.Columns,
            reportRows.Rows);
    }

    private async Task PostCollectionJournalAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long paymentId,
        PaymentLoan loan,
        CreateCollectionRequest request,
        string receiptNo,
        CancellationToken cancellationToken)
    {
        var allocations = await connection.QueryAsync<AllocationRow>(new CommandDefinition("""
            SELECT Component, SUM(Amount) AS Amount
              FROM col.PaymentAllocation
             WHERE PaymentId = @PaymentId
             GROUP BY Component;
            """, new { PaymentId = paymentId }, transaction, cancellationToken: cancellationToken));

        var ledgerRows = await connection.QueryAsync<LedgerCodeRow>(new CommandDefinition("""
            SELECT LedgerId AS Id, LedgerCode AS Code
              FROM gl.Ledger
             WHERE IsActive = 1 AND IsGroupLedger = 0
               AND LedgerCode IN ('CASH', 'BANK', 'LOAN_PORTFOLIO', 'INTEREST_INCOME', 'SERVICE_FEE_INCOME', 'PENALTY_INCOME');
            """, transaction: transaction, cancellationToken: cancellationToken));
        var ledgerIds = ledgerRows.ToDictionary(item => item.Code, item => item.Id, StringComparer.OrdinalIgnoreCase);
        var debitCode = ToDatabaseMode(request.Mode) == "CASH" ? "CASH" : "BANK";
        if (!ledgerIds.ContainsKey(debitCode))
        {
            throw new InvalidOperationException($"Required ledger '{debitCode}' is missing. Run the SQL support migration.");
        }

        var lines = new List<(long LedgerId, decimal Debit, decimal Credit, string Particulars)>
        {
            (ledgerIds[debitCode], request.Amount, 0, $"Collection from {loan.MemberName}")
        };
        foreach (var allocation in allocations)
        {
            var ledgerCode = allocation.Component switch
            {
                "PRINCIPAL" or "SETTLEMENT" => "LOAN_PORTFOLIO",
                "INTEREST" => "INTEREST_INCOME",
                "FEE" or "CHARGE" => "SERVICE_FEE_INCOME",
                "PENALTY" => "PENALTY_INCOME",
                _ => throw new InvalidOperationException($"Unsupported payment allocation component '{allocation.Component}'.")
            };
            if (!ledgerIds.TryGetValue(ledgerCode, out var ledgerId))
            {
                throw new InvalidOperationException($"Required ledger '{ledgerCode}' is missing. Run the SQL support migration.");
            }
            lines.Add((ledgerId, 0, allocation.Amount, allocation.Component));
        }

        var journalId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT gl.JournalEntry(VoucherNo, VoucherType, VoucherDate, BranchId, PayeeName, PaymentMode, TransactionNo, LoanId, PaymentId, Remarks)
            OUTPUT INSERTED.JournalEntryId
            VALUES (@VoucherNo, 'RECOVERY', @VoucherDate, @BranchId, @PayeeName, @Mode, @Reference, @LoanId, @PaymentId, N'Loan repayment');
            """, new
        {
            VoucherNo = receiptNo,
            VoucherDate = ToSqlDate(request.PaidOn),
            BranchId = loan.BranchId,
            PayeeName = loan.MemberName,
            Mode = ToDatabaseMode(request.Mode),
            Reference = NullIfWhiteSpace(request.Reference),
            loan.Id,
            PaymentId = paymentId
        }, transaction, cancellationToken: cancellationToken));

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT gl.JournalLine(JournalEntryId, LineNo, LedgerId, Particulars, DebitAmount, CreditAmount)
                VALUES (@JournalId, @LineNo, @LedgerId, @Particulars, @Debit, @Credit);
                """, new
            {
                JournalId = journalId,
                LineNo = index + 1,
                line.LedgerId,
                line.Particulars,
                line.Debit,
                line.Credit
            }, transaction, cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            EXEC gl.usp_PostJournalEntry @JournalEntryId = @JournalId, @UserId = NULL;
            """, new { JournalId = journalId }, transaction, cancellationToken: cancellationToken));
    }

    private async Task<Member> GetMemberByIdAsync(SqlConnection connection, long id, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<Member>(
            new CommandDefinition(MemberSelect + " AND c.CustomerId = @Id;", new { Id = id }, cancellationToken: cancellationToken));
    }

    private static async Task<BranchInfo> GetDefaultBranchAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        CancellationToken cancellationToken)
    {
        return await connection.QuerySingleAsync<BranchInfo>(new CommandDefinition("""
            SELECT TOP (1) BranchId AS Id, BranchName AS Name
              FROM ref.Branch
             WHERE IsActive = 1
             ORDER BY BranchId;
            """, transaction: transaction, cancellationToken: cancellationToken));
    }

    private static async Task<BranchInfo> GetBranchAsync(
        SqlConnection connection,
        string? name,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            var branch = await connection.QuerySingleOrDefaultAsync<BranchInfo>(new CommandDefinition("""
                SELECT BranchId AS Id, BranchName AS Name
                  FROM ref.Branch
                 WHERE IsActive = 1 AND BranchName = @Name;
                """, new { Name = name.Trim() }, cancellationToken: cancellationToken));
            if (branch is not null)
            {
                return branch;
            }
        }

        return await GetDefaultBranchAsync(connection, null, cancellationToken);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string ToDatabaseMode(string? mode) => mode?.Trim().ToUpperInvariant() switch
    {
        "CASH" => "CASH",
        "UPI" => "UPI",
        "CHEQUE" => "CHEQUE",
        "NEFT" => "NEFT",
        "NACH" => "NACH",
        "SAVINGS_TRANSFER" or "SAVINGS TRANSFER" => "SAVINGS_TRANSFER",
        "BANK_TRANSFER" or "BANK TRANSFER" => "BANK_TRANSFER",
        _ => throw new ArgumentException("Select a supported payment mode.", nameof(mode))
    };

    private static DateTime ToSqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static (string FirstName, string? LastName) SplitName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private sealed record CreatedCustomer(long Id, string MemberNo);
    private sealed record DashboardTotals(int ActiveMembers, int ActiveLoans, decimal PortfolioOutstanding, int OverdueAccounts, decimal CollectionsThisMonth);
    private sealed record DailyCollectionRow(DateTime PaidOn, decimal Amount);
    private sealed record ProductRow(int Id, string Name, long? InterestRatePlanId, string InterestMethod, string Frequency,
        decimal MinAmount, decimal MaxAmount, int MinInstallments, int MaxInstallments, decimal DefaultRatePct);
    private sealed record ApplicationMember(long Id, int BranchId, decimal? MonthlyIncome, long? GroupId);
    private sealed record ApprovalRow(long Id, string Status, decimal RequestedAmount, int RequestedInstallments, decimal InterestRatePct);
    private sealed record PaymentLoan(long Id, long CustomerId, int BranchId, string Status, decimal Outstanding, string MemberName);
    private sealed record LedgerIdRow(long Id, string Name);
    private sealed record LedgerCodeRow(long Id, string Code);
    private sealed record AllocationRow(string Component, decimal Amount);
}
