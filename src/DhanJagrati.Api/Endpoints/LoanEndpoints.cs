using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data;
using DhanJagrati.Data.Accounts;
using DhanJagrati.Data.Admin;
using DhanJagrati.Data.Loans;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Api.Endpoints;

public sealed record ApprovalActionRequest(List<int> LoanIds, DateOnly? Date, string? Remarks);
public sealed record CardPrintRequest(string CardType);

/// <summary>LOAN MASTER: Loan Application, Loan Approve, Loan Card / Signature Card.</summary>
public static class LoanEndpoints
{
    private static readonly string[] EditRoles = ["ADMIN", "MANAGER", "OPERATOR"];
    private static readonly string[] ApproveRoles = ["ADMIN", "MANAGER"];

    public static void MapLoanEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        api.MapGet("/dashboard", (HttpContext http, DashboardRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return repo.GetAsync(s.BranchId, s.BusinessDate, ct);
        }).WithTags("Dashboard");

        api.MapGet("/accounts/cash-bank-ledgers", (IDbConnectionFactory db, CancellationToken ct) => Posting.CashBankLedgersAsync(db, ct))
            .WithTags("Accounts");

        // ---- Loan application ----
        var loans = api.MapGroup("/loans").WithTags("Loans");

        loans.MapGet("", (string? search, string? status, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            repo.SearchAsync(http.Session().BranchId, search, status, ct));

        loans.MapGet("/{id:int}", async (int id, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, id, ct) is { } loan ? Results.Ok(loan) : Results.NotFound());

        loans.MapGet("/{id:int}/schedule", (int id, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            repo.ScheduleAsync(http.Session().BranchId, id, ct));

        loans.MapGet("/navigate", async (Navigate direction, string? from, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            await repo.NavigateAsync(http.Session().BranchId, direction, from, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound());

        loans.MapPost("/calculate", async (LoanTermsRequest request, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            request.Amount <= 0
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["loanAmount"] = ["Enter the loan amount."] })
                : Results.Ok(await repo.PreviewAsync(request, http.Session().BusinessDate, ct)));

        loans.MapPost("", async (LoanInput input, HttpContext http, LoanRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            var id = await repo.CreateAsync(s.BranchId, s.UserId, input, s.BusinessDate, clock.Today, ct);
            return Results.Created($"/api/loans/{id}", new { id });
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        loans.MapPut("/{id:int}", async (int id, LoanInput input, HttpContext http, LoanRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return await repo.UpdateAsync(s.BranchId, s.UserId, id, input, clock.Today, ct) ? Results.NoContent() : Results.NotFound();
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        loans.MapDelete("/{id:int}", async (int id, HttpContext http, LoanRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(http.Session().BranchId, id, ct) ? Results.NoContent() : Results.NotFound())
            .AddEndpointFilter(new RoleFilter(ApproveRoles));

        // ---- Loan approve ----
        var approval = api.MapGroup("/loan-approvals").WithTags("Loan approve");

        approval.MapGet("", (DateOnly? from, DateOnly? to, string? status, string? loanNo, HttpContext http,
            LoanApprovalService service, CancellationToken ct) =>
        {
            var s = http.Session();
            var start = from ?? new DateOnly(s.BusinessDate.Year, s.BusinessDate.Month, 1).AddMonths(-2);
            return service.ListAsync(s.BranchId, start, to ?? s.BusinessDate, status?.ToUpperInvariant() == "APPROVED" ? "APPROVED" : "PENDING", loanNo, ct);
        });

        approval.MapPost("/approve", (ApprovalActionRequest body, HttpContext http, LoanApprovalService service, BusinessClock clock, CancellationToken ct) =>
            RunAsync(body, http, clock, (s, date) => service.ApproveAsync(s.BranchId, s.UserId, body.LoanIds, date, ct)))
            .AddEndpointFilter(new RoleFilter(ApproveRoles));

        approval.MapPost("/disapprove", (ApprovalActionRequest body, HttpContext http, LoanApprovalService service, BusinessClock clock, CancellationToken ct) =>
            RunAsync(body, http, clock, (s, date) => service.DisapproveAsync(s.BranchId, s.UserId, body.LoanIds, date, body.Remarks, ct)))
            .AddEndpointFilter(new RoleFilter(ApproveRoles));

        approval.MapPost("/reject", (ApprovalActionRequest body, HttpContext http, LoanApprovalService service, BusinessClock clock, CancellationToken ct) =>
            RunAsync(body, http, clock, (s, date) => service.RejectAsync(s.BranchId, s.UserId, body.LoanIds, date, body.Remarks, ct)))
            .AddEndpointFilter(new RoleFilter(ApproveRoles));

        // ---- Loan card / signature card ----
        var cards = api.MapGroup("/loan-cards").WithTags("Loan cards");

        cards.MapGet("", (DateOnly? from, DateOnly? to, string? fileNo, string? loanNo, HttpContext http, LoanCardRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return repo.SearchAsync(s.BranchId, from ?? s.BusinessDate.AddDays(-30), to ?? s.BusinessDate, fileNo, loanNo, ct);
        });

        cards.MapGet("/{loanId:int}", async (int loanId, HttpContext http, LoanCardRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, loanId, ct) is { } card ? Results.Ok(card) : Results.NotFound());

        cards.MapPost("/{loanId:int}/printed", async (int loanId, CardPrintRequest body, HttpContext http, LoanCardRepository repo, CancellationToken ct) =>
        {
            var type = body.CardType?.ToUpperInvariant();
            if (type is not ("LOAN" or "SIGNATURE")) return Results.ValidationProblem(new Dictionary<string, string[]> { ["cardType"] = ["LOAN or SIGNATURE."] });
            var s = http.Session();
            await repo.LogPrintAsync(s.BranchId, loanId, type, s.BusinessDate, s.UserId, ct);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> RunAsync(ApprovalActionRequest body, HttpContext http, BusinessClock clock,
        Func<Data.Security.SessionInfo, DateOnly, Task<IReadOnlyList<ApprovalResult>>> action)
    {
        var s = http.Session();
        var date = body.Date ?? s.BusinessDate;
        if (body.LoanIds is not { Count: > 0 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["loanIds"] = ["Select at least one loan."] });
        if (date > clock.Today)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Date cannot be in the future."] });
        return Results.Ok(await action(s, date));
    }
}
