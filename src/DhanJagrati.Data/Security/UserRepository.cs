using Dapper;

namespace DhanJagrati.Data.Security;

public sealed class UserListItem
{
    public int Id { get; init; }
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string RoleCode { get; init; } = "";
    public int DefaultBranchId { get; init; }
    public string DefaultBranchName { get; init; } = "";
    public string? EmployeeCode { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public bool IsLocked { get; init; }
    public DateTime? LastLoginAt { get; init; }
    /// <summary>Comma-separated branch ids the user may work in.</summary>
    public string BranchIds { get; init; } = "";
}

public sealed class UserInput
{
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? RoleCode { get; set; }
    public int DefaultBranchId { get; set; }
    public int[] BranchIds { get; set; } = [];
    public string? EmployeeCode { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Required when creating a user; must be changed at first login.</summary>
    public string? TemporaryPassword { get; set; }
}

/// <summary>HELP/Security > User Master (administrators only).</summary>
public sealed class UserRepository(IDbConnectionFactory db)
{
    public static readonly string[] Roles = ["ADMIN", "MANAGER", "ACCOUNTANT", "OPERATOR", "FIELD_OFFICER"];

    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<UserListItem>(new CommandDefinition("""
            SELECT u.UserId AS Id, u.UserName, u.DisplayName, u.RoleCode, u.DefaultBranchId, b.BranchName AS DefaultBranchName,
                   e.EmployeeCode, u.IsActive, u.MustChangePassword,
                   CAST(CASE WHEN u.LockedUntil > SYSUTCDATETIME() THEN 1 ELSE 0 END AS BIT) AS IsLocked, u.LastLoginAt,
                   ISNULL(STUFF((SELECT ',' + CAST(ub.BranchId AS VARCHAR(10)) FROM sec.UserBranch ub
                                 WHERE ub.UserId = u.UserId FOR XML PATH('')), 1, 1, ''), '') AS BranchIds
            FROM sec.AppUser u
            JOIN sec.Branch b ON b.BranchId = u.DefaultBranchId
            LEFT JOIN emp.Employee e ON e.EmployeeId = u.EmployeeId
            ORDER BY u.UserName
            """, cancellationToken: ct))).AsList();
    }

    public async Task<int> CreateAsync(UserInput input, string passwordHash, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var employeeId = await ResolveEmployeeAsync(c, input, ct);
        await using var tx = c.BeginTransaction();
        var id = await SqlGuard.RunAsync(() => c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT sec.AppUser (UserName, DisplayName, PasswordHash, RoleCode, DefaultBranchId, EmployeeId, MustChangePassword, IsActive, PasswordChangedAt)
            OUTPUT INSERTED.UserId
            VALUES (@UserName, @DisplayName, @passwordHash, @RoleCode, @DefaultBranchId, @employeeId, 1, @IsActive, SYSUTCDATETIME())
            """, new { input.UserName, input.DisplayName, passwordHash, input.RoleCode, input.DefaultBranchId, employeeId, input.IsActive },
            tx, cancellationToken: ct)), "This user name is already taken.");
        await ReplaceBranchesAsync(c, tx, id, input.BranchIds, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(int id, UserInput input, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var employeeId = await ResolveEmployeeAsync(c, input, ct);
        await using var tx = c.BeginTransaction();
        var updated = await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser
            SET DisplayName = @DisplayName, RoleCode = @RoleCode, DefaultBranchId = @DefaultBranchId,
                EmployeeId = @employeeId, IsActive = @IsActive
            WHERE UserId = @id
            """, new { id, input.DisplayName, input.RoleCode, input.DefaultBranchId, employeeId, input.IsActive }, tx, cancellationToken: ct)));
        if (updated == 0) return false;
        await ReplaceBranchesAsync(c, tx, id, input.BranchIds, ct);
        // Deactivated users lose their open sessions immediately.
        if (!input.IsActive)
            await c.ExecuteAsync(new CommandDefinition(
                "UPDATE sec.UserSession SET LogoutAt = SYSUTCDATETIME() WHERE UserId = @id AND LogoutAt IS NULL", new { id }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Admin password reset: temporary password, unlock, end the user's sessions.</summary>
    public async Task<bool> ResetPasswordAsync(int id, string passwordHash, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser
            SET PasswordHash = @passwordHash, MustChangePassword = 1, PasswordChangedAt = SYSUTCDATETIME(),
                FailedLoginCount = 0, LockedUntil = NULL
            WHERE UserId = @id;
            UPDATE sec.UserSession SET LogoutAt = SYSUTCDATETIME() WHERE UserId = @id AND LogoutAt IS NULL;
            """, new { id, passwordHash }, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<BranchOption>> AllBranchesAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<BranchOption>(new CommandDefinition(
            "SELECT BranchId, BranchCode, BranchName, IsHeadOffice FROM sec.Branch WHERE IsActive = 1 ORDER BY BranchName",
            cancellationToken: ct))).AsList();
    }

    public static void Validate(UserInput input, bool isNew, Func<string?, bool> isStrongPassword, string passwordRule)
    {
        input.UserName = Text.Clean(input.UserName, upper: true);
        input.DisplayName = Text.Clean(input.DisplayName, upper: true);
        input.RoleCode = Text.Clean(input.RoleCode, upper: true);
        input.EmployeeCode = Text.Clean(input.EmployeeCode, upper: true);
        input.BranchIds = input.BranchIds.Distinct().ToArray();

        var v = new FieldValidator();
        if (isNew)
        {
            v.Required(input.UserName, "userName");
            v.Pattern(input.UserName, @"^[A-Z0-9._-]{3,50}$", "userName", "3-50 letters, digits, dot, dash or underscore.");
            if (!isStrongPassword(input.TemporaryPassword)) v.Add("temporaryPassword", passwordRule);
        }
        v.Required(input.DisplayName, "displayName");
        v.MaxLength(input.DisplayName, 100, "displayName");
        v.Required(input.RoleCode, "roleCode");
        v.OneOf(input.RoleCode, Roles, "roleCode");
        if (input.BranchIds.Length == 0) v.Add("branchIds", "Select at least one branch.");
        if (!input.BranchIds.Contains(input.DefaultBranchId)) v.Add("defaultBranchId", "Default branch must be one of the allowed branches.");
        v.ThrowIfAny();
    }

    private static async Task<int?> ResolveEmployeeAsync(Microsoft.Data.SqlClient.SqlConnection c, UserInput input, CancellationToken ct)
    {
        if (input.EmployeeCode is null) return null;
        var id = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT EmployeeId FROM emp.Employee WHERE EmployeeCode = @code", new { code = input.EmployeeCode }, cancellationToken: ct));
        if (id is null)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["employeeCode"] = ["Employee code not found."] });
        return id;
    }

    private static async Task ReplaceBranchesAsync(Microsoft.Data.SqlClient.SqlConnection c, System.Data.IDbTransaction tx, int userId, int[] branchIds, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM sec.UserBranch WHERE UserId = @userId", new { userId }, tx, cancellationToken: ct));
        await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition(
            "INSERT sec.UserBranch (UserId, BranchId) VALUES (@userId, @branchId)",
            branchIds.Select(branchId => new { userId, branchId }), tx, cancellationToken: ct)));
    }
}
