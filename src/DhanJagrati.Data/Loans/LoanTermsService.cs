using DhanJagrati.Core.Loans;
using DhanJagrati.Data.Admin;

namespace DhanJagrati.Data.Loans;

public sealed class LoanTermsRequest
{
    public int PlanId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>M / F / O: selects the male or female rate of the plan.</summary>
    public string? Gender { get; set; }
    /// <summary>Overrides the plan rate (optional).</summary>
    public decimal? Rate { get; set; }
    /// <summary>EMI typed by the user: required for MANUAL plans, an override otherwise.</summary>
    public decimal? Emi { get; set; }
    public DateOnly? EmiStartDate { get; set; }
}

public sealed class LoanTerms
{
    public required int PlanId { get; init; }
    public required string PlanCode { get; init; }
    public required string EmiMode { get; init; }
    public required string Method { get; init; }
    public decimal? Factor { get; init; }
    public required decimal Rate { get; init; }
    public required decimal CalculatedEmi { get; init; }
    public required decimal Emi { get; init; }
    public required bool IsEmiOverridden { get; init; }
    public required int Installments { get; init; }
    public required int TenureMonths { get; init; }
    public required decimal TotalRepayable { get; init; }
    public required decimal TotalInterest { get; init; }
    public required decimal ProcessingFeePct { get; init; }
    public required decimal ProcessingFee { get; init; }
    public required bool AllowEmiOverride { get; init; }
    public required string InterestSplit { get; init; }
    public required DateOnly FirstDueDate { get; init; }
    public required IReadOnlyList<ScheduleLine> Schedule { get; init; }
}

/// <summary>
/// Applies a loan plan's configurable EMI rules (see patch 001) to an amount. Used by the
/// application screen's calculator and again on save, so the stored EMI always follows the rules.
/// </summary>
public sealed class LoanTermsService(AdminRepository admin)
{
    public async Task<LoanTerms> ComputeAsync(LoanTermsRequest r, DateOnly onDate, CancellationToken ct)
    {
        var plan = await admin.GetPlanEmiConfigAsync(r.PlanId, onDate, ct)
                   ?? throw new ValidationFailedException(new Dictionary<string, string[]> { ["planId"] = ["Loan plan not found."] });
        var settings = await admin.GetSettingsAsync(ct);
        var overrideSetting = settings.FirstOrDefault(s => s.SettingKey == "LOAN.EMI.ALLOW_OVERRIDE")?.SettingValue != "false";

        var rate = r.Rate ?? (r.Gender == "F" ? plan.RateFemale : plan.RateMale)
            ?? throw new ValidationFailedException(new Dictionary<string, string[]>
                { ["interestRate"] = [$"No interest rate is set for {plan.PlanCode} on {onDate:dd-MMM-yyyy}; enter one."] });

        var method = EmiCodes.ParseMethod(plan.EmiCalcMethod);
        var config = new EmiPlanConfig(method, plan.EmiFactor, EmiCodes.ParseRateBasis(plan.RateBasis),
            EmiCodes.ParseRounding(plan.EmiRounding), plan.EmiRoundTo, EmiCodes.ParseSplit(plan.InterestSplit));
        var frequency = EmiCodes.ParseFrequency(plan.EmiMode);

        decimal calculated;
        try
        {
            calculated = EmiCalculator.Calculate(config,
                new EmiRequest(r.Amount, rate, plan.TenureMonths, plan.NoOfInstallments, frequency, method == EmiCalcMethod.Manual ? r.Emi : null)).Emi;
        }
        catch (EmiCalculationException ex)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]> { [method == EmiCalcMethod.Manual ? "emiAmount" : "loanAmount"] = [ex.Message] });
        }

        var emi = calculated;
        var overridden = false;
        if (method != EmiCalcMethod.Manual && r.Emi is { } typed && typed != calculated)
        {
            if (!plan.AllowEmiOverride || !overrideSetting)
                throw new ValidationFailedException(new Dictionary<string, string[]> { ["emiAmount"] = [$"EMI is fixed by plan {plan.PlanCode} ({calculated:0.00})."] });
            emi = typed;
            overridden = true;
        }
        if (emi * plan.NoOfInstallments < r.Amount)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["emiAmount"] = ["Total of all EMIs is less than the loan amount."] });

        var firstDue = r.EmiStartDate ?? EmiScheduleBuilder.DueDate(onDate, frequency, 2);
        var schedule = EmiScheduleBuilder.Build(r.Amount, emi, plan.NoOfInstallments, config.Split, frequency, firstDue);

        return new LoanTerms
        {
            PlanId = plan.PlanId,
            PlanCode = plan.PlanCode,
            EmiMode = plan.EmiMode,
            Method = plan.EmiCalcMethod,
            Factor = plan.EmiFactor,
            Rate = rate,
            CalculatedEmi = calculated,
            Emi = emi,
            IsEmiOverridden = overridden,
            Installments = plan.NoOfInstallments,
            TenureMonths = plan.TenureMonths,
            TotalRepayable = emi * plan.NoOfInstallments,
            TotalInterest = emi * plan.NoOfInstallments - r.Amount,
            ProcessingFeePct = plan.ProcessingFeePct,
            ProcessingFee = Math.Round(r.Amount * plan.ProcessingFeePct / 100m, 2, MidpointRounding.AwayFromZero),
            AllowEmiOverride = plan.AllowEmiOverride && overrideSetting,
            InterestSplit = plan.InterestSplit,
            FirstDueDate = firstDue,
            Schedule = schedule
        };
    }
}
