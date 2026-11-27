using DhanJagrati.Core.Loans;

namespace DhanJagrati.Core.Tests;

public class EmiCalculatorTests
{
    private static EmiPlanConfig Config(EmiCalcMethod method, decimal? factor = null,
        EmiRounding rounding = EmiRounding.Nearest, decimal roundTo = 1, RateBasis basis = RateBasis.Annual) =>
        new(method, factor, basis, rounding, roundTo, InterestSplit.Flat);

    private static EmiRequest Request(decimal principal, decimal? manual = null) =>
        new(principal, 41m, 11, 11, EmiFrequency.Monthly, manual);

    [Theory]
    [InlineData(30000, 3330)]
    [InlineData(40000, 4440)]
    [InlineData(50000, 5550)]
    [InlineData(35000, 3885)]
    public void Factor_method_reproduces_legacy_screen_values(decimal principal, decimal expectedEmi)
    {
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Factor, 0.111m), Request(principal));
        Assert.Equal(expectedEmi, result.Emi);
        Assert.Equal(expectedEmi * 11, result.TotalRepayable);
    }

    [Fact]
    public void Flat_annual_rate_spreads_interest_evenly()
    {
        // 30000 x 41% x 11/12 = 11275 interest; (30000 + 11275) / 11 = 3752.27 -> 3752
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Flat), Request(30000));
        Assert.Equal(3752m, result.Emi);
    }

    [Fact]
    public void Flat_tenure_rate_applies_rate_once()
    {
        // 30000 x 41% = 12300 interest; 42300 / 11 = 3845.45 -> 3845
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Flat, basis: RateBasis.Tenure), Request(30000));
        Assert.Equal(3845m, result.Emi);
    }

    [Fact]
    public void Reducing_method_uses_amortisation_formula()
    {
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Reducing, rounding: EmiRounding.None), Request(30000));
        // r = 41% / 12; 30000 x r / (1 - (1 + r)^-11) = 3317.60
        Assert.Equal(3317.60m, result.Emi);
    }

    [Fact]
    public void Rounding_up_to_ten()
    {
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Reducing, rounding: EmiRounding.Up, roundTo: 10), Request(30000));
        Assert.Equal(3320m, result.Emi);
    }

    [Fact]
    public void Manual_method_uses_entered_emi()
    {
        var result = EmiCalculator.Calculate(Config(EmiCalcMethod.Manual), Request(30000, manual: 3330));
        Assert.Equal(3330m, result.Emi);
    }

    [Fact]
    public void Manual_method_without_emi_fails()
    {
        Assert.Throws<EmiCalculationException>(() => EmiCalculator.Calculate(Config(EmiCalcMethod.Manual), Request(30000)));
    }

    [Fact]
    public void Factor_without_value_fails()
    {
        Assert.Throws<EmiCalculationException>(() => EmiCalculator.Calculate(Config(EmiCalcMethod.Factor), Request(30000)));
    }

    [Fact]
    public void Emi_total_below_principal_fails()
    {
        Assert.Throws<EmiCalculationException>(() => EmiCalculator.Calculate(Config(EmiCalcMethod.Factor, 0.05m), Request(30000)));
    }
}

public class EmiScheduleBuilderTests
{
    [Theory]
    [InlineData(InterestSplit.Flat)]
    [InlineData(InterestSplit.Reducing)]
    public void Schedule_reconciles_to_principal_and_emi(InterestSplit split)
    {
        var lines = EmiScheduleBuilder.Build(30000m, 3330m, 11, split, EmiFrequency.Monthly, new DateOnly(2026, 12, 11));

        Assert.Equal(11, lines.Count);
        Assert.Equal(30000m, lines.Sum(l => l.PrincipalAmount));
        Assert.Equal(3330m * 11 - 30000m, lines.Sum(l => l.InterestAmount));
        Assert.All(lines, l =>
        {
            Assert.Equal(l.EmiAmount, l.PrincipalAmount + l.InterestAmount);
            Assert.True(l.PrincipalAmount >= 0 && l.InterestAmount >= 0);
        });
        Assert.Equal(new DateOnly(2027, 10, 11), lines[^1].DueDate);
    }

    [Fact]
    public void Reducing_split_charges_more_interest_early()
    {
        var lines = EmiScheduleBuilder.Build(30000m, 3330m, 11, InterestSplit.Reducing, EmiFrequency.Monthly, new DateOnly(2026, 12, 11));
        Assert.True(lines[0].InterestAmount > lines[^2].InterestAmount);
    }

    [Fact]
    public void Weekly_due_dates_step_by_seven_days()
    {
        var lines = EmiScheduleBuilder.Build(10000m, 1100m, 10, InterestSplit.Flat, EmiFrequency.Weekly, new DateOnly(2026, 11, 2));
        Assert.Equal(new DateOnly(2026, 11, 9), lines[1].DueDate);
    }
}
