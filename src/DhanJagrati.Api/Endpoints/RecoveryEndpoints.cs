using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Admin;
using DhanJagrati.Data.Recovery;
using DhanJagrati.Data.Savings;

namespace DhanJagrati.Api.Endpoints;

public sealed record ReverseRequest(DateOnly? Date);
public sealed record ReopenRequest(int LoanId, DateOnly? Date, string? Remarks);
public sealed record CloseAccountRequest(DateOnly? Date);

/// <summary>LOAN MASTER: recovery (single / bulk), settlement; SAVING module.</summary>
public static class RecoveryEndpoints
{
    private static readonly string[] CollectRoles = ["ADMIN", "MANAGER", "OPERATOR", "FIELD_OFFICER"];
    private static readonly string[] ManagerRoles = ["ADMIN", "MANAGER"];

    public static void MapRecoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        // ---- Single recovery ----
        var rec = api.MapGroup("/recoveries").WithTags("Recovery");

        rec.MapGet("/prefill", async (int? loanId, string? loanNo, DateOnly? date, HttpContext http, RecoveryService service, CancellationToken ct) =>
        {
            var s = http.Session();
            return await service.PrefillAsync(s.BranchId, loanId, loanNo, date ?? s.BusinessDate, ct) is { } p ? Results.Ok(p) : Results.NotFound();
        });

        rec.MapGet("", (int loanId, HttpContext http, RecoveryService service, CancellationToken ct) =>
            service.HistoryAsync(http.Session().BranchId, loanId, ct));

        rec.MapPost("", async (RecoveryInput input, HttpContext http, RecoveryService service, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return Results.Ok(await service.PostAsync(s.BranchId, s.UserId, input, s.BusinessDate, clock.Today, ct));
        }).AddEndpointFilter(new RoleFilter(CollectRoles));

        rec.MapPost("/receipts/{receiptNo}/reverse", async (string receiptNo, ReverseRequest body, HttpContext http, RecoveryService service, CancellationToken ct) =>
        {
            var s = http.Session();
            var loanNo = await service.ReverseAsync(s.BranchId, s.UserId, receiptNo, body.Date ?? s.BusinessDate, ct);
            return Results.Ok(new { loanNo });
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));

        // ---- Bulk recovery ----
        rec.MapGet("/bulk-due", (string? employeeCode, int? groupId, DateOnly? from, DateOnly? to, HttpContext http, RecoveryService service, CancellationToken ct) =>
        {
            var s = http.Session();
            return service.BulkDueAsync(s.BranchId, employeeCode, groupId, from ?? s.BusinessDate.AddDays(-30), to ?? s.BusinessDate, ct);
        });

        rec.MapPost("/bulk", async (BulkRecoveryInput input, HttpContext http, RecoveryService service, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return Results.Ok(await service.BulkPostAsync(s.BranchId, s.UserId, input, s.BusinessDate, clock.Today, ct));
        }).AddEndpointFilter(new RoleFilter(CollectRoles));

        // ---- Settlement ----
        var set = api.MapGroup("/settlements").WithTags("Settlement");

        set.MapGet("/quote", async (int? loanId, string? loanNo, DateOnly? date, HttpContext http, SettlementService service, CancellationToken ct) =>
        {
            var s = http.Session();
            return await service.QuoteAsync(s.BranchId, loanId, loanNo, date ?? s.BusinessDate, ct) is { } q ? Results.Ok(q) : Results.NotFound();
        });

        set.MapPost("/close", async (SettlementInput input, HttpContext http, SettlementService service, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return Results.Ok(await service.CloseAsync(s.BranchId, s.UserId, input, s.BusinessDate, clock.Today, ct));
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));

        set.MapPost("/reopen", async (ReopenRequest body, HttpContext http, SettlementService service, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return Results.Ok(await service.ReopenAsync(s.BranchId, s.UserId, body.LoanId, body.Date ?? s.BusinessDate, body.Remarks, clock.Today, ct));
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));

        // ---- Saving module (only when MODULE.SAVING.ENABLED = true) ----
        var sav = api.MapGroup("/savings").WithTags("Saving").AddEndpointFilter<SavingEnabledFilter>();

        sav.MapGet("/products", (SavingRepository repo, CancellationToken ct) => repo.ProductsAsync(ct));

        sav.MapGet("/accounts", (string? search, string? memberNo, HttpContext http, SavingRepository repo, CancellationToken ct) =>
            repo.SearchAsync(http.Session().BranchId, search, memberNo, ct));

        sav.MapGet("/accounts/lookup", async (string accountNo, HttpContext http, SavingRepository repo, CancellationToken ct) =>
            await repo.FindByNumberAsync(http.Session().BranchId, accountNo, ct) is { } a ? Results.Ok(a) : Results.NotFound());

        sav.MapGet("/accounts/{id:int}", async (int id, HttpContext http, SavingRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, id, ct) is { } a ? Results.Ok(a) : Results.NotFound());

        sav.MapPost("/accounts", async (SavingAccountInput input, HttpContext http, SavingRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            input.OpenDate ??= s.BusinessDate;
            var id = await repo.OpenAsync(s.BranchId, s.UserId, input, s.BusinessDate, ct);
            return Results.Created($"/api/savings/accounts/{id}", new { id });
        }).AddEndpointFilter(new RoleFilter(CollectRoles));

        sav.MapPost("/accounts/{id:int}/transactions", async (int id, SavingTxnInput input, HttpContext http, SavingRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return Results.Ok(await repo.TransactAsync(s.BranchId, s.UserId, id, input, s.BusinessDate, clock.Today, ct));
        }).AddEndpointFilter(new RoleFilter(CollectRoles));

        sav.MapPost("/accounts/{id:int}/close", async (int id, CloseAccountRequest body, HttpContext http, SavingRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            await repo.CloseAsync(s.BranchId, s.UserId, id, body.Date ?? s.BusinessDate, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));

        sav.MapGet("/accounts/{id:int}/statement", (int id, DateOnly? from, DateOnly? to, HttpContext http, SavingRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return repo.StatementAsync(s.BranchId, id, from ?? s.BusinessDate.AddMonths(-3), to ?? s.BusinessDate, ct);
        });
    }
}

/// <summary>Saving endpoints answer only while the module is switched on in Settings.</summary>
public sealed class SavingEnabledFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var admin = context.HttpContext.RequestServices.GetRequiredService<AdminRepository>();
        var settings = await admin.GetSettingsAsync(context.HttpContext.RequestAborted);
        var enabled = settings.FirstOrDefault(s => s.SettingKey == "MODULE.SAVING.ENABLED")?.SettingValue == "true";
        return enabled
            ? await next(context)
            : Results.Problem(title: "The Saving module is switched off (Help > Settings).", statusCode: StatusCodes.Status403Forbidden);
    }
}
