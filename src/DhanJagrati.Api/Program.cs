using DhanJagrati.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<DemoFinanceStore>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", mode = "demo" }));
app.MapGet("/api/dashboard", (DemoFinanceStore store) => Results.Ok(store.GetDashboard()));

app.MapGet("/api/members", (string? search, DemoFinanceStore store) =>
    Results.Ok(store.GetMembers(search)));

app.MapPost("/api/members", (CreateMemberRequest request, DemoFinanceStore store) =>
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

    return Results.Created("/api/members", store.AddMember(request));
});

app.MapGet("/api/loans", (DemoFinanceStore store) => Results.Ok(store.GetLoans()));
app.MapPost("/api/loans", (CreateLoanRequest request, DemoFinanceStore store) =>
{
    if (request.Amount <= 0 || request.TenureMonths <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["amount"] = ["Loan amount and tenure must be greater than zero."]
        });
    }

    var loan = store.AddLoan(request);
    return loan is null
        ? Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["memberId"] = ["Select an existing member."]
        })
        : Results.Created("/api/loans", loan);
});

app.MapPost("/api/loans/{loanId}/decision", (string loanId, LoanDecisionRequest request, DemoFinanceStore store) =>
{
    if (!store.DecideLoan(loanId, request.Approved))
    {
        return Results.NotFound(new { message = "Loan application was not found." });
    }

    return Results.Ok(store.GetLoans().Single(application => application.ApplicationNo == loanId));
});

app.MapPost("/api/loans/{applicationNo}/disburse", (string applicationNo, DisburseLoanRequest request, DemoFinanceStore store) =>
{
    var loan = store.DisburseLoan(applicationNo, request);
    return loan is null
        ? Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["applicationNo"] = ["Only an approved application can be disbursed."]
        })
        : Results.Created($"/api/loan-accounts/{loan.LoanNo}", loan);
});

app.MapGet("/api/loan-accounts", (DemoFinanceStore store) => Results.Ok(store.GetLoanAccounts()));
app.MapGet("/api/collections", (DemoFinanceStore store) => Results.Ok(store.GetCollections()));
app.MapPost("/api/collections", (CreateCollectionRequest request, DemoFinanceStore store) =>
{
    if (request.Amount <= 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["amount"] = ["Collection amount must be greater than zero."]
        });
    }

    var result = store.RecordCollection(request);
    return result is null
        ? Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["loanNo"] = ["Select an active loan and an amount no greater than its outstanding balance."]
        })
        : Results.Created("/api/collections", result);
});

app.MapPost("/api/accounting/vouchers", (CreateVoucherRequest request, DemoFinanceStore store) =>
{
    if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.DebitLedger) ||
        string.IsNullOrWhiteSpace(request.CreditLedger))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["voucher"] = ["Enter a positive amount and both debit and credit ledgers."]
        });
    }

    return Results.Created("/api/accounting/vouchers", store.PostVoucher(request));
});

app.MapGet("/api/reports/types", () => Results.Ok(ReportCatalog.All));
app.MapGet("/api/reports/{type}", (string type, string? from, string? to, string? branch, DemoFinanceStore store) =>
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

    var report = store.BuildReport(type, fromDate, toDate, branch ?? "Central Branch");
    return report is null
        ? Results.NotFound(new { message = $"Report '{type}' was not found." })
        : Results.Ok(report);
});

app.Run();
