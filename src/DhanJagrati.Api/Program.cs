using DhanJagrati.Api;
using Dapper;
using System.Globalization;
using Microsoft.Data.SqlClient;

SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

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

app.MapGet("/api/loans/{loanNo}/settlement-quote",
    async (string loanNo, string? asOf, SqlFinanceStore store, CancellationToken cancellationToken) =>
    {
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var asOfDate))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["asOf"] = ["Enter a date in YYYY-MM-DD format."]
            });
        }

        var quote = await store.GetSettlementQuoteAsync(loanNo, asOfDate, cancellationToken);
        return quote is null
            ? Results.NotFound(new { message = "Active loan was not found or has no settlement quote." })
            : Results.Ok(quote);
    });

app.MapPost("/api/loans/{loanNo}/settlement",
    async (string loanNo, SettleLoanRequest request, SqlFinanceStore store, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(loanNo))
        {
            errors["loanNo"] = ["Loan number is required."];
        }

        if (request.Amount <= 0)
        {
            errors["amount"] = ["Settlement amount must be greater than zero."];
        }

        if (request.IdempotencyKey == Guid.Empty)
        {
            errors["idempotencyKey"] = ["A non-empty idempotency key is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Mode) ||
            !new[] { "CASH", "UPI", "CHEQUE", "NEFT", "NACH", "BANK_TRANSFER" }
                .Contains(request.Mode.Trim().ToUpperInvariant()))
        {
            errors["mode"] = ["Select a supported settlement payment mode."];
        }

        if (request.Reference?.Length > 60)
        {
            errors["reference"] = ["Reference cannot exceed 60 characters."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            var result = await store.SettleLoanAsync(loanNo.Trim(), request, cancellationToken);
            if (result is null)
            {
                return Results.NotFound(new { message = "Loan was not found." });
            }

            if (result.Status == "amount_mismatch")
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Settlement amount does not match the quote",
                    detail: $"The current settlement amount is {result.ExpectedAmount:0.00}.");
            }

            if (result.Status == "approval_required")
            {
                return Results.Accepted($"/api/loans/{loanNo}/settlement", result);
            }

            return Results.Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Settlement conflict", detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [exception.Message]
            });
        }
        catch (SqlException exception)
        {
            loggerFactory.CreateLogger("Settlements").LogError(exception, "SQL settlement completion failed.");
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Settlement could not be completed",
                detail: "The loan or settlement state changed before payment was recorded.");
        }
    });

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

app.MapPost("/api/collections/bulk",
    async (BulkCollectionRequest request, SqlFinanceStore store, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0 || request.Items.Count > 100)
        {
            errors["items"] = ["Provide between 1 and 100 collection items."];
        }
        else
        {
            if (request.Items.Any(item => string.IsNullOrWhiteSpace(item.LoanNo)))
            {
                errors["items"] = ["Every item must include a loan number."];
            }

            if (request.Items.Any(item => item.Amount <= 0))
            {
                errors["amount"] = ["Every collection amount must be greater than zero."];
            }

            if (request.Items.Any(item => item.Reference?.Length > 60))
            {
                errors["reference"] = ["References cannot exceed 60 characters."];
            }

            if (request.Items.Where(item => !string.IsNullOrWhiteSpace(item.LoanNo))
                .GroupBy(item => item.LoanNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
            {
                errors["items"] = ["A loan may appear only once in a bulk request."];
            }
        }

        if (string.IsNullOrWhiteSpace(request.Mode) ||
            !new[] { "CASH", "UPI", "CHEQUE", "NEFT", "BANK_TRANSFER", "SAVINGS_TRANSFER" }
                .Contains(request.Mode.Trim().ToUpperInvariant()))
        {
            errors["mode"] = ["Select a supported collection payment mode."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            return Results.Ok(await store.RecordCollectionsBulkAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Collection batch was not posted",
                detail: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [exception.Message]
            });
        }
        catch (SqlException exception)
        {
            loggerFactory.CreateLogger("Collections").LogError(exception, "SQL bulk collection posting failed.");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Collection batch was not posted",
                detail: "The database rejected the batch; no collection items were committed.");
        }
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
