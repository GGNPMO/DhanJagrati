using Dapper;
using DhanJagrati.Core.Loans;
using DhanJagrati.Data.Accounts;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Loans;

public sealed class ApprovalListItem
{
    public int LoanId { get; init; }
    public string LoanNo { get; init; } = "";
    public DateOnly ApplicationDate { get; init; }
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string PlanCode { get; init; } = "";
    public decimal LoanAmount { get; init; }
    public decimal InterestRate { get; init; }
    public short NoOfInstallments { get; init; }
    public decimal EmiAmount { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string Status { get; init; } = "";
    public DateOnly? ApprovedOn { get; init; }
}

public sealed record ApprovalResult(int LoanId, string? LoanNo, bool Success, string Message);

/// <summary>
/// LOAN MASTER > Loan Approve. Each loan is processed in its own transaction so one failure
/// does not block the rest of the batch.
///   Approve     : PENDING  -> APPROVED, EMI schedule generated, disbursement voucher posted.
///   Dis-approve : APPROVED -> PENDING,  only before any recovery; schedule removed, voucher cancelled.
///   Reject      : PENDING  -> REJECTED.
/// </summary>
public sealed class LoanApprovalService(IDbConnectionFactory db)
{
    public async Task<IReadOnlyList<ApprovalListItem>> ListAsync(int branchId, DateOnly from, DateOnly to, string status, string? loanNo, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<ApprovalListItem>(new CommandDefinition("loan.usp_LoanApprovalList",
            new { BranchId = branchId, FromDate = from, ToDate = to, Status = status, LoanNo = Text.Clean(loanNo, upper: true) },
            commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct))).AsList();
    }

    public Task<IReadOnlyList<ApprovalResult>> ApproveAsync(int branchId, int userId, IReadOnlyList<int> ids, DateOnly date, CancellationToken ct) =>
        ForEachAsync(ids, (c, tx, id) => ApproveOneAsync(c, tx, branchId, userId, id, date, ct), ct);

    public Task<IReadOnlyList<ApprovalResult>> DisapproveAsync(int branchId, int userId, IReadOnlyList<int> ids, DateOnly date, string? remarks, CancellationToken ct) =>
        ForEachAsync(ids, (c, tx, id) => DisapproveOneAsync(c, tx, branchId, userId, id, date, remarks, ct), ct);

    public Task<IReadOnlyList<ApprovalResult>> RejectAsync(int branchId, int userId, IReadOnlyList<int> ids, DateOnly date, string? remarks, CancellationToken ct) =>
        ForEachAsync(ids, (c, tx, id) => RejectOneAsync(c, tx, branchId, userId, id, date, remarks, ct), ct);

    private async Task<IReadOnlyList<ApprovalResult>> ForEachAsync(
        IReadOnlyList<int> ids, Func<SqlConnection, System.Data.IDbTransaction, int, Task<ApprovalResult>> action, CancellationToken ct)
    {
        var results = new List<ApprovalResult>();
        await using var c = await db.OpenAsync(ct);
        foreach (var id in ids.Distinct())
        {
            await using var tx = c.BeginTransaction();
            try
            {
                var result = await action(c, tx, id);
                if (result.Success) await tx.CommitAsync(ct); else await tx.RollbackAsync(ct);
                results.Add(result);
            }
            catch (DomainException ex)
            {
                await tx.RollbackAsync(ct);
                results.Add(new ApprovalResult(id, null, false, ex.Message));
            }
        }
        return results;
    }

    private sealed class PendingLoan
    {
        public string LoanNo { get; init; } = "";
        public string Status { get; init; } = "";
        public DateOnly ApplicationDate { get; init; }
        public string MemberName { get; init; } = "";
        public decimal LoanAmount { get; init; }
        public decimal ProcessingFee { get; init; }
        public decimal GstAmount { get; init; }
        public decimal ShareAmount { get; init; }
        public decimal InsuranceAmount { get; init; }
        public decimal NetDisbursedAmount { get; init; }
        public int? DisbursementLedgerId { get; init; }
        public string DisbursementMode { get; init; } = "";
        public string? ChequeNo { get; init; }
        public decimal EmiAmount { get; init; }
        public short NoOfInstallments { get; init; }
        public string EmiMode { get; init; } = "";
        public DateOnly EmiStartDate { get; init; }
        public string InterestSplit { get; init; } = "FLAT";
        public long? DisbursementVoucherId { get; init; }
        public int Recoveries { get; init; }
    }

    private static Task<PendingLoan?> LockAsync(SqlConnection c, System.Data.IDbTransaction tx, int branchId, int id, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<PendingLoan>(new CommandDefinition("""
            SELECT l.LoanNo, l.Status, l.ApplicationDate, m.MemberName, l.LoanAmount, l.ProcessingFee, l.GstAmount, l.ShareAmount,
                   l.InsuranceAmount, l.NetDisbursedAmount, l.DisbursementLedgerId, l.DisbursementMode, l.ChequeNo, l.EmiAmount,
                   l.NoOfInstallments, l.EmiMode, l.EmiStartDate, p.InterestSplit, l.DisbursementVoucherId,
                   (SELECT COUNT(*) FROM loan.LoanRecovery r WHERE r.LoanId = l.LoanId AND r.IsReversed = 0) AS Recoveries
            FROM loan.LoanApplication l WITH (UPDLOCK, ROWLOCK)
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN mst.LoanPlan p ON p.PlanId = l.PlanId
            WHERE l.LoanId = @id AND l.BranchId = @branchId
            """, new { id, branchId }, tx, cancellationToken: ct));

    private static async Task<ApprovalResult> ApproveOneAsync(SqlConnection c, System.Data.IDbTransaction tx, int branchId, int userId, int id, DateOnly date, CancellationToken ct)
    {
        var loan = await LockAsync(c, tx, branchId, id, ct);
        if (loan is null) return new ApprovalResult(id, null, false, "Loan not found in this branch.");
        if (loan.Status != "PENDING") return new ApprovalResult(id, loan.LoanNo, false, $"Loan is {loan.Status}, not pending.");
        if (date < loan.ApplicationDate) return new ApprovalResult(id, loan.LoanNo, false, "Approval date is before the application date.");
        if (loan.EmiStartDate <= date) return new ApprovalResult(id, loan.LoanNo, false, "First EMI date must be after the approval date; edit the application.");

        // Repayment schedule
        var schedule = EmiScheduleBuilder.Build(loan.LoanAmount, loan.EmiAmount, loan.NoOfInstallments,
            EmiCodes.ParseSplit(loan.InterestSplit), EmiCodes.ParseFrequency(loan.EmiMode), loan.EmiStartDate);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT loan.EmiSchedule (LoanId, InstallmentNo, DueDate, EmiAmount, PrincipalAmount, InterestAmount)
            VALUES (@id, @InstallmentNo, @DueDate, @EmiAmount, @PrincipalAmount, @InterestAmount)
            """, schedule.Select(s => new { id, InstallmentNo = (short)s.InstallmentNo, s.DueDate, s.EmiAmount, s.PrincipalAmount, s.InterestAmount }),
            tx, cancellationToken: ct));

        // Disbursement voucher: Dr loan portfolio; Cr cash/bank (net) and each deduction
        var map = await Posting.MappedLedgersAsync(c, tx, ct);
        var payout = loan.DisbursementLedgerId ?? map.Ledger("CASH");
        var narration = $"LOAN DISBURSED {loan.LoanNo} - {loan.MemberName}";
        var (voucherId, voucherNo) = await Posting.PostAsync(c, tx, new VoucherHeader
        {
            Date = date,
            BranchId = branchId,
            VoucherType = "LOAN_DISBURSE",
            PaymentMode = loan.DisbursementMode,
            ChequeNo = loan.ChequeNo,
            ChequeDate = loan.ChequeNo is null ? null : date,
            PayeeName = loan.MemberName,
            Narration = narration,
            SourceModule = "LOAN",
            SourceId = id,
            UserId = userId
        },
        [
            new PostingLine(map.Ledger("LOAN_PORTFOLIO"), branchId, loan.LoanAmount, 0, narration),
            new PostingLine(payout, branchId, 0, loan.NetDisbursedAmount, narration),
            new PostingLine(map.Ledger("PROCESSING_FEE"), branchId, 0, loan.ProcessingFee, $"PROCESSING FEE {loan.LoanNo}"),
            new PostingLine(map.Ledger("GST_PAYABLE"), branchId, 0, loan.GstAmount, $"GST {loan.LoanNo}"),
            new PostingLine(map.Ledger("SHARE_CAPITAL"), branchId, 0, loan.ShareAmount, $"SHARE {loan.LoanNo}"),
            new PostingLine(map.Ledger("INSURANCE"), branchId, 0, loan.InsuranceAmount, $"INSURANCE {loan.LoanNo}")
        ], ct);

        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.LoanApplication
            SET Status = 'APPROVED', ApprovedOn = @date, ApprovedBy = @userId, DisbursedOn = @date,
                DisbursementVoucherId = @voucherId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            WHERE LoanId = @id;
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
            VALUES (@id, 'APPROVE', 'PENDING', 'APPROVED', @date, @remarks, @userId);
            """, new { id, date, userId, voucherId, remarks = $"Voucher {voucherNo}" }, tx, cancellationToken: ct));

        return new ApprovalResult(id, loan.LoanNo, true, $"Approved; voucher {voucherNo}.");
    }

    private static async Task<ApprovalResult> DisapproveOneAsync(SqlConnection c, System.Data.IDbTransaction tx, int branchId, int userId, int id, DateOnly date, string? remarks, CancellationToken ct)
    {
        var loan = await LockAsync(c, tx, branchId, id, ct);
        if (loan is null) return new ApprovalResult(id, null, false, "Loan not found in this branch.");
        if (loan.Status != "APPROVED") return new ApprovalResult(id, loan.LoanNo, false, $"Loan is {loan.Status}, not approved.");
        if (loan.Recoveries > 0) return new ApprovalResult(id, loan.LoanNo, false, "EMIs have been received; it cannot be dis-approved.");

        if (loan.DisbursementVoucherId is { } voucherId) await Posting.CancelAsync(c, tx, voucherId, userId, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            DELETE FROM loan.EmiSchedule WHERE LoanId = @id;
            UPDATE loan.LoanApplication
            SET Status = 'PENDING', ApprovedOn = NULL, ApprovedBy = NULL, DisbursedOn = NULL, DisbursementVoucherId = NULL,
                ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            WHERE LoanId = @id;
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
            VALUES (@id, 'DISAPPROVE', 'APPROVED', 'PENDING', @date, @remarks, @userId);
            """, new { id, date, userId, remarks = Text.Clean(remarks) }, tx, cancellationToken: ct));
        return new ApprovalResult(id, loan.LoanNo, true, "Dis-approved; disbursement voucher cancelled.");
    }

    private static async Task<ApprovalResult> RejectOneAsync(SqlConnection c, System.Data.IDbTransaction tx, int branchId, int userId, int id, DateOnly date, string? remarks, CancellationToken ct)
    {
        var loan = await LockAsync(c, tx, branchId, id, ct);
        if (loan is null) return new ApprovalResult(id, null, false, "Loan not found in this branch.");
        if (loan.Status != "PENDING") return new ApprovalResult(id, loan.LoanNo, false, $"Loan is {loan.Status}; only pending loans can be rejected.");
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.LoanApplication SET Status = 'REJECTED', ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId WHERE LoanId = @id;
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
            VALUES (@id, 'REJECT', 'PENDING', 'REJECTED', @date, @remarks, @userId);
            """, new { id, date, userId, remarks = Text.Clean(remarks) }, tx, cancellationToken: ct));
        return new ApprovalResult(id, loan.LoanNo, true, "Rejected.");
    }
}
