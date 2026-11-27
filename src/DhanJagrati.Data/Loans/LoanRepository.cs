using System.Data;
using Dapper;
using DhanJagrati.Data.Members;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Loans;

public sealed class GuarantorInput
{
    public string? MemberNo { get; set; }
    public string? GuarantorName { get; set; }
    public string? GuardianName { get; set; }
    public string? AddressLine { get; set; }
    public string? MobileNo { get; set; }
    public int? RelationId { get; set; }
    public string? IdProofNo { get; set; }
}

public sealed class CollateralInput
{
    public string? DepositAccountNo { get; set; }
    public decimal DepositAmount { get; set; }
    public DateOnly? MaturityDate { get; set; }
    public decimal? LienAmount { get; set; }
}

/// <summary>Loan application as entered on the screen; EMI terms are recomputed on the server.</summary>
public sealed class LoanInput
{
    public DateOnly? ApplicationDate { get; set; }
    public string? MemberNo { get; set; }
    public int PlanId { get; set; }
    public decimal LoanAmount { get; set; }
    public decimal? InterestRate { get; set; }
    public decimal? EmiAmount { get; set; }
    public short NoOfUnits { get; set; } = 1;
    public DateOnly? EmiStartDate { get; set; }
    public string? DisbursementMode { get; set; }
    public int? DisbursementLedgerId { get; set; }
    public string? ChequeNo { get; set; }
    public string? Remark { get; set; }
    public string? EmployeeCode { get; set; }
    public int? GroupId { get; set; }
    public decimal? ProcessingFeePct { get; set; }
    public decimal? ProcessingFee { get; set; }
    public decimal GstAmount { get; set; }
    public decimal ShareAmount { get; set; }
    public decimal InsuranceAmount { get; set; }
    public string? SavingAccountNo { get; set; }
    public List<GuarantorInput> Guarantors { get; set; } = [];
    public List<CollateralInput> Collaterals { get; set; } = [];
}

public sealed class LoanDetail
{
    public int Id { get; init; }
    public string LoanNo { get; init; } = "";
    public DateOnly ApplicationDate { get; init; }
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public string Status { get; init; } = "";
    // Member
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string? FileNo { get; init; }
    public string Gender { get; init; } = "";
    public string MemberAddress { get; init; } = "";
    public string? MemberMobile { get; init; }
    public string? MemberEmail { get; init; }
    public string? PinCode { get; init; }
    public string? CityName { get; init; }
    public string? StateName { get; init; }
    // Terms
    public int PlanId { get; init; }
    public string PlanCode { get; init; } = "";
    public string EmiMode { get; init; } = "";
    public decimal LoanAmount { get; init; }
    public decimal InterestRate { get; init; }
    public short TenureYears { get; init; }
    public short TenureMonths { get; init; }
    public short NoOfInstallments { get; init; }
    public decimal EmiAmount { get; init; }
    public string? EmiCalcMethod { get; init; }
    public bool IsEmiOverridden { get; init; }
    public short NoOfUnits { get; init; }
    public DateOnly EmiStartDate { get; init; }
    // Disbursement
    public string DisbursementMode { get; init; } = "";
    public int? DisbursementLedgerId { get; init; }
    public string? DisbursementLedgerName { get; init; }
    public string? ChequeNo { get; init; }
    public decimal? ChequeAmount { get; init; }
    public string? Remark { get; init; }
    // Relationship
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public int? GroupId { get; init; }
    // Deductions
    public decimal ProcessingFeePct { get; init; }
    public decimal ProcessingFee { get; init; }
    public decimal GstAmount { get; init; }
    public decimal ShareAmount { get; init; }
    public decimal InsuranceAmount { get; init; }
    public decimal NetDisbursedAmount { get; init; }
    public string? SavingAccountNo { get; init; }
    // Workflow
    public DateOnly? ApprovedOn { get; init; }
    public DateOnly? DisbursedOn { get; init; }
    public string? DisbursementVoucherNo { get; init; }
    public DateOnly? ClosedOn { get; init; }
    public List<GuarantorRow> Guarantors { get; set; } = [];
    public List<CollateralRow> Collaterals { get; set; } = [];
}

public sealed class GuarantorRow
{
    public string? MemberNo { get; init; }
    public string GuarantorName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string? AddressLine { get; init; }
    public string? MobileNo { get; init; }
    public int? RelationId { get; init; }
    public string? IdProofNo { get; init; }
}

public sealed class CollateralRow
{
    public string DepositAccountNo { get; init; } = "";
    public decimal DepositAmount { get; init; }
    public DateOnly? MaturityDate { get; init; }
    public decimal? LienAmount { get; init; }
}

public sealed class LoanListItem
{
    public int Id { get; init; }
    public string LoanNo { get; init; } = "";
    public DateOnly ApplicationDate { get; init; }
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string PlanCode { get; init; } = "";
    public decimal LoanAmount { get; init; }
    public decimal EmiAmount { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string Status { get; init; } = "";
}

public sealed class ScheduleRow
{
    public short InstallmentNo { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal EmiAmount { get; init; }
    public decimal PrincipalAmount { get; init; }
    public decimal InterestAmount { get; init; }
    public decimal PrincipalPaid { get; init; }
    public decimal InterestPaid { get; init; }
    public string Status { get; init; } = "";
}

public sealed class LoanRepository(IDbConnectionFactory db, LoanTermsService terms)
{
    private static readonly string[] DisbursementModes = ["CASH", "CHEQUE", "NEFT", "RTGS", "IMPS"];

    public async Task<IReadOnlyList<LoanListItem>> SearchAsync(int branchId, string? search, string? status, CancellationToken ct)
    {
        var term = Text.Clean(search);
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<LoanListItem>(new CommandDefinition("""
            SELECT TOP (200) l.LoanId AS Id, l.LoanNo, l.ApplicationDate, m.MemberNo, m.MemberName, p.PlanCode,
                   l.LoanAmount, l.EmiAmount, e.EmployeeCode, l.Status
            FROM loan.LoanApplication l
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN mst.LoanPlan p ON p.PlanId = l.PlanId
            JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
            WHERE l.BranchId = @branchId
              AND (@status IS NULL OR l.Status = @status)
              AND (@term IS NULL OR l.LoanNo LIKE @like OR m.MemberNo LIKE @like OR m.MemberName LIKE @like OR m.FileNo LIKE @like)
            ORDER BY l.LoanId DESC
            """, new { branchId, status = Text.Clean(status, upper: true), term, like = $"%{term}%" }, cancellationToken: ct))).AsList();
    }

    public async Task<LoanDetail?> GetAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("""
            SELECT l.LoanId AS Id, l.LoanNo, l.ApplicationDate, l.BranchId, b.BranchName, l.Status,
                   m.MemberNo, m.MemberName, m.GuardianName, m.FileNo, m.Gender, m.AddressLine AS MemberAddress,
                   m.MobileNo AS MemberMobile, m.Email AS MemberEmail, m.PinCode, ci.CityName, st.StateName,
                   l.PlanId, p.PlanCode, l.EmiMode, l.LoanAmount, l.InterestRate, l.TenureYears, l.TenureMonths,
                   l.NoOfInstallments, l.EmiAmount, l.EmiCalcMethod, l.IsEmiOverridden, l.NoOfUnits, l.EmiStartDate,
                   l.DisbursementMode, l.DisbursementLedgerId, lg.LedgerName AS DisbursementLedgerName, l.ChequeNo, l.ChequeAmount, l.Remark,
                   e.EmployeeCode, e.EmployeeName, l.GroupId,
                   l.ProcessingFeePct, l.ProcessingFee, l.GstAmount, l.ShareAmount, l.InsuranceAmount, l.NetDisbursedAmount, l.SavingAccountNo,
                   l.ApprovedOn, l.DisbursedOn, v.VoucherNo AS DisbursementVoucherNo, l.ClosedOn
            FROM loan.LoanApplication l
            JOIN sec.Branch b ON b.BranchId = l.BranchId
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN mst.LoanPlan p ON p.PlanId = l.PlanId
            JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
            LEFT JOIN mst.City ci ON ci.CityId = m.CityId
            LEFT JOIN mst.State st ON st.StateId = m.StateId
            LEFT JOIN acc.Ledger lg ON lg.LedgerId = l.DisbursementLedgerId
            LEFT JOIN acc.Voucher v ON v.VoucherId = l.DisbursementVoucherId
            WHERE l.LoanId = @id AND l.BranchId = @branchId;

            SELECT gm.MemberNo, g.GuarantorName, g.GuardianName, g.AddressLine, g.MobileNo, g.RelationId, g.IdProofNo
            FROM loan.LoanGuarantor g LEFT JOIN loan.Member gm ON gm.MemberId = g.MemberId
            WHERE g.LoanId = @id ORDER BY g.GuarantorId;

            SELECT DepositAccountNo, DepositAmount, MaturityDate, LienAmount
            FROM loan.LoanCollateralDeposit WHERE LoanId = @id ORDER BY CollateralId;
            """, new { id, branchId }, cancellationToken: ct));

        var loan = await multi.ReadSingleOrDefaultAsync<LoanDetail>();
        if (loan is null) return null;
        loan.Guarantors = (await multi.ReadAsync<GuarantorRow>()).AsList();
        loan.Collaterals = (await multi.ReadAsync<CollateralRow>()).AsList();
        return loan;
    }

    public async Task<int?> NavigateAsync(int branchId, Navigate direction, string? fromLoanNo, CancellationToken ct)
    {
        var sql = direction switch
        {
            Navigate.First => "SELECT TOP (1) LoanId FROM loan.LoanApplication WHERE BranchId = @branchId ORDER BY LoanNo",
            Navigate.Last => "SELECT TOP (1) LoanId FROM loan.LoanApplication WHERE BranchId = @branchId ORDER BY LoanNo DESC",
            Navigate.Next => "SELECT TOP (1) LoanId FROM loan.LoanApplication WHERE BranchId = @branchId AND LoanNo > @from ORDER BY LoanNo",
            _ => "SELECT TOP (1) LoanId FROM loan.LoanApplication WHERE BranchId = @branchId AND LoanNo < @from ORDER BY LoanNo DESC"
        };
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { branchId, from = fromLoanNo ?? "" }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ScheduleRow>> ScheduleAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<ScheduleRow>(new CommandDefinition("""
            SELECT s.InstallmentNo, s.DueDate, s.EmiAmount, s.PrincipalAmount, s.InterestAmount, s.PrincipalPaid, s.InterestPaid, s.Status
            FROM loan.EmiSchedule s JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
            WHERE s.LoanId = @id AND l.BranchId = @branchId
            ORDER BY s.InstallmentNo
            """, new { id, branchId }, cancellationToken: ct))).AsList();
    }

    public async Task<int> CreateAsync(int branchId, int userId, LoanInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        input.ApplicationDate ??= businessDate;
        await using var c = await db.OpenAsync(ct);
        var refs = await ValidateAsync(c, branchId, input, today, ct);
        var t = await ComputeTermsAsync(input, refs.Gender, ct);

        await using var tx = c.BeginTransaction();
        var loanNo = await SqlGuard.NextNumberAsync(c, tx, "LOAN", branchId, ct);
        var id = await c.ExecuteScalarAsync<int>(new CommandDefinition($"""
            INSERT loan.LoanApplication ({InsertColumns}, LoanNo, BranchId, CreatedBy)
            OUTPUT INSERTED.LoanId
            VALUES ({string.Join(", ", InsertColumns.Split(", ").Select(col => "@" + col))}, @LoanNo, @BranchId, @CreatedBy)
            """, Parameters(input, refs, t, new { LoanNo = loanNo, BranchId = branchId, CreatedBy = userId }), tx, cancellationToken: ct));
        await ReplaceChildrenAsync(c, tx, id, input, refs, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, ActionBy)
            VALUES (@id, 'CREATE', NULL, 'PENDING', @date, @userId)
            """, new { id, date = input.ApplicationDate, userId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>Only pending applications can be edited.</summary>
    public async Task<bool> UpdateAsync(int branchId, int userId, int id, LoanInput input, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        await using var c = await db.OpenAsync(ct);
        var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT Status FROM loan.LoanApplication WHERE LoanId = @id AND BranchId = @branchId", new { id, branchId }, cancellationToken: ct));
        if (current is null) return false;
        if (current != "PENDING") throw new DomainException($"Only pending applications can be changed (this loan is {current}).", 409);
        if (input.ApplicationDate is null) throw new ValidationFailedException(new Dictionary<string, string[]> { ["applicationDate"] = ["Required."] });

        var refs = await ValidateAsync(c, branchId, input, today, ct);
        var t = await ComputeTermsAsync(input, refs.Gender, ct);

        await using var tx = c.BeginTransaction();
        var set = string.Join(", ", InsertColumns.Split(", ").Select(col => $"{col} = @{col}"));
        var updated = await c.ExecuteAsync(new CommandDefinition($"""
            UPDATE loan.LoanApplication SET {set}, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
            WHERE LoanId = @Id AND BranchId = @BranchId AND Status = 'PENDING'
            """, Parameters(input, refs, t, new { Id = id, BranchId = branchId, ModifiedBy = userId }), tx, cancellationToken: ct));
        if (updated == 0) throw new DomainException("The application was approved or changed by someone else. Reload it.", 409);
        await ReplaceChildrenAsync(c, tx, id, input, refs, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var status = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT Status FROM loan.LoanApplication WITH (UPDLOCK) WHERE LoanId = @id AND BranchId = @branchId",
            new { id, branchId }, tx, cancellationToken: ct));
        if (status is null) return false;
        if (status is not ("PENDING" or "REJECTED"))
            throw new DomainException("Approved loans cannot be deleted. Dis-approve it first if no EMI has been received.", 409);
        await c.ExecuteAsync(new CommandDefinition("""
            DELETE FROM loan.LoanGuarantor WHERE LoanId = @id;
            DELETE FROM loan.LoanCollateralDeposit WHERE LoanId = @id;
            DELETE FROM loan.LoanStatusHistory WHERE LoanId = @id;
            DELETE FROM loan.LoanApplication WHERE LoanId = @id;
            """, new { id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public Task<LoanTerms> PreviewAsync(LoanTermsRequest request, DateOnly onDate, CancellationToken ct) =>
        terms.ComputeAsync(request, onDate, ct);

    // ---------------------------------------------------------------------------------------

    private const string InsertColumns =
        "ApplicationDate, MemberId, PlanId, EmiMode, LoanAmount, InterestRate, TenureYears, TenureMonths, NoOfInstallments, " +
        "EmiAmount, NoOfUnits, EmiStartDate, DisbursementMode, ChequeNo, ChequeAmount, DisbursementLedgerId, Remark, " +
        "EmployeeId, GroupId, ProcessingFeePct, ProcessingFee, GstAmount, ShareAmount, InsuranceAmount, SavingAccountNo, " +
        "IsSavingTransfer, EmiCalcMethod, EmiFactor, IsEmiOverridden";

    private sealed class Refs
    {
        public int? MemberId { get; init; }
        public string Gender { get; init; } = "M";
        public bool MemberActive { get; init; }
        public int? EmployeeId { get; init; }
        public bool EmployeeActive { get; init; }
        public bool PlanActive { get; init; }
        public int GroupOk { get; init; }
        public int LedgerOk { get; init; }
        public string GuarantorMemberNos { get; init; } = "";
    }

    private async Task<LoanTerms> ComputeTermsAsync(LoanInput input, string gender, CancellationToken ct) =>
        await terms.ComputeAsync(new LoanTermsRequest
        {
            PlanId = input.PlanId,
            Amount = input.LoanAmount,
            Gender = gender,
            Rate = input.InterestRate,
            Emi = input.EmiAmount,
            EmiStartDate = input.EmiStartDate
        }, input.ApplicationDate!.Value, ct);

    private static DynamicParameters Parameters(LoanInput i, Refs refs, LoanTerms t, object extra)
    {
        var fee = i.ProcessingFee ?? t.ProcessingFee;
        var net = i.LoanAmount - fee - i.GstAmount - i.ShareAmount - i.InsuranceAmount;
        var p = new DynamicParameters(new
        {
            i.ApplicationDate,
            refs.MemberId,
            i.PlanId,
            t.EmiMode,
            i.LoanAmount,
            InterestRate = t.Rate,
            TenureYears = (short)(t.TenureMonths / 12),
            TenureMonths = (short)(t.TenureMonths % 12),
            NoOfInstallments = (short)t.Installments,
            EmiAmount = t.Emi,
            i.NoOfUnits,
            EmiStartDate = t.FirstDueDate,
            i.DisbursementMode,
            i.ChequeNo,
            ChequeAmount = i.DisbursementMode == "CASH" ? (decimal?)null : net,
            i.DisbursementLedgerId,
            i.Remark,
            refs.EmployeeId,
            i.GroupId,
            ProcessingFeePct = i.ProcessingFeePct ?? t.ProcessingFeePct,
            ProcessingFee = fee,
            i.GstAmount,
            i.ShareAmount,
            i.InsuranceAmount,
            i.SavingAccountNo,
            IsSavingTransfer = false,
            EmiCalcMethod = t.Method,
            EmiFactor = t.Factor,
            IsEmiOverridden = t.IsEmiOverridden
        });
        p.AddDynamicParams(extra);
        return p;
    }

    private static async Task ReplaceChildrenAsync(SqlConnection c, IDbTransaction tx, int loanId, LoanInput input, Refs refs, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM loan.LoanGuarantor WHERE LoanId = @loanId; DELETE FROM loan.LoanCollateralDeposit WHERE LoanId = @loanId;",
            new { loanId }, tx, cancellationToken: ct));
        if (input.Guarantors.Count > 0)
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT loan.LoanGuarantor (LoanId, MemberId, GuarantorName, GuardianName, AddressLine, MobileNo, RelationId, IdProofNo)
                VALUES (@loanId, (SELECT MemberId FROM loan.Member WHERE MemberNo = @MemberNo), @GuarantorName, @GuardianName,
                        @AddressLine, @MobileNo, @RelationId, @IdProofNo)
                """, input.Guarantors.Select(g => new { loanId, g.MemberNo, g.GuarantorName, g.GuardianName, g.AddressLine, g.MobileNo, g.RelationId, g.IdProofNo }),
                tx, cancellationToken: ct));
        if (input.Collaterals.Count > 0)
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT loan.LoanCollateralDeposit (LoanId, DepositAccountNo, DepositAmount, MaturityDate, LienAmount)
                VALUES (@loanId, @DepositAccountNo, @DepositAmount, @MaturityDate, @LienAmount)
                """, input.Collaterals.Select(x => new { loanId, x.DepositAccountNo, x.DepositAmount, x.MaturityDate, x.LienAmount }),
                tx, cancellationToken: ct));
    }

    private static void Normalize(LoanInput i)
    {
        i.MemberNo = Text.Clean(i.MemberNo, upper: true);
        i.EmployeeCode = Text.Clean(i.EmployeeCode, upper: true);
        i.DisbursementMode = Text.Clean(i.DisbursementMode, upper: true) ?? "CASH";
        i.ChequeNo = Text.Clean(i.ChequeNo, upper: true);
        i.Remark = Text.Clean(i.Remark);
        i.SavingAccountNo = Text.Clean(i.SavingAccountNo, upper: true);
        if (i.NoOfUnits <= 0) i.NoOfUnits = 1;
        foreach (var g in i.Guarantors)
        {
            g.MemberNo = Text.Clean(g.MemberNo, upper: true);
            g.GuarantorName = Text.Clean(g.GuarantorName, upper: true);
            g.GuardianName = Text.Clean(g.GuardianName, upper: true);
            g.AddressLine = Text.Clean(g.AddressLine, upper: true);
            g.MobileNo = Text.Clean(g.MobileNo);
            g.IdProofNo = Text.Clean(g.IdProofNo, upper: true);
        }
        foreach (var x in i.Collaterals) x.DepositAccountNo = Text.Clean(x.DepositAccountNo, upper: true);
    }

    private static async Task<Refs> ValidateAsync(SqlConnection c, int branchId, LoanInput i, DateOnly today, CancellationToken ct)
    {
        var v = new FieldValidator();
        v.Required(i.MemberNo, "memberNo");
        v.Required(i.EmployeeCode, "employeeCode");
        if (i.PlanId <= 0) v.Add("planId", "Required.");
        if (i.LoanAmount <= 0) v.Add("loanAmount", "Enter the loan amount.");
        if (i.InterestRate is < 0 or > 100) v.Add("interestRate", "Rate must be between 0 and 100.");
        v.OneOf(i.DisbursementMode, DisbursementModes, "disbursementMode");
        if (i.DisbursementMode is "CHEQUE" && i.ChequeNo is null) v.Add("chequeNo", "Cheque number is required.");
        v.MaxLength(i.ChequeNo, 30, "chequeNo");
        v.MaxLength(i.Remark, 500, "remark");
        v.MaxLength(i.SavingAccountNo, 30, "savingAccountNo");
        if (i.ApplicationDate > today) v.Add("applicationDate", "Cannot be in the future.");
        if (i.EmiStartDate is { } start && i.ApplicationDate is { } app && start <= app) v.Add("emiStartDate", "First EMI must be after the application date.");
        if (i.ProcessingFeePct is < 0 or > 100) v.Add("processingFeePct", "Must be between 0 and 100.");
        var fee = i.ProcessingFee ?? 0;
        if (fee < 0 || i.GstAmount < 0 || i.ShareAmount < 0 || i.InsuranceAmount < 0) v.Add("processingFee", "Deductions cannot be negative.");
        else if (fee + i.GstAmount + i.ShareAmount + i.InsuranceAmount >= i.LoanAmount && i.LoanAmount > 0)
            v.Add("processingFee", "Deductions must be less than the loan amount.");

        for (var k = 0; k < i.Guarantors.Count; k++)
        {
            v.Required(i.Guarantors[k].GuarantorName, $"guarantors[{k}].guarantorName");
            v.Pattern(i.Guarantors[k].MobileNo, @"^[6-9]\d{9}$", $"guarantors[{k}].mobileNo", "Enter a 10-digit mobile number.");
        }
        for (var k = 0; k < i.Collaterals.Count; k++)
        {
            v.Required(i.Collaterals[k].DepositAccountNo, $"collaterals[{k}].depositAccountNo");
            if (i.Collaterals[k].DepositAmount <= 0) v.Add($"collaterals[{k}].depositAmount", "Enter the deposit amount.");
        }
        v.ThrowIfAny();

        var guarantorNos = i.Guarantors.Select(g => g.MemberNo).Where(n => n is not null).Distinct().ToArray();
        var refs = await c.QuerySingleAsync<Refs>(new CommandDefinition("""
            SELECT m.MemberId, ISNULL(m.Gender, 'M') AS Gender, ISNULL(m.IsActive, 0) AS MemberActive,
                   e.EmployeeId, ISNULL(e.IsActive, 0) AS EmployeeActive,
                   ISNULL((SELECT IsActive FROM mst.LoanPlan WHERE PlanId = @PlanId), 0) AS PlanActive,
                   CASE WHEN @GroupId IS NULL OR EXISTS (SELECT 1 FROM mst.MemberGroup WHERE GroupId = @GroupId AND BranchId = @branchId) THEN 1 ELSE 0 END AS GroupOk,
                   CASE WHEN @DisbursementLedgerId IS NULL OR EXISTS (SELECT 1 FROM acc.Ledger WHERE LedgerId = @DisbursementLedgerId
                        AND IsGroup = 0 AND IsActive = 1 AND CashBankType IN ('C','B')) THEN 1 ELSE 0 END AS LedgerOk,
                   ISNULL(STUFF((SELECT ',' + gm.MemberNo FROM loan.Member gm WHERE gm.MemberNo IN @guarantorNos FOR XML PATH('')), 1, 1, ''), '') AS GuarantorMemberNos
            FROM (SELECT 1 AS x) d
            LEFT JOIN loan.Member m ON m.MemberNo = @MemberNo AND m.BranchId = @branchId
            LEFT JOIN emp.Employee e ON e.EmployeeCode = @EmployeeCode
            """, new { i.MemberNo, i.EmployeeCode, i.PlanId, i.GroupId, i.DisbursementLedgerId, branchId, guarantorNos }, cancellationToken: ct));

        if (refs.MemberId is null) v.Add("memberNo", "Member not found in this branch.");
        else if (!refs.MemberActive) v.Add("memberNo", "Member is inactive.");
        if (refs.EmployeeId is null) v.Add("employeeCode", "Employee code not found.");
        else if (!refs.EmployeeActive) v.Add("employeeCode", "Employee is inactive.");
        if (!refs.PlanActive) v.Add("planId", "Select an active loan plan.");
        if (refs.GroupOk == 0) v.Add("groupId", "Group does not belong to this branch.");
        if (refs.LedgerOk == 0) v.Add("disbursementLedgerId", "Select a cash or bank ledger.");
        var found = refs.GuarantorMemberNos.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        for (var k = 0; k < i.Guarantors.Count; k++)
        {
            if (i.Guarantors[k].MemberNo is { } no && !found.Contains(no)) v.Add($"guarantors[{k}].memberNo", "Member not found.");
            if (i.Guarantors[k].MemberNo == i.MemberNo && i.MemberNo is not null) v.Add($"guarantors[{k}].memberNo", "The borrower cannot be a guarantor.");
        }
        v.ThrowIfAny();
        return refs;
    }
}
