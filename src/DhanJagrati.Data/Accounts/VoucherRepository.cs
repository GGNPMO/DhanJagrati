using Dapper;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Data.Accounts;

public sealed class VoucherInput
{
    public DateOnly? VoucherDate { get; set; }
    public int DebitLedgerId { get; set; }
    public int CreditLedgerId { get; set; }
    public decimal Amount { get; set; }
    public string? PaymentMode { get; set; }
    public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public string? PayeeName { get; set; }
    public string? Narration { get; set; }
    /// <summary>Other branch that receives the debit side (branch transfer via INTER BRANCH A/C).</summary>
    public int? TransferToBranchId { get; set; }
    public string? SanctionNo { get; set; }
}

public sealed class VoucherListItem
{
    public long Id { get; init; }
    public string VoucherNo { get; init; } = "";
    public DateOnly VoucherDate { get; init; }
    public string VoucherType { get; init; } = "";
    public string PaymentMode { get; init; } = "";
    public decimal Amount { get; init; }
    public string? Narration { get; init; }
    public string? DebitLedgers { get; init; }
    public string? CreditLedgers { get; init; }
    public string? SourceModule { get; init; }
    public bool IsCancelled { get; init; }
}

public sealed class VoucherDetail
{
    public long Id { get; init; }
    public string VoucherNo { get; init; } = "";
    public DateOnly VoucherDate { get; init; }
    public string BranchName { get; init; } = "";
    public string VoucherType { get; init; } = "";
    public string PaymentMode { get; init; } = "";
    public string? ChequeNo { get; init; }
    public DateOnly? ChequeDate { get; init; }
    public string? PayeeName { get; init; }
    public string? Narration { get; init; }
    public decimal Amount { get; init; }
    public string? TransferToBranchName { get; init; }
    public string? SanctionNo { get; init; }
    public string? SourceModule { get; init; }
    public bool IsCancelled { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string? CreatedByName { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<VoucherLineRow> Lines { get; set; } = [];
}

public sealed class VoucherLineRow
{
    public short LineNumber { get; init; }
    public string BranchName { get; init; } = "";
    public string LedgerName { get; init; } = "";
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public string? Narration { get; init; }
}

/// <summary>ACCOUNT > Ledger Transaction Journal (manual vouchers).</summary>
public sealed class VoucherRepository(IDbConnectionFactory db)
{
    private static readonly string[] Modes = ["CASH", "CHEQUE", "NEFT", "RTGS", "IMPS", "UPI", "TRANSFER"];

    public async Task<IReadOnlyList<VoucherListItem>> SearchAsync(int branchId, DateOnly from, DateOnly to, string? search, bool manualOnly, CancellationToken ct)
    {
        var term = Text.Clean(search);
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<VoucherListItem>(new CommandDefinition("""
            SELECT TOP (500) v.VoucherId AS Id, v.VoucherNo, v.VoucherDate, v.VoucherType, v.PaymentMode, v.Amount, v.Narration,
                   (SELECT STRING_AGG(l.LedgerName, ', ') FROM acc.VoucherLine vl JOIN acc.Ledger l ON l.LedgerId = vl.LedgerId
                     WHERE vl.VoucherId = v.VoucherId AND vl.Debit > 0) AS DebitLedgers,
                   (SELECT STRING_AGG(l.LedgerName, ', ') FROM acc.VoucherLine vl JOIN acc.Ledger l ON l.LedgerId = vl.LedgerId
                     WHERE vl.VoucherId = v.VoucherId AND vl.Credit > 0) AS CreditLedgers,
                   v.SourceModule, v.IsCancelled
            FROM acc.Voucher v
            WHERE v.BranchId = @branchId AND v.VoucherDate BETWEEN @from AND @to
              AND (@manualOnly = 0 OR v.SourceModule IS NULL)
              AND (@term IS NULL OR v.VoucherNo LIKE @like OR v.Narration LIKE @like OR v.PayeeName LIKE @like OR v.ChequeNo LIKE @like)
            ORDER BY v.VoucherDate DESC, v.VoucherId DESC
            """, new { branchId, from, to, manualOnly, term, like = $"%{term}%" }, cancellationToken: ct))).AsList();
    }

    public async Task<VoucherDetail?> GetAsync(int branchId, long id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("""
            SELECT v.VoucherId AS Id, v.VoucherNo, v.VoucherDate, b.BranchName, v.VoucherType, v.PaymentMode, v.ChequeNo, v.ChequeDate,
                   v.PayeeName, v.Narration, v.Amount, tb.BranchName AS TransferToBranchName, v.SanctionNo, v.SourceModule,
                   v.IsCancelled, v.CancelledAt, u.DisplayName AS CreatedByName, v.CreatedAt
            FROM acc.Voucher v
            JOIN sec.Branch b ON b.BranchId = v.BranchId
            LEFT JOIN sec.Branch tb ON tb.BranchId = v.TransferToBranchId
            LEFT JOIN sec.AppUser u ON u.UserId = v.CreatedBy
            WHERE v.VoucherId = @id AND (v.BranchId = @branchId OR v.TransferToBranchId = @branchId);

            SELECT vl.LineNumber, b.BranchName, l.LedgerName, vl.Debit, vl.Credit, vl.Narration
            FROM acc.VoucherLine vl JOIN acc.Ledger l ON l.LedgerId = vl.LedgerId JOIN sec.Branch b ON b.BranchId = vl.BranchId
            WHERE vl.VoucherId = @id ORDER BY vl.LineNumber;
            """, new { id, branchId }, cancellationToken: ct));
        var voucher = await multi.ReadSingleOrDefaultAsync<VoucherDetail>();
        if (voucher is null) return null;
        voucher.Lines = (await multi.ReadAsync<VoucherLineRow>()).AsList();
        return voucher;
    }

    public async Task<long?> NavigateAsync(int branchId, Navigate direction, string? fromNo, CancellationToken ct)
    {
        var sql = direction switch
        {
            Navigate.First => "SELECT TOP (1) VoucherId FROM acc.Voucher WHERE BranchId = @branchId ORDER BY VoucherId",
            Navigate.Last => "SELECT TOP (1) VoucherId FROM acc.Voucher WHERE BranchId = @branchId ORDER BY VoucherId DESC",
            Navigate.Next => "SELECT TOP (1) VoucherId FROM acc.Voucher WHERE BranchId = @branchId AND VoucherId > (SELECT VoucherId FROM acc.Voucher WHERE VoucherNo = @from) ORDER BY VoucherId",
            _ => "SELECT TOP (1) VoucherId FROM acc.Voucher WHERE BranchId = @branchId AND VoucherId < (SELECT VoucherId FROM acc.Voucher WHERE VoucherNo = @from) ORDER BY VoucherId DESC"
        };
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long?>(new CommandDefinition(sql, new { branchId, from = fromNo ?? "" }, cancellationToken: ct));
    }

    public async Task<long> CreateAsync(int branchId, int userId, VoucherInput input, DateOnly businessDate, DateOnly today, CancellationToken ct)
    {
        input.PaymentMode = Text.Clean(input.PaymentMode, upper: true) ?? "CASH";
        input.ChequeNo = Text.Clean(input.ChequeNo, upper: true);
        input.PayeeName = Text.Clean(input.PayeeName, upper: true);
        input.Narration = Text.Clean(input.Narration, upper: true);
        input.SanctionNo = Text.Clean(input.SanctionNo, upper: true);
        if (input.TransferToBranchId == branchId) input.TransferToBranchId = null;
        var date = input.VoucherDate ?? businessDate;

        var v = new FieldValidator();
        if (input.DebitLedgerId <= 0) v.Add("debitLedgerId", "Choose the ledger to debit.");
        if (input.CreditLedgerId <= 0) v.Add("creditLedgerId", "Choose the ledger to credit.");
        if (input.DebitLedgerId > 0 && input.DebitLedgerId == input.CreditLedgerId) v.Add("creditLedgerId", "Debit and credit ledgers must differ.");
        if (input.Amount <= 0) v.Add("amount", "Enter an amount greater than zero.");
        if (decimal.Round(input.Amount, 2) != input.Amount) v.Add("amount", "Use at most two decimals.");
        v.OneOf(input.PaymentMode, Modes, "paymentMode");
        if (input.PaymentMode == "CHEQUE" && input.ChequeNo is null) v.Add("chequeNo", "Cheque number is required.");
        if (date > today) v.Add("voucherDate", "Cannot be in the future.");
        v.Required(input.Narration, "narration");
        v.MaxLength(input.Narration, 500, "narration");
        v.MaxLength(input.PayeeName, 150, "payeeName");
        v.MaxLength(input.ChequeNo, 30, "chequeNo");
        v.MaxLength(input.SanctionNo, 30, "sanctionNo");
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        var ledgers = (await c.QueryAsync<LedgerCheck>(new CommandDefinition("""
            SELECT LedgerId, LedgerName, IsGroup, IsActive, CashBankType, ApplySanction FROM acc.Ledger WHERE LedgerId IN (@DebitLedgerId, @CreditLedgerId)
            """, new { input.DebitLedgerId, input.CreditLedgerId }, cancellationToken: ct))).ToDictionary(l => l.LedgerId);
        foreach (var (field, id) in new[] { ("debitLedgerId", input.DebitLedgerId), ("creditLedgerId", input.CreditLedgerId) })
        {
            if (!ledgers.TryGetValue(id, out var l)) v.Add(field, "Ledger not found.");
            else if (l.IsGroup) v.Add(field, "Choose a ledger, not a group.");
            else if (!l.IsActive) v.Add(field, "Ledger is inactive.");
        }
        v.ThrowIfAny();
        var debit = ledgers[input.DebitLedgerId];
        var credit = ledgers[input.CreditLedgerId];
        if ((debit.ApplySanction || credit.ApplySanction) && input.SanctionNo is null) v.Add("sanctionNo", "A sanction number is required for this ledger.");
        if (input.TransferToBranchId is { } target)
        {
            var exists = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM sec.Branch WHERE BranchId = @target AND IsActive = 1", new { target }, cancellationToken: ct));
            if (exists == 0) v.Add("transferToBranchId", "Branch not found.");
        }
        v.ThrowIfAny();

        var debitCash = debit.CashBankType != "N";
        var creditCash = credit.CashBankType != "N";
        var type = input.TransferToBranchId is not null ? "BRANCH_TRANSFER"
            : debitCash && creditCash ? "CONTRA"
            : creditCash ? "PAYMENT"
            : debitCash ? "RECEIPT"
            : "JOURNAL";

        await using var tx = c.BeginTransaction();
        List<PostingLine> lines;
        if (input.TransferToBranchId is { } toBranch)
        {
            // This branch: Dr INTER BRANCH, Cr credit ledger.  Target branch: Dr debit ledger, Cr INTER BRANCH.
            var interBranch = (await Posting.MappedLedgersAsync(c, tx, ct)).Ledger("INTER_BRANCH");
            await Posting.EnsureDayOpenAsync(c, tx, toBranch, date, ct);
            lines =
            [
                new PostingLine(interBranch, branchId, input.Amount, 0, input.Narration),
                new PostingLine(input.CreditLedgerId, branchId, 0, input.Amount, input.Narration),
                new PostingLine(input.DebitLedgerId, toBranch, input.Amount, 0, input.Narration),
                new PostingLine(interBranch, toBranch, 0, input.Amount, input.Narration)
            ];
        }
        else
        {
            lines =
            [
                new PostingLine(input.DebitLedgerId, branchId, input.Amount, 0, input.Narration),
                new PostingLine(input.CreditLedgerId, branchId, 0, input.Amount, input.Narration)
            ];
        }

        var (voucherId, _) = await Posting.PostAsync(c, tx, new VoucherHeader
        {
            Date = date,
            BranchId = branchId,
            VoucherType = type,
            PaymentMode = input.PaymentMode,
            ChequeNo = input.ChequeNo,
            ChequeDate = input.ChequeNo is null ? null : input.ChequeDate ?? date,
            PayeeName = input.PayeeName,
            Narration = input.Narration,
            UserId = userId
        }, lines, ct);

        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE acc.Voucher SET TransferToBranchId = @TransferToBranchId, SanctionNo = @SanctionNo WHERE VoucherId = @voucherId",
            new { input.TransferToBranchId, input.SanctionNo, voucherId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return voucherId;
    }

    /// <summary>Only manual vouchers are cancelled here; loan/recovery vouchers are reversed from their own screens.</summary>
    public async Task CancelAsync(int branchId, int userId, long id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var info = await c.QuerySingleOrDefaultAsync<CancelInfo>(new CommandDefinition(
            "SELECT SourceModule, IsCancelled, TransferToBranchId, VoucherDate FROM acc.Voucher WITH (UPDLOCK) WHERE VoucherId = @id AND BranchId = @branchId",
            new { id, branchId }, tx, cancellationToken: ct)) ?? throw new DomainException("Voucher not found in this branch.", 404);
        if (info.IsCancelled) throw new DomainException("Voucher is already cancelled.", 409);
        if (info.SourceModule is not null)
            throw new DomainException($"This voucher was posted by {info.SourceModule}; reverse it from that screen.", 409);
        if (info.TransferToBranchId is { } other) await Posting.EnsureDayOpenAsync(c, tx, other, info.VoucherDate, ct);
        await Posting.CancelAsync(c, tx, id, userId, ct);
        await tx.CommitAsync(ct);
    }

    private sealed class LedgerCheck
    {
        public int LedgerId { get; init; }
        public string LedgerName { get; init; } = "";
        public bool IsGroup { get; init; }
        public bool IsActive { get; init; }
        public string CashBankType { get; init; } = "N";
        public bool ApplySanction { get; init; }
    }

    private sealed class CancelInfo
    {
        public string? SourceModule { get; init; }
        public bool IsCancelled { get; init; }
        public int? TransferToBranchId { get; init; }
        public DateOnly VoucherDate { get; init; }
    }
}
