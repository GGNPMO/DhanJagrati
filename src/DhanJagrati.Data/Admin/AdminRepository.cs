using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Admin;

public sealed record AppSetting(string SettingKey, string SettingValue, string DataType, string? Description);

public sealed class DayClosingRow
{
    public DateOnly BusinessDate { get; init; }
    public string ClosedByName { get; init; } = "";
    public DateTime ClosedAt { get; init; }
    public string? Remarks { get; init; }
}

public sealed class LoanPlanEmiConfig
{
    public int PlanId { get; init; }
    public string PlanCode { get; init; } = "";
    public string EmiMode { get; init; } = "M";
    public short TenureMonths { get; init; }
    public short NoOfInstallments { get; init; }
    public decimal ProcessingFeePct { get; init; }
    public string EmiCalcMethod { get; init; } = "MANUAL";
    public decimal? EmiFactor { get; init; }
    public string RateBasis { get; init; } = "ANNUAL";
    public string EmiRounding { get; init; } = "NEAREST";
    public decimal EmiRoundTo { get; init; } = 1;
    public string InterestSplit { get; init; } = "FLAT";
    public bool AllowEmiOverride { get; init; }
    public decimal? RateMale { get; init; }
    public decimal? RateFemale { get; init; }
}

public sealed class AdminRepository(IDbConnectionFactory db)
{
    public async Task<IReadOnlyList<AppSetting>> GetSettingsAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<AppSetting>(new CommandDefinition(
            "SELECT SettingKey, SettingValue, DataType, Description FROM sec.AppSetting ORDER BY SettingKey",
            cancellationToken: ct))).AsList();
    }

    public async Task<bool> UpdateSettingAsync(string key, string value, int userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppSetting SET SettingValue = @value, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            WHERE SettingKey = @key
            """, new { key, value, userId }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<DayClosingRow>> GetDayClosingsAsync(int branchId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<DayClosingRow>(new CommandDefinition("""
            SELECT TOP (60) dc.BusinessDate, u.DisplayName AS ClosedByName, dc.ClosedAt, dc.Remarks
            FROM sec.DayClosing dc JOIN sec.AppUser u ON u.UserId = dc.ClosedBy
            WHERE dc.BranchId = @branchId
            ORDER BY dc.BusinessDate DESC
            """, new { branchId }, cancellationToken: ct))).AsList();
    }

    public async Task<bool> IsDayClosedAsync(int branchId, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sec.DayClosing WHERE BranchId = @branchId AND BusinessDate = @date",
            new { branchId, date }, cancellationToken: ct)) > 0;
    }

    public async Task CloseDayAsync(int branchId, DateOnly date, int userId, string? remarks, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT sec.DayClosing (BranchId, BusinessDate, ClosedBy, Remarks) VALUES (@branchId, @date, @userId, @remarks)
                """, new { branchId, date, userId, remarks }, cancellationToken: ct));
        }
        catch (SqlException ex) when (SqlErrors.IsDuplicate(ex))
        {
            throw new DomainException("This date is already closed for the branch.", 409);
        }
    }

    public async Task<bool> ReopenDayAsync(int branchId, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM sec.DayClosing WHERE BranchId = @branchId AND BusinessDate = @date",
            new { branchId, date }, cancellationToken: ct)) == 1;
    }

    /// <summary>Plan EMI configuration plus the interest rate effective on <paramref name="onDate"/>.</summary>
    public async Task<LoanPlanEmiConfig?> GetPlanEmiConfigAsync(int planId, DateOnly onDate, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<LoanPlanEmiConfig>(new CommandDefinition("""
            SELECT p.PlanId, p.PlanCode, p.EmiMode, p.TenureMonths, p.NoOfInstallments, p.ProcessingFeePct,
                   p.EmiCalcMethod, p.EmiFactor, p.RateBasis, p.EmiRounding, p.EmiRoundTo, p.InterestSplit,
                   p.AllowEmiOverride, r.RateMale, r.RateFemale
            FROM mst.LoanPlan p
            OUTER APPLY (SELECT TOP (1) ir.RateMale, ir.RateFemale FROM mst.InterestRate ir
                         WHERE ir.PlanId = p.PlanId AND ir.EffectiveFrom <= @onDate
                           AND (ir.EffectiveTo IS NULL OR ir.EffectiveTo >= @onDate)
                         ORDER BY ir.EffectiveFrom DESC) r
            WHERE p.PlanId = @planId
            """, new { planId, onDate }, cancellationToken: ct));
    }

    public async Task<bool> CanConnectAsync(CancellationToken ct)
    {
        try
        {
            await using var c = await db.OpenAsync(ct);
            return await c.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: ct)) == 1;
        }
        catch (Exception ex) when (ex is SqlException or DatabaseUnavailableException)
        {
            return false;
        }
    }
}
