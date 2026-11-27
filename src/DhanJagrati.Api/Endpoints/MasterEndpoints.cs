using System.Text.Json;
using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Masters;

namespace DhanJagrati.Api.Endpoints;

/// <summary>MASTER menu: one set of CRUD endpoints for every definition in <see cref="MasterRegistry"/>.</summary>
public static class MasterEndpoints
{
    public static void MapMasterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/masters/{entity}")
            .WithTags("Masters")
            .RequireAuthorization()
            .AddEndpointFilter<PasswordChangedFilter>();

        group.MapGet("", async (string entity, int? parentId, string? search, bool? activeOnly,
            HttpContext http, MasterRepository repo, CancellationToken ct) =>
        {
            if (!MasterRegistry.All.TryGetValue(entity, out var def)) return Results.NotFound();
            var rows = await repo.ListAsync(def, http.Session().BranchId, parentId, search, activeOnly ?? false, ct);
            return Results.Ok(rows.Select(ToDictionary));
        });

        group.MapGet("/{id:int}", async (string entity, int id, HttpContext http, MasterRepository repo, CancellationToken ct) =>
        {
            if (!MasterRegistry.All.TryGetValue(entity, out var def)) return Results.NotFound();
            var row = await repo.GetAsync(def, http.Session().BranchId, id, ct);
            return row is null ? Results.NotFound() : Results.Ok(ToDictionary(row));
        });

        group.MapPost("", async (string entity, Dictionary<string, JsonElement> body,
            HttpContext http, MasterRepository repo, CancellationToken ct) =>
        {
            if (!MasterRegistry.All.TryGetValue(entity, out var def)) return Results.NotFound();
            if (Forbidden(def, http) is { } denied) return denied;
            var id = await repo.InsertAsync(def, http.Session().BranchId, body, ct);
            return Results.Created($"/api/masters/{def.Key}/{id}", new { id });
        });

        group.MapPut("/{id:int}", async (string entity, int id, Dictionary<string, JsonElement> body,
            HttpContext http, MasterRepository repo, CancellationToken ct) =>
        {
            if (!MasterRegistry.All.TryGetValue(entity, out var def)) return Results.NotFound();
            if (Forbidden(def, http) is { } denied) return denied;
            return await repo.UpdateAsync(def, http.Session().BranchId, id, body, ct) ? Results.NoContent() : Results.NotFound();
        });

        group.MapDelete("/{id:int}", async (string entity, int id, HttpContext http, MasterRepository repo, CancellationToken ct) =>
        {
            if (!MasterRegistry.All.TryGetValue(entity, out var def)) return Results.NotFound();
            if (Forbidden(def, http) is { } denied) return denied;
            return await repo.DeleteAsync(def, http.Session().BranchId, id, ct) ? Results.NoContent() : Results.NotFound();
        });
    }

    private static IResult? Forbidden(MasterDefinition def, HttpContext http) =>
        http.Session().HasRole(def.WriteRoles)
            ? null
            : Results.Problem(title: "Your role cannot change master data.", statusCode: StatusCodes.Status403Forbidden);

    private static Dictionary<string, object?> ToDictionary(dynamic row)
    {
        var source = (IDictionary<string, object?>)row;
        var result = new Dictionary<string, object?>(source.Count);
        foreach (var (key, value) in source)
            result[key] = value is DateTime dt && dt.TimeOfDay == TimeSpan.Zero ? DateOnly.FromDateTime(dt) : value;
        return result;
    }
}
