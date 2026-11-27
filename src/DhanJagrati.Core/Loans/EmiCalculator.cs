namespace DhanJagrati.Core.Loans;

/// <summary>How the EMI amount is derived. Stored per loan plan (mst.LoanPlan.EmiCalcMethod).</summary>
public enum EmiCalcMethod { Manual, Flat, Reducing, Factor }

/// <summary>Whether the plan's interest rate is per annum or for the whole tenure.</summary>
public enum RateBasis { Annual, Tenure }

public enum EmiRounding { None, Nearest, Up, Down }

/// <summary>How each EMI is split into principal and interest in the schedule.</summary>
public enum InterestSplit { Flat, Reducing }

public enum EmiFrequency { Daily, Weekly, Fortnightly, Monthly }

public sealed record EmiPlanConfig(
    EmiCalcMethod Method,
    decimal? Factor,
    RateBasis RateBasis,
    EmiRounding Rounding,
    decimal RoundTo,
    InterestSplit Split);

public sealed record EmiRequest(
    decimal Principal,
    decimal RatePercent,
    int TenureMonths,
    int Installments,
    EmiFrequency Frequency,
    decimal? ManualEmi = null);

public sealed record EmiResult(decimal Emi, int Installments, decimal TotalRepayable, decimal TotalInterest);

public sealed class EmiCalculationException(string message) : Exception(message);

/// <summary>
/// Configurable EMI calculation. The legacy formula is not yet confirmed, so every
/// method is driven by the plan configuration rather than hard-coded.
/// </summary>
public static class EmiCalculator
{
    public static EmiResult Calculate(EmiPlanConfig config, EmiRequest request)
    {
        Validate(request);

        decimal emi = config.Method switch
        {
            EmiCalcMethod.Manual => request.ManualEmi is > 0
                ? request.ManualEmi.Value
                : throw new EmiCalculationException("EMI amount must be entered for a plan with the MANUAL method."),
            EmiCalcMethod.Flat => Round(FlatEmi(config, request), config),
            EmiCalcMethod.Reducing => Round(ReducingEmi(config, request), config),
            EmiCalcMethod.Factor => config.Factor is > 0
                ? Round(request.Principal * config.Factor.Value, config)
                : throw new EmiCalculationException("EMI factor is not configured for this plan."),
            _ => throw new EmiCalculationException($"Unsupported EMI method {config.Method}.")
        };

        if (emi <= 0)
            throw new EmiCalculationException("Calculated EMI must be greater than zero.");

        var total = emi * request.Installments;
        if (total < request.Principal)
            throw new EmiCalculationException("Total of all EMIs is less than the loan amount; check the plan configuration.");

        return new EmiResult(emi, request.Installments, total, total - request.Principal);
    }

    public static int PeriodsPerYear(EmiFrequency frequency) => frequency switch
    {
        EmiFrequency.Daily => 365,
        EmiFrequency.Weekly => 52,
        EmiFrequency.Fortnightly => 26,
        _ => 12
    };

    private static decimal FlatEmi(EmiPlanConfig config, EmiRequest r)
    {
        var interest = config.RateBasis == RateBasis.Annual
            ? r.Principal * r.RatePercent / 100m * r.TenureMonths / 12m
            : r.Principal * r.RatePercent / 100m;
        return (r.Principal + interest) / r.Installments;
    }

    private static decimal ReducingEmi(EmiPlanConfig config, EmiRequest r)
    {
        var periodRate = config.RateBasis == RateBasis.Annual
            ? (double)r.RatePercent / 100d / PeriodsPerYear(r.Frequency)
            : (double)r.RatePercent / 100d / r.Installments;
        if (periodRate == 0)
            return r.Principal / r.Installments;

        var p = (double)r.Principal;
        var emi = p * periodRate / (1 - Math.Pow(1 + periodRate, -r.Installments));
        return (decimal)emi;
    }

    internal static decimal Round(decimal value, EmiPlanConfig config)
    {
        if (config.Rounding == EmiRounding.None)
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);

        var step = config.RoundTo <= 0 ? 1m : config.RoundTo;
        var units = value / step;
        units = config.Rounding switch
        {
            EmiRounding.Up => Math.Ceiling(units),
            EmiRounding.Down => Math.Floor(units),
            _ => Math.Round(units, MidpointRounding.AwayFromZero)
        };
        return units * step;
    }

    private static void Validate(EmiRequest r)
    {
        if (r.Principal <= 0) throw new EmiCalculationException("Loan amount must be greater than zero.");
        if (r.RatePercent < 0) throw new EmiCalculationException("Interest rate cannot be negative.");
        if (r.Installments <= 0) throw new EmiCalculationException("Number of installments must be greater than zero.");
        if (r.TenureMonths < 0) throw new EmiCalculationException("Tenure cannot be negative.");
    }
}
