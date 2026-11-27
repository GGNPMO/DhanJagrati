namespace DhanJagrati.Data.Masters;

public enum FieldKind { Text, Int, Decimal, Bool, Date, Choice }

/// <summary>An editable column of a master table. Column names come only from code, never from requests.</summary>
public sealed record MasterField(
    string Column,
    FieldKind Kind,
    bool Required = false,
    int MaxLength = 0,
    decimal? Min = null,
    decimal? Max = null,
    IReadOnlyList<string>? Choices = null,
    bool Upper = false,
    object? Default = null);

public sealed class MasterDefinition
{
    public required string Key { get; init; }
    public required string Table { get; init; }
    public required string Alias { get; init; }
    public required string IdColumn { get; init; }
    /// <summary>SELECT list (without the Id column), may reference joined tables.</summary>
    public required string SelectList { get; init; }
    /// <summary>FROM clause including joins, e.g. "mst.District d JOIN mst.State st ON ...".</summary>
    public required string From { get; init; }
    public required string OrderBy { get; init; }
    public required string SearchColumn { get; init; }
    public required IReadOnlyList<MasterField> Fields { get; init; }
    /// <summary>Field used by ?parentId= filtering (e.g. StateId for districts).</summary>
    public string? ParentColumn { get; init; }
    /// <summary>Rows belong to the session branch (BranchId is set by the server, never by the client).</summary>
    public bool BranchScoped { get; init; }
    public bool HasIsActive { get; init; } = true;
    public IReadOnlyList<string> WriteRoles { get; init; } = ["ADMIN", "MANAGER"];
    /// <summary>Cross-field validation; returns (field, message) pairs.</summary>
    public Func<IReadOnlyDictionary<string, object?>, IEnumerable<(string Field, string Message)>>? Validate { get; init; }
}
