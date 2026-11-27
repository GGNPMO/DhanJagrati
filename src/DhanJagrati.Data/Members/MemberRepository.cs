using Dapper;

namespace DhanJagrati.Data.Members;

/// <summary>Editable member (customer) fields. Property names match loan.Member columns.</summary>
public sealed class MemberInput
{
    public string? FileNo { get; set; }
    public string? AadhaarNo { get; set; }
    public DateOnly? RegistrationDate { get; set; }
    public int? SalutationId { get; set; }
    public string? MemberName { get; set; }
    public int? GuardianRelationId { get; set; }
    public string? GuardianName { get; set; }
    public string? MotherName { get; set; }
    public string? Gender { get; set; }
    public int? ReligionId { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public decimal? MonthlyIncome { get; set; }
    public string? MaritalStatus { get; set; }
    public bool IsSeniorCitizen { get; set; }
    public bool IsHandicapped { get; set; }
    public bool IsSpecialCategory { get; set; }
    public int? OccupationId { get; set; }
    public string? PanNo { get; set; }
    public int? BankId { get; set; }
    public string? IfscCode { get; set; }
    public string? BankAccountNo { get; set; }
    public string? AddressLine { get; set; }
    public string? PermanentAddress { get; set; }
    public int? VillageId { get; set; }
    public int? CityId { get; set; }
    public int? StateId { get; set; }
    public int? CountryId { get; set; }
    public string? PinCode { get; set; }
    public string? MobileNo { get; set; }
    public string? PhoneNo { get; set; }
    public string? Email { get; set; }
    public string? PassportNo { get; set; }
    public string? NomineeName { get; set; }
    public string? NomineeGuardianName { get; set; }
    public DateOnly? NomineeDob { get; set; }
    public int? NomineeRelationId { get; set; }
    public string? RefMemberNo { get; set; }
    public bool KycAadhaar { get; set; }
    public bool KycVoterId { get; set; }
    public bool KycDrivingLicense { get; set; }
    public bool KycOtherProof { get; set; }
    public string? IdProofNo { get; set; }
    public int? GroupId { get; set; }
    public int? DesignationId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MemberDetail
{
    public int Id { get; init; }
    public string MemberNo { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public string? FileNo { get; init; }
    public string? AadhaarNo { get; init; }
    public DateOnly RegistrationDate { get; init; }
    public int? SalutationId { get; init; }
    public string MemberName { get; init; } = "";
    public int? GuardianRelationId { get; init; }
    public string? GuardianName { get; init; }
    public string? MotherName { get; init; }
    public string Gender { get; init; } = "";
    public int? ReligionId { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public decimal? MonthlyIncome { get; init; }
    public string? MaritalStatus { get; init; }
    public bool IsSeniorCitizen { get; init; }
    public bool IsHandicapped { get; init; }
    public bool IsSpecialCategory { get; init; }
    public int? OccupationId { get; init; }
    public string? PanNo { get; init; }
    public int? BankId { get; init; }
    public string? IfscCode { get; init; }
    public string? BankAccountNo { get; init; }
    public string AddressLine { get; init; } = "";
    public string? PermanentAddress { get; init; }
    public int? VillageId { get; init; }
    public int? CityId { get; init; }
    public int? StateId { get; init; }
    public int? CountryId { get; init; }
    public string? PinCode { get; init; }
    public string? MobileNo { get; init; }
    public string? PhoneNo { get; init; }
    public string? Email { get; init; }
    public string? PassportNo { get; init; }
    public string? NomineeName { get; init; }
    public string? NomineeGuardianName { get; init; }
    public DateOnly? NomineeDob { get; init; }
    public int? NomineeRelationId { get; init; }
    public string? RefMemberNo { get; init; }
    public bool KycAadhaar { get; init; }
    public bool KycVoterId { get; init; }
    public bool KycDrivingLicense { get; init; }
    public bool KycOtherProof { get; init; }
    public string? IdProofNo { get; init; }
    public int? GroupId { get; init; }
    public int? DesignationId { get; init; }
    public bool HasPhoto { get; init; }
    public bool HasSignature { get; init; }
    public bool IsPhotoVerified { get; init; }
    public bool IsActive { get; init; }
}

public sealed class MemberListItem
{
    public int Id { get; init; }
    public string MemberNo { get; init; } = "";
    public string? FileNo { get; init; }
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string? MobileNo { get; init; }
    public string? AadhaarNo { get; init; }
    public DateOnly RegistrationDate { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>Short member details used to auto-fill other screens (employee, loan application).</summary>
public sealed class MemberSummary
{
    public int Id { get; init; }
    public string MemberNo { get; init; } = "";
    public int BranchId { get; init; }
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string Gender { get; init; } = "";
    public DateOnly? DateOfBirth { get; init; }
    public string AddressLine { get; init; } = "";
    public int? CityId { get; init; }
    public int? StateId { get; init; }
    public int? CountryId { get; init; }
    public string? PinCode { get; init; }
    public string? MobileNo { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; }
}

public enum Navigate { First, Previous, Next, Last }

public sealed class MemberRepository(IDbConnectionFactory db)
{
    /// <summary>Columns written from <see cref="MemberInput"/> (identifiers are code constants).</summary>
    private static readonly string[] Columns =
    [
        "FileNo", "AadhaarNo", "RegistrationDate", "SalutationId", "MemberName", "GuardianRelationId", "GuardianName",
        "MotherName", "Gender", "ReligionId", "DateOfBirth", "MonthlyIncome", "MaritalStatus", "IsSeniorCitizen",
        "IsHandicapped", "IsSpecialCategory", "OccupationId", "PanNo", "BankId", "IfscCode", "BankAccountNo",
        "AddressLine", "PermanentAddress", "VillageId", "CityId", "StateId", "CountryId", "PinCode", "MobileNo",
        "PhoneNo", "Email", "PassportNo", "NomineeName", "NomineeGuardianName", "NomineeDob", "NomineeRelationId",
        "KycAadhaar", "KycVoterId", "KycDrivingLicense", "KycOtherProof", "IdProofNo", "GroupId", "DesignationId", "IsActive"
    ];

    private const string DetailSelect = """
        SELECT m.MemberId AS Id, m.MemberNo, m.BranchId, b.BranchName, m.FileNo, m.AadhaarNo, m.RegistrationDate,
               m.SalutationId, m.MemberName, m.GuardianRelationId, m.GuardianName, m.MotherName, m.Gender, m.ReligionId,
               m.DateOfBirth, m.MonthlyIncome, m.MaritalStatus, m.IsSeniorCitizen, m.IsHandicapped, m.IsSpecialCategory,
               m.OccupationId, m.PanNo, m.BankId, m.IfscCode, m.BankAccountNo, m.AddressLine, m.PermanentAddress,
               m.VillageId, m.CityId, m.StateId, m.CountryId, m.PinCode, m.MobileNo, m.PhoneNo, m.Email, m.PassportNo,
               m.NomineeName, m.NomineeGuardianName, m.NomineeDob, m.NomineeRelationId, rm.MemberNo AS RefMemberNo,
               m.KycAadhaar, m.KycVoterId, m.KycDrivingLicense, m.KycOtherProof, m.IdProofNo, m.GroupId, m.DesignationId,
               CAST(CASE WHEN m.PhotoPath IS NULL THEN 0 ELSE 1 END AS BIT) AS HasPhoto,
               CAST(CASE WHEN m.SignaturePath IS NULL THEN 0 ELSE 1 END AS BIT) AS HasSignature,
               m.IsPhotoVerified, m.IsActive
        FROM loan.Member m
        JOIN sec.Branch b ON b.BranchId = m.BranchId
        LEFT JOIN loan.Member rm ON rm.MemberId = m.RefMemberId
        """;

    public async Task<IReadOnlyList<MemberListItem>> SearchAsync(int branchId, string? search, CancellationToken ct)
    {
        var term = Text.Clean(search);
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<MemberListItem>(new CommandDefinition("""
            SELECT TOP (200) m.MemberId AS Id, m.MemberNo, m.FileNo, m.MemberName, m.GuardianName, m.MobileNo,
                   m.AadhaarNo, m.RegistrationDate, m.IsActive
            FROM loan.Member m
            WHERE m.BranchId = @branchId
              AND (@term IS NULL OR m.MemberNo LIKE @like OR m.MemberName LIKE @like OR m.FileNo LIKE @like
                   OR m.MobileNo LIKE @like OR m.AadhaarNo LIKE @like)
            ORDER BY CASE WHEN @term IS NULL THEN m.MemberId END DESC, m.MemberName
            """, new { branchId, term, like = $"%{term}%" }, cancellationToken: ct))).AsList();
    }

    public async Task<MemberDetail?> GetAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<MemberDetail>(new CommandDefinition(
            DetailSelect + " WHERE m.MemberId = @id AND m.BranchId = @branchId", new { id, branchId }, cancellationToken: ct));
    }

    /// <summary>Lookup by member number in any branch (an advisor may be a member of another branch).</summary>
    public async Task<MemberSummary?> FindByNumberAsync(string memberNo, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<MemberSummary>(new CommandDefinition("""
            SELECT MemberId AS Id, MemberNo, BranchId, MemberName, GuardianName, Gender, DateOfBirth, AddressLine,
                   CityId, StateId, CountryId, PinCode, MobileNo, Email, IsActive
            FROM loan.Member WHERE MemberNo = @memberNo
            """, new { memberNo = memberNo.Trim().ToUpperInvariant() }, cancellationToken: ct));
    }

    public async Task<int?> NavigateAsync(int branchId, Navigate direction, string? fromMemberNo, CancellationToken ct)
    {
        var sql = direction switch
        {
            Navigate.First => "SELECT TOP (1) MemberId FROM loan.Member WHERE BranchId = @branchId ORDER BY MemberNo",
            Navigate.Last => "SELECT TOP (1) MemberId FROM loan.Member WHERE BranchId = @branchId ORDER BY MemberNo DESC",
            Navigate.Next => "SELECT TOP (1) MemberId FROM loan.Member WHERE BranchId = @branchId AND MemberNo > @from ORDER BY MemberNo",
            _ => "SELECT TOP (1) MemberId FROM loan.Member WHERE BranchId = @branchId AND MemberNo < @from ORDER BY MemberNo DESC"
        };
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { branchId, from = fromMemberNo ?? "" }, cancellationToken: ct));
    }

    public async Task<int> CreateAsync(int branchId, int userId, MemberInput input, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        await using var c = await db.OpenAsync(ct);
        await ValidateAsync(c, branchId, input, today, null, ct);

        await using var tx = c.BeginTransaction();
        var memberNo = await SqlGuard.NextNumberAsync(c, tx, "MEMBER", 0, ct);
        var p = new DynamicParameters(input);
        p.AddDynamicParams(new { MemberNo = memberNo, BranchId = branchId, CreatedBy = userId });

        var sql = $"""
            INSERT loan.Member (MemberNo, BranchId, CreatedBy, RefMemberId, {string.Join(", ", Columns)})
            OUTPUT INSERTED.MemberId
            VALUES (@MemberNo, @BranchId, @CreatedBy,
                    (SELECT MemberId FROM loan.Member WHERE MemberNo = @RefMemberNo),
                    {string.Join(", ", Columns.Select(col => "@" + col))})
            """;
        var id = await SqlGuard.RunAsync(() => c.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, tx, cancellationToken: ct)),
            "A member with this Aadhaar number already exists.");
        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> UpdateAsync(int branchId, int userId, int id, MemberInput input, DateOnly today, CancellationToken ct)
    {
        Normalize(input);
        await using var c = await db.OpenAsync(ct);
        await ValidateAsync(c, branchId, input, today, id, ct);

        var p = new DynamicParameters(input);
        p.AddDynamicParams(new { Id = id, BranchId = branchId, ModifiedBy = userId });
        var sql = $"""
            UPDATE loan.Member
            SET {string.Join(", ", Columns.Select(col => $"{col} = @{col}"))},
                RefMemberId = (SELECT MemberId FROM loan.Member WHERE MemberNo = @RefMemberNo),
                ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @ModifiedBy
            WHERE MemberId = @Id AND BranchId = @BranchId
            """;
        return await SqlGuard.RunAsync(() => c.ExecuteAsync(new CommandDefinition(sql, p, cancellationToken: ct)),
            "A member with this Aadhaar number already exists.") == 1;
    }

    public async Task<bool> DeleteAsync(int branchId, int id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        try
        {
            return await c.ExecuteAsync(new CommandDefinition(
                "DELETE FROM loan.Member WHERE MemberId = @id AND BranchId = @branchId", new { id, branchId }, cancellationToken: ct)) == 1;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (SqlErrors.IsForeignKeyViolation(ex))
        {
            throw new DomainException("This member has loans, accounts or references and cannot be deleted. Mark it inactive instead.", 409);
        }
    }

    public async Task<string?> GetFilePathAsync(int branchId, int id, bool signature, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<string?>(new CommandDefinition(
            $"SELECT {(signature ? "SignaturePath" : "PhotoPath")} FROM loan.Member WHERE MemberId = @id AND BranchId = @branchId",
            new { id, branchId }, cancellationToken: ct));
    }

    /// <summary>Stores the new file path and returns the previous one (to delete). A new photo needs re-verification.</summary>
    public async Task<(bool Found, string? Previous)> SetFilePathAsync(int branchId, int id, bool signature, string path, int userId, CancellationToken ct)
    {
        var column = signature ? "SignaturePath" : "PhotoPath";
        await using var c = await db.OpenAsync(ct);
        var previous = await c.QuerySingleOrDefaultAsync<FileChange>(new CommandDefinition($"""
            UPDATE loan.Member SET {column} = @path, IsPhotoVerified = 0, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            OUTPUT INSERTED.MemberId, DELETED.{column} AS OldPath
            WHERE MemberId = @id AND BranchId = @branchId
            """, new { id, branchId, path, userId }, cancellationToken: ct));
        return previous is null ? (false, null) : (true, previous.OldPath);
    }

    private sealed class FileChange
    {
        public int MemberId { get; init; }
        public string? OldPath { get; init; }
    }

    public async Task<bool> SetPhotoVerifiedAsync(int branchId, int id, bool verified, int userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.Member SET IsPhotoVerified = @verified, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @userId
            WHERE MemberId = @id AND BranchId = @branchId AND (@verified = 0 OR (PhotoPath IS NOT NULL AND SignaturePath IS NOT NULL))
            """, new { id, branchId, verified, userId }, cancellationToken: ct)) == 1;
    }

    private static readonly string[] Genders = ["M", "F", "O"];
    private static readonly string[] MaritalStatuses = ["SINGLE", "MARRIED", "WIDOWED", "DIVORCED"];

    private static void Normalize(MemberInput m)
    {
        m.FileNo = Text.Clean(m.FileNo, upper: true);
        m.AadhaarNo = Text.Clean(m.AadhaarNo)?.Replace(" ", "");
        m.MemberName = Text.Clean(m.MemberName, upper: true);
        m.GuardianName = Text.Clean(m.GuardianName, upper: true);
        m.MotherName = Text.Clean(m.MotherName, upper: true);
        m.Gender = Text.Clean(m.Gender, upper: true);
        m.MaritalStatus = Text.Clean(m.MaritalStatus, upper: true);
        m.PanNo = Text.Clean(m.PanNo, upper: true);
        m.IfscCode = Text.Clean(m.IfscCode, upper: true);
        m.BankAccountNo = Text.Clean(m.BankAccountNo);
        m.AddressLine = Text.Clean(m.AddressLine, upper: true);
        m.PermanentAddress = Text.Clean(m.PermanentAddress, upper: true);
        m.PinCode = Text.Clean(m.PinCode);
        m.MobileNo = Text.Clean(m.MobileNo);
        m.PhoneNo = Text.Clean(m.PhoneNo);
        m.Email = Text.Clean(m.Email)?.ToLowerInvariant();
        m.PassportNo = Text.Clean(m.PassportNo, upper: true);
        m.NomineeName = Text.Clean(m.NomineeName, upper: true);
        m.NomineeGuardianName = Text.Clean(m.NomineeGuardianName, upper: true);
        m.RefMemberNo = Text.Clean(m.RefMemberNo, upper: true);
        m.IdProofNo = Text.Clean(m.IdProofNo, upper: true);
    }

    private static async Task ValidateAsync(Microsoft.Data.SqlClient.SqlConnection c, int branchId, MemberInput m, DateOnly today, int? selfId, CancellationToken ct)
    {
        var v = new FieldValidator();
        v.Required(m.MemberName, "memberName");
        v.MaxLength(m.MemberName, 100, "memberName");
        v.Required(m.Gender, "gender");
        v.OneOf(m.Gender, Genders, "gender");
        v.OneOf(m.MaritalStatus, MaritalStatuses, "maritalStatus");
        v.Required(m.RegistrationDate, "registrationDate");
        v.Required(m.AddressLine, "addressLine");
        v.MaxLength(m.AddressLine, 300, "addressLine");
        v.MaxLength(m.PermanentAddress, 300, "permanentAddress");
        v.MaxLength(m.GuardianName, 100, "guardianName");
        v.MaxLength(m.MotherName, 100, "motherName");
        v.MaxLength(m.FileNo, 20, "fileNo");
        v.MaxLength(m.BankAccountNo, 30, "bankAccountNo");
        v.MaxLength(m.PhoneNo, 20, "phoneNo");
        v.MaxLength(m.PassportNo, 20, "passportNo");
        v.MaxLength(m.IdProofNo, 30, "idProofNo");
        v.MaxLength(m.NomineeName, 100, "nomineeName");
        v.MaxLength(m.NomineeGuardianName, 100, "nomineeGuardianName");
        v.MaxLength(m.Email, 120, "email");
        v.Pattern(m.AadhaarNo, @"^\d{12}$", "aadhaarNo", "Aadhaar must be 12 digits.");
        v.Pattern(m.MobileNo, @"^[6-9]\d{9}$", "mobileNo", "Enter a 10-digit mobile number.");
        v.Pattern(m.PinCode, @"^\d{6}$", "pinCode", "PIN code must be 6 digits.");
        v.Pattern(m.PanNo, @"^[A-Z]{5}\d{4}[A-Z]$", "panNo", "Invalid PAN format (e.g. ABCDE1234F).");
        v.Pattern(m.IfscCode, @"^[A-Z]{4}0[A-Z0-9]{6}$", "ifscCode", "Invalid IFSC code (e.g. SBIN0001234).");
        v.Pattern(m.Email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", "email", "Invalid email address.");
        if (m.MonthlyIncome is < 0) v.Add("monthlyIncome", "Cannot be negative.");
        if (m.RegistrationDate > today) v.Add("registrationDate", "Cannot be in the future.");
        if (m.DateOfBirth is { } dob)
        {
            var onDate = m.RegistrationDate ?? today;
            var age = onDate.Year - dob.Year - (onDate.DayOfYear < dob.DayOfYear ? 1 : 0);
            if (dob > today) v.Add("dateOfBirth", "Cannot be in the future.");
            else if (age < 18) v.Add("dateOfBirth", "Member must be at least 18 years old.");
            else if (age > 100) v.Add("dateOfBirth", "Check the date of birth.");
        }
        if (m.NomineeDob > today) v.Add("nomineeDob", "Cannot be in the future.");

        if (!v.HasErrors)
        {
            var checks = await c.QuerySingleAsync<(int RefOk, int GroupOk, int GuardianOk)>(new CommandDefinition("""
                SELECT
                  CASE WHEN @RefMemberNo IS NULL OR EXISTS (SELECT 1 FROM loan.Member WHERE MemberNo = @RefMemberNo
                                                             AND (@SelfId IS NULL OR MemberId <> @SelfId)) THEN 1 ELSE 0 END,
                  CASE WHEN @GroupId IS NULL OR EXISTS (SELECT 1 FROM mst.MemberGroup WHERE GroupId = @GroupId AND BranchId = @BranchId) THEN 1 ELSE 0 END,
                  CASE WHEN @GuardianRelationId IS NULL OR EXISTS (SELECT 1 FROM mst.Salutation WHERE SalutationId = @GuardianRelationId AND IsRelation = 1) THEN 1 ELSE 0 END
                """, new { m.RefMemberNo, m.GroupId, m.GuardianRelationId, BranchId = branchId, SelfId = selfId }, cancellationToken: ct));
            if (checks.RefOk == 0) v.Add("refMemberNo", "Reference member not found.");
            if (checks.GroupOk == 0) v.Add("groupId", "Group does not belong to this branch.");
            if (checks.GuardianOk == 0) v.Add("guardianRelationId", "Choose S/O, W/O or D/O.");
        }
        v.ThrowIfAny();
    }
}
