using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Api;

public static class ReferenceDataEndpoints
{
    private static readonly string[] Resources =
    [
        "countries", "states", "districts", "cities", "tehsils", "villages", "khasra",
        "interest-rate-plans", "loan-products", "account-groups", "ledgers", "employees"
    ];

    /// <summary>Maps SQL-backed reference and master-data endpoints.</summary>
    public static WebApplication MapReferenceDataEndpoints(this WebApplication app)
    {
        var connectionString = app.Configuration.GetConnectionString("Microfinance");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Microfinance' is not configured.");
        }
        var store = new ReferenceDataStore(connectionString);
        var root = app.MapGroup("/api/reference");

        foreach (var resource in Resources)
        {
            var collection = root.MapGroup("/" + resource);
            collection.MapGet("", (int? parentId, CancellationToken ct) =>
                Run(async () => Results.Ok(await store.ListAsync(resource, null, parentId, false, ct))))
                .WithSummary($"List {resource}")
                .WithDescription("Returns reference records. Hierarchical resources accept the optional positive parentId query parameter.");

            collection.MapPost("", (JsonElement body, CancellationToken ct) =>
                Run(async () =>
                {
                    var created = await store.CreateAsync(resource, null, body, ct);
                    var id = ((IDictionary<string, object>)created)["id"];
                    return Results.Created($"/api/reference/{resource}/{id}", created);
                }))
                .WithSummary($"Create {resource} record");

            collection.MapGet("/{id:long}", (long id, CancellationToken ct) =>
                Run(async () =>
                {
                    if (id <= 0)
                    {
                        return Validation("id", "The id must be positive.");
                    }
                    var value = await store.GetAsync(resource, null, id, ct);
                    return value is null ? NotFound() : Results.Ok(value);
                }))
                .WithSummary($"Get {resource} record");

            collection.MapPatch("/{id:long}", (long id, JsonElement body, CancellationToken ct) =>
                Run(async () =>
                {
                    if (id <= 0)
                    {
                        return Validation("id", "The id must be positive.");
                    }
                    var value = await store.UpdateAsync(resource, null, id, body, ct);
                    return value is null ? NotFound() : Results.Ok(value);
                }))
                .WithSummary($"Update {resource} record");

            collection.MapDelete("/{id:long}", (long id, CancellationToken ct) =>
                Run(async () =>
                {
                    if (id <= 0)
                    {
                        return Validation("id", "The id must be positive.");
                    }
                    return await store.DeleteAsync(resource, null, id, ct) ? Results.NoContent() : NotFound();
                }))
                .WithSummary($"Delete {resource} record");
        }

        var lookups = root.MapGroup("/lookups");
        lookups.MapGet("/{category}", (string category, CancellationToken ct) =>
            Run(async () => Results.Ok(await store.ListAsync("lookups", category, null, false, ct))))
            .WithSummary("List lookup values by category")
            .WithDescription("Category is restricted to SALUTATION, OCCUPATION, RELIGION, or DESIGNATION.");
        lookups.MapPost("/{category}", (string category, JsonElement body, CancellationToken ct) =>
            Run(async () =>
            {
                var created = await store.CreateAsync("lookups", category, body, ct);
                var id = ((IDictionary<string, object>)created)["id"];
                return Results.Created($"/api/reference/lookups/{category}/{id}", created);
            }))
            .WithSummary("Create a lookup value");
        lookups.MapGet("/{category}/{id:long}", (string category, long id, CancellationToken ct) =>
            Run(async () =>
            {
                if (id <= 0)
                {
                    return Validation("id", "The id must be positive.");
                }
                var value = await store.GetAsync("lookups", category, id, ct);
                return value is null ? NotFound() : Results.Ok(value);
            }))
            .WithSummary("Get a lookup value");
        lookups.MapPatch("/{category}/{id:long}", (string category, long id, JsonElement body, CancellationToken ct) =>
            Run(async () =>
            {
                if (id <= 0)
                {
                    return Validation("id", "The id must be positive.");
                }
                var value = await store.UpdateAsync("lookups", category, id, body, ct);
                return value is null ? NotFound() : Results.Ok(value);
            }))
            .WithSummary("Update a lookup value");
        lookups.MapDelete("/{category}/{id:long}", (string category, long id, CancellationToken ct) =>
            Run(async () =>
            {
                if (id <= 0)
                {
                    return Validation("id", "The id must be positive.");
                }
                return await store.DeleteAsync("lookups", category, id, ct) ? Results.NoContent() : NotFound();
            }))
            .WithSummary("Delete a lookup value");

        root.MapGet("/relationship-managers", (CancellationToken ct) =>
            Run(async () => Results.Ok(await store.ListAsync("employees", null, null, true, ct))))
            .WithSummary("List active relationship managers")
            .WithDescription("Returns active, non-deleted employee records; create and update them through /api/reference/employees.");

        var relationships = root.MapGroup("/employee-relationships");
        relationships.MapGet("", (int? parentId, CancellationToken ct) =>
            Run(async () => Results.Ok(await store.ListAsync("employee-relationships", null, parentId, false, ct))))
            .WithSummary("List employee introducer relationships");
        relationships.MapPost("", (JsonElement body, CancellationToken ct) =>
            Run(async () =>
            {
                var created = await store.CreateAsync("employee-relationships", null, body, ct);
                var row = (IDictionary<string, object>)created;
                var employeeId = Convert.ToInt32(row["employeeId"], CultureInfo.InvariantCulture);
                var effectiveFrom = Convert.ToString(row["effectiveFrom"], CultureInfo.InvariantCulture)!;
                return Results.Created(
                    $"/api/reference/employee-relationships/{employeeId}/{effectiveFrom}", created);
            }))
            .WithSummary("Create an employee introducer relationship");
        relationships.MapGet("/{employeeId:int}/{effectiveFrom}", (int employeeId, string effectiveFrom, CancellationToken ct) =>
            Run(async () =>
            {
                if (employeeId <= 0)
                {
                    return Validation("employeeId", "The employeeId must be positive.");
                }
                if (!TryDate(effectiveFrom, out var date))
                {
                    return Validation("effectiveFrom", "Use an ISO date in yyyy-MM-dd format.");
                }
                var value = await store.GetEmployeeRelationshipAsync(employeeId, date, ct);
                return value is null ? NotFound() : Results.Ok(value);
            }))
            .WithSummary("Get an employee introducer relationship");
        relationships.MapPatch("/{employeeId:int}/{effectiveFrom}", (int employeeId, string effectiveFrom,
            JsonElement body, CancellationToken ct) =>
            Run(async () =>
            {
                if (employeeId <= 0)
                {
                    return Validation("employeeId", "The employeeId must be positive.");
                }
                if (!TryDate(effectiveFrom, out var date))
                {
                    return Validation("effectiveFrom", "Use an ISO date in yyyy-MM-dd format.");
                }
                var value = await store.UpdateEmployeeRelationshipAsync(employeeId, date, body, ct);
                return value is null ? NotFound() : Results.Ok(value);
            }))
            .WithSummary("Update an employee introducer relationship");
        relationships.MapDelete("/{employeeId:int}/{effectiveFrom}", (int employeeId, string effectiveFrom, CancellationToken ct) =>
            Run(async () =>
            {
                if (employeeId <= 0)
                {
                    return Validation("employeeId", "The employeeId must be positive.");
                }
                if (!TryDate(effectiveFrom, out var date))
                {
                    return Validation("effectiveFrom", "Use an ISO date in yyyy-MM-dd format.");
                }
                return await store.DeleteEmployeeRelationshipAsync(employeeId, date, ct) ? Results.NoContent() : NotFound();
            }))
            .WithSummary("Delete an employee introducer relationship");

        return app;
    }

    private static async Task<IResult> Run(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation();
        }
        catch (ReferenceDataValidationException exception)
        {
            return Validation("request", exception.Message);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 or 547)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: exception.Number == 547 ? "Reference is in use" : "Duplicate reference value",
                detail: exception.Number == 547
                    ? "The record is referenced by another record and cannot be changed or deleted."
                    : "A record with the supplied unique value already exists.");
        }
        catch (SqlException exception) when (exception.Number is 515 or 8114 or 8115 or 2628)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid reference data",
                detail: "The supplied values do not fit the database column constraints.");
        }
        catch (SqlException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Reference-data database unavailable",
                detail: "The database operation could not be completed.");
        }
    }

    private static IResult NotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Reference record not found");

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
