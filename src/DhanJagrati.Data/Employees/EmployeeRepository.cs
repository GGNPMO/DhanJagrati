using Dapper;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Data.Employees;

/// <summary>Editable advisor / employee fields (legacy "Advisor Entry Form").</summary>
public sealed class EmployeeInput
{
    public DateOnly? JoiningDate { get; set; }
    /// <summary>Introducer advisor code; empty = company is the introducer (root of the chain).</summary>
    public string? IntroducerCode { get; set; }
    public int? InitialRankId { get; set; }
    public int? CurrentRankId { get; set; }
    public string? MemberNo { get; set; }
    public string? EmployeeName { get; set; }
    public string? GuardianName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? AddressLine { get; set; }
    public int? CityId { get; set; }
    public int? StateId { get; set; }
    public int? CountryId { get; set; }
    public string? PinCode { get; set; }
    public string? MobileNo { get; set; }
    public string? PhoneNo { get; set; }
    public string? Email { get; set; }
    public string? PassportNo { get; set; }
    public string? NomineeName { get; set; }
    public DateOnly? NomineeDob { get; set; }
    public int? NomineeRelationId { get; set; }
    public decimal JoiningFee { get; set; }
    public string? JoiningFeeMode { get; set; }
    public string? JoiningFeeRefNo { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class EmployeeDetail
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public DateOnly JoiningDate { get; init; }
    public string? IntroducerCode { get; init; }
    public string? IntroducerName { get; init; }
    public short? IntroducerRankNo { get; init; }
    public int? InitialRankId { get; init; }
    public int? CurrentRankId { get; init; }
    public string? MemberNo { get; init; }
    public string EmployeeName { get; init; } = "";
    public string? GuardianName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? AddressLine { get; init; }
    public int? CityId { get; init; }
    public int? StateId { get; init; }
    public int? CountryId { get; init; }
    public string? PinCode { get; init; }
    public string? MobileNo { get; init; }
    public string? PhoneNo { get; init; }
    public string? Email { get; init; }
    public string? PassportNo { get; init; }
    public string? NomineeName { get; init; }
    public DateOnly? NomineeDob { get; init; }
    public int? NomineeRelationId { get; init; }
    public decimal JoiningFee { get; init; }
    public string? JoiningFeeMode { get; init; }
    public string? JoiningFeeRefNo { get; init; }
    public bool IsActive { get; init; }
}

public sealed class EmployeeListItem
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string? RankCode { get; init; }
    public string? MobileNo { get; init; }
    public DateOnly JoiningDate { get; init; }
    public string? IntroducerCode { get; init; }
    public string BranchName { get; init; } = "";
    public bool IsActive { get; init; }
}

public sealed class EmployeeSummary
{
    public int Id { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public short? RankNo { get; init; }
    public string? RankCode { get; init; }
    public bool IsActive { get; init; }
}

public sealed record ChainLink(int Level, string AdvisorCode, string? AdvisorRank, string AdvisorName);

public sealed class EmployeeRepository(IDbConnectionFactory db)
{
    private static readonly string[] Columns =
    [
        "JoiningDate", "InitialRankId", "CurrentRankId", "EmployeeName", "GuardianName", "DateOfBirth", "Gender",
        "AddressLine", "CityId", "StateId", "CountryId", "PinCode", "MobileNo", "PhoneNo", "Email", "PassportNo",
        "NomineeName", "NomineeDob", "NomineeRelationId", "JoiningFee", "JoiningFeeMode", "JoiningFeeRefNo", "IsActive"
    ];

    private static readonly string[] Genders = ["M", "F", "O"];
    private static readonly string[] FeeModes = ["CASH", "CHEQUE", "NEFT", "UPI"];

    public async Task<IReadOnlyList<EmployeeListItem>> SearchAsync(int? branchId, string? search, bool activeOnly, CancellationToken ct)
    {
        var term = Text.Clean(search);
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<EmployeeListItem>(new CommandDefinition("""
            SELECT TOP (300) e.EmployeeId AS Id, e.EmployeeCode, e.EmployeeName, r.RankCode, e.MobileNo, e.JoiningDate,
                   i.EmployeeCode AS IntroducerCode, b.BranchName, e.IsActive
            FROM emp.Employee e
            JOIN sec.Branch b ON b.BranchId = e.BranchId
            LEFT JOIN emp.AdvisorRank r ON r.RankId = e.CurrentRankId
            LEFT JOIN emp.Employee i ON i.EmployeeId = e.IntroducerId
            WHERE (@branchId IS NULL OR e.BranchId = @branchId)
              AND (@activeOnly = 0 OR e.IsActive = 1)
              AND (@term IS NULL OR e.EmployeeCode LIKE @like OR e.EmployeeName LIKE @like OR e.MobileNo LIKE @like)
            ORDER BY e.EmployeeCode
            """, new { branchId, activeOnly, term, like = $"%{term}%" }, cancellationToken: ct))).AsList();
    }

    public async Task<EmployeeDetail?> GetAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<EmployeeDetail>(new CommandDefinition($"""
            SELECT e.EmployeeId AS Id, e.EmployeeCode, e.BranchId, b.BranchName, i.EmployeeCode AS IntroducerCode,
                   i.EmployeeName AS IntroducerName, e.IntroducerRankNo, m.MemberNo,
                   {string.Join(", ", Columns.Select(col => "e." + col))}
            FROM emp.Employee e
            JOIN sec.Branch b ON b.BranchId = e.BranchId
            LEFT JOIN emp.Employee i ON i.EmployeeId = e.IntroducerId
            LEFT JOIN loan.Member m ON m.MemberId = e.MemberId
            WHERE e.EmployeeId = @id AND e.BranchId = @branchId
            """, new { id, branchId }, cancellationToken: ct));
    }

    /// <summary>Lookup by advisor code in any branch (introducers, loan relationship managers).</summary>
    public async Task<EmployeeSummary?> FindByCodeAsync(string code, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<EmployeeSummary>(new CommandDefinition("""
            SELECT e.EmployeeId AS Id, e.EmployeeCode, e.EmployeeName, e.BranchId, b.BranchName, r.RankNo, r.RankCode, e.IsActive
            FROM emp.Employee e
            JOIN sec.Branch b ON b.BranchId = e.BranchId
            LEFT JOIN emp.AdvisorRank r ON r.RankId = e.CurrentRankId
            WHERE e.EmployeeCode = @code
            """, new { code = code.Trim().ToUpperInvariant() }, cancellationToken: ct));
    }

    public async Task<int?> NavigateAsync(int branchId, Navigate direction, string? fromCode, CancellationToken ct)
    {
        var sql = direction switch
        {
            Navigate.First => "SELECT TOP (1) EmployeeId FROM emp.Employee WHERE BranchId = @branchId ORDER BY EmployeeCode",
            Navigate.Last => "SELECT TOP (1) EmployeeId FROM emp.Employee WHERE BranchId = @branchId ORDER BY EmployeeCode DESC",
            Navigate.Next => "SELECT TOP (1) EmployeeId FROM emp.Employee WHERE BranchId = @branchId AND EmployeeCode > @from ORDER BY EmployeeCode",
            _ => "SELECT TOP (1) EmployeeId FROM emp.Employee WHERE BranchId = @branchId AND EmployeeCode < @from ORDER BY EmployeeCode DESC"
        };
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { branchId, from = fromCode ?? "" }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ChainLink>> GetParentChainAsync(int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<ChainLink>(new CommandDefinition("emp.usp_EmployeeParentChain", new { EmployeeId = id },
            commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct))).AsList();
    }

    public async Task<int> CreateAsync(int branchId, int userId, EmployeeInput input, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        await using var c = await db.OpenAsync(ct);
        var links = await ValidateAsync(c, input, today, null, ct);

        await using var tx = c.BeginTransaction();
        var code = await SqlGuard.NextNumberAsync(c, tx, "EMPLOYEE", 0, ct);
        var p = new DynamicParameters(input);
        p.AddDynamicParams(new
        {
            EmployeeCode = code, BranchId = branchId, CreatedBy = userId,
            links.IntroducerId, links.IntroducerRankNo, links.MemberId
        });
        var sql = $"""
            INSERT emp.Employee (EmployeeCode, BranchId, CreatedBy, IntroducerId, IntroducerRankNo, MemberId, {string.Join(", ", Columns)})
            OUTPUT INSERTED.EmployeeId
            VALUES (@EmployeeCode, @BranchId, @CreatedBy, @IntroducerId, @IntroducerRankNo, @MemberId, {string.Join(", ", Columns.Select(col => "@" + col))})
            """;
        var id = await SqlGuard.RunAsync(() => c.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, tx, cancellationToken: ct)));
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(int branchId, int userId, int id, EmployeeInput input, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        await using var c = await db.OpenAsync(ct);
        var links = await ValidateAsync(c, input, today, id, ct);

        var p = new DynamicParameters(input);
        p.AddDynamicParams(new { Id = id, BranchId = branchId, ModifiedBy = userId, links.IntroducerId, links.IntroducerRankNo, links.MemberId });
        var sql = $"""
            UPDATE emp.Employee
            SET {string.Join(", ", Columns.Select(col => $"{col} = @{col}"))},
                IntroducerId = @IntroducerId,
                IntroducerRankNo = CASE WHEN ISNULL(IntroducerId, 0) = ISNULL(@IntroducerId, 0) THEN IntroducerRankNo ELSE @IntroducerRankNo END,
                MemberId = @MemberId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
            WHERE EmployeeId = @Id AND BranchId = @BranchId
            """;
        return await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct))) == 1;
    }

    public async Task<bool> DeleteAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        try
        {
            return await c.ExecuteAsync(new CommandDefinition(
                "DELETE FROM emp.Employee WHERE EmployeeId = @id AND BranchId = @branchId", new { id, branchId }, cancellationToken: ct)) == 1;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (SqlErrors.IsForeignKeyViolation(ex))
        {
            throw new DomainException("This advisor has loans, a team, a login or attendance records and cannot be deleted. Mark it inactive instead.", 409);
        }
    }

    private static void Normalize(EmployeeInput e)
    {
        e.IntroducerCode = Text.Clean(e.IntroducerCode, upper: true);
        e.MemberNo = Text.Clean(e.MemberNo, upper: true);
        e.EmployeeName = Text.Clean(e.EmployeeName, upper: true);
        e.GuardianName = Text.Clean(e.GuardianName, upper: true);
        e.Gender = Text.Clean(e.Gender, upper: true);
        e.AddressLine = Text.Clean(e.AddressLine, upper: true);
        e.PinCode = Text.Clean(e.PinCode);
        e.MobileNo = Text.Clean(e.MobileNo);
        e.PhoneNo = Text.Clean(e.PhoneNo);
        e.Email = Text.Clean(e.Email)?.ToLowerInvariant();
        e.PassportNo = Text.Clean(e.PassportNo, upper: true);
        e.NomineeName = Text.Clean(e.NomineeName, upper: true);
        e.JoiningFeeMode = Text.Clean(e.JoiningFeeMode, upper: true);
        e.JoiningFeeRefNo = Text.Clean(e.JoiningFeeRefNo, upper: true);
    }

    private sealed class Links
    {
        public int? IntroducerId { get; init; }
        public short? IntroducerRankNo { get; init; }
        public int? MemberId { get; init; }
        public int CreatesCycle { get; init; }
    }

    private static async Task<Links> ValidateAsync(Microsoft.Data.SqlClient.SqlConnection c, EmployeeInput e, DateOnly today, int? selfId, CancellationToken ct)
    {
        var v = new FieldValidator();
        v.Required(e.EmployeeName, "employeeName");
        v.MaxLength(e.EmployeeName, 100, "employeeName");
        v.MaxLength(e.GuardianName, 100, "guardianName");
        v.Required(e.JoiningDate, "joiningDate");
        v.OneOf(e.Gender, Genders, "gender");
        v.OneOf(e.JoiningFeeMode, FeeModes, "joiningFeeMode");
        v.MaxLength(e.AddressLine, 300, "addressLine");
        v.MaxLength(e.PhoneNo, 20, "phoneNo");
        v.MaxLength(e.PassportNo, 20, "passportNo");
        v.MaxLength(e.NomineeName, 100, "nomineeName");
        v.MaxLength(e.JoiningFeeRefNo, 40, "joiningFeeRefNo");
        v.MaxLength(e.Email, 120, "email");
        v.Pattern(e.MobileNo, @"^[6-9]\d{9}$", "mobileNo", "Enter a 10-digit mobile number.");
        v.Pattern(e.PinCode, @"^\d{6}$", "pinCode", "PIN code must be 6 digits.");
        v.Pattern(e.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", "email", "Invalid email address.");
        if (e.JoiningDate > today) v.Add("joiningDate", "Cannot be in the future.");
        if (e.DateOfBirth > today) v.Add("dateOfBirth", "Cannot be in the future.");
        if (e.NomineeDob > today) v.Add("nomineeDob", "Cannot be in the future.");
        if (e.JoiningFee < 0) v.Add("joiningFee", "Cannot be negative.");
        if (e.JoiningFee > 0 && e.JoiningFeeMode is null) v.Add("joiningFeeMode", "Select how the joining fee was paid.");
        v.ThrowIfAny();

        // Resolve introducer and member; reject an introducer chain that loops back to this advisor.
        var links = await c.QuerySingleAsync<Links>(new CommandDefinition("""
            DECLARE @IntroducerId INT = (SELECT EmployeeId FROM emp.Employee WHERE EmployeeCode = @IntroducerCode);
            WITH Chain AS
            (
                SELECT EmployeeId, IntroducerId, 0 AS Depth FROM emp.Employee WHERE EmployeeId = @IntroducerId
                UNION ALL
                SELECT p.EmployeeId, p.IntroducerId, ch.Depth + 1 FROM Chain ch JOIN emp.Employee p ON p.EmployeeId = ch.IntroducerId
                WHERE ch.Depth < 50
            )
            SELECT @IntroducerId AS IntroducerId,
                   (SELECT r.RankNo FROM emp.Employee i JOIN emp.AdvisorRank r ON r.RankId = i.CurrentRankId WHERE i.EmployeeId = @IntroducerId) AS IntroducerRankNo,
                   (SELECT MemberId FROM loan.Member WHERE MemberNo = @MemberNo) AS MemberId,
                   CASE WHEN @SelfId IS NOT NULL AND EXISTS (SELECT 1 FROM Chain WHERE EmployeeId = @SelfId) THEN 1 ELSE 0 END AS CreatesCycle
            OPTION (MAXRECURSION 50);
            """, new { e.IntroducerCode, e.MemberNo, SelfId = selfId }, cancellationToken: ct));

        if (e.IntroducerCode is not null && links.IntroducerId is null) v.Add("introducerCode", "Introducer code not found.");
        if (links.CreatesCycle == 1) v.Add("introducerCode", "This introducer is in the advisor's own team (circular chain).");
        if (e.MemberNo is not null && links.MemberId is null) v.Add("memberNo", "Member not found.");
        v.ThrowIfAny();
        return links;
    }
}
