using System.Data;
using Dapper;

namespace DhanJagrati.Data.Accounts;

public sealed class CashBookTotals
{
    public decimal OpeningBalance { get; init; }
    public decimal TotalReceipts { get; init; }
    public decimal TotalPayments { get; init; }
    public decimal ClosingBalance { get; init; }
}

public sealed class CashBookSummaryRow
{
    public int LedgerId { get; init; }
    public string LedgerName { get; init; } = "";
    public decimal Receipt { get; init; }
    public decimal Payment { get; init; }
}

public sealed class CashBookDetailRow
{
    public long SrNo { get; init; }
    public DateOnly VoucherDate { get; init; }
    public string VoucherNo { get; init; } = "";
    public int LedgerId { get; init; }
    public string AccountHead { get; init; } = "";
    public string? Particulars { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public string PaymentMode { get; init; } = "";
    public string? ChequeNo { get; init; }
}

public sealed record CashBook(CashBookTotals Totals, IReadOnlyList<CashBookSummaryRow> Summary, IReadOnlyList<CashBookDetailRow> Detail);

public sealed class TrialBalanceRow
{
    public long SrNo { get; init; }
    public int LedgerId { get; init; }
    public string LedgerName { get; init; } = "";
    public string NatureName { get; init; } = "";
    public string? GroupName { get; set; }
    public decimal OpeningNet { get; init; }
    public decimal PeriodDebit { get; init; }
    public decimal PeriodCredit { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
}

public sealed class StatementRow
{
    public int? LedgerId { get; init; }
    public string LedgerName { get; init; } = "";
    public string Side { get; init; } = "";
    public decimal Amount { get; init; }
    public string? GroupName { get; set; }
}

public sealed class ProfitLossTotals
{
    public decimal TotalExpense { get; init; }
    public decimal TotalIncome { get; init; }
    public decimal NetProfit { get; init; }
    public decimal NetLoss { get; init; }
}

public sealed record ProfitLoss(IReadOnlyList<StatementRow> Rows, ProfitLossTotals Totals);

public sealed class LedgerStatementLine
{
    public DateOnly VoucherDate { get; init; }
    public string VoucherNo { get; init; } = "";
    public string VoucherType { get; init; } = "";
    public string PaymentMode { get; init; } = "";
    public string? ChequeNo { get; init; }
    public string? Particulars { get; init; }
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public decimal RunningBalance { get; init; }
}

public sealed record LedgerStatement(decimal OpeningNet, IReadOnlyList<LedgerStatementLine> Lines);

/// <summary>ACCOUNT reports, backed by the acc.usp_* procedures of the bootstrap schema.</summary>
public sealed class ReportRepository(IDbConnectionFactory db)
{
    public async Task<CashBook> CashBookAsync(int branchId, DateOnly from, DateOnly to, int? cashLedgerId, int? groupLedgerId, string mode, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("acc.usp_CashBook",
            new { BranchId = branchId, FromDate = from, ToDate = to, CashLedgerId = cashLedgerId, GroupLedgerId = groupLedgerId, Mode = mode },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var totals = await multi.ReadSingleAsync<CashBookTotals>();
        if (mode == "SUMMARY")
            return new CashBook(totals, (await multi.ReadAsync<CashBookSummaryRow>()).AsList(), []);
        return new CashBook(totals, [], (await multi.ReadAsync<CashBookDetailRow>()).AsList());
    }

    public async Task<IReadOnlyList<TrialBalanceRow>> TrialBalanceAsync(int? branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = (await c.QueryAsync<TrialBalanceRow>(new CommandDefinition("acc.usp_TrialBalance",
            new { BranchId = branchId, FromDate = from, ToDate = to }, commandType: CommandType.StoredProcedure, cancellationToken: ct))).AsList();
        var groups = await GroupNamesAsync(c, ct);
        foreach (var r in rows) r.GroupName = groups.GetValueOrDefault(r.LedgerId);
        return rows;
    }

    public async Task<ProfitLoss> ProfitLossAsync(int? branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("acc.usp_ProfitAndLoss",
            new { BranchId = branchId, FromDate = from, ToDate = to }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var rows = (await multi.ReadAsync<StatementRow>()).AsList();
        var totals = await multi.ReadSingleAsync<ProfitLossTotals>();
        var groups = await GroupNamesAsync(c, ct);
        foreach (var r in rows) r.GroupName = r.LedgerId is { } id ? groups.GetValueOrDefault(id) : null;
        return new ProfitLoss(rows, totals);
    }

    public async Task<IReadOnlyList<StatementRow>> BalanceSheetAsync(int? branchId, DateOnly asOn, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = (await c.QueryAsync<StatementRow>(new CommandDefinition("acc.usp_BalanceSheet",
            new { BranchId = branchId, ToDate = asOn }, commandType: CommandType.StoredProcedure, cancellationToken: ct))).AsList();
        var groups = await GroupNamesAsync(c, ct);
        foreach (var r in rows) r.GroupName = r.LedgerId is { } id ? groups.GetValueOrDefault(id) : "PROFIT & LOSS";
        return rows;
    }

    public async Task<LedgerStatement> LedgerStatementAsync(int? branchId, int ledgerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("acc.usp_LedgerStatement",
            new { BranchId = branchId, LedgerId = ledgerId, FromDate = from, ToDate = to }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        var opening = await multi.ReadSingleAsync<decimal>();
        return new LedgerStatement(opening, (await multi.ReadAsync<LedgerStatementLine>()).AsList());
    }

    /// <summary>Ledger -> immediate group name (used to group report lines).</summary>
    private static async Task<Dictionary<int, string>> GroupNamesAsync(Microsoft.Data.SqlClient.SqlConnection c, CancellationToken ct) =>
        (await c.QueryAsync<(int LedgerId, string GroupName)>(new CommandDefinition("""
            SELECT l.LedgerId, g.LedgerName FROM acc.Ledger l JOIN acc.Ledger g ON g.LedgerId = l.ParentLedgerId
            """, cancellationToken: ct))).ToDictionary(x => x.LedgerId, x => x.GroupName);

    /// <summary>Whether the user may report on a branch (null = all branches: administrators only).</summary>
    public async Task<bool> CanAccessBranchAsync(int userId, int? branchId, bool isAdmin, CancellationToken ct)
    {
        if (branchId is null) return isAdmin;
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sec.UserBranch WHERE UserId = @userId AND BranchId = @branchId", new { userId, branchId }, cancellationToken: ct)) > 0;
    }
}
