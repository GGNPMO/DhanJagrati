using DhanJagrati.Api;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Microfinance");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Microfinance' is missing. Configure ConnectionStrings:Microfinance or the ConnectionStrings__Microfinance environment variable.");
}

builder.Services.AddScoped(_ => new SqlFinanceStore(connectionString));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/api/health", async (SqlFinanceStore store, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
{
    try
    {
        await store.CheckHealthAsync(cancellationToken);
        return Results.Ok(new { status = "ok", mode = "sql-server" });
    }
    catch (SqlException exception)
    {
        loggerFactory.CreateLogger("Health").LogError(exception, "SQL Server health check failed.");
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unavailable");
    }
});

app.MapGet("/api/branch", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetDefaultBranchAsync(cancellationToken)));

app.MapGet("/api/dashboard", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetDashboardAsync(cancellationToken)));

app.MapGet("/api/members", async (string? search, SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetMembersAsync(search, cancellationToken)));

app.MapPost("/api/members", async (CreateMemberRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.FullName))
    {
        errors["fullName"] = ["Name is required."];
    }

    if (string.IsNullOrWhiteSpace(request.Phone))
    {
        errors["phone"] = ["Primary phone is required."];
    }

    if (request.DateOfBirth is null)
    {
        errors["dateOfBirth"] = ["Date of birth is required."];
    }

    if (request.MonthlyIncome < 0)
    {
        errors["monthlyIncome"] = ["Monthly income cannot be negative."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var member = await store.AddMemberAsync(request, cancellationToken);
    return Results.Created($"/api/members/{member.Id}", member);
});

app.MapGet("/api/loan-products", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetLoanProductsAsync(cancellationToken)));

app.MapGet("/api/loans", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetLoanApplicationsAsync(cancellationToken)));

app.MapPost("/api/loans", async (CreateLoanRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
{
    if (request.Amount <= 0 || request.TenureMonths <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["amount"] = ["Loan amount and repayment term must be greater than zero."]
        });
    }

    var application = await store.AddLoanAsync(request, cancellationToken);
    return application is null
        ? Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["loan"] = ["Select an active member and a monthly loan product with an amount and term inside its configured limits."]
        })
        : Results.Created($"/api/loans/{application.ApplicationNo}", application);
});

app.MapPost("/api/loans/{applicationNo}/decision",
    async (string applicationNo, LoanDecisionRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        if (!await store.DecideLoanAsync(applicationNo, request.Approved, cancellationToken))
        {
            return Results.NotFound(new { message = "Application was not found or is no longer awaiting a decision." });
        }

        var application = (await store.GetLoanApplicationsAsync(cancellationToken))
            .Single(item => item.ApplicationNo == applicationNo);
        return Results.Ok(application);
    });

app.MapPost("/api/loans/{applicationNo}/disburse",
    async (string applicationNo, DisburseLoanRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        var loan = await store.DisburseLoanAsync(applicationNo, request, cancellationToken);
        return loan is null
            ? Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["applicationNo"] = ["Only an approved application can be disbursed."]
            })
            : Results.Created($"/api/loan-accounts/{loan.LoanNo}", loan);
    });

app.MapGet("/api/loan-accounts", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetLoanAccountsAsync(cancellationToken)));

app.MapGet("/api/collections", async (SqlFinanceStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.GetCollectionsAsync(cancellationToken)));

app.MapPost("/api/collections",
    async (CreateCollectionRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["amount"] = ["Collection amount must be greater than zero."]
            });
        }

        var collection = await store.RecordCollectionAsync(request, cancellationToken);
        return collection is null
            ? Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["loanNo"] = ["Select an active loan and enter an amount within its outstanding balance."]
            })
            : Results.Created($"/api/collections/{collection.ReceiptNo}", collection);
    });

app.MapPost("/api/accounting/vouchers",
    async (CreateVoucherRequest request, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.DebitLedger) ||
            string.IsNullOrWhiteSpace(request.CreditLedger) ||
            request.DebitLedger.Equals(request.CreditLedger, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["voucher"] = ["Enter a positive amount and choose two different debit and credit ledgers."]
            });
        }

        var report = await store.PostVoucherAsync(request, cancellationToken);
        return Results.Created("/api/accounting/vouchers", report);
    });

app.MapGet("/api/reports/types", () => Results.Ok(ReportCatalog.All));
app.MapGet("/api/reports/{type}",
    async (string type, string? from, string? to, string? branch, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!DateOnly.TryParse(from, out var fromDate))
        {
            fromDate = new DateOnly(today.Year, today.Month, 1);
        }

        if (!DateOnly.TryParse(to, out var toDate))
        {
            toDate = today;
        }

        if (fromDate > toDate)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dateRange"] = ["The start date must be on or before the end date."]
            });
        }

        var report = await store.BuildReportAsync(type, fromDate, toDate, branch, cancellationToken);
        return report is null
            ? Results.NotFound(new { message = $"Report '{type}' was not found." })
            : Results.Ok(report);
    });

app.Run();
