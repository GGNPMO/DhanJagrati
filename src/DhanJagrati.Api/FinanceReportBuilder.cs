using System.Globalization;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api;

internal sealed record FinanceReportRows(
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

internal static class FinanceReportBuilder
{
    public static async Task<FinanceReportRows> BuildAsync(
        SqlConnection connection,
        string type,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        return type switch
        {
            "cashbook" or "cashbook-detail" => await BuildCashbookAsync(connection, type, from, to, branch, cancellationToken),
            "trial-balance" => await BuildTrialBalanceAsync(connection, to, branch, cancellationToken),
            "profit-loss" => await BuildProfitLossAsync(connection, from, to, branch, cancellationToken),
            "balance-sheet" => await BuildBalanceSheetAsync(connection, to, branch, cancellationToken),
            "loan-applications" => await BuildApplicationsAsync(connection, from, to, branch, cancellationToken),
            "recovery-due" => await BuildRecoveryDueAsync(connection, to, branch, cancellationToken),
            "recovery-ledger" => await BuildRecoveryLedgerAsync(connection, from, to, branch, cancellationToken),
            "relationship-manager" => await BuildRelationshipManagersAsync(connection, branch, cancellationToken),
            "loan-closer" => await BuildLoanClosuresAsync(connection, from, to, branch, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported report type.")
        };
    }

    private static async Task<FinanceReportRows> BuildCashbookAsync(
        SqlConnection connection,
        string type,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var allActivity = await GetActivityAsync(connection, branch.Id, to, cancellationToken);
        var openingBalances = await GetOpeningBalancesAsync(connection, branch.Id, to, cancellationToken);
        var opening = ComputeBalances(allActivity, openingBalances, from.AddDays(-1))
            .Where(item => item.IsCash || item.IsBank)
            .Sum(item => item.Balance);
        var period = allActivity
            .Where(item => (item.IsCash || item.IsBank) &&
                DateOnly.FromDateTime(item.Date) >= from && DateOnly.FromDateTime(item.Date) <= to)
            .OrderBy(item => item.Date).ThenBy(item => item.VoucherNo, StringComparer.Ordinal).ToArray();
        var receipts = period.Sum(item => item.Debit);
        var payments = period.Sum(item => item.Credit);

        if (type == "cashbook-detail")
        {
            var detailRows = period.Select(item => Row(
                ("date", FormatDate(item.Date)),
                ("voucher", item.VoucherNo),
                ("ledger", item.LedgerName),
                ("particulars", item.Particulars),
                ("debit", Money(item.Debit)),
                ("credit", Money(item.Credit)),
                ("mode", item.Mode),
                ("reference", item.Reference))).ToArray();
            return new FinanceReportRows(
                [
                    Metric("Opening balance", Money(opening)),
                    Metric("Total receipts", Money(receipts), "positive"),
                    Metric("Total payments", Money(payments)),
                    Metric("Closing balance", Money(opening + receipts - payments), "positive")
                ],
                [
                    Column("date", "Date"), Column("voucher", "Voucher no."), Column("ledger", "Account head"),
                    Column("particulars", "Particulars"), Column("debit", "Debit", "right"),
                    Column("credit", "Credit", "right"), Column("mode", "Mode"), Column("reference", "Cheque / reference")
                ],
                detailRows);
        }

        var runningBalance = opening;
        var dailyRows = period.GroupBy(item => item.Date).OrderBy(group => group.Key).Select(group =>
        {
            var dayIn = group.Sum(item => item.Debit);
            var dayOut = group.Sum(item => item.Credit);
            runningBalance += dayIn - dayOut;
            return Row(("date", FormatDate(group.Key)), ("receipts", Money(dayIn)), ("payments", Money(dayOut)), ("closing", Money(runningBalance)));
        }).ToArray();

        return new FinanceReportRows(
            [
                Metric("Opening balance", Money(opening)),
                Metric("Receipts", Money(receipts), "positive"),
                Metric("Payments", Money(payments)),
                Metric("Closing balance", Money(opening + receipts - payments), "positive")
            ],
            [Column("date", "Voucher date"), Column("receipts", "Receipts", "right"), Column("payments", "Payments", "right"), Column("closing", "Closing balance", "right")],
            dailyRows);
    }

    private static async Task<FinanceReportRows> BuildTrialBalanceAsync(
        SqlConnection connection,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var activity = await GetActivityAsync(connection, branch.Id, to, cancellationToken);
        var openings = await GetOpeningBalancesAsync(connection, branch.Id, to, cancellationToken);
        var balances = ComputeBalances(activity, openings, to).Where(item => item.Balance != 0).OrderBy(item => item.LedgerName).ToArray();
        var debitTotal = balances.Sum(item => Math.Max(item.Balance, 0));
        var creditTotal = balances.Sum(item => Math.Max(-item.Balance, 0));

        return new FinanceReportRows(
            [
                Metric("Ledger accounts", balances.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Total debit", Money(debitTotal)),
                Metric("Total credit", Money(creditTotal), "positive")
            ],
            [Column("ledger", "Ledger account"), Column("debit", "Debit balance", "right"), Column("credit", "Credit balance", "right")],
            balances.Select(item => Row(
                ("ledger", item.LedgerName),
                ("debit", Money(Math.Max(item.Balance, 0))),
                ("credit", Money(Math.Max(-item.Balance, 0))))).ToArray());
    }

    private static async Task<FinanceReportRows> BuildProfitLossAsync(
        SqlConnection connection,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var activity = await GetActivityAsync(connection, branch.Id, to, cancellationToken);
        var period = activity.Where(item =>
            DateOnly.FromDateTime(item.Date) >= from && DateOnly.FromDateTime(item.Date) <= to).ToArray();
        var income = period.Where(item => item.AccountClass == "INCOME")
            .GroupBy(item => item.LedgerName).Select(group => new
            {
                Ledger = group.Key,
                Amount = group.Sum(item => item.Credit - item.Debit)
            }).Where(item => item.Amount != 0).OrderBy(item => item.Ledger).ToArray();
        var expenses = period.Where(item => item.AccountClass == "EXPENSE")
            .GroupBy(item => item.LedgerName).Select(group => new
            {
                Ledger = group.Key,
                Amount = group.Sum(item => item.Debit - item.Credit)
            }).Where(item => item.Amount != 0).OrderBy(item => item.Ledger).ToArray();
        var totalIncome = income.Sum(item => item.Amount);
        var totalExpenses = expenses.Sum(item => item.Amount);
        var surplus = totalIncome - totalExpenses;
        var rows = income.Select(item => Row(("category", item.Ledger), ("income", Money(item.Amount)), ("expense", "—")))
            .Concat(expenses.Select(item => Row(("category", item.Ledger), ("income", "—"), ("expense", Money(item.Amount)))))
            .Append(Row(
                ("category", surplus >= 0 ? "Net surplus" : "Net deficit"),
                ("income", surplus >= 0 ? Money(surplus) : "—"),
                ("expense", surplus < 0 ? Money(-surplus) : "—"))).ToArray();

        return new FinanceReportRows(
            [
                Metric("Total income", Money(totalIncome), "positive"),
                Metric("Total expenses", Money(totalExpenses)),
                Metric(surplus >= 0 ? "Surplus" : "Deficit", Money(Math.Abs(surplus)), surplus >= 0 ? "positive" : "negative")
            ],
            [Column("category", "Particulars"), Column("income", "Income", "right"), Column("expense", "Expenses", "right")],
            rows);
    }

    private static async Task<FinanceReportRows> BuildBalanceSheetAsync(
        SqlConnection connection,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var activity = await GetActivityAsync(connection, branch.Id, to, cancellationToken);
        var openings = await GetOpeningBalancesAsync(connection, branch.Id, to, cancellationToken);
        var balances = ComputeBalances(activity, openings, to);
        var assets = balances.Where(item => item.AccountClass == "ASSET" && item.Balance != 0)
            .OrderBy(item => item.LedgerName).ToArray();
        var liabilities = balances.Where(item => item.AccountClass == "LIABILITY" && item.Balance != 0)
            .OrderBy(item => item.LedgerName).ToArray();
        var equity = balances.Where(item => item.AccountClass == "EQUITY" && item.Balance != 0)
            .OrderBy(item => item.LedgerName).ToArray();

        var yearStart = new DateOnly(to.Year, 1, 1);
        var yearActivity = activity.Where(item => DateOnly.FromDateTime(item.Date) >= yearStart).ToArray();
        var currentIncome = yearActivity.Where(item => item.AccountClass == "INCOME").Sum(item => item.Credit - item.Debit);
        var currentExpenses = yearActivity.Where(item => item.AccountClass == "EXPENSE").Sum(item => item.Debit - item.Credit);
        var currentSurplus = currentIncome - currentExpenses;
        var totalAssets = assets.Sum(item => item.Balance);
        var totalLiabilitiesAndEquity = liabilities.Sum(item => -item.Balance) + equity.Sum(item => -item.Balance) + currentSurplus;

        var rows = assets.Select(item => Row(("section", $"ASSETS · {item.LedgerName}"), ("amount", Money(item.Balance))))
            .Append(Row(("section", "TOTAL ASSETS"), ("amount", Money(totalAssets))))
            .Concat(liabilities.Select(item => Row(("section", $"LIABILITIES · {item.LedgerName}"), ("amount", Money(-item.Balance)))))
            .Concat(equity.Select(item => Row(("section", $"EQUITY · {item.LedgerName}"), ("amount", Money(-item.Balance)))))
            .Append(Row(("section", "EQUITY · Current year surplus"), ("amount", Money(currentSurplus))))
            .Append(Row(("section", "TOTAL LIABILITIES & EQUITY"), ("amount", Money(totalLiabilitiesAndEquity))))
            .ToArray();

        return new FinanceReportRows(
            [
                Metric("Total assets", Money(totalAssets), "positive"),
                Metric("Liabilities & equity", Money(totalLiabilitiesAndEquity)),
                Metric("Difference", Money(Math.Abs(totalAssets - totalLiabilitiesAndEquity)),
                    totalAssets == totalLiabilitiesAndEquity ? "positive" : "negative")
            ],
            [Column("section", "Statement of financial position"), Column("amount", "Amount", "right")],
            rows);
    }

    private static async Task<FinanceReportRows> BuildApplicationsAsync(
        SqlConnection connection,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var rows = (await connection.QueryAsync<ApplicationReportRow>(new CommandDefinition("""
            SELECT CAST(a.ApplicationDate AS datetime2) AS AppliedOn, a.ApplicationNo, c.FullName AS MemberName,
                   p.ProductName AS Product, a.RequestedAmount AS Amount, a.RequestedInstallments AS Installments,
                   COALESCE(a.PurposeDescription, '') AS Purpose,
                   CASE a.Status WHEN 'SUBMITTED' THEN 'Awaiting review' WHEN 'UNDER_REVIEW' THEN 'Awaiting review'
                        WHEN 'APPROVED' THEN 'Approved' WHEN 'DISBURSED' THEN 'Disbursed'
                        WHEN 'REJECTED' THEN 'Declined' ELSE a.Status END AS Status,
                   COALESCE(e.EmployeeName, N'Unassigned') AS Manager
              FROM lon.LoanApplication a
              JOIN cust.Customer c ON c.CustomerId = a.CustomerId
              JOIN lon.LoanProduct p ON p.ProductId = a.ProductId
              LEFT JOIN sec.Employee e ON e.EmployeeId = a.EmployeeId
             WHERE a.BranchId = @BranchId AND a.ApplicationDate >= @From AND a.ApplicationDate <= @To
             ORDER BY a.ApplicationDate DESC, a.ApplicationId DESC;
            """, new { BranchId = branch.Id, From = SqlDate(from), To = SqlDate(to) }, cancellationToken: cancellationToken))).ToArray();

        var underReview = rows.Count(item => item.Status == "Awaiting review");
        return new FinanceReportRows(
            [
                Metric("Applications", rows.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Under review", underReview.ToString(CultureInfo.InvariantCulture)),
                Metric("Requested", Money(rows.Sum(item => item.Amount)))
            ],
            [
                Column("date", "Applied on"), Column("application", "Application no."), Column("member", "Member"),
                Column("product", "Product"), Column("amount", "Requested", "right"), Column("tenure", "Tenure"),
                Column("purpose", "Purpose"), Column("status", "Status"), Column("manager", "Relationship manager")
            ],
            rows.Select(item => Row(
                ("date", FormatDate(item.AppliedOn)),
                ("application", item.ApplicationNo),
                ("member", item.MemberName),
                ("product", item.Product),
                ("amount", Money(item.Amount)),
                ("tenure", $"{item.Installments} installments"),
                ("purpose", item.Purpose),
                ("status", item.Status),
                ("manager", item.Manager))).ToArray());
    }

    private static async Task<FinanceReportRows> BuildRecoveryDueAsync(
        SqlConnection connection,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var due = (await connection.QueryAsync<DueReportRow>(new CommandDefinition("""
            SELECT l.LoanAccountNo AS LoanNo, c.FullName AS MemberName, c.PrimaryPhone AS Phone,
                   COALESCE(g.GroupName, N'') AS GroupName, CAST(MIN(s.DueDate) AS datetime2) AS DueDate,
                   SUM(s.TotalDue - s.PrincipalPaid - s.InterestPaid - s.FeePaid) AS Installment,
                   l.PrincipalOutstanding + l.InterestOutstanding + l.FeesOutstanding + l.PenaltyOutstanding AS Outstanding,
                   COALESCE(e.EmployeeName, N'Unassigned') AS Manager
              FROM lon.LoanSchedule s
              JOIN lon.Loan l ON l.LoanId = s.LoanId
              JOIN cust.Customer c ON c.CustomerId = l.CustomerId
              LEFT JOIN sec.Employee e ON e.EmployeeId = l.EmployeeId
              OUTER APPLY (
                  SELECT TOP (1) cg.GroupName
                    FROM cust.GroupMember gm JOIN cust.CustomerGroup cg ON cg.GroupId = gm.GroupId
                   WHERE gm.CustomerId = c.CustomerId AND gm.LeftOn IS NULL AND cg.Status = 'ACTIVE'
                   ORDER BY gm.JoinedOn DESC
              ) g
             WHERE l.BranchId = @BranchId AND l.Status = 'ACTIVE' AND s.IsActive = 1
               AND s.Status IN ('PENDING','PARTIAL','OVERDUE') AND s.DueDate <= @To
             GROUP BY l.LoanAccountNo, c.FullName, c.PrimaryPhone, g.GroupName, l.PrincipalOutstanding,
                      l.InterestOutstanding, l.FeesOutstanding, l.PenaltyOutstanding, e.EmployeeName
             ORDER BY MIN(s.DueDate);
            """, new { BranchId = branch.Id, To = SqlDate(to) }, cancellationToken: cancellationToken))).ToArray();

        return new FinanceReportRows(
            [
                Metric("Accounts due", due.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Installments due", Money(due.Sum(item => item.Installment))),
                Metric("Outstanding", Money(due.Sum(item => item.Outstanding)))
            ],
            [
                Column("loan", "Loan no."), Column("member", "Member"), Column("phone", "Contact"), Column("group", "Group"),
                Column("due", "Due date"), Column("installment", "Installment", "right"),
                Column("outstanding", "Outstanding", "right"), Column("manager", "Relationship manager")
            ],
            due.Select(item => Row(
                ("loan", item.LoanNo),
                ("member", item.MemberName),
                ("phone", item.Phone),
                ("group", item.GroupName),
                ("due", FormatDate(item.DueDate)),
                ("installment", Money(item.Installment)),
                ("outstanding", Money(item.Outstanding)),
                ("manager", item.Manager))).ToArray());
    }

    private static async Task<FinanceReportRows> BuildRecoveryLedgerAsync(
        SqlConnection connection,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var payments = (await connection.QueryAsync<PaymentReportRow>(new CommandDefinition("""
            SELECT CAST(p.PaymentDate AS datetime2) AS PaidOn, p.ReceiptNo, l.LoanAccountNo AS LoanNo,
                   c.FullName AS MemberName, p.Amount,
                   CASE p.Mode WHEN 'BANK_TRANSFER' THEN 'Bank transfer' ELSE
                        CONCAT(UPPER(LEFT(p.Mode, 1)), LOWER(SUBSTRING(p.Mode, 2, 30))) END AS Mode,
                   COALESCE(p.Reference, p.TransactionNo, p.ChequeNo, '') AS Reference
              FROM col.Payment p
              JOIN lon.Loan l ON l.LoanId = p.LoanId
              JOIN cust.Customer c ON c.CustomerId = p.CustomerId
             WHERE l.BranchId = @BranchId AND p.Status = 'POSTED'
               AND p.PaymentDate >= @From AND p.PaymentDate <= @To
             ORDER BY p.PaymentDate DESC, p.PaymentId DESC;
            """, new { BranchId = branch.Id, From = SqlDate(from), To = SqlDate(to) }, cancellationToken: cancellationToken))).ToArray();

        return new FinanceReportRows(
            [
                Metric("Receipts", payments.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Collected", Money(payments.Sum(item => item.Amount)), "positive")
            ],
            [
                Column("date", "Receipt date"), Column("receipt", "Receipt no."), Column("loan", "Loan no."),
                Column("member", "Member"), Column("amount", "Amount", "right"),
                Column("mode", "Payment mode"), Column("reference", "Cheque / reference")
            ],
            payments.Select(item => Row(
                ("date", FormatDate(item.PaidOn)),
                ("receipt", item.ReceiptNo),
                ("loan", item.LoanNo),
                ("member", item.MemberName),
                ("amount", Money(item.Amount)),
                ("mode", item.Mode),
                ("reference", item.Reference))).ToArray());
    }

    private static async Task<FinanceReportRows> BuildRelationshipManagersAsync(
        SqlConnection connection,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var results = (await connection.QueryAsync<ManagerReportRow>(new CommandDefinition("""
            SELECT COALESCE(e.EmployeeName, N'Unassigned') AS Manager,
                   COUNT(*) AS Accounts, COUNT(DISTINCT l.CustomerId) AS MemberCount,
                   SUM(l.PrincipalAmount) AS Sanctioned,
                   SUM(l.PrincipalOutstanding + l.InterestOutstanding + l.FeesOutstanding + l.PenaltyOutstanding) AS Outstanding
              FROM lon.Loan l
              LEFT JOIN sec.Employee e ON e.EmployeeId = l.EmployeeId
             WHERE l.BranchId = @BranchId AND l.Status = 'ACTIVE'
             GROUP BY e.EmployeeName
             ORDER BY Manager;
            """, new { BranchId = branch.Id }, cancellationToken: cancellationToken))).ToArray();

        var memberNames = (await connection.QueryAsync<ManagerMembersRow>(new CommandDefinition("""
            SELECT COALESCE(e.EmployeeName, N'Unassigned') AS Manager, STRING_AGG(c.FullName, N', ') AS Members
              FROM lon.Loan l
              JOIN cust.Customer c ON c.CustomerId = l.CustomerId
              LEFT JOIN sec.Employee e ON e.EmployeeId = l.EmployeeId
             WHERE l.BranchId = @BranchId AND l.Status = 'ACTIVE'
             GROUP BY e.EmployeeName;
            """, new { BranchId = branch.Id }, cancellationToken: cancellationToken)))
            .ToDictionary(item => item.Manager, item => item.Members ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        return new FinanceReportRows(
            [
                Metric("Managers", results.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Portfolio", Money(results.Sum(item => item.Outstanding)))
            ],
            [
                Column("manager", "Relationship manager"), Column("accounts", "Active accounts", "right"),
                Column("members", "Members"), Column("sanctioned", "Sanctioned", "right"),
                Column("outstanding", "Outstanding", "right")
            ],
            results.Select(item => Row(
                ("manager", item.Manager),
                ("accounts", item.Accounts.ToString(CultureInfo.InvariantCulture)),
                ("members", memberNames.GetValueOrDefault(item.Manager, string.Empty)),
                ("sanctioned", Money(item.Sanctioned)),
                ("outstanding", Money(item.Outstanding)))).ToArray());
    }

    private static async Task<FinanceReportRows> BuildLoanClosuresAsync(
        SqlConnection connection,
        DateOnly from,
        DateOnly to,
        BranchInfo branch,
        CancellationToken cancellationToken)
    {
        var loans = (await connection.QueryAsync<ClosureReportRow>(new CommandDefinition("""
            SELECT l.LoanAccountNo AS LoanNo, c.FullName AS MemberName, p.ProductName AS Product,
                   l.PrincipalAmount AS Amount, CAST(l.ClosedDate AS datetime2) AS ClosedDate,
                   COALESCE(e.EmployeeName, N'Unassigned') AS Manager
              FROM lon.Loan l
              JOIN cust.Customer c ON c.CustomerId = l.CustomerId
              JOIN lon.LoanProduct p ON p.ProductId = l.ProductId
              LEFT JOIN sec.Employee e ON e.EmployeeId = l.EmployeeId
             WHERE l.BranchId = @BranchId AND l.Status IN ('CLOSED','FORECLOSED','SETTLED')
               AND l.ClosedDate >= @From AND l.ClosedDate <= @To
             ORDER BY l.ClosedDate DESC, l.LoanId DESC;
            """, new { BranchId = branch.Id, From = SqlDate(from), To = SqlDate(to) }, cancellationToken: cancellationToken))).ToArray();

        return new FinanceReportRows(
            [
                Metric("Loans closed", loans.Length.ToString(CultureInfo.InvariantCulture)),
                Metric("Settled amount", Money(loans.Sum(item => item.Amount)))
            ],
            [
                Column("loan", "Loan no."), Column("member", "Member"), Column("product", "Product"),
                Column("amount", "Original amount", "right"), Column("closed", "Closure date"),
                Column("manager", "Relationship manager")
            ],
            loans.Select(item => Row(
                ("loan", item.LoanNo),
                ("member", item.MemberName),
                ("product", item.Product),
                ("amount", Money(item.Amount)),
                ("closed", FormatDate(item.ClosedDate)),
                ("manager", item.Manager))).ToArray());
    }

    private static async Task<ActivityRow[]> GetActivityAsync(
        SqlConnection connection,
        int branchId,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ActivityRow>(new CommandDefinition("""
            SELECT CAST(VoucherDate AS datetime2) AS Date, VoucherNo, LedgerName, AccountClass, IsCash, IsBank,
                   COALESCE(Particulars, VoucherRemarks, '') AS Particulars,
                   DebitAmount AS Debit, CreditAmount AS Credit,
                   COALESCE(PaymentMode, '') AS Mode,
                   COALESCE(ChequeNo, BankName, TransactionNo, '') AS Reference
              FROM gl.vw_PostedJournalActivity
             WHERE BranchId = @BranchId AND VoucherDate <= @To;
            """, new { BranchId = branchId, To = SqlDate(to) }, cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    private static async Task<OpeningBalanceRow[]> GetOpeningBalancesAsync(
        SqlConnection connection,
        int branchId,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<OpeningBalanceRow>(new CommandDefinition("""
            WITH LatestOpening AS (
                SELECT o.LedgerId, o.AsOfDate, o.DebitAmount, o.CreditAmount,
                       ROW_NUMBER() OVER (PARTITION BY o.LedgerId ORDER BY o.AsOfDate DESC) AS RowNo
                  FROM gl.LedgerOpeningBalance o
                 WHERE o.BranchId = @BranchId AND o.AsOfDate <= @To
            )
            SELECT l.LedgerId, l.LedgerName, l.AccountClass, l.IsCash, l.IsBank,
                   CAST(o.AsOfDate AS datetime2) AS AsOfDate,
                   COALESCE(o.DebitAmount, 0) AS Debit, COALESCE(o.CreditAmount, 0) AS Credit
              FROM gl.Ledger l
              LEFT JOIN LatestOpening o ON o.LedgerId = l.LedgerId AND o.RowNo = 1;
            """, new { BranchId = branchId, To = SqlDate(to) }, cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    private static IReadOnlyList<LedgerBalance> ComputeBalances(
        IReadOnlyList<ActivityRow> activity,
        IReadOnlyList<OpeningBalanceRow> openings,
        DateOnly asOf)
    {
        var cutOff = asOf.ToDateTime(TimeOnly.MaxValue);
        return openings.Select(opening =>
        {
            var openingDate = opening.AsOfDate is null ? DateTime.MinValue : opening.AsOfDate.Value;
            var movement = activity.Where(item =>
                    item.LedgerName == opening.LedgerName && item.Date <= cutOff &&
                    (opening.AsOfDate is null || item.Date > openingDate))
                .Sum(item => item.Debit - item.Credit);
            return new LedgerBalance(
                opening.LedgerName,
                opening.AccountClass,
                opening.IsCash,
                opening.IsBank,
                opening.Debit - opening.Credit + movement);
        }).ToArray();
    }

    private static DateTime SqlDate(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);
    private static string FormatDate(DateTime date) => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => "₹ " + value.ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));
    private static ReportMetric Metric(string label, string value, string tone = "") => new(label, value, tone);
    private static ReportColumn Column(string key, string label, string alignment = "left") => new(key, label, alignment);
    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value);

    private sealed record ActivityRow(
        DateTime Date, string VoucherNo, string LedgerName, string AccountClass,
        bool IsCash, bool IsBank, string Particulars, decimal Debit, decimal Credit, string Mode, string Reference);
    private sealed record OpeningBalanceRow(
        long LedgerId, string LedgerName, string AccountClass, bool IsCash, bool IsBank,
        DateTime? AsOfDate, decimal Debit, decimal Credit);
    private sealed record LedgerBalance(string LedgerName, string AccountClass, bool IsCash, bool IsBank, decimal Balance);
    private sealed record ApplicationReportRow(
        DateTime AppliedOn, string ApplicationNo, string MemberName, string Product, decimal Amount,
        int Installments, string Purpose, string Status, string Manager);
    private sealed record DueReportRow(
        string LoanNo, string MemberName, string Phone, string GroupName, DateTime DueDate,
        decimal Installment, decimal Outstanding, string Manager);
    private sealed record PaymentReportRow(
        DateTime PaidOn, string ReceiptNo, string LoanNo, string MemberName, decimal Amount, string Mode, string Reference);
    private sealed record ManagerReportRow(string Manager, int Accounts, int MemberCount, decimal Sanctioned, decimal Outstanding);
    private sealed record ManagerMembersRow(string Manager, string? Members);
    private sealed record ClosureReportRow(
        string LoanNo, string MemberName, string Product, decimal Amount, DateTime ClosedDate, string Manager);
}
