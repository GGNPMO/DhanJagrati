using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Identity;

namespace DhanJagrati.Api.Endpoints;

public sealed record ResetPasswordRequest(string? TemporaryPassword);

/// <summary>User Master (administrators): logins, roles, branch access, password reset.</summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").WithTags("Users")
            .RequireAuthorization()
            .AddEndpointFilter<PasswordChangedFilter>()
            .AddEndpointFilter(new RoleFilter("ADMIN"));

        g.MapGet("", (UserRepository repo, CancellationToken ct) => repo.ListAsync(ct));
        g.MapGet("/branches", (UserRepository repo, CancellationToken ct) => repo.AllBranchesAsync(ct));

        g.MapPost("", async (UserInput input, UserRepository repo, IPasswordHasher<AppUserAccount> hasher, CancellationToken ct) =>
        {
            UserRepository.Validate(input, isNew: true, PasswordPolicy.IsStrong, PasswordPolicy.Description);
            var id = await repo.CreateAsync(input, hasher.HashPassword(new AppUserAccount(), input.TemporaryPassword!), ct);
            return Results.Created($"/api/users/{id}", new { id });
        });

        g.MapPut("/{id:int}", async (int id, UserInput input, HttpContext http, UserRepository repo, CancellationToken ct) =>
        {
            UserRepository.Validate(input, isNew: false, PasswordPolicy.IsStrong, PasswordPolicy.Description);
            if (id == http.Session().UserId && (!input.IsActive || input.RoleCode != "ADMIN"))
                return Results.Problem(title: "You cannot deactivate yourself or remove your own administrator role.", statusCode: 400);
            return await repo.UpdateAsync(id, input, ct) ? Results.NoContent() : Results.NotFound();
        });

        g.MapPost("/{id:int}/reset-password", async (int id, ResetPasswordRequest body, HttpContext http,
            UserRepository repo, SecurityRepository security, IPasswordHasher<AppUserAccount> hasher, CancellationToken ct) =>
        {
            if (!PasswordPolicy.IsStrong(body.TemporaryPassword))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["temporaryPassword"] = [PasswordPolicy.Description] });
            if (!await repo.ResetPasswordAsync(id, hasher.HashPassword(new AppUserAccount(), body.TemporaryPassword!), ct))
                return Results.NotFound();
            var s = http.Session();
            await security.AuditAsync($"{s.UserName}>{id}", "PASSWORD_CHANGE", s.BranchId, http.Connection.RemoteIpAddress?.ToString(), ct);
            return Results.NoContent();
        });
    }
}
