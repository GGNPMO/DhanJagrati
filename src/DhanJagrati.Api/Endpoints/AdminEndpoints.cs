using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Core.Loans;
using DhanJagrati.Data.Admin;

namespace DhanJagrati.Api.Endpoints;

public sealed record UpdateSettingRequest(string? Value);
public sealed record DayClosingRequest(DateOnly? BusinessDate, string? Remarks);
public sealed record EmiPreviewRequest(decimal Amount, char Gender, decimal? Rate, decimal? ManualEmi, DateOnly? FirstDueDate);
public sealed record EmiPreviewResponse(
    string Method, decimal Rate, decimal Emi, int Installments, decimal TotalRepayable, decimal TotalInterest,
    decimal ProcessingFee, bool AllowEmiOverride, IReadOnlyList<ScheduleLine> Schedule);

public static class AdminEndpoints
{
    private static readonly string[] AdminRoles = ["ADMIN"];
    private static readonly string[] ManagerRoles = ["ADMIN", "MANAGER"];

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        // ---- Settings (module switches, EMI defaults) ----
        api.MapGet("/settings", (AdminRepository repo, CancellationToken ct) => repo.GetSettingsAsync(ct)).WithTags("Settings");
        api.MapPut("/settings/{key}", async (string key, UpdateSettingRequest body, HttpContext http, AdminRepository repo, CancellationToken ct) =>
        {
            var settings = await repo.GetSettingsAsync(ct);
            var setting = settings.FirstOrDefault(s => s.SettingKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (setting is null) return Results.NotFound();
            var value = body.Value?.Trim() ?? "";
            if (!IsValidSetting(setting.DataType, value))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = [$"Expected a {setting.DataType} value."] });
            await repo.UpdateSettingAsync(setting.SettingKey, value, http.Session().UserId, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(AdminRoles)).WithTags("Settings");

        // ---- MASTER > Date Closing by User ----
        var closing = api.MapGroup("/day-closing").WithTags("Day closing");
        closing.MapGet("", (HttpContext http, AdminRepository repo, CancellationToken ct) =>
            repo.GetDayClosingsAsync(http.Session().BranchId, ct));
        closing.MapPost("", async (DayClosingRequest body, HttpContext http, AdminRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            var date = body.BusinessDate ?? s.BusinessDate;
            if (date > clock.Today)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["businessDate"] = ["Cannot close a future date."] });
            await repo.CloseDayAsync(s.BranchId, date, s.UserId, body.Remarks?.Trim() is { Length: > 0 } r ? r[..Math.Min(r.Length, 250)] : null, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));
        closing.MapDelete("/{date}", async (DateOnly date, HttpContext http, AdminRepository repo, CancellationToken ct) =>
            await repo.ReopenDayAsync(http.Session().BranchId, date, ct) ? Results.NoContent() : Results.NotFound())
            .AddEndpointFilter(new RoleFilter(AdminRoles));

        // ---- Loan plan EMI preview (uses the plan's configurable EMI method) ----
        api.MapPost("/loan-plans/{planId:int}/emi-preview", async (int planId, EmiPreviewRequest body,
            HttpContext http, AdminRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var plan = await repo.GetPlanEmiConfigAsync(planId, s.BusinessDate, ct);
            if (plan is null) return Results.NotFound();

            var rate = body.Rate ?? (char.ToUpperInvariant(body.Gender) == 'F' ? plan.RateFemale : plan.RateMale)
                ?? throw new EmiCalculationException($"No interest rate is effective for plan {plan.PlanCode} on {s.BusinessDate:dd-MMM-yyyy}.");

            var config = new EmiPlanConfig(
                EmiCodes.ParseMethod(plan.EmiCalcMethod), plan.EmiFactor, EmiCodes.ParseRateBasis(plan.RateBasis),
                EmiCodes.ParseRounding(plan.EmiRounding), plan.EmiRoundTo, EmiCodes.ParseSplit(plan.InterestSplit));
            var frequency = EmiCodes.ParseFrequency(plan.EmiMode);
            var result = EmiCalculator.Calculate(config,
                new EmiRequest(body.Amount, rate, plan.TenureMonths, plan.NoOfInstallments, frequency, body.ManualEmi));

            var firstDue = body.FirstDueDate ?? EmiScheduleBuilder.DueDate(s.BusinessDate, frequency, 2);
            var schedule = EmiScheduleBuilder.Build(body.Amount, result.Emi, result.Installments, config.Split, frequency, firstDue);
            var processingFee = Math.Round(body.Amount * plan.ProcessingFeePct / 100m, 2, MidpointRounding.AwayFromZero);

            return Results.Ok(new EmiPreviewResponse(plan.EmiCalcMethod, rate, result.Emi, result.Installments,
                result.TotalRepayable, result.TotalInterest, processingFee, plan.AllowEmiOverride, schedule));
        }).WithTags("Loan plans");
    }

    private static bool IsValidSetting(string dataType, string value) => dataType switch
    {
        "BOOL" => value is "true" or "false",
        "INT" => int.TryParse(value, out _),
        "DECIMAL" => decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _),
        "DATE" => DateOnly.TryParseExact(value, "yyyy-MM-dd", out _),
        _ => value.Length is > 0 and <= 400
    };
}
