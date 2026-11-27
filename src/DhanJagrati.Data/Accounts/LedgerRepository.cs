using Dapper;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Accounts;

public sealed record AccountNatureRow(byte NatureId, string NatureCode, string NatureName, string ReportHead, string NormalBalance);

public sealed class LedgerRow
{
    public int Id { get; init; }
    public string LedgerName { get; init; } = "";
    public byte NatureId { get; init; }
    public string NatureName { get; init; } = "";
    public bool IsGroup { get; init; }
    public int? ParentLedgerId { get; init; }
    public string ReportHead { get; init; } = "";
    public string CashBankType { get; init; } = "N";
    public string? BankAccountNo { get; init; }
    public string? IfscCode { get; init; }
    public bool IsEditable { get; init; }
    public bool IsVisibleInBranch { get; init; }
    public bool IsDefaultGroup { get; init; }
    public bool ApplySanction { get; init; }
    public bool IsSystem { get; init; }
    public bool IsActive { get; init; }
    public decimal? OpeningBalance { get; init; }
    public string? OpeningBalType { get; init; }
    public DateOnly? OpeningDate { get; init; }
    public bool HasPostings { get; init; }
    public string? MappingKeys { get; init; }
}

public sealed class LedgerInput
{
    public string? LedgerName { get; set; }
    public bool IsGroup { get; set; }
    public byte? NatureId { get; set; }
    public int? ParentLedgerId { get; set; }
    public string? ReportHead { get; set; }
    public string? CashBankType { get; set; }
    public string? BankAccountNo { get; set; }
    public string? IfscCode { get; set; }
    public bool IsVisibleInBranch { get; set; } = true;
    public bool IsDefaultGroup { get; set; }
    public bool ApplySanction { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Opening balance for the session branch (0 / null removes it).</summary>
    public decimal? OpeningBalance { get; set; }
    public string? OpeningBalType { get; set; }
    public DateOnly? OpeningDate { get; set; }
}

/// <summary>ACCOUNT > Add Groups and Ledgers (chart of accounts) with per-branch opening balances.</summary>
public sealed class LedgerRepository(IDbConnectionFactory db)
{
    public async Task<IReadOnlyList<AccountNatureRow>> NaturesAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<AccountNatureRow>(new CommandDefinition(
            "SELECT NatureId, NatureCode, NatureName, ReportHead, NormalBalance FROM acc.AccountNature ORDER BY NatureId", cancellationToken: ct))).AsList();
    }

    public async Task<IReadOnlyList<LedgerRow>> ListAsync(int branchId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<LedgerRow>(new CommandDefinition("""
            SELECT l.LedgerId AS Id, l.LedgerName, l.NatureId, n.NatureName, l.IsGroup, l.ParentLedgerId, l.ReportHead, l.CashBankType,
                   l.BankAccountNo, l.IfscCode, l.IsEditable, l.IsVisibleInBranch, l.IsDefaultGroup, l.ApplySanction, l.IsSystem, l.IsActive,
                   o.OpeningBalance, o.BalType AS OpeningBalType, o.OpeningDate,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM acc.VoucherLine vl WHERE vl.LedgerId = l.LedgerId) THEN 1 ELSE 0 END AS BIT) AS HasPostings,
                   (SELECT STRING_AGG(m.MappingKey, ', ') FROM acc.LedgerMapping m WHERE m.LedgerId = l.LedgerId) AS MappingKeys
            FROM acc.Ledger l
            JOIN acc.AccountNature n ON n.NatureId = l.NatureId
            LEFT JOIN acc.LedgerOpening o ON o.LedgerId = l.LedgerId AND o.BranchId = @branchId
            ORDER BY l.IsGroup DESC, l.LedgerName
            """, new { branchId }, cancellationToken: ct))).AsList();
    }

    public async Task<int> CreateAsync(int branchId, int userId, LedgerInput input, DateOnly businessDate, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var (natureId, reportHead) = await ValidateAsync(c, input, null, ct);
        await using var tx = c.BeginTransaction();
        var id = await SqlGuard.RunAsync(() => c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT acc.Ledger (LedgerName, NatureId, IsGroup, ParentLedgerId, ReportHead, CashBankType, BankAccountNo, IfscCode,
                               IsVisibleInBranch, IsDefaultGroup, ApplySanction, IsActive, CreatedBy)
            OUTPUT INSERTED.LedgerId
            VALUES (@LedgerName, @natureId, @IsGroup, @ParentLedgerId, @reportHead, @CashBankType, @BankAccountNo, @IfscCode,
                    @IsVisibleInBranch, @IsDefaultGroup, @ApplySanction, @IsActive, @userId)
            """, new
        {
            input.LedgerName, natureId, input.IsGroup, input.ParentLedgerId, reportHead, input.CashBankType, input.BankAccountNo, input.IfscCode,
            input.IsVisibleInBranch, input.IsDefaultGroup, input.ApplySanction, input.IsActive, userId
        }, tx, cancellationToken: ct)), "A ledger or group with this name already exists.");
        await SaveOpeningAsync(c, tx, id, branchId, input, businessDate, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(int branchId, int id, LedgerInput input, DateOnly businessDate, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var current = await c.QuerySingleOrDefaultAsync<LedgerRow>(new CommandDefinition("""
            SELECT LedgerId AS Id, LedgerName, NatureId, IsGroup, ParentLedgerId, CashBankType, IsEditable, IsSystem
            FROM acc.Ledger WHERE LedgerId = @id
            """, new { id }, cancellationToken: ct));
        if (current is null) return false;
        if (!current.IsEditable) throw new DomainException("This is a fixed primary group and cannot be changed.", 409);
        if (input.IsGroup != current.IsGroup) throw new DomainException("A ledger cannot be turned into a group (or back).", 409);
        if (current.IsSystem && (input.ParentLedgerId != current.ParentLedgerId || (input.CashBankType ?? "N") != current.CashBankType))
            throw new DomainException("System ledgers keep their group and cash/bank type; only name, bank details and flags can change.", 409);

        var (natureId, reportHead) = await ValidateAsync(c, input, id, ct);
        await using var tx = c.BeginTransaction();
        await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition("""
            UPDATE acc.Ledger
            SET LedgerName = @LedgerName, NatureId = @natureId, ParentLedgerId = @ParentLedgerId, ReportHead = @reportHead,
                CashBankType = @CashBankType, BankAccountNo = @BankAccountNo, IfscCode = @IfscCode, IsVisibleInBranch = @IsVisibleInBranch,
                IsDefaultGroup = @IsDefaultGroup, ApplySanction = @ApplySanction, IsActive = @IsActive
            WHERE LedgerId = @id
            """, new
        {
            id, input.LedgerName, natureId, input.ParentLedgerId, reportHead, input.CashBankType, input.BankAccountNo, input.IfscCode,
            input.IsVisibleInBranch, input.IsDefaultGroup, input.ApplySanction, input.IsActive
        }, tx, cancellationToken: ct)), "A ledger or group with this name already exists.");
        if (!input.IsGroup) await SaveOpeningAsync(c, tx, id, branchId, input, businessDate, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var info = await c.QuerySingleOrDefaultAsync<DeleteCheck>(new CommandDefinition("""
            SELECT IsSystem, IsEditable,
                   (SELECT COUNT(*) FROM acc.Ledger WHERE ParentLedgerId = @id) AS Children,
                   (SELECT COUNT(*) FROM acc.VoucherLine WHERE LedgerId = @id) AS Postings,
                   (SELECT COUNT(*) FROM acc.LedgerMapping WHERE LedgerId = @id) AS Mappings
            FROM acc.Ledger WHERE LedgerId = @id
            """, new { id }, cancellationToken: ct));
        if (info is null) return false;
        if (info.IsSystem || !info.IsEditable || info.Mappings > 0) throw new DomainException("System ledgers cannot be deleted.", 409);
        if (info.Children > 0) throw new DomainException("Move or delete the ledgers under this group first.", 409);
        if (info.Postings > 0) throw new DomainException("This ledger has postings; mark it inactive instead.", 409);

        await using var tx = c.BeginTransaction();
        try
        {
            await c.ExecuteAsync(new CommandDefinition(
                "DELETE FROM acc.LedgerOpening WHERE LedgerId = @id; DELETE FROM acc.Ledger WHERE LedgerId = @id;", new { id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }
        catch (SqlException ex) when (SqlErrors.IsForeignKeyViolation(ex))
        {
            throw new DomainException("This ledger is used by loans or saving products; mark it inactive instead.", 409);
        }
        return true;
    }

    private static async Task SaveOpeningAsync(SqlConnection c, System.Data.IDbTransaction tx, int ledgerId, int branchId, LedgerInput input, DateOnly businessDate, CancellationToken ct)
    {
        if (input.IsGroup) return;
        if (input.OpeningBalance is null or 0)
        {
            await c.ExecuteAsync(new CommandDefinition("DELETE FROM acc.LedgerOpening WHERE LedgerId = @ledgerId AND BranchId = @branchId",
                new { ledgerId, branchId }, tx, cancellationToken: ct));
            return;
        }
        await c.ExecuteAsync(new CommandDefinition("""
            MERGE acc.LedgerOpening AS t
            USING (SELECT @ledgerId AS LedgerId, @branchId AS BranchId) AS s ON t.LedgerId = s.LedgerId AND t.BranchId = s.BranchId
            WHEN MATCHED THEN UPDATE SET OpeningBalance = @amount, BalType = @balType, OpeningDate = @date
            WHEN NOT MATCHED THEN INSERT (LedgerId, BranchId, OpeningDate, OpeningBalance, BalType) VALUES (@ledgerId, @branchId, @date, @amount, @balType);
            """, new { ledgerId, branchId, amount = input.OpeningBalance, balType = input.OpeningBalType, date = input.OpeningDate ?? businessDate },
            tx, cancellationToken: ct));
    }

    /// <summary>Normalises the input; nature and report head come from the parent group when there is one.</summary>
    private static async Task<(byte NatureId, string ReportHead)> ValidateAsync(SqlConnection c, LedgerInput input, int? selfId, CancellationToken ct)
    {
        input.LedgerName = Text.Clean(input.LedgerName, upper: true);
        input.CashBankType = input.IsGroup ? "N" : Text.Clean(input.CashBankType, upper: true) ?? "N";
        input.BankAccountNo = input.CashBankType == "B" ? Text.Clean(input.BankAccountNo, upper: true) : null;
        input.IfscCode = input.CashBankType == "B" ? Text.Clean(input.IfscCode, upper: true) : null;
        input.ReportHead = Text.Clean(input.ReportHead, upper: true);
        input.OpeningBalType = Text.Clean(input.OpeningBalType, upper: true);

        var v = new FieldValidator();
        v.Required(input.LedgerName, "ledgerName");
        v.MaxLength(input.LedgerName, 150, "ledgerName");
        v.OneOf(input.CashBankType, ["C", "B", "N"], "cashBankType");
        v.MaxLength(input.BankAccountNo, 30, "bankAccountNo");
        v.Pattern(input.IfscCode, @"^[A-Z]{4}0[A-Z0-9]{6}$", "ifscCode", "Invalid IFSC code.");
        if (!input.IsGroup && input.ParentLedgerId is null) v.Add("parentLedgerId", "Choose the group this ledger is under.");
        if (input.OpeningBalance is < 0) v.Add("openingBalance", "Enter a positive amount and choose Dr or Cr.");
        if (input.OpeningBalance is > 0) v.OneOf(input.OpeningBalType, ["D", "C"], "openingBalType");
        if (input.OpeningBalance is > 0 && input.OpeningBalType is null) v.Add("openingBalType", "Choose Dr or Cr.");
        v.ThrowIfAny();

        if (input.ParentLedgerId is { } parentId)
        {
            var parent = await c.QuerySingleOrDefaultAsync<ParentInfo>(new CommandDefinition("""
                WITH Chain AS (
                    SELECT LedgerId, ParentLedgerId, 0 AS Depth FROM acc.Ledger WHERE LedgerId = @parentId
                    UNION ALL
                    SELECT l.LedgerId, l.ParentLedgerId, ch.Depth + 1 FROM Chain ch JOIN acc.Ledger l ON l.LedgerId = ch.ParentLedgerId WHERE ch.Depth < 30)
                SELECT p.NatureId, p.ReportHead, p.IsGroup,
                       CAST(CASE WHEN @selfId IS NOT NULL AND EXISTS (SELECT 1 FROM Chain WHERE LedgerId = @selfId) THEN 1 ELSE 0 END AS BIT) AS CreatesCycle
                FROM acc.Ledger p WHERE p.LedgerId = @parentId
                OPTION (MAXRECURSION 30);
                """, new { parentId, selfId }, cancellationToken: ct));
            if (parent is null || !parent.IsGroup) v.Add("parentLedgerId", "Choose an existing group.");
            else if (parent.CreatesCycle) v.Add("parentLedgerId", "A group cannot be placed under itself or its own sub-group.");
            v.ThrowIfAny();
            return (parent!.NatureId, parent.ReportHead);
        }

        // Primary group (no parent)
        if (input.NatureId is null) v.Add("natureId", "Choose the account type for a primary group.");
        v.ThrowIfAny();
        var head = await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT ReportHead FROM acc.AccountNature WHERE NatureId = @NatureId", new { input.NatureId }, cancellationToken: ct));
        if (head is null) v.Add("natureId", "Unknown account type.");
        v.ThrowIfAny();
        return (input.NatureId!.Value, input.ReportHead is "BS" or "PL" ? input.ReportHead : head!);
    }

    private sealed class ParentInfo
    {
        public byte NatureId { get; init; }
        public string ReportHead { get; init; } = "";
        public bool IsGroup { get; init; }
        public bool CreatesCycle { get; init; }
    }

    private sealed class DeleteCheck
    {
        public bool IsSystem { get; init; }
        public bool IsEditable { get; init; }
        public int Children { get; init; }
        public int Postings { get; init; }
        public int Mappings { get; init; }
    }
}
