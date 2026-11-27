using System.Data;
using Dapper;
using DhanJagrati.Core.Loans;
using DhanJagrati.Data.Accounts;
using DhanJagrati.Data.Savings;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Recovery;

public sealed class RecoveryInput
{
    public int LoanId { get; set; }
    public DateOnly? TransDate { get; set; }
    /// <summary>Amount towards EMIs (principal + interest).</summary>
    public decimal Amount { get; set; }
    public decimal LateFee { get; set; }
    public decimal ChequeBounceCharges { get; set; }
    public string? PaymentMode { get; set; }
    public int? LedgerId { get; set; }
    public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public string? BankName { get; set; }
    public string? RefTransNo { get; set; }
    public string? Remarks { get; set; }
    public string? EmployeeCode { get; set; }
    public string? SavingAccountNo { get; set; }
}

public sealed class RecoveryPrefill
{
    public required LoanHeader Loan { get; init; }
    public int? NextInstallmentNo { get; init; }
    public DateOnly? NextDueDate { get; init; }
    public DateOnly? FollowingDueDate { get; init; }
    public decimal NextInstallmentBalance { get; init; }
    public int OverdueCount { get; init; }
    public decimal OverdueAmount { get; init; }
    public decimal LateFeeSuggested { get; init; }
    public decimal TotalOutstanding { get; init; }
    public int PaidInstallments { get; init; }
    public required IReadOnlyList<ScheduleState> Schedule { get; init; }
}

public sealed record ReceiptLine(int InstallmentNo, string TransNo, decimal Principal, decimal Interest, decimal LateFee);

public sealed record RecoveryReceipt(
    string ReceiptNo, string VoucherNo, DateOnly TransDate, int LoanId, string LoanNo, string MemberName, string PaymentMode,
    IReadOnlyList<ReceiptLine> Lines, decimal BounceCharges, decimal Total, bool LoanClosed, decimal? SavingBalance);

public sealed class ReceiptHistoryItem
{
    public string ReceiptNo { get; init; } = "";
    public DateOnly TransDate { get; init; }
    public string PaymentMode { get; init; } = "";
    public string Installments { get; init; } = "";
    public decimal Principal { get; init; }
    public decimal Interest { get; init; }
    public decimal LateFee { get; init; }
    public decimal BounceCharges { get; init; }
    public decimal Total { get; init; }
    public bool IsReversed { get; init; }
    public string? VoucherNo { get; init; }
    public bool IsBulk { get; init; }
}

public sealed class BulkDueItem
{
    public int LoanId { get; init; }
    public string LoanNo { get; init; } = "";
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? SavingAccountNo { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string? GroupName { get; init; }
    public decimal EmiAmount { get; init; }
    public short FirstInstallmentNo { get; init; }
    public DateOnly FirstDueDate { get; init; }
    public int DueCount { get; init; }
    public decimal DueAmount { get; init; }
}

public sealed class BulkRecoveryInput
{
    public DateOnly? PaidDate { get; set; }
    public string? PaymentMode { get; set; }
    public int? LedgerId { get; set; }
    public string? EmployeeCode { get; set; }
    public int? GroupId { get; set; }
    public DateOnly DueFrom { get; set; }
    public DateOnly DueTo { get; set; }
    public List<BulkItem> Items { get; set; } = [];
}

public sealed class BulkItem
{
    public int LoanId { get; set; }
    public decimal Amount { get; set; }
}

public sealed record BulkResult(int LoanId, string? LoanNo, bool Success, string Message, string? ReceiptNo);

/// <summary>LOAN MASTER > Loan Recovery (single), Loan Bulk Recovery and receipt reversal.</summary>
public sealed class RecoveryService(IDbConnectionFactory db)
{
    private static readonly string[] Modes = ["CASH", "CHEQUE", "NEFT", "RTGS", "IMPS", "UPI", "SAVING"];

    public async Task<RecoveryPrefill?> PrefillAsync(int branchId, int? loanId, string? loanNo, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var position = await LoanPosition.LoadAsync(c, null, branchId, loanId, loanNo, forUpdate: false, ct);
        if (position is null) return null;
        var (loan, schedule) = position.Value;
        var positions = schedule.Select(s => s.ToPosition()).ToList();
        var open = positions.Where(p => p.IsOpen).ToList();
        var (enabled, grace) = await LoanPosition.LateFeeSettingsAsync(c, null, ct);
        var owed = enabled ? RecoveryMath.LateFeeOwed(positions, date, grace, loan.LateFeePerInstallment) : new Dictionary<int, decimal>();

        return new RecoveryPrefill
        {
            Loan = loan,
            NextInstallmentNo = open.FirstOrDefault()?.InstallmentNo,
            NextDueDate = open.FirstOrDefault()?.DueDate,
            FollowingDueDate = open.Skip(1).FirstOrDefault()?.DueDate,
            NextInstallmentBalance = open.FirstOrDefault()?.BalanceDue ?? 0,
            OverdueCount = open.Count(p => p.DueDate <= date),
            OverdueAmount = open.Where(p => p.DueDate <= date).Sum(p => p.BalanceDue),
            LateFeeSuggested = owed.Values.Sum(),
            TotalOutstanding = open.Sum(p => p.BalanceDue),
            PaidInstallments = positions.Count(p => !p.IsOpen),
            Schedule = schedule
        };
    }

    public async Task<IReadOnlyList<ReceiptHistoryItem>> HistoryAsync(int branchId, int loanId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<ReceiptHistoryItem>(new CommandDefinition("""
            SELECT r.ReceiptNo, MIN(r.TransDate) AS TransDate, MIN(r.PaymentMode) AS PaymentMode,
                   STRING_AGG(CAST(r.InstallmentNo AS VARCHAR(10)), ', ') WITHIN GROUP (ORDER BY r.InstallmentNo) AS Installments,
                   SUM(r.PrincipalAmount) AS Principal, SUM(r.InterestAmount) AS Interest, SUM(r.LateFeePaid) AS LateFee,
                   SUM(r.ChequeBounceCharges) AS BounceCharges, SUM(r.NetAmount) AS Total,
                   CAST(MAX(CAST(r.IsReversed AS INT)) AS BIT) AS IsReversed, MIN(v.VoucherNo) AS VoucherNo,
                   CAST(MAX(CASE WHEN r.BulkBatchId IS NULL THEN 0 ELSE 1 END) AS BIT) AS IsBulk
            FROM loan.LoanRecovery r
            JOIN loan.LoanApplication l ON l.LoanId = r.LoanId
            LEFT JOIN acc.Voucher v ON v.VoucherId = r.VoucherId
            WHERE r.LoanId = @loanId AND l.BranchId = @branchId
            GROUP BY r.ReceiptNo
            ORDER BY MIN(r.RecoveryId) DESC
            """, new { loanId, branchId }, cancellationToken: ct))).AsList();
    }

    public async Task<RecoveryReceipt> PostAsync(int branchId, int userId, RecoveryInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var receipt = await PostCoreAsync(c, tx, branchId, userId, input, input.TransDate ?? businessDate, today, null, ct);
        await tx.CommitAsync(ct);
        return receipt;
    }

    private static async Task<RecoveryReceipt> PostCoreAsync(SqlConnection c, IDbTransaction tx, int branchId, int userId, RecoveryInput input,
        DateOnly date, DateOnly today, int? batchId, CancellationToken ct)
    {
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.ChequeNo = Text.Clean(input.ChequeNo, upper: true);
        input.BankName = Text.Clean(input.BankName, upper: true);
        input.RefTransNo = Text.Clean(input.RefTransNo, upper: true);
        input.Remarks = Text.Clean(input.Remarks);
        input.EmployeeCode = Text.Clean(input.EmployeeCode, upper: true);
        input.SavingAccountNo = Text.Clean(input.SavingAccountNo, upper: true);

        var v = new FieldValidator();
        v.OneOf(input.PaymentMode, Modes, "paymentMode");
        if (input.Amount < 0 || input.LateFee < 0 || input.ChequeBounceCharges < 0) v.Add("amount", "Amounts cannot be negative.");
        if (input.Amount + input.LateFee + input.ChequeBounceCharges <= 0) v.Add("amount", "Enter the amount received.");
        if (date > today) v.Add("transDate", "Cannot be in the future.");
        if (input.PaymentMode == "CHEQUE" && input.ChequeNo is null) v.Add("chequeNo", "Cheque number is required.");
        v.MaxLength(input.ChequeNo, 30, "chequeNo");
        v.MaxLength(input.BankName, 120, "bankName");
        v.MaxLength(input.RefTransNo, 40, "refTransNo");
        v.MaxLength(input.Remarks, 300, "remarks");
        v.ThrowIfAny();

        var position = await LoanPosition.LoadAsync(c, tx, branchId, input.LoanId, null, forUpdate: true, ct)
                       ?? throw new DomainException("Loan not found in this branch.", 404);
        var (loan, schedule) = position;
        if (loan.Status != "APPROVED") throw new DomainException($"Loan {loan.LoanNo} is {loan.Status}; EMIs can only be received on approved loans.", 409);
        if (loan.DisbursedOn is { } disbursed && date < disbursed) throw new DomainException("Recovery date is before the disbursement date.", 400);

        var positions = schedule.Select(s => s.ToPosition()).ToList();
        var (enabled, grace) = await LoanPosition.LateFeeSettingsAsync(c, tx, ct);
        var owed = enabled ? RecoveryMath.LateFeeOwed(positions, date, grace, loan.LateFeePerInstallment) : new Dictionary<int, decimal>();

        IReadOnlyList<Allocation> allocations;
        try
        {
            allocations = RecoveryMath.Allocate(positions, input.Amount, input.LateFee, owed);
        }
        catch (EmiCalculationException ex)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["amount"] = [ex.Message] });
        }
        if (allocations.Count == 0)
        {
            var first = positions.FirstOrDefault(p => p.IsOpen) ?? throw new DomainException("The loan has no outstanding EMI.", 409);
            allocations = [new Allocation(first.InstallmentNo, 0, 0, 0)];
        }

        var principal = allocations.Sum(a => a.Principal);
        var interest = allocations.Sum(a => a.Interest);
        var lateFee = allocations.Sum(a => a.LateFee);
        var total = principal + interest + lateFee + input.ChequeBounceCharges;

        // Where the money comes from
        var map = await Posting.MappedLedgersAsync(c, tx, ct);
        int receiptLedger;
        int? savingAccountId = null;
        if (input.PaymentMode == "SAVING")
        {
            var accountNo = input.SavingAccountNo ?? loan.SavingAccountNo
                            ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["savingAccountNo"] = ["Enter the saving account to debit."] });
            var account = await c.QuerySingleOrDefaultAsync<SavingDebitAccount>(new CommandDefinition("""
                SELECT a.AccountId, p.DepositLedgerId FROM sav.SavingAccount a JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
                JOIN loan.LoanApplication l ON l.MemberId = a.MemberId
                WHERE a.AccountNo = @accountNo AND a.BranchId = @branchId AND l.LoanId = @LoanId
                """, new { accountNo, branchId, loan.LoanId }, tx, cancellationToken: ct));
            if (account is null)
                throw new ValidationFailedException(new Dictionary<string, string[]> { ["savingAccountNo"] = ["Saving account of this member not found in the branch."] });
            savingAccountId = account.AccountId;
            receiptLedger = account.DepositLedgerId;
        }
        else
        {
            receiptLedger = input.LedgerId ?? map.Ledger("CASH");
            var ok = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM acc.Ledger WHERE LedgerId = @receiptLedger AND IsGroup = 0 AND IsActive = 1 AND CashBankType IN ('C','B')",
                new { receiptLedger }, tx, cancellationToken: ct));
            if (ok == 0) throw new ValidationFailedException(new Dictionary<string, string[]> { ["ledgerId"] = ["Select a cash or bank ledger."] });
        }

        var employeeId = loan.EmployeeId;
        if (input.EmployeeCode is not null)
            employeeId = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT EmployeeId FROM emp.Employee WHERE EmployeeCode = @EmployeeCode", new { input.EmployeeCode }, tx, cancellationToken: ct))
                ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["employeeCode"] = ["Employee code not found."] });

        var receiptNo = await SqlGuard.NextNumberAsync(c, tx, "RECEIPT", 0, ct);
        var narration = $"EMI RECEIVED {loan.LoanNo} - {loan.MemberName} (RCPT {receiptNo})";
        var (voucherId, voucherNo) = await Posting.PostAsync(c, tx, new VoucherHeader
        {
            Date = date,
            BranchId = branchId,
            VoucherType = "LOAN_RECOVERY",
            PaymentMode = input.PaymentMode,
            ChequeNo = input.ChequeNo,
            ChequeDate = input.ChequeNo is null ? null : input.ChequeDate ?? date,
            PayeeName = loan.MemberName,
            Narration = narration,
            SourceModule = "RECOVERY",
            SourceId = loan.LoanId,
            UserId = userId
        },
        [
            new PostingLine(receiptLedger, branchId, total, 0, narration),
            new PostingLine(map.Ledger("LOAN_PORTFOLIO"), branchId, 0, principal, narration),
            new PostingLine(map.Ledger("LOAN_INTEREST"), branchId, 0, interest, narration),
            new PostingLine(map.Ledger("LATE_FEE"), branchId, 0, lateFee, narration),
            new PostingLine(map.Ledger("BOUNCE_CHARGES"), branchId, 0, input.ChequeBounceCharges, narration)
        ], ct);

        long? savingTxnId = null;
        decimal? savingBalance = null;
        if (savingAccountId is { } accountId)
        {
            var txn = await SavingRepository.PostTxnAsync(c, tx, accountId, branchId, date, 'D', total, "LOAN_RECOVERY", "TRANSFER",
                null, narration, "RECOVERY", loan.LoanId, voucherId, userId, ct);
            savingTxnId = txn.TxnId;
            savingBalance = txn.BalanceAfter;
        }

        var lines = new List<ReceiptLine>();
        for (var k = 0; k < allocations.Count; k++)
        {
            var a = allocations[k];
            var bounce = k == 0 ? input.ChequeBounceCharges : 0;
            var transNo = await SqlGuard.NextNumberAsync(c, tx, "RECOVERY", 0, ct);
            var installment = schedule.First(s => s.InstallmentNo == a.InstallmentNo);
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT loan.LoanRecovery (TransNo, TransDate, BranchId, LoanId, InstallmentNo, EmiAmount, PrincipalAmount, InterestAmount,
                    PreviousLateFee, CurrentLateFee, LateFeePaid, ChequeBounceCharges, NetAmount, PaymentMode, RefTransNo, ChequeNo, ChequeDate,
                    BankName, ReceiptNo, Remarks, EmployeeId, BulkBatchId, VoucherId, SavingTxnId, CreatedBy)
                VALUES (@transNo, @date, @branchId, @LoanId, @InstallmentNo, @EmiAmount, @Principal, @Interest,
                    0, @currentLateFee, @LateFee, @bounce, @net, @PaymentMode, @RefTransNo, @ChequeNo, @chequeDate,
                    @BankName, @receiptNo, @Remarks, @employeeId, @batchId, @voucherId, @savingTxnId, @userId)
                """, new
            {
                transNo, date, branchId, loan.LoanId, InstallmentNo = (short)a.InstallmentNo, installment.EmiAmount, a.Principal, a.Interest,
                currentLateFee = owed.TryGetValue(a.InstallmentNo, out var o) ? o : 0m, a.LateFee, bounce, net = a.Total + bounce,
                input.PaymentMode, input.RefTransNo, input.ChequeNo, chequeDate = input.ChequeNo is null ? (DateOnly?)null : input.ChequeDate ?? date,
                input.BankName, receiptNo, input.Remarks, employeeId, batchId, voucherId, savingTxnId, userId
            }, tx, cancellationToken: ct));
            lines.Add(new ReceiptLine(a.InstallmentNo, transNo, a.Principal, a.Interest, a.LateFee));
        }

        await LoanPosition.ApplyToScheduleAsync(c, tx, loan.LoanId, allocations.Select(a => (a.InstallmentNo, a.Principal, a.Interest, a.LateFee)), date, ct);

        // Fully repaid -> close the loan
        var openLeft = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM loan.EmiSchedule WHERE LoanId = @LoanId AND Status IN ('DUE','PARTIAL')", new { loan.LoanId }, tx, cancellationToken: ct));
        if (openLeft == 0)
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE loan.LoanApplication SET Status = 'CLOSED', ClosedOn = @date, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId WHERE LoanId = @LoanId;
                INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
                VALUES (@LoanId, 'CLOSE', 'APPROVED', 'CLOSED', @date, @remarks, @userId);
                """, new { loan.LoanId, date, userId, remarks = $"Fully repaid (receipt {receiptNo})" }, tx, cancellationToken: ct));

        return new RecoveryReceipt(receiptNo, voucherNo, date, loan.LoanId, loan.LoanNo, loan.MemberName, input.PaymentMode, lines,
            input.ChequeBounceCharges, total, openLeft == 0, savingBalance);
    }

    /// <summary>Reverses every line of a receipt: schedule restored, voucher cancelled, saving account re-credited.</summary>
    public async Task<string> ReverseAsync(int branchId, int userId, string receiptNo, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var rows = (await c.QueryAsync<ReversalRow>(new CommandDefinition("""
            SELECT r.RecoveryId, r.LoanId, r.InstallmentNo, r.PrincipalAmount, r.InterestAmount, r.LateFeePaid, r.NetAmount,
                   r.VoucherId, r.SavingTxnId, l.Status AS LoanStatus, l.LoanNo
            FROM loan.LoanRecovery r WITH (UPDLOCK, ROWLOCK)
            JOIN loan.LoanApplication l ON l.LoanId = r.LoanId
            WHERE r.ReceiptNo = @receiptNo AND r.IsReversed = 0 AND l.BranchId = @branchId
            """, new { receiptNo, branchId }, tx, cancellationToken: ct))).AsList();
        if (rows.Count == 0) throw new DomainException("Receipt not found (or already reversed) in this branch.", 404);

        var loanId = rows[0].LoanId;
        var settled = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM loan.LoanSettlement s
            WHERE s.LoanId = @loanId AND s.ActionType = 'CLOSE'
              AND NOT EXISTS (SELECT 1 FROM loan.LoanSettlement r WHERE r.LoanId = s.LoanId AND r.ActionType = 'REOPEN' AND r.SettlementId > s.SettlementId)
            """, new { loanId }, tx, cancellationToken: ct));
        if (settled > 0) throw new DomainException("The loan is closed by settlement; re-open it before reversing receipts.", 409);

        if (rows[0].VoucherId is { } voucherId) await Posting.CancelAsync(c, tx, voucherId, userId, ct);

        await LoanPosition.ApplyToScheduleAsync(c, tx, loanId, rows.Select(r => ((int)r.InstallmentNo, -r.PrincipalAmount, -r.InterestAmount, -r.LateFeePaid)), null, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE loan.LoanRecovery SET IsReversed = 1 WHERE ReceiptNo = @receiptNo",
            new { receiptNo }, tx, cancellationToken: ct));

        var savingTxnId = rows.Select(r => r.SavingTxnId).FirstOrDefault(id => id is not null);
        if (savingTxnId is not null)
        {
            var accountId = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT AccountId FROM sav.SavingTransaction WHERE TxnId = @savingTxnId", new { savingTxnId }, tx, cancellationToken: ct));
            await SavingRepository.PostTxnAsync(c, tx, accountId, branchId, date, 'C', rows.Sum(r => r.NetAmount), "REVERSAL", "SYSTEM", null,
                $"REVERSAL OF RECEIPT {receiptNo}", "RECOVERY", loanId, null, userId, ct);
            await c.ExecuteAsync(new CommandDefinition("UPDATE sav.SavingTransaction SET IsReversed = 1 WHERE TxnId = @savingTxnId",
                new { savingTxnId }, tx, cancellationToken: ct));
        }

        if (rows[0].LoanStatus == "CLOSED")
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE loan.LoanApplication SET Status = 'APPROVED', ClosedOn = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId WHERE LoanId = @loanId;
                INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
                VALUES (@loanId, 'REOPEN', 'CLOSED', 'APPROVED', @date, @remarks, @userId);
                """, new { loanId, date, userId, remarks = $"Receipt {receiptNo} reversed" }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return rows[0].LoanNo;
    }

    // ---- Bulk recovery ----

    public async Task<IReadOnlyList<BulkDueItem>> BulkDueAsync(int branchId, string? employeeCode, int? groupId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<BulkDueItem>(new CommandDefinition("""
            SELECT l.LoanId, l.LoanNo, m.MemberNo, m.MemberName, sa.AccountNo AS SavingAccountNo, e.EmployeeCode, e.EmployeeName, g.GroupName,
                   l.EmiAmount, MIN(s.InstallmentNo) AS FirstInstallmentNo, MIN(s.DueDate) AS FirstDueDate, COUNT(*) AS DueCount,
                   SUM(s.EmiAmount - s.PrincipalPaid - s.InterestPaid) AS DueAmount
            FROM loan.EmiSchedule s
            JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
            LEFT JOIN mst.MemberGroup g ON g.GroupId = l.GroupId
            LEFT JOIN sav.SavingAccount sa ON sa.AccountId = l.SavingAccountId
            WHERE l.BranchId = @branchId AND l.Status = 'APPROVED' AND s.Status IN ('DUE','PARTIAL')
              AND s.DueDate BETWEEN @from AND @to
              AND (@employeeCode IS NULL OR e.EmployeeCode = @employeeCode)
              AND (@groupId IS NULL OR l.GroupId = @groupId)
            GROUP BY l.LoanId, l.LoanNo, m.MemberNo, m.MemberName, sa.AccountNo, e.EmployeeCode, e.EmployeeName, g.GroupName, l.EmiAmount
            ORDER BY MIN(s.DueDate), l.LoanNo
            """, new { branchId, employeeCode = Text.Clean(employeeCode, upper: true), groupId, from, to }, cancellationToken: ct))).AsList();
    }

    /// <summary>One batch row, then each loan in its own transaction (a failure does not stop the others).</summary>
    public async Task<IReadOnlyList<BulkResult>> BulkPostAsync(int branchId, int userId, BulkRecoveryInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.EmployeeCode = Text.Clean(input.EmployeeCode, upper: true);
        var date = input.PaidDate ?? businessDate;
        var v = new FieldValidator();
        v.OneOf(input.PaymentMode, ["CASH", "SAVING"], "paymentMode");
        if (input.Items.Count == 0) v.Add("items", "Select at least one loan.");
        if (input.Items.Any(i => i.Amount <= 0)) v.Add("items", "Every selected loan needs an amount.");
        if (date > today) v.Add("paidDate", "Cannot be in the future.");
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        var employeeId = input.EmployeeCode is null ? null : await c.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT EmployeeId FROM emp.Employee WHERE EmployeeCode = @EmployeeCode", new { input.EmployeeCode }, cancellationToken: ct));
        var batchId = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT loan.BulkRecoveryBatch (BranchId, PaidBranchId, EmployeeId, GroupId, DueFrom, DueTo, PaidDate, PaymentMode, SelectionMode, CreatedBy)
            OUTPUT INSERTED.BatchId
            VALUES (@branchId, @branchId, @employeeId, @GroupId, @DueFrom, @DueTo, @date, @PaymentMode, @selection, @userId)
            """, new
        {
            branchId, employeeId, input.GroupId, input.DueFrom, input.DueTo, date, input.PaymentMode, userId,
            selection = input.GroupId is not null ? "GROUP" : employeeId is not null ? "ADVISOR" : "LOAN"
        }, cancellationToken: ct));

        var results = new List<BulkResult>();
        decimal total = 0;
        foreach (var item in input.Items.GroupBy(i => i.LoanId).Select(g => new BulkItem { LoanId = g.Key, Amount = g.Sum(x => x.Amount) }))
        {
            await using var tx = c.BeginTransaction();
            try
            {
                var receipt = await PostCoreAsync(c, tx, branchId, userId, new RecoveryInput
                {
                    LoanId = item.LoanId,
                    Amount = item.Amount,
                    PaymentMode = input.PaymentMode,
                    LedgerId = input.LedgerId,
                    Remarks = $"BULK RECOVERY #{batchId}"
                }, date, today, batchId, ct);
                await tx.CommitAsync(ct);
                total += receipt.Total;
                results.Add(new BulkResult(item.LoanId, receipt.LoanNo, true, $"Receipt {receipt.ReceiptNo}", receipt.ReceiptNo));
            }
            catch (Exception ex) when (ex is DomainException or ValidationFailedException)
            {
                await tx.RollbackAsync(ct);
                var message = ex is ValidationFailedException vf ? string.Join(" ", vf.Errors.SelectMany(e => e.Value)) : ex.Message;
                results.Add(new BulkResult(item.LoanId, null, false, message, null));
            }
        }

        await c.ExecuteAsync(new CommandDefinition("UPDATE loan.BulkRecoveryBatch SET TotalAmount = @total WHERE BatchId = @batchId",
            new { total, batchId }, cancellationToken: ct));
        return results;
    }

    private sealed class SavingDebitAccount
    {
        public int AccountId { get; init; }
        public int DepositLedgerId { get; init; }
    }

    private sealed class ReversalRow
    {
        public long RecoveryId { get; init; }
        public int LoanId { get; init; }
        public short InstallmentNo { get; init; }
        public decimal PrincipalAmount { get; init; }
        public decimal InterestAmount { get; init; }
        public decimal LateFeePaid { get; init; }
        public decimal NetAmount { get; init; }
        public long? VoucherId { get; init; }
        public long? SavingTxnId { get; init; }
        public string LoanStatus { get; init; } = "";
        public string LoanNo { get; init; } = "";
    }
}
