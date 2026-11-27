namespace DhanJagrati.Core.Loans;

/// <summary>Unpaid position of one installment.</summary>
public sealed record InstallmentPosition(
    int InstallmentNo,
    DateOnly DueDate,
    decimal EmiAmount,
    decimal PrincipalAmount,
    decimal InterestAmount,
    decimal PrincipalPaid,
    decimal InterestPaid,
    decimal LateFeePaid)
{
    public decimal PrincipalDue => PrincipalAmount - PrincipalPaid;
    public decimal InterestDue => InterestAmount - InterestPaid;
    public decimal BalanceDue => PrincipalDue + InterestDue;
    public bool IsOpen => BalanceDue > 0;
}

public sealed record Allocation(int InstallmentNo, decimal Principal, decimal Interest, decimal LateFee)
{
    public decimal Total => Principal + Interest + LateFee;
}

public sealed record SettlementQuote(
    decimal PrincipalReceived,
    decimal InterestReceived,
    decimal BalancePrincipal,
    decimal InterestDueTillDate,
    decimal InterestWaived,
    decimal BalanceInstallmentAmount,
    decimal LateFee,
    decimal AmountToBeReceived,
    decimal PreCloseCharges,
    decimal NetCloseAmount);

/// <summary>Recovery and settlement arithmetic (no I/O; unit tested).</summary>
public static class RecoveryMath
{
    /// <summary>Late fee still owed per installment: plan fee for each EMI unpaid past due date + grace days.</summary>
    public static IReadOnlyDictionary<int, decimal> LateFeeOwed(IEnumerable<InstallmentPosition> schedule, DateOnly onDate, int graceDays, decimal feePerInstallment)
    {
        var result = new Dictionary<int, decimal>();
        if (feePerInstallment <= 0) return result;
        foreach (var s in schedule.Where(s => s.IsOpen && s.DueDate.AddDays(graceDays) < onDate))
        {
            var owed = feePerInstallment - s.LateFeePaid;
            if (owed > 0) result[s.InstallmentNo] = owed;
        }
        return result;
    }

    /// <summary>
    /// Applies an EMI payment to the oldest open installments first (interest, then principal),
    /// and spreads the late fee over the installments that owe it (any excess on the first one).
    /// </summary>
    public static IReadOnlyList<Allocation> Allocate(IReadOnlyList<InstallmentPosition> schedule, decimal emiPayment, decimal lateFee, IReadOnlyDictionary<int, decimal> lateFeeOwed)
    {
        if (emiPayment < 0 || lateFee < 0) throw new EmiCalculationException("Amounts cannot be negative.");
        var open = schedule.Where(s => s.IsOpen).OrderBy(s => s.InstallmentNo).ToList();
        var totalDue = open.Sum(s => s.BalanceDue);
        if (emiPayment > totalDue) throw new EmiCalculationException($"Payment exceeds the outstanding balance ({totalDue:0.00}). Use Loan Settlement to close the loan.");

        var parts = new Dictionary<int, (decimal P, decimal I, decimal L)>();
        var remaining = emiPayment;
        foreach (var s in open)
        {
            if (remaining <= 0) break;
            var interest = Math.Min(remaining, s.InterestDue);
            remaining -= interest;
            var principal = Math.Min(remaining, s.PrincipalDue);
            remaining -= principal;
            parts[s.InstallmentNo] = (principal, interest, 0);
        }

        var fee = lateFee;
        foreach (var (no, owed) in lateFeeOwed.OrderBy(x => x.Key))
        {
            if (fee <= 0) break;
            var take = Math.Min(fee, owed);
            parts[no] = parts.TryGetValue(no, out var p) ? (p.P, p.I, p.L + take) : (0, 0, take);
            fee -= take;
        }
        if (fee > 0)
        {
            var first = parts.Count > 0 ? parts.Keys.Min() : open.FirstOrDefault()?.InstallmentNo
                        ?? throw new EmiCalculationException("The loan has no open installment.");
            parts[first] = parts.TryGetValue(first, out var p) ? (p.P, p.I, p.L + fee) : (0, 0, fee);
        }

        return parts.OrderBy(x => x.Key)
            .Select(x => new Allocation(x.Key, x.Value.P, x.Value.I, x.Value.L))
            .Where(a => a.Total > 0)
            .ToList();
    }

    /// <summary>
    /// Pre-closure: remaining principal plus interest of installments due up to the date; interest of
    /// future installments is waived. Pre-close charges are a percentage of the remaining principal.
    /// </summary>
    public static SettlementQuote Quote(IReadOnlyList<InstallmentPosition> schedule, DateOnly onDate, decimal lateFee, decimal preClosePct)
    {
        var balancePrincipal = schedule.Sum(s => s.PrincipalDue);
        var interestDue = schedule.Where(s => s.DueDate <= onDate).Sum(s => s.InterestDue);
        var interestWaived = schedule.Where(s => s.DueDate > onDate).Sum(s => s.InterestDue);
        var toReceive = balancePrincipal + interestDue;
        var charges = Math.Round(balancePrincipal * preClosePct / 100m, 2, MidpointRounding.AwayFromZero);
        return new SettlementQuote(
            schedule.Sum(s => s.PrincipalPaid),
            schedule.Sum(s => s.InterestPaid),
            balancePrincipal,
            interestDue,
            interestWaived,
            schedule.Sum(s => s.BalanceDue),
            lateFee,
            toReceive,
            charges,
            toReceive + lateFee + charges);
    }
}
