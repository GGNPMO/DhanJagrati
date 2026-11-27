using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data;

public interface IDbConnectionFactory
{
    Task<SqlConnection> OpenAsync(CancellationToken ct = default);
}

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch (SqlException ex)
        {
            await connection.DisposeAsync();
            throw new DatabaseUnavailableException(ex);
        }
    }
}

/// <summary>The database could not be reached (mapped to HTTP 503).</summary>
public sealed class DatabaseUnavailableException(Exception inner) : Exception("The database is not reachable.", inner);

public static class DapperSetup
{
    private static int _configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1) return;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(value.ToString()!)
        };
    }

    private sealed class NullableDateOnlyHandler : SqlMapper.TypeHandler<DateOnly?>
    {
        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value is null ? DBNull.Value : value.Value.ToDateTime(TimeOnly.MinValue);
        }

        public override DateOnly? Parse(object value) => value switch
        {
            null or DBNull => null,
            DateOnly d => d,
            DateTime dt => DateOnly.FromDateTime(dt),
            _ => DateOnly.Parse(value.ToString()!)
        };
    }
}

public static class SqlErrors
{
    public static bool IsDuplicate(SqlException ex) => ex.Number is 2627 or 2601;
    public static bool IsForeignKeyViolation(SqlException ex) =>
        ex.Number == 547 && ex.Message.Contains("REFERENCE", StringComparison.OrdinalIgnoreCase);
    public static bool IsCheckViolation(SqlException ex) =>
        ex.Number == 547 && ex.Message.Contains("CHECK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Business-rule failure that should reach the client as a 400/409 with a readable message.</summary>
public sealed class DomainException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

/// <summary>Field-level validation failure (keys are camelCase field names).</summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors) : Exception("Validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Collects field errors and throws them together.</summary>
public sealed class FieldValidator
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public bool HasErrors => _errors.Count > 0;

    public void Add(string field, string message) =>
        (_errors.TryGetValue(field, out var list) ? list : _errors[field] = []).Add(message);

    public void Required(object? value, string field)
    {
        if (value is null || value is string s && string.IsNullOrWhiteSpace(s)) Add(field, "Required.");
    }

    public void MaxLength(string? value, int max, string field)
    {
        if (value is not null && value.Length > max) Add(field, $"Maximum {max} characters.");
    }

    public void Pattern(string? value, string regex, string field, string message)
    {
        if (!string.IsNullOrEmpty(value) && !System.Text.RegularExpressions.Regex.IsMatch(value, regex)) Add(field, message);
    }

    public void OneOf(string? value, IReadOnlyCollection<string> allowed, string field)
    {
        if (value is not null && !allowed.Contains(value)) Add(field, $"Must be one of: {string.Join(", ", allowed)}.");
    }

    public void ThrowIfAny()
    {
        if (HasErrors) throw new ValidationFailedException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}

public static class Text
{
    /// <summary>Trim; empty becomes null; optional upper-casing (legacy data is upper case).</summary>
    public static string? Clean(string? value, bool upper = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return upper ? trimmed.ToUpperInvariant() : trimmed;
    }
}

/// <summary>Translates constraint violations into readable domain errors.</summary>
public static class SqlGuard
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> action, string duplicateMessage = "A record with the same name/code already exists.")
    {
        try
        {
            return await action();
        }
        catch (SqlException ex) when (SqlErrors.IsDuplicate(ex))
        {
            throw new DomainException(duplicateMessage, 409);
        }
        catch (SqlException ex) when (SqlErrors.IsForeignKeyViolation(ex))
        {
            throw new DomainException("A selected related record does not exist or is still in use.", 409);
        }
        catch (SqlException ex) when (SqlErrors.IsCheckViolation(ex))
        {
            throw new DomainException("One or more values are not allowed.", 400);
        }
    }

    public static async Task RunAsync(Func<Task> action, string duplicateMessage = "A record with the same name/code already exists.") =>
        await RunAsync(async () => { await action(); return 0; }, duplicateMessage);

    /// <summary>Next document number (sec.usp_NextNumber) inside the caller's transaction.</summary>
    public static async Task<string> NextNumberAsync(SqlConnection connection, System.Data.IDbTransaction tx, string seriesKey, int branchId, CancellationToken ct)
    {
        var p = new DynamicParameters();
        p.Add("SeriesKey", seriesKey);
        p.Add("BranchId", branchId);
        p.Add("Number", dbType: System.Data.DbType.String, size: 30, direction: System.Data.ParameterDirection.Output);
        await connection.ExecuteAsync(new CommandDefinition("sec.usp_NextNumber", p, tx,
            commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        return p.Get<string>("Number");
    }
}
