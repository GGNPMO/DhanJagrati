using System.Security.Claims;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DhanJagrati.Api.Infrastructure;

public static class SessionAuth
{
    public const string SessionIdClaim = "dj:sid";
    private const string SessionItemKey = "dj:session";
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(60);

    public static IServiceCollection AddSessionAuth(this IServiceCollection services, IWebHostEnvironment env)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "dj.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                // Development runs over plain http behind Vite; everywhere else the cookie is HTTPS-only.
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.ExpireTimeSpan = TimeSpan.FromHours(10);
                o.SlidingExpiration = true;
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; },
                    OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; },
                    OnValidatePrincipal = ValidateSessionAsync
                };
            });
        services.AddAuthorization();
        return services;
    }

    /// <summary>Every request re-checks the server-side session; branch and business date come from it.</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (!Guid.TryParse(principal?.FindFirstValue(SessionIdClaim), out var sessionId)
            || !int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            ctx.RejectPrincipal();
            return;
        }

        var repo = ctx.HttpContext.RequestServices.GetRequiredService<SecurityRepository>();
        var session = await repo.GetActiveSessionAsync(sessionId, userId, IdleTimeout, ctx.HttpContext.RequestAborted);
        if (session is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        ctx.HttpContext.Items[SessionItemKey] = session;
    }

    public static ClaimsPrincipal CreatePrincipal(UserRecord user, Guid sessionId) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(SessionIdClaim, sessionId.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>The validated session for this request (only on endpoints that require authorization).</summary>
    public static SessionInfo Session(this HttpContext http) =>
        http.Items[SessionItemKey] as SessionInfo
        ?? throw new InvalidOperationException("No active session on this request.");

    public static bool HasRole(this SessionInfo session, IEnumerable<string> roles) =>
        roles.Contains(session.RoleCode, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Blocks everything except HELP > Change Password while a password change is pending.</summary>
public sealed class PasswordChangedFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.HttpContext.Session().MustChangePassword)
            return Results.Problem(
                title: "Password change required",
                detail: "Change your password before continuing.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["code"] = "PASSWORD_CHANGE_REQUIRED" });
        return await next(context);
    }
}

public sealed class RoleFilter(params string[] roles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Session().HasRole(roles)
            ? await next(context)
            : Results.Problem(title: "Not permitted", detail: "Your role does not allow this action.", statusCode: StatusCodes.Status403Forbidden);
}
