using System.Data;
using Dapper;
using DhanJagrati.Data.Accounts;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Savings;

public sealed class SavingProductRow
{
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public decimal InterestRate { get; init; }
    public decimal MinOpeningDeposit { get; init; }
    public decimal MinBalance { get; init; }
    public bool AllowWithdrawal { get; init; }
}

public sealed class SavingAccountInput
{
    public string? MemberNo { get; set; }
    public string? JointMemberNo { get; set; }
    public int ProductId { get; set; }
    public DateOnly? OpenDate { get; set; }
    public string? EmployeeCode { get; set; }
    public string? NomineeName { get; set; }
    public int? NomineeRelationId { get; set; }
    public decimal OpeningDeposit { get; set; }
    public string? PaymentMode { get; set; }
    public int? LedgerId { get; set; }
    public string? ChequeNo { get; set; }
}

public sealed class SavingAccountDetail
{
    public int Id { get; init; }
    public string AccountNo { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string? MobileNo { get; init; }
    public string? JointMemberNo { get; init; }
    public string? JointMemberName { get; init; }
    public int ProductId { get; init; }
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public DateOnly OpenDate { get; init; }
    public string? EmployeeCode { get; init; }
    public string? EmployeeName { get; init; }
    public string? NomineeName { get; init; }
    public int? NomineeRelationId { get; init; }
    public decimal CurrentBalance { get; init; }
    public decimal MinBalance { get; init; }
    public bool AllowWithdrawal { get; init; }
    public string Status { get; init; } = "";
    public DateOnly? ClosedOn { get; init; }
}

public sealed class SavingListItem
{
    public int Id { get; init; }
    public string AccountNo { get; init; } = "";
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public decimal CurrentBalance { get; init; }
    public string Status { get; init; } = "";
    public DateOnly OpenDate { get; init; }
}

public sealed class SavingTxnInput
{
    public string? TxnType { get; set; }
    public DateOnly? TxnDate { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMode { get; set; }
    public int? LedgerId { get; set; }
    public string? ChequeNo { get; set; }
    public string? Narration { get; set; }
}

public sealed class StatementLine
{
    public string TxnNo { get; init; } = "";
    public DateOnly TxnDate { get; init; }
    public string TxnType { get; init; } = "";
    public string PaymentMode { get; init; } = "";
    public string? ChequeNo { get; init; }
    public string? Narration { get; init; }
    public decimal Withdrawal { get; init; }
    public decimal Deposit { get; init; }
    public decimal BalanceAfter { get; init; }
}

public sealed record SavingStatement(decimal OpeningBalance, IReadOnlyList<StatementLine> Lines);
public sealed record SavingTxnResult(long TxnId, string TxnNo, decimal BalanceAfter);

/// <summary>Saving module: accounts, deposits/withdrawals, statement. Each money movement posts a voucher.</summary>
public sealed class SavingRepository(IDbConnectionFactory db)
{
    private static readonly string[] CashModes = ["CASH", "CHEQUE", "NEFT", "RTGS", "IMPS", "UPI"];

    public async Task<IReadOnlyList<SavingProductRow>> ProductsAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<SavingProductRow>(new CommandDefinition(
            "SELECT ProductId, ProductCode, ProductName, InterestRate, MinOpeningDeposit, MinBalance, AllowWithdrawal FROM sav.SavingProduct WHERE IsActive = 1 ORDER BY ProductCode",
            cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<SavingListItem>> SearchAsync(int branchId, string? search, string? memberNo, CancellationToken ct)
    {
        var term = Text.Clean(search);
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<SavingListItem>(new CommandDefinition("""
            SELECT TOP (200) a.AccountId AS Id, a.AccountNo, m.MemberNo, m.MemberName, p.ProductCode, a.CurrentBalance, a.Status, a.OpenDate
            FROM sav.SavingAccount a JOIN loan.Member m ON m.MemberId = a.MemberId JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
            WHERE a.BranchId = @branchId
              AND (@memberNo IS NULL OR m.MemberNo = @memberNo)
              AND (@term IS NULL OR a.AccountNo LIKE @like OR m.MemberNo LIKE @like OR m.MemberName LIKE @like)
            ORDER BY a.AccountId DESC
            """, new { branchId, term, like = $"%{term}%", memberNo = Text.Clean(memberNo, upper: true) }, cancellationToken: ct))).AsList();
    }

    public async Task<SavingAccountDetail?> GetAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await GetAsync(c, null, branchId, id, null, ct);
    }

    public async Task<SavingAccountDetail?> FindByNumberAsync(int branchId, string accountNo, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await GetAsync(c, null, branchId, null, Text.Clean(accountNo, upper: true), ct);
    }

    private static Task<SavingAccountDetail?> GetAsync(SqlConnection c, IDbTransaction? tx, int branchId, int? id, string? accountNo, CancellationToken ct) =>
        c.QuerySingleOrDefaultAsync<SavingAccountDetail>(new CommandDefinition("""
            SELECT a.AccountId AS Id, a.AccountNo, a.BranchId, b.BranchName, m.MemberNo, m.MemberName, m.GuardianName, m.MobileNo,
                   jm.MemberNo AS JointMemberNo, jm.MemberName AS JointMemberName, a.ProductId, p.ProductCode, p.ProductName, a.OpenDate,
                   e.EmployeeCode, e.EmployeeName, a.NomineeName, a.NomineeRelationId, a.CurrentBalance, p.MinBalance, p.AllowWithdrawal,
                   a.Status, a.ClosedOn
            FROM sav.SavingAccount a
            JOIN sec.Branch b ON b.BranchId = a.BranchId
            JOIN loan.Member m ON m.MemberId = a.MemberId
            JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
            LEFT JOIN loan.Member jm ON jm.MemberId = a.JointMemberId
            LEFT JOIN emp.Employee e ON e.EmployeeId = a.EmployeeId
            WHERE a.BranchId = @branchId AND (a.AccountId = @id OR a.AccountNo = @accountNo)
            """, new { branchId, id, accountNo }, tx, cancellationToken: ct));

    public async Task<int> OpenAsync(int branchId, int userId, SavingAccountInput input, DateOnly today, CancellationToken ct)
    {
        input.MemberNo = Text.Clean(input.MemberNo, upper: true);
        input.JointMemberNo = Text.Clean(input.JointMemberNo, upper: true);
        input.EmployeeCode = Text.Clean(input.EmployeeCode, upper: true);
        input.NomineeName = Text.Clean(input.NomineeName, upper: true);
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.ChequeNo = Text.Clean(input.ChequeNo, upper: true);

        var v = new FieldValidator();
        v.Required(input.MemberNo, "memberNo");
        if (input.ProductId <= 0) v.Add("productId", "Required.");
        if (input.OpeningDeposit < 0) v.Add("openingDeposit", "Cannot be negative.");
        if (input.OpenDate > today) v.Add("openDate", "Cannot be in the future.");
        v.OneOf(input.PaymentMode, CashModes, "paymentMode");
        v.MaxLength(input.NomineeName, 100, "nomineeName");
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        var refs = await c.QuerySingleAsync<OpenRefs>(new CommandDefinition("""
            SELECT (SELECT MemberId FROM loan.Member WHERE MemberNo = @MemberNo AND BranchId = @branchId AND IsActive = 1) AS MemberId,
                   (SELECT MemberId FROM loan.Member WHERE MemberNo = @JointMemberNo) AS JointMemberId,
                   (SELECT EmployeeId FROM emp.Employee WHERE EmployeeCode = @EmployeeCode AND IsActive = 1) AS EmployeeId,
                   p.MinOpeningDeposit, p.DepositLedgerId, p.IsActive AS ProductActive
            FROM (SELECT 1 AS x) d LEFT JOIN sav.SavingProduct p ON p.ProductId = @ProductId
            """, new { input.MemberNo, input.JointMemberNo, input.EmployeeCode, input.ProductId, branchId }, cancellationToken: ct));
        if (refs.MemberId is null) v.Add("memberNo", "Active member not found in this branch.");
        if (input.JointMemberNo is not null && refs.JointMemberId is null) v.Add("jointMemberNo", "Member not found.");
        if (input.EmployeeCode is not null && refs.EmployeeId is null) v.Add("employeeCode", "Active employee not found.");
        if (refs.ProductActive != true) v.Add("productId", "Select an active product.");
        else if (input.OpeningDeposit < refs.MinOpeningDeposit) v.Add("openingDeposit", $"Minimum opening deposit is {refs.MinOpeningDeposit:0.00}.");
        v.ThrowIfAny();

        var openDate = input.OpenDate ?? today;
        await using var tx = c.BeginTransaction();
        var accountNo = await SqlGuard.NextNumberAsync(c, tx, "SAVING_ACCOUNT", branchId, ct);
        var accountId = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT sav.SavingAccount (AccountNo, BranchId, MemberId, JointMemberId, ProductId, OpenDate, EmployeeId, NomineeName, NomineeRelationId, CreatedBy)
            OUTPUT INSERTED.AccountId
            VALUES (@accountNo, @branchId, @MemberId, @JointMemberId, @ProductId, @openDate, @EmployeeId, @NomineeName, @NomineeRelationId, @userId)
            """, new { accountNo, branchId, refs.MemberId, refs.JointMemberId, input.ProductId, openDate, refs.EmployeeId, input.NomineeName, input.NomineeRelationId, userId },
            tx, cancellationToken: ct));

        if (input.OpeningDeposit > 0)
            await CashTransactionAsync(c, tx, branchId, userId, accountId, accountNo, refs.DepositLedgerId!.Value, "OPENING", 'C',
                input.OpeningDeposit, openDate, input.PaymentMode, input.LedgerId, input.ChequeNo, "ACCOUNT OPENING DEPOSIT", ct);

        await tx.CommitAsync(ct);
        return accountId;
    }

    public async Task<SavingTxnResult> TransactAsync(int branchId, int userId, int accountId, SavingTxnInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        input.TxnType = Text.Clean(input.TxnType, upper: true);
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.ChequeNo = Text.Clean(input.ChequeNo, upper: true);
        input.Narration = Text.Clean(input.Narration, upper: true);
        var date = input.TxnDate ?? businessDate;

        var v = new FieldValidator();
        v.OneOf(input.TxnType, ["DEPOSIT", "WITHDRAWAL"], "txnType");
        v.Required(input.TxnType, "txnType");
        if (input.Amount <= 0) v.Add("amount", "Enter an amount greater than zero.");
        v.OneOf(input.PaymentMode, CashModes, "paymentMode");
        if (date > today) v.Add("txnDate", "Cannot be in the future.");
        v.MaxLength(input.Narration, 300, "narration");
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var account = await GetAsync(c, tx, branchId, accountId, null, ct) ?? throw new DomainException("Account not found in this branch.", 404);
        if (input.TxnType == "WITHDRAWAL" && !account.AllowWithdrawal) throw new DomainException("This product does not allow withdrawals.", 409);
        if (date < account.OpenDate) throw new DomainException("Date is before the account opening date.", 400);
        var depositLedger = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT DepositLedgerId FROM sav.SavingProduct WHERE ProductId = @ProductId", new { account.ProductId }, tx, cancellationToken: ct));

        var result = await CashTransactionAsync(c, tx, branchId, userId, accountId, account.AccountNo, depositLedger, input.TxnType!,
            input.TxnType == "DEPOSIT" ? 'C' : 'D', input.Amount, date, input.PaymentMode!, input.LedgerId, input.ChequeNo, input.Narration, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<bool> CloseAsync(int branchId, int userId, int accountId, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var updated = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sav.SavingAccount SET Status = 'CLOSED', ClosedOn = @date, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            WHERE AccountId = @accountId AND BranchId = @branchId AND Status <> 'CLOSED' AND CurrentBalance = 0
            """, new { accountId, branchId, date, userId }, cancellationToken: ct));
        if (updated == 0) throw new DomainException("Only an open account with zero balance can be closed. Withdraw the balance first.", 409);
        return true;
    }

    public async Task<SavingStatement> StatementAsync(int branchId, int accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var owned = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sav.SavingAccount WHERE AccountId = @accountId AND BranchId = @branchId", new { accountId, branchId }, cancellationToken: ct));
        if (owned == 0) throw new DomainException("Account not found in this branch.", 404);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("sav.usp_SavingStatement",
            new { AccountId = accountId, FromDate = from, ToDate = to }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var opening = await multi.ReadSingleAsync<decimal>();
        var lines = (await multi.ReadAsync<StatementLine>()).AsList();
        return new SavingStatement(opening, lines);
    }

    /// <summary>Deposit / withdrawal against cash or bank: voucher + saving transaction.</summary>
    private static async Task<SavingTxnResult> CashTransactionAsync(SqlConnection c, IDbTransaction tx, int branchId, int userId, int accountId,
        string accountNo, int depositLedgerId, string txnType, char direction, decimal amount, DateOnly date, string paymentMode, int? ledgerId,
        string? chequeNo, string? narration, CancellationToken ct)
    {
        var map = await Posting.MappedLedgersAsync(c, tx, ct);
        var cashLedger = ledgerId ?? map.Ledger("CASH");
        var text = narration ?? $"{txnType} {accountNo}";
        var (voucherId, _) = await Posting.PostAsync(c, tx, new VoucherHeader
        {
            Date = date,
            BranchId = branchId,
            VoucherType = direction == 'C' ? "SAVING_DEPOSIT" : "SAVING_WITHDRAWAL",
            PaymentMode = paymentMode,
            ChequeNo = chequeNo,
            ChequeDate = chequeNo is null ? null : date,
            Narration = text,
            SourceModule = "SAVING",
            SourceId = accountId,
            UserId = userId
        },
        direction == 'C'
            ? [new PostingLine(cashLedger, branchId, amount, 0, text), new PostingLine(depositLedgerId, branchId, 0, amount, text)]
            : [new PostingLine(depositLedgerId, branchId, amount, 0, text), new PostingLine(cashLedger, branchId, 0, amount, text)], ct);

        return await PostTxnAsync(c, tx, accountId, branchId, date, direction, amount, txnType, paymentMode == "CASH" ? "CASH" : paymentMode,
            chequeNo, text, "SAVING", accountId, voucherId, userId, ct);
    }

    /// <summary>
    /// Moves the account balance atomically (never below the product minimum on debits) and records the
    /// transaction. Shared with loan recovery / disbursement, inside the caller's transaction.
    /// </summary>
    public static async Task<SavingTxnResult> PostTxnAsync(SqlConnection c, IDbTransaction tx, int accountId, int branchId, DateOnly date, char direction,
        decimal amount, string txnType, string paymentMode, string? chequeNo, string? narration, string? sourceModule, long? sourceId,
        long? voucherId, int userId, CancellationToken ct)
    {
        var delta = direction == 'C' ? amount : -amount;
        var balance = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
            UPDATE a SET CurrentBalance = a.CurrentBalance + @delta, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            OUTPUT INSERTED.CurrentBalance
            FROM sav.SavingAccount a JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
            WHERE a.AccountId = @accountId AND a.Status = 'ACTIVE'
              AND (@delta >= 0 OR a.CurrentBalance + @delta >= CASE WHEN @txnType = 'REVERSAL' THEN 0 ELSE p.MinBalance END)
            """, new { accountId, delta, userId, txnType }, tx, cancellationToken: ct));
        if (balance is null)
        {
            var info = await c.QuerySingleOrDefaultAsync<AccountState>(new CommandDefinition("""
                SELECT a.Status, a.CurrentBalance, p.MinBalance FROM sav.SavingAccount a JOIN sav.SavingProduct p ON p.ProductId = a.ProductId
                WHERE a.AccountId = @accountId
                """, new { accountId }, tx, cancellationToken: ct));
            throw info is null || info.Status != "ACTIVE"
                ? new DomainException($"Saving account is {info?.Status ?? "missing"}.", 409)
                : new DomainException($"Insufficient balance: available {info.CurrentBalance - info.MinBalance:0.00} (minimum balance {info.MinBalance:0.00}).", 409);
        }

        var txnNo = await SqlGuard.NextNumberAsync(c, tx, "SAVING_TXN", 0, ct);
        var txnId = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT sav.SavingTransaction (TxnNo, AccountId, TxnDate, BranchId, TxnType, Direction, Amount, BalanceAfter, PaymentMode,
                                          ChequeNo, ChequeDate, Narration, SourceModule, SourceId, VoucherId, CreatedBy)
            OUTPUT INSERTED.TxnId
            VALUES (@txnNo, @accountId, @date, @branchId, @txnType, @direction, @amount, @balance, @paymentMode,
                    @chequeNo, @chequeDate, @narration, @sourceModule, @sourceId, @voucherId, @userId)
            """, new
        {
            txnNo, accountId, date, branchId, txnType, direction = direction.ToString(), amount, balance, paymentMode,
            chequeNo, chequeDate = chequeNo is null ? (DateOnly?)null : date, narration, sourceModule, sourceId, voucherId, userId
        }, tx, cancellationToken: ct));
        return new SavingTxnResult(txnId, txnNo, balance.Value);
    }

    private sealed class OpenRefs
    {
        public int? MemberId { get; init; }
        public int? JointMemberId { get; init; }
        public int? EmployeeId { get; init; }
        public decimal? MinOpeningDeposit { get; init; }
        public int? DepositLedgerId { get; init; }
        public bool? ProductActive { get; init; }
    }

    private sealed class AccountState
    {
        public string Status { get; init; } = "";
        public decimal CurrentBalance { get; init; }
        public decimal MinBalance { get; init; }
    }
}
