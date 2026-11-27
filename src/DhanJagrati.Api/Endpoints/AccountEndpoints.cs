using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Accounts;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Api.Endpoints;

/// <summary>ACCOUNT menu: groups & ledgers, journal vouchers, cash book, trial balance, P&L, balance sheet.</summary>
public static class AccountEndpoints
{
    private static readonly string[] AccountRoles = ["ADMIN", "MANAGER", "ACCOUNTANT"];

    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var acc = app.MapGroup("/api/accounts").WithTags("Accounts")
            .RequireAuthorization()
            .AddEndpointFilter<PasswordChangedFilter>()
            .AddEndpointFilter(new RoleFilter(AccountRoles));

        // ---- Groups & ledgers ----
        acc.MapGet("/natures", (LedgerRepository repo, CancellationToken ct) => repo.NaturesAsync(ct));
        acc.MapGet("/ledgers", (HttpContext http, LedgerRepository repo, CancellationToken ct) => repo.ListAsync(http.Session().BranchId, ct));
        acc.MapPost("/ledgers", async (LedgerInput input, HttpContext http, LedgerRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var id = await repo.CreateAsync(s.BranchId, s.UserId, input, s.BusinessDate, ct);
            return Results.Created($"/api/accounts/ledgers/{id}", new { id });
        });
        acc.MapPut("/ledgers/{id:int}", async (int id, LedgerInput input, HttpContext http, LedgerRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return await repo.UpdateAsync(s.BranchId, id, input, s.BusinessDate, ct) ? Results.NoContent() : Results.NotFound();
        });
        acc.MapDelete("/ledgers/{id:int}", async (int id, LedgerRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        // ---- Vouchers (Ledger Transaction Journal) ----
        acc.MapGet("/vouchers", (DateOnly? from, DateOnly? to, string? search, bool? manualOnly, HttpContext http, VoucherRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return repo.SearchAsync(s.BranchId, from ?? s.BusinessDate.AddDays(-30), to ?? s.BusinessDate, search, manualOnly ?? false, ct);
        });
        acc.MapGet("/vouchers/{id:long}", async (long id, HttpContext http, VoucherRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, id, ct) is { } v ? Results.Ok(v) : Results.NotFound());
        acc.MapGet("/vouchers/navigate", async (Navigate direction, string? from, HttpContext http, VoucherRepository repo, CancellationToken ct) =>
            await repo.NavigateAsync(http.Session().BranchId, direction, from, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound());
        acc.MapPost("/vouchers", async (VoucherInput input, HttpContext http, VoucherRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            var id = await repo.CreateAsync(s.BranchId, s.UserId, input, s.BusinessDate, clock.Today, ct);
            return Results.Created($"/api/accounts/vouchers/{id}", new { id });
        });
        acc.MapPost("/vouchers/{id:long}/cancel", async (long id, HttpContext http, VoucherRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            await repo.CancelAsync(s.BranchId, s.UserId, id, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter("ADMIN", "MANAGER"));

        // ---- Reports (branchId = 0 means all branches, administrators only) ----
        acc.MapGet("/reports/cash-book", async (int? branchId, DateOnly? from, DateOnly? to, int? cashLedgerId, int? groupLedgerId, string? mode,
            HttpContext http, ReportRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var branch = branchId is null or 0 ? s.BranchId : branchId.Value;
            if (!await repo.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)) return Forbidden();
            var m = mode?.ToUpperInvariant() is "DETAIL" or "LEDGER" ? mode.ToUpperInvariant() : "SUMMARY";
            return Results.Ok(await repo.CashBookAsync(branch, from ?? s.BusinessDate, to ?? s.BusinessDate, cashLedgerId, groupLedgerId, m, ct));
        });

        acc.MapGet("/reports/trial-balance", async (int? branchId, DateOnly? from, DateOnly? to, HttpContext http, ReportRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var branch = Branch(branchId, s.BranchId);
            if (!await repo.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)) return Forbidden();
            return Results.Ok(await repo.TrialBalanceAsync(branch, from ?? FinancialYearStart(s.BusinessDate), to ?? s.BusinessDate, ct));
        });

        acc.MapGet("/reports/profit-loss", async (int? branchId, DateOnly? from, DateOnly? to, HttpContext http, ReportRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var branch = Branch(branchId, s.BranchId);
            if (!await repo.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)) return Forbidden();
            return Results.Ok(await repo.ProfitLossAsync(branch, from ?? FinancialYearStart(s.BusinessDate), to ?? s.BusinessDate, ct));
        });

        acc.MapGet("/reports/balance-sheet", async (int? branchId, DateOnly? to, HttpContext http, ReportRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var branch = Branch(branchId, s.BranchId);
            if (!await repo.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)) return Forbidden();
            return Results.Ok(await repo.BalanceSheetAsync(branch, to ?? s.BusinessDate, ct));
        });

        acc.MapGet("/reports/ledger-statement", async (int ledgerId, int? branchId, DateOnly? from, DateOnly? to, HttpContext http, ReportRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            var branch = Branch(branchId, s.BranchId);
            if (!await repo.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)) return Forbidden();
            return Results.Ok(await repo.LedgerStatementAsync(branch, ledgerId, from ?? s.BusinessDate.AddDays(-30), to ?? s.BusinessDate, ct));
        });
    }

    /// <summary>null -> session branch; 0 -> all branches.</summary>
    private static int? Branch(int? requested, int session) => requested switch
    {
        null => session,
        0 => null,
        _ => requested
    };

    /// <summary>Indian financial year starts on 1 April.</summary>
    private static DateOnly FinancialYearStart(DateOnly d) => new(d.Month >= 4 ? d.Year : d.Year - 1, 4, 1);

    private static IResult Forbidden() =>
        Results.Problem(title: "You do not have access to this branch (all-branch reports are for administrators).", statusCode: StatusCodes.Status403Forbidden);
}
