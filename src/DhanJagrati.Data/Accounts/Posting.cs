using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Accounts;

public sealed record PostingLine(int LedgerId, int BranchId, decimal Debit, decimal Credit, string? Narration = null);

public sealed class VoucherHeader
{
    public required DateOnly Date { get; init; }
    public required int BranchId { get; init; }
    public required string VoucherType { get; init; }
    public required string PaymentMode { get; init; }
    public string? ChequeNo { get; init; }
    public DateOnly? ChequeDate { get; init; }
    public string? PayeeName { get; init; }
    public string? Narration { get; init; }
    public string? SourceModule { get; init; }
    public long? SourceId { get; init; }
    public required int UserId { get; init; }
}

/// <summary>
/// Double-entry posting used by every module (loans now, recovery / accounts later).
/// Always runs inside the caller's transaction so the business rows and the voucher commit together.
/// </summary>
public static class Posting
{
    /// <summary>acc.LedgerMapping key -> LedgerId (CASH, LOAN_PORTFOLIO, PROCESSING_FEE ...).</summary>
    public static async Task<IReadOnlyDictionary<string, int>> MappedLedgersAsync(SqlConnection c, IDbTransaction tx, CancellationToken ct)
    {
        var rows = await c.QueryAsync<(string Key, int LedgerId)>(new CommandDefinition(
            "SELECT MappingKey, LedgerId FROM acc.LedgerMapping", transaction: tx, cancellationToken: ct));
        return rows.ToDictionary(r => r.Key, r => r.LedgerId, StringComparer.OrdinalIgnoreCase);
    }

    public static int Ledger(this IReadOnlyDictionary<string, int> map, string key) =>
        map.TryGetValue(key, out var id) ? id : throw new DomainException($"Ledger mapping '{key}' is not configured.", 500);

    /// <summary>Rejects postings on a date closed with "Date Closing by User".</summary>
    public static async Task EnsureDayOpenAsync(SqlConnection c, IDbTransaction tx, int branchId, DateOnly date, CancellationToken ct)
    {
        var closed = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sec.DayClosing WHERE BranchId = @branchId AND BusinessDate = @date",
            new { branchId, date }, tx, cancellationToken: ct));
        if (closed > 0)
            throw new DomainException($"{date:dd-MMM-yyyy} is closed for this branch; no entries can be posted.", 409);
    }

    public static async Task<(long VoucherId, string VoucherNo)> PostAsync(
        SqlConnection c, IDbTransaction tx, VoucherHeader header, IEnumerable<PostingLine> lines, CancellationToken ct)
    {
        var posted = lines
            .Select(l => l with { Debit = Math.Round(l.Debit, 2), Credit = Math.Round(l.Credit, 2) })
            .Where(l => l.Debit != 0 || l.Credit != 0)
            .ToList();

        if (posted.Count < 2) throw new DomainException("A voucher needs at least one debit and one credit line.", 500);
        foreach (var l in posted)
            if (l.Debit < 0 || l.Credit < 0 || (l.Debit > 0 && l.Credit > 0))
                throw new DomainException("Each voucher line must be a positive debit or credit.", 500);
        foreach (var branch in posted.GroupBy(l => l.BranchId))
            if (branch.Sum(l => l.Debit) != branch.Sum(l => l.Credit))
                throw new DomainException("Voucher does not balance (debits must equal credits for each branch).", 500);

        await EnsureDayOpenAsync(c, tx, header.BranchId, header.Date, ct);

        var voucherNo = await SqlGuard.NextNumberAsync(c, tx, "VOUCHER", 0, ct);
        var amount = posted.Sum(l => l.Debit);
        var voucherId = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT acc.Voucher (VoucherNo, VoucherDate, BranchId, VoucherType, PaymentMode, ChequeNo, ChequeDate, PayeeName,
                                Narration, Amount, SourceModule, SourceId, CreatedBy)
            OUTPUT INSERTED.VoucherId
            VALUES (@voucherNo, @Date, @BranchId, @VoucherType, @PaymentMode, @ChequeNo, @ChequeDate, @PayeeName,
                    @Narration, @amount, @SourceModule, @SourceId, @UserId)
            """, new
        {
            voucherNo, header.Date, header.BranchId, header.VoucherType, header.PaymentMode, header.ChequeNo, header.ChequeDate,
            header.PayeeName, header.Narration, amount, header.SourceModule, header.SourceId, header.UserId
        }, tx, cancellationToken: ct));

        await c.ExecuteAsync(new CommandDefinition("""
            INSERT acc.VoucherLine (VoucherId, LineNumber, BranchId, LedgerId, Debit, Credit, Narration)
            VALUES (@voucherId, @LineNumber, @BranchId, @LedgerId, @Debit, @Credit, @Narration)
            """, posted.Select((l, i) => new { voucherId, LineNumber = (short)(i + 1), l.BranchId, l.LedgerId, l.Debit, l.Credit, l.Narration }),
            tx, cancellationToken: ct));

        return (voucherId, voucherNo);
    }

    public static async Task CancelAsync(SqlConnection c, IDbTransaction tx, long voucherId, int userId, CancellationToken ct)
    {
        var info = await c.QuerySingleOrDefaultAsync<VoucherInfo>(new CommandDefinition(
            "SELECT BranchId, VoucherDate FROM acc.Voucher WHERE VoucherId = @voucherId AND IsCancelled = 0",
            new { voucherId }, tx, cancellationToken: ct));
        if (info is null) return;
        await EnsureDayOpenAsync(c, tx, info.BranchId, info.VoucherDate, ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE acc.Voucher SET IsCancelled = 1, CancelledAt = SYSUTCDATETIME(), CancelledBy = @userId WHERE VoucherId = @voucherId
            """, new { voucherId, userId }, tx, cancellationToken: ct));
    }

    private sealed class VoucherInfo
    {
        public int BranchId { get; init; }
        public DateOnly VoucherDate { get; init; }
    }

    /// <summary>Active cash and bank ledgers (disbursement / receipt accounts).</summary>
    public static async Task<IReadOnlyList<CashBankLedger>> CashBankLedgersAsync(IDbConnectionFactory db, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<CashBankLedger>(new CommandDefinition("""
            SELECT LedgerId, LedgerName, CashBankType FROM acc.Ledger
            WHERE IsGroup = 0 AND IsActive = 1 AND CashBankType IN ('C', 'B')
            ORDER BY CashBankType DESC, LedgerName
            """, cancellationToken: ct))).AsList();
    }
}

public sealed record CashBankLedger(int LedgerId, string LedgerName, string CashBankType);
