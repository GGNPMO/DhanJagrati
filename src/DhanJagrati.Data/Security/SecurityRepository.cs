using Dapper;

namespace DhanJagrati.Data.Security;

public sealed record UserRecord(
    int UserId, string UserName, string DisplayName, string? PasswordHash, string RoleCode,
    int DefaultBranchId, bool MustChangePassword, int FailedLoginCount, DateTime? LockedUntil, bool IsActive);

public sealed class SessionInfo
{
    public Guid SessionId { get; init; }
    public int UserId { get; init; }
    public string UserName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string RoleCode { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchCode { get; init; } = "";
    public string BranchName { get; init; } = "";
    public DateOnly BusinessDate { get; init; }
    public bool MustChangePassword { get; init; }
}

public sealed record BranchOption(int BranchId, string BranchCode, string BranchName, bool IsHeadOffice);

public sealed record CompanyInfo(string CompanyName, string? AddressLine, string? City, string? PinCode);

public sealed class SecurityRepository(IDbConnectionFactory db)
{
    public async Task<UserRecord?> FindUserAsync(string userName, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition("""
            SELECT UserId, UserName, DisplayName, PasswordHash, RoleCode, DefaultBranchId,
                   MustChangePassword, FailedLoginCount, LockedUntil, IsActive
            FROM sec.AppUser WHERE UserName = @userName
            """, new { userName }, cancellationToken: ct));
    }

    public async Task<UserRecord?> GetUserAsync(int userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<UserRecord>(new CommandDefinition("""
            SELECT UserId, UserName, DisplayName, PasswordHash, RoleCode, DefaultBranchId,
                   MustChangePassword, FailedLoginCount, LockedUntil, IsActive
            FROM sec.AppUser WHERE UserId = @userId
            """, new { userId }, cancellationToken: ct));
    }

    public async Task RecordFailedLoginAsync(int userId, int maxAttempts, TimeSpan lockFor, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser
            SET FailedLoginCount = FailedLoginCount + 1,
                LockedUntil = CASE WHEN FailedLoginCount + 1 >= @maxAttempts
                                   THEN DATEADD(SECOND, @lockSeconds, SYSUTCDATETIME()) ELSE LockedUntil END
            WHERE UserId = @userId
            """, new { userId, maxAttempts, lockSeconds = (int)lockFor.TotalSeconds }, cancellationToken: ct));
    }

    /// <summary>Resets the failure counter and opens a session on the user's default branch.</summary>
    public async Task<Guid> StartSessionAsync(UserRecord user, DateOnly businessDate, string? clientIp, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        var sessionId = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser SET FailedLoginCount = 0, LockedUntil = NULL, LastLoginAt = SYSUTCDATETIME() WHERE UserId = @UserId;
            INSERT sec.UserSession (SessionId, UserId, BranchId, BusinessDate, ClientIp)
            VALUES (@sessionId, @UserId, @DefaultBranchId, @businessDate, @clientIp);
            """, new { user.UserId, user.DefaultBranchId, sessionId, businessDate, clientIp }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return sessionId;
    }

    public async Task<SessionInfo?> GetActiveSessionAsync(Guid sessionId, int userId, TimeSpan idleTimeout, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<SessionInfo>(new CommandDefinition("""
            UPDATE s SET LastSeenAt = SYSUTCDATETIME()
            OUTPUT inserted.SessionId, u.UserId, u.UserName, u.DisplayName, u.RoleCode,
                   b.BranchId, b.BranchCode, b.BranchName, inserted.BusinessDate, u.MustChangePassword
            FROM sec.UserSession s
            JOIN sec.AppUser u ON u.UserId = s.UserId
            JOIN sec.Branch b  ON b.BranchId = s.BranchId
            WHERE s.SessionId = @sessionId AND s.UserId = @userId AND s.LogoutAt IS NULL
              AND u.IsActive = 1 AND b.IsActive = 1
              AND s.LastSeenAt > DATEADD(SECOND, -@idleSeconds, SYSUTCDATETIME())
            """, new { sessionId, userId, idleSeconds = (int)idleTimeout.TotalSeconds }, cancellationToken: ct));
    }

    public async Task EndSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE sec.UserSession SET LogoutAt = SYSUTCDATETIME() WHERE SessionId = @sessionId AND LogoutAt IS NULL",
            new { sessionId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<BranchOption>> GetUserBranchesAsync(int userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<BranchOption>(new CommandDefinition("""
            SELECT b.BranchId, b.BranchCode, b.BranchName, b.IsHeadOffice
            FROM sec.UserBranch ub JOIN sec.Branch b ON b.BranchId = ub.BranchId
            WHERE ub.UserId = @userId AND b.IsActive = 1
            ORDER BY b.BranchName
            """, new { userId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<bool> SwitchBranchAsync(Guid sessionId, int userId, int branchId, DateOnly businessDate, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var updated = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE s SET BranchId = @branchId, BusinessDate = @businessDate
            FROM sec.UserSession s
            WHERE s.SessionId = @sessionId AND s.UserId = @userId AND s.LogoutAt IS NULL
              AND EXISTS (SELECT 1 FROM sec.UserBranch ub JOIN sec.Branch b ON b.BranchId = ub.BranchId
                          WHERE ub.UserId = @userId AND ub.BranchId = @branchId AND b.IsActive = 1)
            """, new { sessionId, userId, branchId, businessDate }, cancellationToken: ct));
        return updated == 1;
    }

    public async Task SetPasswordAsync(int userId, string passwordHash, bool mustChange, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser
            SET PasswordHash = @passwordHash, MustChangePassword = @mustChange,
                PasswordChangedAt = SYSUTCDATETIME(), FailedLoginCount = 0, LockedUntil = NULL
            WHERE UserId = @userId
            """, new { userId, passwordHash, mustChange }, cancellationToken: ct));
    }

    public async Task AuditAsync(string userName, string eventType, int? branchId, string? clientIp, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT sec.LoginAudit (UserName, EventType, BranchId, ClientIp) VALUES (@userName, @eventType, @branchId, @clientIp)
            """, new { userName = Truncate(userName, 50), eventType, branchId, clientIp }, cancellationToken: ct));
    }

    public async Task<CompanyInfo?> GetCompanyAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QueryFirstOrDefaultAsync<CompanyInfo>(new CommandDefinition(
            "SELECT TOP (1) CompanyName, AddressLine, City, PinCode FROM sec.Company ORDER BY CompanyId", cancellationToken: ct));
    }

/// <summary>
    /// Sets a temporary password (must be changed at next login). Without <paramref name="overwrite"/>
    /// it only applies to a user that has no password yet. Also clears any lockout.
    /// </summary>
    public async Task<int> SetTemporaryPasswordAsync(string userName, string passwordHash, bool overwrite, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE sec.AppUser
            SET PasswordHash = @passwordHash, MustChangePassword = 1, PasswordChangedAt = SYSUTCDATETIME(),
                FailedLoginCount = 0, LockedUntil = NULL, IsActive = 1
            WHERE UserName = @userName AND (@overwrite = 1 OR PasswordHash IS NULL)
            """, new { userName, passwordHash, overwrite }, cancellationToken: ct));
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
