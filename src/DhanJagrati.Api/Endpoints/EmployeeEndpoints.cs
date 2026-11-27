using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Employees;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Api.Endpoints;

public sealed record AttendanceSaveRequest(DateOnly Date, List<AttendanceEntry> Entries);

/// <summary>EMPLOYEE > Advisor / Loan Relationship Manager, and MASTER > Daily Attendance.</summary>
public static class EmployeeEndpoints
{
    private static readonly string[] EditRoles = ["ADMIN", "MANAGER"];
    private static readonly string[] AttendanceRoles = ["ADMIN", "MANAGER", "OPERATOR"];

    public static void MapEmployeeEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/employees").WithTags("Employees").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        g.MapGet("", (string? search, bool? allBranches, bool? activeOnly, HttpContext http, EmployeeRepository repo, CancellationToken ct) =>
            repo.SearchAsync(allBranches == true ? null : http.Session().BranchId, search, activeOnly ?? false, ct));

        g.MapGet("/{id:int}", async (int id, HttpContext http, EmployeeRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, id, ct) is { } e ? Results.Ok(e) : Results.NotFound());

        g.MapGet("/lookup", async (string code, EmployeeRepository repo, CancellationToken ct) =>
            await repo.FindByCodeAsync(code, ct) is { } e ? Results.Ok(e) : Results.NotFound());

        g.MapGet("/navigate", async (Navigate direction, string? from, HttpContext http, EmployeeRepository repo, CancellationToken ct) =>
            await repo.NavigateAsync(http.Session().BranchId, direction, from, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound());

        g.MapGet("/{id:int}/parent-chain", (int id, EmployeeRepository repo, CancellationToken ct) => repo.GetParentChainAsync(id, ct));

        g.MapPost("", async (EmployeeInput input, HttpContext http, EmployeeRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            input.JoiningDate ??= s.BusinessDate;
            var id = await repo.CreateAsync(s.BranchId, s.UserId, input, clock.Today, ct);
            return Results.Created($"/api/employees/{id}", new { id });
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        g.MapPut("/{id:int}", async (int id, EmployeeInput input, HttpContext http, EmployeeRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return await repo.UpdateAsync(s.BranchId, s.UserId, id, input, clock.Today, ct) ? Results.NoContent() : Results.NotFound();
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        g.MapDelete("/{id:int}", async (int id, HttpContext http, EmployeeRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(http.Session().BranchId, id, ct) ? Results.NoContent() : Results.NotFound())
            .AddEndpointFilter(new RoleFilter(EditRoles));

        // ---- Daily attendance ----
        var attendance = app.MapGroup("/api/attendance").WithTags("Attendance").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        attendance.MapGet("", (DateOnly? date, HttpContext http, AttendanceRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return repo.GetDayAsync(s.BranchId, date ?? s.BusinessDate, ct);
        });

        attendance.MapPut("", async (AttendanceSaveRequest body, HttpContext http, AttendanceRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            if (body.Date > clock.Today)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["date"] = ["Cannot mark attendance for a future date."] });
            var s = http.Session();
            await repo.SaveDayAsync(s.BranchId, body.Date, body.Entries ?? [], s.UserId, ct);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(AttendanceRoles));
    }
}
