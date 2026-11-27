using DhanJagrati.Core.Loans;

namespace DhanJagrati.Core.Tests;

public class RecoveryMathTests
{
    // 3 x 1110 EMIs: principal 1000 + interest 110 each, due on the 10th of Jan/Feb/Mar
    private static List<InstallmentPosition> Schedule() =>
    [
        new(1, new DateOnly(2026, 1, 10), 1110, 1000, 110, 0, 0, 0),
        new(2, new DateOnly(2026, 2, 10), 1110, 1000, 110, 0, 0, 0),
        new(3, new DateOnly(2026, 3, 10), 1110, 1000, 110, 0, 0, 0),
    ];

    [Fact]
    public void Payment_goes_to_oldest_installment_interest_first()
    {
        var a = RecoveryMath.Allocate(Schedule(), 1500, 0, new Dictionary<int, decimal>());
        Assert.Equal(2, a.Count);
        Assert.Equal(new Allocation(1, 1000, 110, 0), a[0]);
        Assert.Equal(new Allocation(2, 280, 110, 0), a[1]);
        Assert.Equal(1500, a.Sum(x => x.Total));
    }

    [Fact]
    public void Partially_paid_installment_is_completed_first()
    {
        var s = Schedule();
        s[0] = s[0] with { InterestPaid = 110, PrincipalPaid = 400 };
        var a = RecoveryMath.Allocate(s, 700, 0, new Dictionary<int, decimal>());
        Assert.Equal(new Allocation(1, 600, 0, 0), a[0]);
        Assert.Equal(new Allocation(2, 0, 100, 0), a[1]);
    }

    [Fact]
    public void Overpayment_is_rejected()
    {
        Assert.Throws<EmiCalculationException>(() => RecoveryMath.Allocate(Schedule(), 3331, 0, new Dictionary<int, decimal>()));
    }

    [Fact]
    public void Late_fee_owed_after_grace_days_only_for_unpaid_installments()
    {
        var s = Schedule();
        s[0] = s[0] with { InterestPaid = 110, PrincipalPaid = 1000 }; // paid
        var owed = RecoveryMath.LateFeeOwed(s, new DateOnly(2026, 3, 12), graceDays: 3, feePerInstallment: 4);
        Assert.Single(owed);          // #2 overdue beyond grace; #3 still within grace
        Assert.Equal(4, owed[2]);
    }

    [Fact]
    public void Late_fee_is_spread_over_installments_that_owe_it()
    {
        var owed = new Dictionary<int, decimal> { [1] = 4, [2] = 4 };
        var a = RecoveryMath.Allocate(Schedule(), 1110, 10, owed);
        Assert.Equal(4 + 2, a.First(x => x.InstallmentNo == 1).LateFee); // 4 owed + 2 excess
        Assert.Equal(4, a.First(x => x.InstallmentNo == 2).LateFee);
        Assert.Equal(1120, a.Sum(x => x.Total));
    }

    [Fact]
    public void Settlement_quote_waives_future_interest()
    {
        var s = Schedule();
        s[0] = s[0] with { InterestPaid = 110, PrincipalPaid = 1000 };
        var q = RecoveryMath.Quote(s, new DateOnly(2026, 2, 15), lateFee: 4, preClosePct: 1);
        Assert.Equal(2000, q.BalancePrincipal);
        Assert.Equal(110, q.InterestDueTillDate);   // #2 is due
        Assert.Equal(110, q.InterestWaived);        // #3 is future
        Assert.Equal(2110, q.AmountToBeReceived);
        Assert.Equal(20, q.PreCloseCharges);
        Assert.Equal(2134, q.NetCloseAmount);
    }
}
