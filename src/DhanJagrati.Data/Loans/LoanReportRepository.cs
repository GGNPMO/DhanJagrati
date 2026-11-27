using System.Data;
using Dapper;

namespace DhanJagrati.Data.Loans;

/// <summary>LOAN MASTER reports (7.1 - 7.5). Rows are returned as column/value maps for the generic report grid.</summary>
public sealed class LoanReportRepository(IDbConnectionFactory db)
{
    private static readonly string[] ApplicationTypes = ["APPLICATION", "SANCTION", "PAYMENT", "WITH_SAVING"];

    public async Task<IReadOnlyList<IDictionary<string, object?>>> ApplicationsAsync(int branchId, DateOnly from, DateOnly to, string? reportType,
        string? loanNo, string? memberNo, int? planId, CancellationToken ct)
    {
        var type = Text.Clean(reportType, upper: true);
        if (type is null || !ApplicationTypes.Contains(type)) type = "APPLICATION";
        return await ProcAsync("loan.usp_LoanApplicationReport", new
        {
            BranchId = branchId, FromDate = from, ToDate = to, ReportType = type,
            LoanNo = Text.Clean(loanNo, upper: true), MemberNo = Text.Clean(memberNo, upper: true), PlanId = planId
        }, ct);
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> RecoveryDueAsync(int branchId, DateOnly from, DateOnly to, string? employeeCode, int? groupId, string? emiMode, CancellationToken ct) =>
        ProcAsync("loan.usp_RecoveryDueReport", new
        {
            BranchId = branchId, FromDate = from, ToDate = to, EmployeeCode = Text.Clean(employeeCode, upper: true), GroupId = groupId,
            EmiMode = Text.Clean(emiMode, upper: true) is { } m && "DWFM".Contains(m) && m.Length == 1 ? m : null
        }, ct);

    public Task<IReadOnlyList<IDictionary<string, object?>>> EmployeeWiseAsync(string? employeeCode, string? employeeName, int? rankId, CancellationToken ct) =>
        ProcAsync("loan.usp_EmployeeWiseLoanReport", new
        {
            EmployeeCode = Text.Clean(employeeCode, upper: true), EmployeeName = Text.Clean(employeeName, upper: true), RankId = rankId
        }, ct);

    public Task<IReadOnlyList<IDictionary<string, object?>>> RecoveryLedgerAsync(int branchId, DateOnly from, DateOnly to, string? employeeCode, string? loanNo, CancellationToken ct) =>
        ProcAsync("loan.usp_LoanRecoveryLedger", new
        {
            BranchId = branchId, FromDate = from, ToDate = to, EmployeeCode = Text.Clean(employeeCode, upper: true), LoanNo = Text.Clean(loanNo, upper: true)
        }, ct);

    /// <summary>Loans closed in the period, whether by settlement or by full repayment.</summary>
    public async Task<IReadOnlyList<IDictionary<string, object?>>> ClosuresAsync(int branchId, DateOnly from, DateOnly to, string? loanNo, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync(new CommandDefinition("""
            SELECT l.ClosedOn AS CloseDate, l.LoanNo, l.ApplicationDate, m.MemberNo, m.MemberName, l.LoanAmount, l.InterestRate,
                   l.NoOfInstallments, l.EmiAmount,
                   CASE WHEN s.SettlementId IS NULL THEN 'REPAID' ELSE 'SETTLED' END AS ClosedBy,
                   o.PrincipalReceived AS TotalPrincipalReceived, o.InterestReceived AS TotalInterestReceived,
                   ISNULL(o.LateFeeReceived, 0) + ISNULL(s.LateFeeReceived, 0) AS LateFeeReceived,
                   ISNULL(s.PreCloseCharges, 0) AS PreCloseCharges, ISNULL(s.NetCloseAmount, 0) AS SettlementAmount,
                   (SELECT ISNULL(SUM(s2.InterestAmount - s2.InterestPaid), 0) FROM loan.EmiSchedule s2
                     WHERE s2.LoanId = l.LoanId AND s2.Status = 'WAIVED') AS InterestWaived,
                   e.EmployeeCode, e.EmployeeName
            FROM loan.LoanApplication l
            JOIN loan.Member m ON m.MemberId = l.MemberId
            JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
            LEFT JOIN loan.vw_LoanOutstanding o ON o.LoanId = l.LoanId
            OUTER APPLY (SELECT TOP (1) ls.SettlementId, ls.LateFeeReceived, ls.PreCloseCharges, ls.NetCloseAmount
                         FROM loan.LoanSettlement ls WHERE ls.LoanId = l.LoanId AND ls.ActionType = 'CLOSE'
                           AND NOT EXISTS (SELECT 1 FROM loan.LoanSettlement r WHERE r.LoanId = ls.LoanId AND r.ActionType = 'REOPEN' AND r.SettlementId > ls.SettlementId)
                         ORDER BY ls.SettlementId DESC) s
            WHERE l.BranchId = @branchId AND l.Status = 'CLOSED' AND l.ClosedOn BETWEEN @from AND @to
              AND (@loanNo IS NULL OR l.LoanNo = @loanNo)
            ORDER BY l.ClosedOn, l.LoanNo
            """, new { branchId, from, to, loanNo = Text.Clean(loanNo, upper: true) }, cancellationToken: ct));
        return rows.Select(Rows.ToDictionary).ToList();
    }

    private async Task<IReadOnlyList<IDictionary<string, object?>>> ProcAsync(string proc, object args, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync(new CommandDefinition(proc, args, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows.Select(Rows.ToDictionary).ToList();
    }
}

public static class Rows
{
    /// <summary>Dapper dynamic row -> dictionary; whole-day DateTime values become DateOnly (serialised as yyyy-MM-dd).</summary>
    public static IDictionary<string, object?> ToDictionary(dynamic row)
    {
        var source = (IDictionary<string, object?>)row;
        var result = new Dictionary<string, object?>(source.Count);
        foreach (var (key, value) in source)
            result[key] = value is DateTime dt && dt.TimeOfDay == TimeSpan.Zero ? DateOnly.FromDateTime(dt) : value;
        return result;
    }
}
