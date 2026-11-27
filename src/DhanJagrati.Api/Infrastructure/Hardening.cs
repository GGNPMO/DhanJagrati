using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;

namespace DhanJagrati.Api.Infrastructure;

/// <summary>Production hardening: rate limits, security headers, key persistence, proxy headers, HTTPS-only cookies.</summary>
public static class Hardening
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddHardening(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        // Brute-force protection on top of the per-account lockout: 10 login attempts per minute per client IP.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LoginPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (ctx, ct) =>
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, title = "Too many login attempts. Wait a minute and try again." }, ct);
        });

        // Cookie-encryption keys survive restarts / app-pool recycles (otherwise everyone is logged out).
        var keysPath = config["App:KeysPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "keys");
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection().SetApplicationName("DhanJagrati").PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        // Running behind IIS / a reverse proxy on the same machine: trust its X-Forwarded-* headers.
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Add(IPAddress.Loopback);
            o.KnownProxies.Add(IPAddress.IPv6Loopback);
        });

        services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 2 * 1024 * 1024);

        if (!env.IsDevelopment())
            services.AddHsts(o => o.MaxAge = TimeSpan.FromDays(180));

        return services;
    }

    public static WebApplication UseHardening(this WebApplication app)
    {
        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment()) app.UseHsts();

        app.Use(async (http, next) =>
        {
            var h = http.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            if (http.Request.Path.StartsWithSegments("/api"))
            {
                h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                h["Cache-Control"] = "no-store";
            }
            await next();
        });

        app.UseRateLimiter();
        return app;
    }
}
