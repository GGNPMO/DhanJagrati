using System.Data;
using Dapper;
using DhanJagrati.Core.Loans;
using Microsoft.Data.SqlClient;

namespace DhanJagrati.Data.Recovery;

public sealed class LoanHeader
{
    public int LoanId { get; init; }
    public string LoanNo { get; init; } = "";
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public string Status { get; init; } = "";
    public DateOnly ApplicationDate { get; init; }
    public DateOnly? ApprovedOn { get; init; }
    public DateOnly? DisbursedOn { get; init; }
    public DateOnly? ClosedOn { get; init; }
    public string MemberNo { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string? GuardianName { get; init; }
    public string PlanCode { get; init; } = "";
    public decimal LoanAmount { get; init; }
    public decimal InterestRate { get; init; }
    public short NoOfInstallments { get; init; }
    public decimal EmiAmount { get; init; }
    public string EmiMode { get; init; } = "";
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string EmployeeName { get; init; } = "";
    public decimal LateFeePerInstallment { get; init; }
    public decimal PreCloseChargesPct { get; init; }
    public int? SavingAccountId { get; init; }
    public string? SavingAccountNo { get; init; }
}

public sealed class ScheduleState
{
    public short InstallmentNo { get; init; }
    public DateOnly DueDate { get; init; }
    public decimal EmiAmount { get; init; }
    public decimal PrincipalAmount { get; init; }
    public decimal InterestAmount { get; init; }
    public decimal PrincipalPaid { get; init; }
    public decimal InterestPaid { get; init; }
    public decimal LateFeePaid { get; init; }
    public string Status { get; init; } = "";

    public InstallmentPosition ToPosition() =>
        new(InstallmentNo, DueDate, EmiAmount, PrincipalAmount, InterestAmount, PrincipalPaid,
            Status == "WAIVED" ? InterestAmount : InterestPaid, LateFeePaid);
}

/// <summary>Loads (and optionally locks) a loan with its repayment schedule.</summary>
internal static class LoanPosition
{
    private const string HeaderSql = """
        SELECT l.LoanId, l.LoanNo, l.BranchId, b.BranchName, l.Status, l.ApplicationDate, l.ApprovedOn, l.DisbursedOn, l.ClosedOn,
               m.MemberNo, m.MemberName, m.GuardianName, p.PlanCode, l.LoanAmount, l.InterestRate, l.NoOfInstallments, l.EmiAmount,
               l.EmiMode, l.EmployeeId, e.EmployeeCode, e.EmployeeName, p.LateFeePerInstallment, p.PreCloseChargesPct,
               l.SavingAccountId, sa.AccountNo AS SavingAccountNo
        FROM loan.LoanApplication l {0}
        JOIN sec.Branch b ON b.BranchId = l.BranchId
        JOIN loan.Member m ON m.MemberId = l.MemberId
        JOIN mst.LoanPlan p ON p.PlanId = l.PlanId
        JOIN emp.Employee e ON e.EmployeeId = l.EmployeeId
        LEFT JOIN sav.SavingAccount sa ON sa.AccountId = l.SavingAccountId
        WHERE l.BranchId = @branchId AND (l.LoanId = @loanId OR l.LoanNo = @loanNo)
        """;

    public static async Task<(LoanHeader Loan, List<ScheduleState> Schedule)?> LoadAsync(
        SqlConnection c, IDbTransaction? tx, int branchId, int? loanId, string? loanNo, bool forUpdate, CancellationToken ct)
    {
        var loan = await c.QuerySingleOrDefaultAsync<LoanHeader>(new CommandDefinition(
            string.Format(HeaderSql, forUpdate ? "WITH (UPDLOCK, ROWLOCK)" : ""),
            new { branchId, loanId, loanNo = Text.Clean(loanNo, upper: true) }, tx, cancellationToken: ct));
        if (loan is null) return null;
        var schedule = (await c.QueryAsync<ScheduleState>(new CommandDefinition($"""
            SELECT InstallmentNo, DueDate, EmiAmount, PrincipalAmount, InterestAmount, PrincipalPaid, InterestPaid, LateFeePaid, Status
            FROM loan.EmiSchedule {(forUpdate ? "WITH (UPDLOCK, ROWLOCK)" : "")}
            WHERE LoanId = @LoanId ORDER BY InstallmentNo
            """, new { loan.LoanId }, tx, cancellationToken: ct))).AsList();
        return (loan, schedule);
    }

    /// <summary>Late-fee settings (LOAN.LATEFEE.MODE / GRACE_DAYS).</summary>
    public static async Task<(bool Enabled, int GraceDays)> LateFeeSettingsAsync(SqlConnection c, IDbTransaction? tx, CancellationToken ct)
    {
        var rows = (await c.QueryAsync<(string Key, string Value)>(new CommandDefinition(
            "SELECT SettingKey, SettingValue FROM sec.AppSetting WHERE SettingKey IN ('LOAN.LATEFEE.MODE', 'LOAN.LATEFEE.GRACE_DAYS')",
            transaction: tx, cancellationToken: ct))).ToDictionary(r => r.Key, r => r.Value);
        var enabled = !rows.TryGetValue("LOAN.LATEFEE.MODE", out var mode) || mode != "NONE";
        var grace = rows.TryGetValue("LOAN.LATEFEE.GRACE_DAYS", out var g) && int.TryParse(g, out var days) ? Math.Max(0, days) : 0;
        return (enabled, grace);
    }

    /// <summary>Applies paid amounts to schedule rows and recomputes their status.</summary>
    public static Task ApplyToScheduleAsync(SqlConnection c, IDbTransaction tx, int loanId, IEnumerable<(int InstallmentNo, decimal Principal, decimal Interest, decimal LateFee)> parts, DateOnly? paidDate, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
            UPDATE loan.EmiSchedule
            SET PrincipalPaid = PrincipalPaid + @Principal,
                InterestPaid = InterestPaid + @Interest,
                LateFeePaid = CASE WHEN LateFeePaid + @LateFee < 0 THEN 0 ELSE LateFeePaid + @LateFee END,
                LastPaidDate = COALESCE(@paidDate, LastPaidDate),
                Status = CASE WHEN PrincipalPaid + @Principal >= PrincipalAmount AND InterestPaid + @Interest >= InterestAmount THEN 'PAID'
                              WHEN PrincipalPaid + @Principal + InterestPaid + @Interest > 0 THEN 'PARTIAL'
                              ELSE 'DUE' END
            WHERE LoanId = @loanId AND InstallmentNo = @InstallmentNo
            """, parts.Select(p => new { loanId, InstallmentNo = (short)p.InstallmentNo, p.Principal, p.Interest, p.LateFee, paidDate }), tx, cancellationToken: ct));
}
