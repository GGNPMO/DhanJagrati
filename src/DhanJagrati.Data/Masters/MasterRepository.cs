using System.Globalization;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Masters;

/// <summary>
/// Generic CRUD over <see cref="MasterRegistry"/>. Identifiers are taken from the definition only;
/// all values are parameterised.
/// </summary>
public sealed class MasterRepository(IDbConnectionFactory db)
{
    private const int MaxRows = 5000;

    public async Task<IEnumerable<dynamic>> ListAsync(MasterDefinition def, int branchId, int? parentId, string? search, bool activeOnly, CancellationToken ct)
    {
        var sql = new StringBuilder($"SELECT TOP ({MaxRows}) {def.Alias}.{def.IdColumn} AS Id, {def.SelectList} FROM {def.From} WHERE 1 = 1");
        var p = new DynamicParameters();
        if (def.BranchScoped) { sql.Append($" AND {def.Alias}.BranchId = @branchId"); p.Add("branchId", branchId); }
        if (parentId is not null && def.ParentColumn is not null) { sql.Append($" AND {def.Alias}.{def.ParentColumn} = @parentId"); p.Add("parentId", parentId); }
        if (!string.IsNullOrWhiteSpace(search)) { sql.Append($" AND {def.SearchColumn} LIKE @search"); p.Add("search", $"%{EscapeLike(search.Trim())}%"); }
        if (activeOnly && def.HasIsActive) sql.Append($" AND {def.Alias}.IsActive = 1");
        sql.Append($" ORDER BY {def.OrderBy}");

        await using var c = await db.OpenAsync(ct);
        return await c.QueryAsync(new CommandDefinition(sql.ToString(), p, cancellationToken: ct));
    }

    public async Task<dynamic?> GetAsync(MasterDefinition def, int branchId, int id, CancellationToken ct)
    {
        var sql = $"SELECT {def.Alias}.{def.IdColumn} AS Id, {def.SelectList} FROM {def.From} WHERE {def.Alias}.{def.IdColumn} = @id"
                  + (def.BranchScoped ? $" AND {def.Alias}.BranchId = @branchId" : "");
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync(new CommandDefinition(sql, new { id, branchId }, cancellationToken: ct));
    }

    public async Task<int> InsertAsync(MasterDefinition def, int branchId, IReadOnlyDictionary<string, JsonElement> body, CancellationToken ct)
    {
        var values = Parse(def, body);
        var columns = def.Fields.Select(f => f.Column).ToList();
        var p = new DynamicParameters();
        foreach (var f in def.Fields) p.Add(f.Column, values[f.Column]);
        if (def.BranchScoped) { columns.Add("BranchId"); p.Add("BranchId", branchId); }

        var sql = $"INSERT {def.Table} ({string.Join(", ", columns)}) OUTPUT INSERTED.{def.IdColumn} VALUES ({string.Join(", ", columns.Select(c => "@" + c))})";
        await using var c = await db.OpenAsync(ct);
        return await SqlGuard.RunAsync(() => c.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, cancellationToken: ct)));
    }

    public async Task<bool> UpdateAsync(MasterDefinition def, int branchId, int id, IReadOnlyDictionary<string, JsonElement> body, CancellationToken ct)
    {
        var values = Parse(def, body);
        var p = new DynamicParameters();
        foreach (var f in def.Fields) p.Add(f.Column, values[f.Column]);
        p.Add("__id", id);
        p.Add("__branchId", branchId);

        var sql = $"UPDATE {def.Table} SET {string.Join(", ", def.Fields.Select(f => $"{f.Column} = @{f.Column}"))} WHERE {def.IdColumn} = @__id"
                  + (def.BranchScoped ? " AND BranchId = @__branchId" : "");
        await using var c = await db.OpenAsync(ct);
        return await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct))) == 1;
    }

    public async Task<bool> DeleteAsync(MasterDefinition def, int branchId, int id, CancellationToken ct)
    {
        var sql = $"DELETE FROM {def.Table} WHERE {def.IdColumn} = @id" + (def.BranchScoped ? " AND BranchId = @branchId" : "");
        await using var c = await db.OpenAsync(ct);
        try
        {
            return await c.ExecuteAsync(new CommandDefinition(sql, new { id, branchId }, cancellationToken: ct)) == 1;
        }
        catch (SqlException ex) when (SqlErrors.IsForeignKeyViolation(ex))
        {
            throw new DomainException(def.HasIsActive
                ? "This record is in use and cannot be deleted. Mark it inactive instead."
                : "This record is in use and cannot be deleted.", 409);
        }
    }

    /// <summary>Converts and validates the JSON body against the field definitions.</summary>
    public static Dictionary<string, object?> Parse(MasterDefinition def, IReadOnlyDictionary<string, JsonElement> body)
    {
        var lookup = new Dictionary<string, JsonElement>(body, StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Error(string field, string message) =>
            (errors.TryGetValue(field, out var list) ? list : errors[field] = []).Add(message);

        foreach (var f in def.Fields)
        {
            lookup.TryGetValue(f.Column, out var element);
            object? value;
            try
            {
                value = Convert(f, element);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException)
            {
                Error(f.Column, "Invalid value.");
                continue;
            }

            value ??= f.Default;
            if (value is null && f.Required) { Error(f.Column, "Required."); continue; }

            switch (value)
            {
                case string s when f.MaxLength > 0 && s.Length > f.MaxLength:
                    Error(f.Column, $"Maximum {f.MaxLength} characters."); break;
                case string s when f.Choices is not null && !f.Choices.Contains(s):
                    Error(f.Column, $"Must be one of: {string.Join(", ", f.Choices)}."); break;
                case int i when (f.Min is { } min && i < min) || (f.Max is { } max && i > max):
                case decimal d when (f.Min is { } dmin && d < dmin) || (f.Max is { } dmax && d > dmax):
                    Error(f.Column, $"Must be between {f.Min?.ToString(CultureInfo.InvariantCulture) ?? "-"} and {f.Max?.ToString(CultureInfo.InvariantCulture) ?? "-"}."); break;
            }
            values[f.Column] = value;
        }

        if (errors.Count == 0 && def.Validate is not null)
            foreach (var (field, message) in def.Validate(values))
                Error(field, message);

        if (errors.Count > 0)
            throw new ValidationFailedException(errors.ToDictionary(e => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value.ToArray()));
        return values;
    }

    private static object? Convert(MasterField f, JsonElement e)
    {
        if (e.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return null;
        if (e.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(e.GetString()) && f.Kind != FieldKind.Bool) return null;

        return f.Kind switch
        {
            FieldKind.Text or FieldKind.Choice => e.ValueKind == JsonValueKind.String
                ? (f.Upper ? e.GetString()!.Trim().ToUpperInvariant() : e.GetString()!.Trim())
                : throw new FormatException(),
            FieldKind.Int => e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            FieldKind.Decimal => e.ValueKind == JsonValueKind.Number ? e.GetDecimal() : decimal.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            FieldKind.Bool => e.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new FormatException()
            },
            FieldKind.Date => DateOnly.ParseExact(e.GetString()![..10], "yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => throw new FormatException()
        };
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
