using Dapper;

namespace DhanJagrati.Data.Admin;

public sealed class DashboardSummary
{
    public int ActiveMembers { get; init; }
    public int NewMembersThisMonth { get; init; }
    public int ActiveAdvisors { get; init; }
    public int PendingLoans { get; init; }
    public decimal PendingAmount { get; init; }
    public int ActiveLoans { get; init; }
    public decimal OutstandingPrincipal { get; init; }
    public decimal DisbursedThisMonth { get; init; }
    public int DisbursedCountThisMonth { get; init; }
    public int DueToday { get; init; }
    public decimal DueTodayAmount { get; init; }
    public int Overdue { get; init; }
    public decimal OverdueAmount { get; init; }
    public List<RecentLoan> RecentLoans { get; set; } = [];
    public List<MonthPoint> Disbursements { get; set; } = [];
}

public sealed class RecentLoan
{
    public int Id { get; init; }
    public string LoanNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public decimal LoanAmount { get; init; }
    public string Status { get; init; } = "";
    public DateOnly ApplicationDate { get; init; }
}

public sealed class MonthPoint
{
    public string Month { get; init; } = "";
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

/// <summary>Branch dashboard figures for the home page.</summary>
public sealed class DashboardRepository(IDbConnectionFactory db)
{
    public async Task<DashboardSummary> GetAsync(int branchId, DateOnly businessDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(businessDate.Year, businessDate.Month, 1);
        var sixMonthsAgo = monthStart.AddMonths(-5);
        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition("""
            SELECT
              (SELECT COUNT(*) FROM loan.Member WHERE BranchId = @branchId AND IsActive = 1) AS ActiveMembers,
              (SELECT COUNT(*) FROM loan.Member WHERE BranchId = @branchId AND RegistrationDate >= @monthStart) AS NewMembersThisMonth,
              (SELECT COUNT(*) FROM emp.Employee WHERE BranchId = @branchId AND IsActive = 1) AS ActiveAdvisors,
              (SELECT COUNT(*) FROM loan.LoanApplication WHERE BranchId = @branchId AND Status = 'PENDING') AS PendingLoans,
              (SELECT ISNULL(SUM(LoanAmount), 0) FROM loan.LoanApplication WHERE BranchId = @branchId AND Status = 'PENDING') AS PendingAmount,
              (SELECT COUNT(*) FROM loan.LoanApplication WHERE BranchId = @branchId AND Status = 'APPROVED') AS ActiveLoans,
              (SELECT ISNULL(SUM(o.BalancePrincipal), 0) FROM loan.vw_LoanOutstanding o
                 JOIN loan.LoanApplication l ON l.LoanId = o.LoanId WHERE l.BranchId = @branchId AND l.Status = 'APPROVED') AS OutstandingPrincipal,
              (SELECT ISNULL(SUM(LoanAmount), 0) FROM loan.LoanApplication WHERE BranchId = @branchId AND DisbursedOn BETWEEN @monthStart AND @businessDate) AS DisbursedThisMonth,
              (SELECT COUNT(*) FROM loan.LoanApplication WHERE BranchId = @branchId AND DisbursedOn BETWEEN @monthStart AND @businessDate) AS DisbursedCountThisMonth,
              (SELECT COUNT(*) FROM loan.EmiSchedule s JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
                 WHERE l.BranchId = @branchId AND l.Status = 'APPROVED' AND s.Status IN ('DUE','PARTIAL') AND s.DueDate = @businessDate) AS DueToday,
              (SELECT ISNULL(SUM(s.EmiAmount - s.PrincipalPaid - s.InterestPaid), 0) FROM loan.EmiSchedule s JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
                 WHERE l.BranchId = @branchId AND l.Status = 'APPROVED' AND s.Status IN ('DUE','PARTIAL') AND s.DueDate = @businessDate) AS DueTodayAmount,
              (SELECT COUNT(*) FROM loan.EmiSchedule s JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
                 WHERE l.BranchId = @branchId AND l.Status = 'APPROVED' AND s.Status IN ('DUE','PARTIAL') AND s.DueDate < @businessDate) AS Overdue,
              (SELECT ISNULL(SUM(s.EmiAmount - s.PrincipalPaid - s.InterestPaid), 0) FROM loan.EmiSchedule s JOIN loan.LoanApplication l ON l.LoanId = s.LoanId
                 WHERE l.BranchId = @branchId AND l.Status = 'APPROVED' AND s.Status IN ('DUE','PARTIAL') AND s.DueDate < @businessDate) AS OverdueAmount;

            SELECT TOP (6) l.LoanId AS Id, l.LoanNo, m.MemberName, l.LoanAmount, l.Status, l.ApplicationDate
            FROM loan.LoanApplication l JOIN loan.Member m ON m.MemberId = l.MemberId
            WHERE l.BranchId = @branchId ORDER BY l.LoanId DESC;

            SELECT FORMAT(DATEFROMPARTS(YEAR(DisbursedOn), MONTH(DisbursedOn), 1), 'MMM yy') AS Month,
                   SUM(LoanAmount) AS Amount, COUNT(*) AS Count, MIN(DisbursedOn) AS SortKey
            FROM loan.LoanApplication
            WHERE BranchId = @branchId AND DisbursedOn BETWEEN @sixMonthsAgo AND @businessDate
            GROUP BY YEAR(DisbursedOn), MONTH(DisbursedOn)
            ORDER BY SortKey;
            """, new { branchId, businessDate, monthStart, sixMonthsAgo }, cancellationToken: ct));

        var summary = await multi.ReadSingleAsync<DashboardSummary>();
        summary.RecentLoans = (await multi.ReadAsync<RecentLoan>()).AsList();
        summary.Disbursements = (await multi.ReadAsync<MonthPoint>()).AsList();
        return summary;
    }
}
