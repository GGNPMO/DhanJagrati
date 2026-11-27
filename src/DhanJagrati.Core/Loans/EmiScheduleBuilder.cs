namespace DhanJagrati.Core.Loans;

public sealed record ScheduleLine(int InstallmentNo, DateOnly DueDate, decimal EmiAmount, decimal PrincipalAmount, decimal InterestAmount);

/// <summary>
/// Builds the repayment schedule for a fixed EMI. Totals always reconcile exactly:
/// principal parts sum to the loan amount and every line satisfies Principal + Interest = EMI.
/// </summary>
public static class EmiScheduleBuilder
{
    public static IReadOnlyList<ScheduleLine> Build(
        decimal principal, decimal emi, int installments, InterestSplit split,
        EmiFrequency frequency, DateOnly firstDueDate)
    {
        if (principal <= 0 || emi <= 0 || installments <= 0)
            throw new EmiCalculationException("Principal, EMI and installments must be greater than zero.");
        if (emi * installments < principal)
            throw new EmiCalculationException("Total of all EMIs is less than the loan amount.");

        var lines = new List<ScheduleLine>(installments);
        var totalInterest = emi * installments - principal;
        var periodRate = split == InterestSplit.Reducing ? ImpliedPeriodRate(principal, emi, installments) : 0d;
        var flatInterest = Math.Round(totalInterest / installments, 2, MidpointRounding.AwayFromZero);

        decimal balance = principal;
        for (var k = 1; k <= installments; k++)
        {
            decimal interest, principalPart;
            if (k == installments)
            {
                principalPart = balance;
                interest = emi - principalPart;
            }
            else
            {
                interest = split == InterestSplit.Flat
                    ? flatInterest
                    : Math.Round(balance * (decimal)periodRate, 2, MidpointRounding.AwayFromZero);
                interest = Math.Clamp(interest, 0, emi);
                principalPart = Math.Min(emi - interest, balance);
                interest = emi - principalPart;
            }

            balance -= principalPart;
            lines.Add(new ScheduleLine(k, DueDate(firstDueDate, frequency, k), emi, principalPart, interest));
        }

        return lines;
    }

    public static DateOnly DueDate(DateOnly first, EmiFrequency frequency, int installmentNo) => frequency switch
    {
        EmiFrequency.Daily => first.AddDays(installmentNo - 1),
        EmiFrequency.Weekly => first.AddDays(7 * (installmentNo - 1)),
        EmiFrequency.Fortnightly => first.AddDays(14 * (installmentNo - 1)),
        _ => first.AddMonths(installmentNo - 1)
    };

    /// <summary>Periodic rate r that makes the EMI amortise the principal over n installments (bisection).</summary>
    internal static double ImpliedPeriodRate(decimal principal, decimal emi, int n)
    {
        double p = (double)principal, e = (double)emi;
        if (e * n <= p) return 0;

        double lo = 0, hi = 1;
        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var pv = e * (1 - Math.Pow(1 + mid, -n)) / mid;
            if (pv > p) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
