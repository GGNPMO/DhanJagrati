using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Admin;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace DhanJagrati.Api.Endpoints;

public sealed record LoginRequest(string? UserName, string? Password);
public sealed record ChangePasswordRequest(string? UserName, string? OldPassword, string? NewPassword, string? ConfirmPassword);
public sealed record SelectBranchRequest(int BranchId, DateOnly BusinessDate);

public sealed record MeResponse(
    string UserName, string DisplayName, string Role,
    int BranchId, string BranchCode, string BranchName, DateOnly BusinessDate, DateOnly Today,
    bool MustChangePassword, string? CompanyName, string? CompanyAddress, IReadOnlyList<SettingValue> Settings);

/// <summary>Key/value pair (not a dictionary, so the JSON key policy does not rewrite setting keys).</summary>
public sealed record SettingValue(string Key, string Value);

/// <summary>HELP menu: login, change password, select branch.</summary>
public static class AuthEndpoints
{
    private const int MaxFailedLogins = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly string[] BackDateRoles = ["ADMIN", "MANAGER"];

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");

        auth.MapPost("/login", LoginAsync).AllowAnonymous();

        var secured = auth.MapGroup("").RequireAuthorization();
        secured.MapPost("/logout", LogoutAsync);
        secured.MapGet("/me", MeAsync);
        secured.MapPost("/change-password", ChangePasswordAsync);
        secured.MapGet("/branches", (HttpContext http, SecurityRepository repo, CancellationToken ct) =>
            repo.GetUserBranchesAsync(http.Session().UserId, ct)).AddEndpointFilter<PasswordChangedFilter>();
        secured.MapPost("/select-branch", SelectBranchAsync).AddEndpointFilter<PasswordChangedFilter>();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, HttpContext http, SecurityRepository repo,
        IPasswordHasher<AppUserAccount> hasher, BusinessClock clock, CancellationToken ct)
    {
        var userName = request.UserName?.Trim() ?? "";
        var ip = http.Connection.RemoteIpAddress?.ToString();
        if (userName.Length == 0 || string.IsNullOrEmpty(request.Password))
            return Results.Problem(title: "Enter user name and password.", statusCode: 400);

        var user = await repo.FindUserAsync(userName, ct);
        if (user is { LockedUntil: { } lockedUntil } && lockedUntil > DateTime.UtcNow)
        {
            await repo.AuditAsync(userName, "LOGIN_FAIL", null, ip, ct);
            return Results.Problem(title: "Account temporarily locked after repeated failed logins. Try again later.", statusCode: 423);
        }

        var verified = user is { IsActive: true, PasswordHash: not null }
            ? hasher.VerifyHashedPassword(new AppUserAccount(), user.PasswordHash, request.Password)
            : PasswordVerificationResult.Failed;

        if (user is null || verified == PasswordVerificationResult.Failed)
        {
            if (user is not null) await repo.RecordFailedLoginAsync(user.UserId, MaxFailedLogins, LockDuration, ct);
            await repo.AuditAsync(userName, "LOGIN_FAIL", null, ip, ct);
            return Results.Problem(title: "Invalid user name or password.", statusCode: 401);
        }

        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
            await repo.SetPasswordAsync(user.UserId, hasher.HashPassword(new AppUserAccount(), request.Password), user.MustChangePassword, ct);

        var sessionId = await repo.StartSessionAsync(user, clock.Today, ip, ct);
        await repo.AuditAsync(user.UserName, "LOGIN_OK", user.DefaultBranchId, ip, ct);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, SessionAuth.CreatePrincipal(user, sessionId));
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, SecurityRepository repo, CancellationToken ct)
    {
        var session = http.Session();
        await repo.EndSessionAsync(session.SessionId, ct);
        await repo.AuditAsync(session.UserName, "LOGOUT", session.BranchId, http.Connection.RemoteIpAddress?.ToString(), ct);
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    private static async Task<MeResponse> MeAsync(
        HttpContext http, SecurityRepository security, AdminRepository admin, BusinessClock clock, CancellationToken ct)
    {
        var s = http.Session();
        var company = await security.GetCompanyAsync(ct);
        var settings = await admin.GetSettingsAsync(ct);
        return new MeResponse(
            s.UserName, s.DisplayName, s.RoleCode, s.BranchId, s.BranchCode, s.BranchName, s.BusinessDate, clock.Today,
            s.MustChangePassword, company?.CompanyName,
            company is null ? null : string.Join(", ", new[] { company.AddressLine, company.City, company.PinCode }.Where(x => !string.IsNullOrWhiteSpace(x))),
            settings.Select(x => new SettingValue(x.SettingKey, x.SettingValue)).ToList());
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, HttpContext http, SecurityRepository repo,
        IPasswordHasher<AppUserAccount> hasher, CancellationToken ct)
    {
        var s = http.Session();
        var errors = new Dictionary<string, string[]>();
        if (!string.Equals(request.UserName?.Trim(), s.UserName, StringComparison.OrdinalIgnoreCase))
            errors["userName"] = ["You can only change the password of the logged-in user."];
        if (string.IsNullOrEmpty(request.OldPassword))
            errors["oldPassword"] = ["Required."];
        if (!PasswordPolicy.IsStrong(request.NewPassword))
            errors["newPassword"] = [PasswordPolicy.Description];
        else if (request.NewPassword == request.OldPassword)
            errors["newPassword"] = ["New password must differ from the old password."];
        if (request.NewPassword != request.ConfirmPassword)
            errors["confirmPassword"] = ["Passwords do not match."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors, title: "Please correct the highlighted fields.");

        var user = await repo.GetUserAsync(s.UserId, ct);
        if (user?.PasswordHash is null
            || hasher.VerifyHashedPassword(new AppUserAccount(), user.PasswordHash, request.OldPassword!) == PasswordVerificationResult.Failed)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["oldPassword"] = ["Old password is incorrect."] },
                title: "Please correct the highlighted fields.");

        await repo.SetPasswordAsync(s.UserId, hasher.HashPassword(new AppUserAccount(), request.NewPassword!), mustChange: false, ct);
        await repo.AuditAsync(s.UserName, "PASSWORD_CHANGE", s.BranchId, http.Connection.RemoteIpAddress?.ToString(), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SelectBranchAsync(
        SelectBranchRequest request, HttpContext http, SecurityRepository repo, BusinessClock clock, CancellationToken ct)
    {
        var s = http.Session();
        var today = clock.Today;
        if (request.BusinessDate > today)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["businessDate"] = ["Login date cannot be in the future."] });
        if (request.BusinessDate < today && !s.HasRole(BackDateRoles))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["businessDate"] = ["Only managers can work on a back date."] });

        if (!await repo.SwitchBranchAsync(s.SessionId, s.UserId, request.BranchId, request.BusinessDate, ct))
            return Results.Problem(title: "You are not allowed to work in this branch.", statusCode: 403);

        await repo.AuditAsync(s.UserName, "BRANCH_CHANGE", request.BranchId, http.Connection.RemoteIpAddress?.ToString(), ct);
        return Results.NoContent();
    }
}
