using System.Net.Http.Json;
using System.Text.Json;
using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace DhanJagrati.Api.Tests;

/// <summary>
/// End-to-end business flow through the real API and SQL Server, on a throwaway database.
/// Set DJ_TEST_SQL to a server connection string (login needs CREATE DATABASE), e.g.
///   Server=.\SQLEXPRESS;User ID=...;Password=...;TrustServerCertificate=True
/// Without it the test is reported as skipped.
/// </summary>
[Collection("api")]
public sealed class LoanLifecycleTests(ITestOutputHelper output)
{
    [SkippableFact]
    public async Task Full_business_day_keeps_the_books_balanced()
    {
        var server = Environment.GetEnvironmentVariable("DJ_TEST_SQL");
        Skip.If(string.IsNullOrWhiteSpace(server), "Set DJ_TEST_SQL to run the end-to-end test against SQL Server.");

        await using var db = await TestDatabase.CreateAsync(server!);
        output.WriteLine($"Test database: {db.Name}");
        using var factory = new ApiFactory(db.ConnectionString);
        var api = factory.Client();

        // ---------- Help: first login, forced password change ----------
        using (var scope = factory.Services.CreateScope())
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUserAccount>>();
            var security = scope.ServiceProvider.GetRequiredService<SecurityRepository>();
            await security.SetTemporaryPasswordAsync("SYSTEM", hasher.HashPassword(new AppUserAccount(), "Temp@1234"), overwrite: true, default);
        }
        await api.PostJson("/api/auth/login", new { userName = "SYSTEM", password = "wrong-pass1" }, 401);
        await api.PostJson("/api/auth/login", new { userName = "SYSTEM", password = "Temp@1234" }, 204);
        var me = await api.GetJson("/api/auth/me");
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());
        var blocked = await api.GetJson("/api/masters/states", 403);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", blocked.Str("code"));
        await api.PostJson("/api/auth/change-password",
            new { userName = "SYSTEM", oldPassword = "Temp@1234", newPassword = "Admin@5678", confirmPassword = "Admin@5678" }, 204);
        me = await api.GetJson("/api/auth/me");
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
        var today = DateOnly.Parse(me.Str("businessDate"));

        // ---------- Master: configure the EMI rule (FACTOR 0.111 reproduces the legacy EMIs) ----------
        var plans = await api.GetJson("/api/masters/loan-plans");
        var planId = plans.EnumerateArray().First(p => p.Str("planCode") == "EPL-11").Int("id");
        await api.PutJson($"/api/masters/loan-plans/{planId}", new
        {
            planCode = "EPL-11", planName = "EASY PERSONAL LOAN", emiMode = "M", tenureMonths = 11, noOfInstallments = 11,
            processingFeePct = 3, lateFeePerInstallment = 4, preCloseChargesPct = 1, emiCalcMethod = "FACTOR", emiFactor = 0.111,
            rateBasis = "ANNUAL", emiRounding = "NEAREST", emiRoundTo = 1, interestSplit = "FLAT", allowEmiOverride = true, isActive = true
        });
        var preview = await api.PostJson($"/api/loan-plans/{planId}/emi-preview", new { amount = 30000, gender = 'M' });
        Assert.Equal(3330m, preview.Dec("emi"));

        // ---------- Member and advisors ----------
        var memberId = (await api.PostJson("/api/members", new
        {
            memberName = "Test Member", gender = "M", addressLine = "Nagala Aam, Firozabad", dateOfBirth = "1990-05-10",
            mobileNo = "9876543210", aadhaarNo = "210495299885", guardianName = "Test Guardian"
        }, 201)).Int("id");
        var member = await api.GetJson($"/api/members/{memberId}");
        Assert.Equal("M000000001", member.Str("memberNo"));
        Assert.Equal("TEST MEMBER", member.Str("memberName"));
        await api.PostJson("/api/members", new { memberName = "Too Young", gender = "F", addressLine = "X", dateOfBirth = today.AddYears(-10).ToString("yyyy-MM-dd") }, 400);

        var advisor1 = (await api.PostJson("/api/employees", new { employeeName = "Rupesh Kumar", gender = "M" }, 201)).Int("id");
        var advisor1Code = (await api.GetJson($"/api/employees/{advisor1}")).Str("employeeCode");
        var advisor2 = (await api.PostJson("/api/employees", new { employeeName = "Vipan Kumar", introducerCode = advisor1Code }, 201)).Int("id");
        var advisor2Code = (await api.GetJson($"/api/employees/{advisor2}")).Str("employeeCode");
        var chain = await api.GetJson($"/api/employees/{advisor2}/parent-chain");
        Assert.Equal(2, chain.GetArrayLength());
        // Making advisor1 report to advisor2 would create a loop
        await api.PutJson($"/api/employees/{advisor1}", new { employeeName = "Rupesh Kumar", introducerCode = advisor2Code, joiningDate = today.ToString("yyyy-MM-dd") }, 400);

        // ---------- Loan application -> approval ----------
        var loanId = (await api.PostJson("/api/loans", new
        {
            memberNo = "M000000001", planId, loanAmount = 30000, employeeCode = advisor1Code, disbursementMode = "CASH",
            emiStartDate = today.AddMonths(1).ToString("yyyy-MM-dd")
        }, 201)).Int("id");
        var loan = await api.GetJson($"/api/loans/{loanId}");
        Assert.Equal(3330m, loan.Dec("emiAmount"));
        Assert.Equal(900m, loan.Dec("processingFee"));
        Assert.Equal(29100m, loan.Dec("netDisbursedAmount"));
        Assert.Equal("PENDING", loan.Str("status"));

        var approve = await api.PostJson("/api/loan-approvals/approve", new { loanIds = new[] { loanId }, date = today });
        Assert.True(approve[0].GetProperty("success").GetBoolean(), approve.ToString());
        var schedule = await api.GetJson($"/api/loans/{loanId}/schedule");
        Assert.Equal(11, schedule.GetArrayLength());
        Assert.Equal(30000m, schedule.EnumerateArray().Sum(s => s.Dec("principalAmount")));
        Assert.Equal(36630m, schedule.EnumerateArray().Sum(s => s.Dec("emiAmount")));
        await api.PutJson($"/api/loans/{loanId}", new { memberNo = "M000000001", planId, loanAmount = 1, employeeCode = advisor1Code, applicationDate = today }, 409);

        // ---------- Recovery, reversal, recovery again ----------
        var receipt = await api.PostJson("/api/recoveries", new { loanId, amount = 3330 });
        Assert.Equal(3330m, receipt.Dec("total"));
        var receiptNo = receipt.Str("receiptNo");
        await api.PostJson($"/api/recoveries/receipts/{receiptNo}/reverse", new { date = today });
        var prefill = await api.GetJson($"/api/recoveries/prefill?loanId={loanId}");
        Assert.Equal(1, prefill.Int("nextInstallmentNo"));
        Assert.Equal(36630m, prefill.Dec("totalOutstanding"));

        receipt = await api.PostJson("/api/recoveries", new { loanId, amount = 5000 });
        Assert.Equal(2, receipt.GetProperty("lines").GetArrayLength());       // EMI 1 in full + part of EMI 2
        await api.PostJson("/api/recoveries", new { loanId, amount = 99999 }, 400); // more than outstanding

        // ---------- Settlement close / re-open / close ----------
        var quote = await api.GetJson($"/api/settlements/quote?loanId={loanId}");
        Assert.True(quote.GetProperty("canClose").GetBoolean());
        var balancePrincipal = quote.GetProperty("quote").Dec("balancePrincipal");
        Assert.True(balancePrincipal is > 0 and < 30000);
        await api.PostJson("/api/settlements/close", new { loanId, date = today });
        Assert.Equal("CLOSED", (await api.GetJson($"/api/loans/{loanId}")).Str("status"));
        await api.PostJson("/api/settlements/reopen", new { loanId, date = today });
        Assert.Equal("APPROVED", (await api.GetJson($"/api/loans/{loanId}")).Str("status"));
        Assert.Equal(balancePrincipal, (await api.GetJson($"/api/settlements/quote?loanId={loanId}")).GetProperty("quote").Dec("balancePrincipal"));
        await api.PostJson("/api/settlements/close", new { loanId, date = today });

        // ---------- Accounts: opening balances, bank ledger, contra voucher ----------
        var ledgers = await api.GetJson("/api/accounts/ledgers");
        int LedgerId(string name) => ledgers.EnumerateArray().First(l => l.Str("ledgerName") == name).Int("id");
        await api.PutJson($"/api/accounts/ledgers/{LedgerId("CAPITAL A/C")}", new
        {
            ledgerName = "CAPITAL A/C", isGroup = false, parentLedgerId = LedgerId("CAPITAL ACCOUNT"), cashBankType = "N",
            isVisibleInBranch = true, isActive = true, openingBalance = 100000, openingBalType = "C"
        });
        await api.PutJson($"/api/accounts/ledgers/{LedgerId("CASH A/C")}", new
        {
            ledgerName = "CASH A/C", isGroup = false, parentLedgerId = LedgerId("CASH-IN-HAND"), cashBankType = "C",
            isVisibleInBranch = true, isActive = true, openingBalance = 100000, openingBalType = "D"
        });
        var bankId = (await api.PostJson("/api/accounts/ledgers", new
        {
            ledgerName = "Icici Bank A/c", isGroup = false, parentLedgerId = LedgerId("BANK ACCOUNTS"), cashBankType = "B",
            bankAccountNo = "628705011219", ifscCode = "ICIC0006287", isVisibleInBranch = true, isActive = true
        }, 201)).Int("id");
        await api.PostJson("/api/accounts/vouchers", new
        {
            debitLedgerId = bankId, creditLedgerId = LedgerId("CASH A/C"), amount = 10000, paymentMode = "CASH", narration = "Cash deposited in bank"
        }, 201);
        await api.PostJson("/api/accounts/vouchers", new
        {
            debitLedgerId = LedgerId("STAFF WELFARE EXPENSES A/C"), creditLedgerId = LedgerId("CASH A/C"), amount = 85, paymentMode = "CASH",
            narration = "Tea and snacks for office"
        }, 201);

        // ---------- Saving module ----------
        await api.PutJson("/api/settings/MODULE.SAVING.ENABLED", new { value = "true" });
        var products = await api.GetJson("/api/savings/products");
        var accountId = (await api.PostJson("/api/savings/accounts", new
        {
            memberNo = "M000000001", productId = products[0].Int("productId"), openingDeposit = 2000, paymentMode = "CASH"
        }, 201)).Int("id");
        await api.PostJson($"/api/savings/accounts/{accountId}/transactions", new { txnType = "WITHDRAWAL", amount = 500, paymentMode = "CASH" });
        await api.PostJson($"/api/savings/accounts/{accountId}/transactions", new { txnType = "WITHDRAWAL", amount = 5000, paymentMode = "CASH" }, 409);
        Assert.Equal(1500m, (await api.GetJson($"/api/savings/accounts/{accountId}")).Dec("currentBalance"));

        // ---------- Reports: the books must balance ----------
        var from = new DateOnly(today.Month >= 4 ? today.Year : today.Year - 1, 4, 1).ToString("yyyy-MM-dd");
        var tb = await api.GetJson($"/api/accounts/reports/trial-balance?from={from}&to={today:yyyy-MM-dd}");
        var totalDr = tb.EnumerateArray().Sum(r => r.Dec("debit"));
        var totalCr = tb.EnumerateArray().Sum(r => r.Dec("credit"));
        output.WriteLine($"Trial balance Dr {totalDr} / Cr {totalCr}");
        Assert.Equal(totalDr, totalCr);

        var bs = await api.GetJson($"/api/accounts/reports/balance-sheet?to={today:yyyy-MM-dd}");
        var liabilities = bs.EnumerateArray().Where(r => r.Str("side") == "LIABILITY").Sum(r => r.Dec("amount"));
        var assets = bs.EnumerateArray().Where(r => r.Str("side") == "ASSET").Sum(r => r.Dec("amount"));
        Assert.Equal(liabilities, assets);

        var pl = await api.GetJson($"/api/accounts/reports/profit-loss?from={from}&to={today:yyyy-MM-dd}");
        var income = pl.GetProperty("rows").EnumerateArray().Where(r => r.Str("side") == "INCOME").ToList();
        Assert.Contains(income, r => r.Str("ledgerName") == "LOAN PROCESSING FEE A/C" && r.Dec("amount") == 900m);

        var cash = await api.GetJson($"/api/accounts/reports/cash-book?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        var totals = cash.GetProperty("totals");
        Assert.Equal(totals.Dec("openingBalance") + totals.Dec("totalReceipts") - totals.Dec("totalPayments"), totals.Dec("closingBalance"));
        var cashLedger = await api.GetJson($"/api/accounts/reports/ledger-statement?ledgerId={LedgerId("CASH A/C")}&from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(totals.Dec("closingBalance"), cashLedger.GetProperty("lines").EnumerateArray().Last().Dec("runningBalance"));

        // ---------- Loan reports ----------
        var applications = await api.GetJson($"/api/loan-reports/applications?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(1, applications.GetArrayLength());
        var recoveries = await api.GetJson($"/api/loan-reports/recovery-ledger?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(2, recoveries.GetArrayLength());            // the reversed receipt is excluded
        var closures = await api.GetJson($"/api/loan-reports/closures?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal("SETTLED", closures[0].Str("closedBy"));
        var byEmployee = await api.GetJson("/api/loan-reports/employee-wise");
        Assert.Contains(byEmployee.EnumerateArray(), r => r.Str("employeeCode") == advisor1Code && r.Int("closedLoans") == 1);
        await api.GetJson("/api/dashboard");

        // ---------- Day closing blocks further postings ----------
        await api.PostJson("/api/day-closing", new { businessDate = today, remarks = "End of day" }, 204);
        await api.PostJson("/api/accounts/vouchers", new
        {
            debitLedgerId = bankId, creditLedgerId = LedgerId("CASH A/C"), amount = 1, paymentMode = "CASH", narration = "After closing"
        }, 409);
    }
}
