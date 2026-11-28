using System.Text.Json;
using DhanJagrati.Api.Endpoints;
using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data;
using DhanJagrati.Data.Admin;
using DhanJagrati.Data.Employees;
using DhanJagrati.Data.Loans;
using DhanJagrati.Data.Masters;
using DhanJagrati.Data.Members;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DhanJagrati")
    ?? throw new InvalidOperationException(
        "Connection string 'DhanJagrati' is missing. Set it with: dotnet user-secrets set \"ConnectionStrings:DhanJagrati\" \"<connection string>\" --project src/DhanJagrati.Api");

DapperSetup.Configure();
builder.Services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BusinessClock>();
builder.Services.AddSingleton<IPasswordHasher<AppUserAccount>, PasswordHasher<AppUserAccount>>();
builder.Services.AddScoped<SecurityRepository>();
builder.Services.AddScoped<MasterRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<MemberRepository>();
builder.Services.AddScoped<EmployeeRepository>();
builder.Services.AddScoped<AttendanceRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<DashboardRepository>();
builder.Services.AddScoped<LoanTermsService>();
builder.Services.AddScoped<LoanRepository>();
builder.Services.AddScoped<LoanApprovalService>();
builder.Services.AddScoped<LoanCardRepository>();
builder.Services.AddScoped<DhanJagrati.Data.Recovery.RecoveryService>();
builder.Services.AddScoped<DhanJagrati.Data.Recovery.SettlementService>();
builder.Services.AddScoped<DhanJagrati.Data.Savings.SavingRepository>();
builder.Services.AddScoped<DhanJagrati.Data.Accounts.LedgerRepository>();
builder.Services.AddScoped<DhanJagrati.Data.Accounts.VoucherRepository>();
builder.Services.AddScoped<DhanJagrati.Data.Accounts.ReportRepository>();
builder.Services.AddScoped<LoanReportRepository>();
builder.Services.AddSingleton<FileStore>();

builder.Services.AddHardening(builder.Configuration, builder.Environment);
builder.Services.AddSessionAuth(builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

// One-time setup: dotnet DhanJagrati.Api.dll create-admin [--user SYSTEM] [--reset]
if (args.Length > 0 && args[0].Equals(CreateAdminCommand.Name, StringComparison.OrdinalIgnoreCase))
    return await CreateAdminCommand.RunAsync(app.Services, args);

app.UseExceptionHandler();
app.UseHardening();

// Single-site hosting: the built React app (src\DhanJagrati.Web\dist copied to wwwroot) is served by the API,
// so browser and API share one origin. In development Vite serves the UI instead and wwwroot may be empty.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // index.html must never be cached so new releases load immediately; hashed assets can be.
        ctx.Context.Response.Headers.CacheControl = ctx.File.Name == "index.html" ? "no-cache" : "public,max-age=604800";
    }
});
app.UseStatusCodePages();
app.UseMiddleware<ClientHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/api/health", async (AdminRepository repo, CancellationToken ct) =>
{
    var database = await repo.CanConnectAsync(ct);
    return Results.Json(new { status = database ? "ok" : "degraded", database },
        statusCode: database ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapMasterEndpoints();
app.MapAdminEndpoints();
app.MapMemberEndpoints();
app.MapEmployeeEndpoints();
app.MapUserEndpoints();
app.MapLoanEndpoints();
app.MapRecoveryEndpoints();
app.MapAccountEndpoints();
app.MapLoanReportEndpoints();

// Unknown /api paths stay 404; every other path is a client-side route of the React app.
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

await app.RunAsync();
return 0;

/// <summary>Entry point type, exposed for WebApplicationFactory in the API tests.</summary>
public partial class Program;
