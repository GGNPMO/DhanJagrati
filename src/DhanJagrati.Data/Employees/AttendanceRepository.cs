using Dapper;

namespace DhanJagrati.Data.Employees;

public sealed class AttendanceRow
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string? Status { get; init; }
    /// <summary>HH:mm:ss</summary>
    public string? InTime { get; init; }
    public string? OutTime { get; init; }
    public string? Remarks { get; init; }
}

public sealed class AttendanceEntry
{
    public int EmployeeId { get; set; }
    public string? Status { get; set; }
    /// <summary>HH:mm or HH:mm:ss</summary>
    public string? InTime { get; set; }
    public string? OutTime { get; set; }
    public string? Remarks { get; set; }
}

/// <summary>MASTER > Daily Attendance for the active advisors of the session branch.</summary>
public sealed class AttendanceRepository(IDbConnectionFactory db)
{
    public static readonly string[] Statuses = ["P", "A", "L", "H", "W"];

    public async Task<IReadOnlyList<AttendanceRow>> GetDayAsync(int branchId, DateOnly date, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<AttendanceRow>(new CommandDefinition("""
            SELECT e.EmployeeId, e.EmployeeCode, e.EmployeeName, a.Status,
                   CAST(a.InTime AS VARCHAR(8)) AS InTime, CAST(a.OutTime AS VARCHAR(8)) AS OutTime, a.Remarks
            FROM emp.Employee e
            LEFT JOIN emp.Attendance a ON a.EmployeeId = e.EmployeeId AND a.AttendanceDate = @date
            WHERE e.BranchId = @branchId AND (e.IsActive = 1 OR a.EmployeeId IS NOT NULL)
            ORDER BY e.EmployeeCode
            """, new { branchId, date }, cancellationToken: ct))).AsList();
    }

    /// <summary>Upserts the given entries; an entry without status removes that day's record.</summary>
    public async Task SaveDayAsync(int branchId, DateOnly date, IReadOnlyList<AttendanceEntry> entries, int userId, CancellationToken ct)
    {
        var v = new FieldValidator();
        var times = new Dictionary<int, (TimeOnly? In, TimeOnly? Out)>();
        foreach (var e in entries)
        {
            e.Status = Text.Clean(e.Status, upper: true);
            e.Remarks = Text.Clean(e.Remarks);
            v.OneOf(e.Status, Statuses, $"entries[{e.EmployeeId}].status");
            v.MaxLength(e.Remarks, 200, $"entries[{e.EmployeeId}].remarks");
            var inTime = ParseTime(e.InTime, $"entries[{e.EmployeeId}].inTime", v);
            var outTime = ParseTime(e.OutTime, $"entries[{e.EmployeeId}].outTime", v);
            if (inTime is { } i && outTime is { } o && o < i) v.Add($"entries[{e.EmployeeId}].outTime", "Out time is before in time.");
            times[e.EmployeeId] = (inTime, outTime);
        }
        v.ThrowIfAny();

        await using var c = await db.OpenAsync(ct);
        var ids = entries.Select(e => e.EmployeeId).Distinct().ToArray();
        var allowed = (await c.QueryAsync<int>(new CommandDefinition(
            "SELECT EmployeeId FROM emp.Employee WHERE BranchId = @branchId AND EmployeeId IN @ids",
            new { branchId, ids }, cancellationToken: ct))).ToHashSet();
        if (ids.Any(id => !allowed.Contains(id)))
            throw new DomainException("Attendance can only be marked for advisors of the current branch.", 403);

        await using var tx = c.BeginTransaction();
        foreach (var e in entries)
        {
            if (e.Status is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM emp.Attendance WHERE EmployeeId = @EmployeeId AND AttendanceDate = @date",
                    new { e.EmployeeId, date }, tx, cancellationToken: ct));
                continue;
            }
            await c.ExecuteAsync(new CommandDefinition("""
                MERGE emp.Attendance AS t
                USING (SELECT @EmployeeId AS EmployeeId, @date AS AttendanceDate) AS s
                   ON t.EmployeeId = s.EmployeeId AND t.AttendanceDate = s.AttendanceDate
                WHEN MATCHED THEN UPDATE SET Status = @Status, InTime = @InTime, OutTime = @OutTime, Remarks = @Remarks, MarkedBy = @userId
                WHEN NOT MATCHED THEN INSERT (EmployeeId, AttendanceDate, Status, InTime, OutTime, Remarks, MarkedBy)
                     VALUES (@EmployeeId, @date, @Status, @InTime, @OutTime, @Remarks, @userId);
                """, new
            {
                e.EmployeeId, date, e.Status, userId, e.Remarks,
                InTime = times[e.EmployeeId].In?.ToTimeSpan(), OutTime = times[e.EmployeeId].Out?.ToTimeSpan()
            }, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    private static TimeOnly? ParseTime(string? value, string field, FieldValidator v)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeOnly.TryParseExact(value.Trim(), ["HH:mm", "HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var time))
            return time;
        v.Add(field, "Use HH:mm (24-hour).");
        return null;
    }
}
