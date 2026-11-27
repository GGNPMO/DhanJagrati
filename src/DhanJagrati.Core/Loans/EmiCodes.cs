namespace DhanJagrati.Core.Loans;

/// <summary>Maps the database codes (mst.LoanPlan / loan.LoanApplication) to the EMI enums.</summary>
public static class EmiCodes
{
    public static EmiCalcMethod ParseMethod(string code) => code switch
    {
        "MANUAL" => EmiCalcMethod.Manual,
        "FLAT" => EmiCalcMethod.Flat,
        "REDUCING" => EmiCalcMethod.Reducing,
        "FACTOR" => EmiCalcMethod.Factor,
        _ => throw new EmiCalculationException($"Unknown EMI method '{code}'.")
    };

    public static RateBasis ParseRateBasis(string code) =>
        code == "TENURE" ? RateBasis.Tenure : RateBasis.Annual;

    public static EmiRounding ParseRounding(string code) => code switch
    {
        "NONE" => EmiRounding.None,
        "UP" => EmiRounding.Up,
        "DOWN" => EmiRounding.Down,
        _ => EmiRounding.Nearest
    };

    public static InterestSplit ParseSplit(string code) =>
        code == "REDUCING" ? InterestSplit.Reducing : InterestSplit.Flat;

    public static EmiFrequency ParseFrequency(string code) => code switch
    {
        "D" => EmiFrequency.Daily,
        "W" => EmiFrequency.Weekly,
        "F" => EmiFrequency.Fortnightly,
        "M" => EmiFrequency.Monthly,
        _ => throw new EmiCalculationException($"Unknown EMI mode '{code}'.")
    };
}
