using Dapper;
using DhanJagrati.Core.Loans;
using DhanJagrati.Data.Accounts;
using DhanJagrati.Data.Savings;

namespace DhanJagrati.Data.Recovery;

public sealed class SettlementView
{
    public required LoanHeader Loan { get; init; }
    public required SettlementQuote Quote { get; init; }
    public decimal SuggestedLateFee { get; init; }
    public int OpenInstallments { get; init; }
    public bool CanClose { get; init; }
    public bool CanReopen { get; init; }
    public LastSettlement? Last { get; init; }
}

public sealed class LastSettlement
{
    public int SettlementId { get; init; }
    public string ActionType { get; init; } = "";
    public DateOnly ActionDate { get; init; }
    public decimal NetCloseAmount { get; init; }
    public string? VoucherNo { get; init; }
    public string? Remarks { get; init; }
}

public sealed class SettlementInput
{
    public int LoanId { get; set; }
    public DateOnly? Date { get; set; }
    public decimal? LateFee { get; set; }
    public decimal? PreCloseCharges { get; set; }
    public string? PaymentMode { get; set; }
    public int? LedgerId { get; set; }
    public string? ChequeNo { get; set; }
    public string? SavingAccountNo { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>
/// LOAN MASTER > Loan Settlement. Close = pre-closure: remaining principal + interest due till date
/// + late fee + pre-close charges; interest of future EMIs is waived. Re-open reverses it exactly.
/// </summary>
public sealed class SettlementService(IDbConnectionFactory db)
{
    private static readonly string[] Modes = ["CASH", "CHEQUE", "NEFT", "RTGS", "IMPS", "UPI", "SAVING"];

    public async Task<SettlementView?> QuoteAsync(int branchId, int? loanId, string? loanNo, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var position = await LoanPosition.LoadAsync(c, null, branchId, loanId, loanNo, forUpdate: false, ct);
        if (position is null) return null;
        var (loan, schedule) = position.Value;
        var positions = schedule.Select(s => s.ToPosition()).ToList();
        var (enabled, grace) = await LoanPosition.LateFeeSettingsAsync(c, null, ct);
        var lateFee = enabled ? RecoveryMath.LateFeeOwed(positions, date, grace, loan.LateFeePerInstallment).Values.Sum() : 0;
        var last = await LastAsync(c, null, loan.LoanId, ct);

        return new SettlementView
        {
            Loan = loan,
            Quote = RecoveryMath.Quote(positions, date, lateFee, loan.PreCloseChargesPct),
            SuggestedLateFee = lateFee,
            OpenInstallments = positions.Count(p => p.IsOpen),
            CanClose = loan.Status == "APPROVED" && positions.Any(p => p.IsOpen),
            CanReopen = loan.Status == "CLOSED" && last?.ActionType == "CLOSE",
            Last = last
        };
    }

    private static Task<LastSettlement?> LastAsync(Microsoft.Data.SqlClient.SqlConnection c, System.Data.IDbTransaction? tx, int loanId, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<LastSettlement>(new CommandDefinition("""
            SELECT TOP (1) s.SettlementId, s.ActionType, s.ActionDate, s.NetCloseAmount, v.VoucherNo, s.Remarks
            FROM loan.LoanSettlement s LEFT JOIN acc.Voucher v ON v.VoucherId = s.VoucherId
            WHERE s.LoanId = @loanId ORDER BY s.SettlementId DESC
            """, new { loanId }, tx, cancellationToken: ct));

    public async Task<LastSettlement> CloseAsync(int branchId, int userId, SettlementInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.ChequeNo = Text.Clean(input.ChequeNo, upper: true);
        input.SavingAccountNo = Text.Clean(input.SavingAccountNo, upper: true);
        input.Remarks = Text.Clean(input.Remarks);
        var date = input.Date ?? businessDate;

        var v = new FieldValidator();
        v.OneOf(input.PaymentMode, Modes, "paymentMode");
        if (input.LateFee < 0) v.Add("lateFee", "Cannot be negative.");
        if (input.PreCloseCharges < 0) v.Add("preCloseCharges", "Cannot be negative.");
        if (date > today) v.Add("date", "Cannot be in the future.");
        if (input.PaymentMode == "CHEQUE" && input.ChequeNo is null) v.Add("chequeNo", "Cheque number is required.");
        v.MaxLength(input.Remarks, 300, "remarks");
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var position = await LoanPosition.LoadAsync(c, tx, branchId, input.LoanId, null, forUpdate: true, ct)
                       ?? throw new DomainException("Loan not found in this branch.", 404);
        var (loan, schedule) = position;
        if (loan.Status != "APPROVED") throw new DomainException($"Loan is {loan.Status}; only approved loans can be settled.", 409);
        if (loan.DisbursedOn is { } disbursed && date < disbursed) throw new DomainException("Settlement date is before the disbursement date.", 400);

        var positions = schedule.Select(s => s.ToPosition()).ToList();
        var open = positions.Where(p => p.IsOpen).ToList();
        if (open.Count == 0) throw new DomainException("Nothing is outstanding on this loan.", 409);

        var (enabled, grace) = await LoanPosition.LateFeeSettingsAsync(c, tx, ct);
        var lateFee = input.LateFee ?? (enabled ? RecoveryMath.LateFeeOwed(positions, date, grace, loan.LateFeePerInstallment).Values.Sum() : 0);
        var quote = RecoveryMath.Quote(positions, date, lateFee, loan.PreCloseChargesPct);
        var charges = input.PreCloseCharges ?? quote.PreCloseCharges;
        var net = quote.AmountToBeReceived + lateFee + charges;

        var map = await Posting.MappedLedgersAsync(c, tx, ct);
        int receiptLedger;
        int? savingAccountId = null;
        if (input.PaymentMode == "SAVING")
        {
            var accountNo = input.SavingAccountNo ?? loan.SavingAccountNo
                            ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["savingAccountNo"] = ["Enter the saving account to debit."] });
            var account = await c.QuerySingleOrDefaultAsync<DebitAccount>(new CommandDefinition("""
                SELECT a.AccountId, p.DepositLedgerId FROM sav.SavingAccount a JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
                JOIN loan.LoanApplication l ON l.MemberId = a.MemberId
                WHERE a.AccountNo = @accountNo AND a.BranchId = @branchId AND l.LoanId = @LoanId
                """, new { accountNo, branchId, loan.LoanId }, tx, cancellationToken: ct))
                ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["savingAccountNo"] = ["Saving account of this member not found in the branch."] });
            savingAccountId = account.AccountId;
            receiptLedger = account.DepositLedgerId;
        }
        else
        {
            receiptLedger = input.LedgerId ?? map.Ledger("CASH");
        }

        var settlementId = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT loan.LoanSettlement (LoanId, ActionType, ActionDate, TotalPrincipalReceived, TotalInterestReceived, BalancePrincipal,
                BalanceInterest, LateFeeBalance, BalanceInstallmentAmt, AmountToBeReceived, LateFeeReceived, PreCloseCharges, NetCloseAmount,
                PaymentMode, Remarks, CreatedBy)
            OUTPUT INSERTED.SettlementId
            VALUES (@LoanId, 'CLOSE', @date, @PrincipalReceived, @InterestReceived, @BalancePrincipal, @InterestDueTillDate, @lateFee,
                @BalanceInstallmentAmount, @AmountToBeReceived, @lateFee, @charges, @net, @PaymentMode, @Remarks, @userId)
            """, new
        {
            loan.LoanId, date, quote.PrincipalReceived, quote.InterestReceived, quote.BalancePrincipal, quote.InterestDueTillDate, lateFee,
            quote.BalanceInstallmentAmount, quote.AmountToBeReceived, charges, net, input.PaymentMode, input.Remarks, userId
        }, tx, cancellationToken: ct));

        var narration = $"LOAN PRE-CLOSED {loan.LoanNo} - {loan.MemberName}";
        var (voucherId, _) = await Posting.PostAsync(c, tx, new VoucherHeader
        {
            Date = date,
            BranchId = branchId,
            VoucherType = "LOAN_CLOSE",
            PaymentMode = input.PaymentMode,
            ChequeNo = input.ChequeNo,
            ChequeDate = input.ChequeNo is null ? null : date,
            PayeeName = loan.MemberName,
            Narration = narration,
            SourceModule = "SETTLEMENT",
            SourceId = settlementId,
            UserId = userId
        },
        [
            new PostingLine(receiptLedger, branchId, net, 0, narration),
            new PostingLine(map.Ledger("LOAN_PORTFOLIO"), branchId, 0, quote.BalancePrincipal, narration),
            new PostingLine(map.Ledger("LOAN_INTEREST"), branchId, 0, quote.InterestDueTillDate, narration),
            new PostingLine(map.Ledger("LATE_FEE"), branchId, 0, lateFee, narration),
            new PostingLine(map.Ledger("PRECLOSE_CHARGES"), branchId, 0, charges, narration)
        ], ct);

        long? savingTxnId = null;
        if (savingAccountId is { } accountId)
            savingTxnId = (await SavingRepository.PostTxnAsync(c, tx, accountId, branchId, date, 'D', net, "LOAN_RECOVERY", "TRANSFER", null,
                narration, "SETTLEMENT", settlementId, voucherId, userId, ct)).TxnId;

        // Schedule: principal and due interest paid; future interest waived. Lines allow an exact re-open.
        var lines = open.Select(p => new
        {
            settlementId,
            InstallmentNo = (short)p.InstallmentNo,
            PrincipalPaid = p.PrincipalDue,
            InterestPaid = p.DueDate <= date ? p.InterestDue : 0m,
            PreviousStatus = schedule.First(s => s.InstallmentNo == p.InstallmentNo).Status
        }).ToList();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT loan.LoanSettlementLine (SettlementId, InstallmentNo, PrincipalPaid, InterestPaid, PreviousStatus)
            VALUES (@settlementId, @InstallmentNo, @PrincipalPaid, @InterestPaid, @PreviousStatus)
            """, lines, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.EmiSchedule
            SET PrincipalPaid = PrincipalPaid + @PrincipalPaid, InterestPaid = InterestPaid + @InterestPaid, LastPaidDate = @date,
                Status = CASE WHEN InterestPaid + @InterestPaid >= InterestAmount THEN 'PAID' ELSE 'WAIVED' END
            WHERE LoanId = @loanId AND InstallmentNo = @InstallmentNo
            """, lines.Select(l => new { loanId = loan.LoanId, l.InstallmentNo, l.PrincipalPaid, l.InterestPaid, date }), tx, cancellationToken: ct));

        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.LoanSettlement SET VoucherId = @voucherId, SavingTxnId = @savingTxnId WHERE SettlementId = @settlementId;
            UPDATE loan.LoanApplication SET Status = 'CLOSED', ClosedOn = @date, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId WHERE LoanId = @LoanId;
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
            VALUES (@LoanId, 'CLOSE', 'APPROVED', 'CLOSED', @date, @remarks, @userId);
            """, new { voucherId, savingTxnId, settlementId, loan.LoanId, date, userId, remarks = input.Remarks ?? "Pre-closed by settlement" }, tx, cancellationToken: ct));

        var result = await LastAsync(c, tx, loan.LoanId, ct);
        await tx.CommitAsync(ct);
        return result!;
    }

    public async Task<LastSettlement> ReopenAsync(int branchId, int userId, int loanId, DateOnly date, string? remarks, DateOnly today, CancellationToken ct)
    {
        if (date > today) throw new ValidationFailedException(new Dictionary<string, string[]> { ["date"] = ["Cannot be in the future."] });
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var position = await LoanPosition.LoadAsync(c, tx, branchId, loanId, null, forUpdate: true, ct)
                       ?? throw new DomainException("Loan not found in this branch.", 404);
        var loan = position.Loan;
        if (loan.Status != "CLOSED") throw new DomainException("Only closed loans can be re-opened.", 409);

        var last = await c.QuerySingleOrDefaultAsync<SettlementRow>(new CommandDefinition("""
            SELECT TOP (1) SettlementId, ActionType, VoucherId, SavingTxnId, NetCloseAmount
            FROM loan.LoanSettlement WITH (UPDLOCK) WHERE LoanId = @loanId ORDER BY SettlementId DESC
            """, new { loanId }, tx, cancellationToken: ct));
        if (last is null || last.ActionType != "CLOSE")
            throw new DomainException("This loan was closed by full repayment, not by settlement. Reverse its last receipt instead.", 409);

        if (last.VoucherId is { } voucherId) await Posting.CancelAsync(c, tx, voucherId, userId, ct);

        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE s SET PrincipalPaid = s.PrincipalPaid - l.PrincipalPaid, InterestPaid = s.InterestPaid - l.InterestPaid, Status = l.PreviousStatus
            FROM loan.EmiSchedule s JOIN loan.LoanSettlementLine l ON l.InstallmentNo = s.InstallmentNo
            WHERE s.LoanId = @loanId AND l.SettlementId = @SettlementId
            """, new { loanId, last.SettlementId }, tx, cancellationToken: ct));

        if (last.SavingTxnId is { } savingTxnId)
        {
            var accountId = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT AccountId FROM sav.SavingTransaction WHERE TxnId = @savingTxnId", new { savingTxnId }, tx, cancellationToken: ct));
            await SavingRepository.PostTxnAsync(c, tx, accountId, branchId, date, 'C', last.NetCloseAmount, "REVERSAL", "SYSTEM", null,
                $"SETTLEMENT REVERSED {loan.LoanNo}", "SETTLEMENT", last.SettlementId, null, userId, ct);
            await c.ExecuteAsync(new CommandDefinition("UPDATE sav.SavingTransaction SET IsReversed = 1 WHERE TxnId = @savingTxnId",
                new { savingTxnId }, tx, cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition("""
            INSERT loan.LoanSettlement (LoanId, ActionType, ActionDate, Remarks, CreatedBy) VALUES (@loanId, 'REOPEN', @date, @remarks, @userId);
            UPDATE loan.LoanApplication SET Status = 'APPROVED', ClosedOn = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId WHERE LoanId = @loanId;
            INSERT loan.LoanStatusHistory (LoanId, ActionType, FromStatus, ToStatus, ActionDate, Remarks, ActionBy)
            VALUES (@loanId, 'REOPEN', 'CLOSED', 'APPROVED', @date, @remarks, @userId);
            """, new { loanId, date, userId, remarks = Text.Clean(remarks) ?? "Settlement reversed" }, tx, cancellationToken: ct));

        var result = await LastAsync(c, tx, loanId, ct);
        await tx.CommitAsync(ct);
        return result!;
    }

    private sealed class DebitAccount
    {
        public int AccountId { get; init; }
        public int DepositLedgerId { get; init; }
    }

    private sealed class SettlementRow
    {
        public int SettlementId { get; init; }
        public string ActionType { get; init; } = "";
        public long? VoucherId { get; init; }
        public long? SavingTxnId { get; init; }
        public decimal NetCloseAmount { get; init; }
    }
}
