using Dapper;

namespace DhanJagrati.Data.Loans;

public sealed class CardSearchItem
{
    public int LoanId { get; init; }
    public string LoanNo { get; init; } = "";
    public string? FileNo { get; init; }
    public string MemberName { get; init; } = "";
    public DateOnly? DisbursedOn { get; init; }
    public decimal LoanAmount { get; init; }
    public int Prints { get; init; }
}

public sealed class LoanCard
{
    public int LoanId { get; init; }
    public int MemberId { get; init; }
    public string LoanNo { get; init; } = "";
    public string? FileNo { get; init; }
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string AddressLine { get; init; } = "";
    public string? MobileNo { get; init; }
    public string Gender { get; init; } = "";
    public int? Age { get; init; }
    public string? OccupationName { get; init; }
    public DateOnly DateOfJoining { get; init; }
    public DateOnly ApplicationDate { get; init; }
    public DateOnly? DisbursedOn { get; init; }
    public decimal LoanAmount { get; init; }
    public decimal InterestRate { get; init; }
    public decimal EmiAmount { get; init; }
    public short NoOfInstallments { get; init; }
    public string EmiMode { get; init; } = "";
    public DateOnly EmiStartDate { get; init; }
    public string PlanCode { get; init; } = "";
    public string DisbursementMode { get; init; } = "";
    public string? ChequeNo { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public string BranchName { get; init; } = "";
    public bool HasPhoto { get; init; }
    public bool HasSignature { get; init; }
    public List<ScheduleRow> Schedule { get; set; } = [];
}

public sealed class LoanCardRepository(IDbConnectionFactory db)
{
    public async Task<IReadOnlyList<CardSearchItem>> SearchAsync(int branchId, DateOnly? from, DateOnly? to, string? fileNo, string? loanNo, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        return (await c.QueryAsync<CardSearchItem>(new CommandDefinition("""
            SELECT TOP (300) l.LoanId, l.LoanNo, m.FileNo, m.MemberName, l.DisbursedOn, l.LoanAmount,
                   (SELECT COUNT(*) FROM loan.CardPrintLog p WHERE p.LoanId = l.LoanId) AS Prints
            FROM loan.LoanApplication l
            JOIN loan.Member m ON m.MemberId = l.MemberId
            WHERE l.BranchId = @branchId AND l.Status IN ('APPROVED', 'CLOSED')
              AND (@loanNo IS NULL OR l.LoanNo = @loanNo)
              AND (@fileNo IS NULL OR m.FileNo = @fileNo)
              AND (@loanNo IS NOT NULL OR @fileNo IS NOT NULL OR l.DisbursedOn BETWEEN @from AND @to)
            ORDER BY l.DisbursedOn DESC, l.LoanNo DESC
            """, new { branchId, from, to, fileNo = Text.Clean(fileNo, upper: true), loanNo = Text.Clean(loanNo, upper: true) },
            cancellationToken: ct))).AsList();
    }

    public async Task<LoanCard?> GetAsync(int branchId, int loanId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("""
            SELECT l.LoanId, m.MemberId, l.LoanNo, m.FileNo, m.MemberNo, m.MemberName, m.GuardianName, m.AddressLine, m.MobileNo, m.Gender,
                   CASE WHEN m.DateOfBirth IS NULL THEN NULL ELSE
                        DATEDIFF(YEAR, m.DateOfBirth, l.ApplicationDate)
                        - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, m.DateOfBirth, l.ApplicationDate), m.DateOfBirth) > l.ApplicationDate THEN 1 ELSE 0 END END AS Age,
                   o.OccupationName, m.RegistrationDate AS DateOfJoining, l.ApplicationDate, l.DisbursedOn, l.LoanAmount,
                   l.InterestRate, l.EmiAmount, l.NoOfInstallments, l.EmiMode, l.EmiStartDate, p.PlanCode, l.DisbursementMode,
                   l.ChequeNo, e.EmployeeCode, e.EmployeeName, b.BranchName,
                   CAST(CASE WHEN m.PhotoPath IS NULL THEN 0 ELSE 1 END AS BIT) AS HasPhoto,
                   CAST(CASE WHEN m.SignaturePath IS NULL THEN 0 ELSE 1 END AS BIT) AS HasSignature
            FROM loan.LoanApplication l
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN mst.LoanPlan p ON p.PlanId = l.PlanId
            JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
            JOIN sec.Branch b ON b.BranchId = l.BranchId
            LEFT JOIN mst.Occupation o ON o.OccupationId = m.OccupationId
            WHERE l.LoanId = @loanId AND l.BranchId = @branchId AND l.Status IN ('APPROVED', 'CLOSED');

            SELECT InstallmentNo, DueDate, EmiAmount, PrincipalAmount, InterestAmount, PrincipalPaid, InterestPaid, Status
            FROM loan.EmiSchedule WHERE LoanId = @loanId ORDER BY InstallmentNo;
            """, new { branchId, loanId }, cancellationToken: ct));
        var card = await multi.ReadSingleOrDefaultAsync<LoanCard>();
        if (card is null) return null;
        card.Schedule = (await multi.ReadAsync<ScheduleRow>()).AsList();
        return card;
    }

    public async Task LogPrintAsync(int branchId, int loanId, string cardType, DateOnly printDate, int userId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT loan.CardPrintLog (LoanId, CardType, PrintDate, PrintedBy)
            SELECT LoanId, @cardType, @printDate, @userId FROM loan.LoanApplication WHERE LoanId = @loanId AND BranchId = @branchId
            """, new { branchId, loanId, cardType, printDate, userId }, cancellationToken: ct));
    }
}
