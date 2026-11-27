using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Accounts;
using DhanJagrati.Data.Loans;

namespace DhanJagrati.Api.Endpoints;

/// <summary>LOAN MASTER reports 7.1 - 7.5 and EMPLOYEE > employee-wise loan report.</summary>
public static class LoanReportEndpoints
{
    private static readonly string[] ReportRoles = ["ADMIN", "MANAGER", "ACCOUNTANT", "OPERATOR"];

    public static void MapLoanReportEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/loan-reports").WithTags("Loan reports")
            .RequireAuthorization()
            .AddEndpointFilter<PasswordChangedFilter>()
            .AddEndpointFilter(new RoleFilter(ReportRoles));

        g.MapGet("/applications", async (int? branchId, DateOnly? from, DateOnly? to, string? reportType, string? loanNo, string? memberNo, int? planId,
            HttpContext http, LoanReportRepository repo, ReportRepository access, CancellationToken ct) =>
        {
            var (branch, denied) = await BranchAsync(http, access, branchId, ct);
            if (denied is not null) return denied;
            var s = http.Session();
            return Results.Ok(await repo.ApplicationsAsync(branch, from ?? MonthStart(s.BusinessDate), to ?? s.BusinessDate, reportType, loanNo, memberNo, planId, ct));
        });

        g.MapGet("/recovery-due", async (int? branchId, DateOnly? from, DateOnly? to, string? employeeCode, int? groupId, string? emiMode,
            HttpContext http, LoanReportRepository repo, ReportRepository access, CancellationToken ct) =>
        {
            var (branch, denied) = await BranchAsync(http, access, branchId, ct);
            if (denied is not null) return denied;
            var s = http.Session();
            return Results.Ok(await repo.RecoveryDueAsync(branch, from ?? s.BusinessDate, to ?? s.BusinessDate, employeeCode, groupId, emiMode, ct));
        });

        g.MapGet("/recovery-ledger", async (int? branchId, DateOnly? from, DateOnly? to, string? employeeCode, string? loanNo,
            HttpContext http, LoanReportRepository repo, ReportRepository access, CancellationToken ct) =>
        {
            var (branch, denied) = await BranchAsync(http, access, branchId, ct);
            if (denied is not null) return denied;
            var s = http.Session();
            return Results.Ok(await repo.RecoveryLedgerAsync(branch, from ?? s.BusinessDate, to ?? s.BusinessDate, employeeCode, loanNo, ct));
        });

        g.MapGet("/closures", async (int? branchId, DateOnly? from, DateOnly? to, string? loanNo,
            HttpContext http, LoanReportRepository repo, ReportRepository access, CancellationToken ct) =>
        {
            var (branch, denied) = await BranchAsync(http, access, branchId, ct);
            if (denied is not null) return denied;
            var s = http.Session();
            return Results.Ok(await repo.ClosuresAsync(branch, from ?? MonthStart(s.BusinessDate), to ?? s.BusinessDate, loanNo, ct));
        });

        // Company-wide (advisors span branches): managers and administrators only
        g.MapGet("/employee-wise", (string? employeeCode, string? employeeName, int? rankId, LoanReportRepository repo, CancellationToken ct) =>
            repo.EmployeeWiseAsync(employeeCode, employeeName, rankId, ct))
            .AddEndpointFilter(new RoleFilter("ADMIN", "MANAGER"));
    }

    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    /// <summary>Loan reports are per branch: null / 0 = session branch; otherwise the user must have access.</summary>
    private static async Task<(int Branch, IResult? Denied)> BranchAsync(HttpContext http, ReportRepository access, int? branchId, CancellationToken ct)
    {
        var s = http.Session();
        var branch = branchId is null or 0 ? s.BranchId : branchId.Value;
        return await access.CanAccessBranchAsync(s.UserId, branch, s.RoleCode == "ADMIN", ct)
            ? (branch, null)
            : (branch, Results.Problem(title: "You do not have access to this branch.", statusCode: StatusCodes.Status403Forbidden));
    }
}
