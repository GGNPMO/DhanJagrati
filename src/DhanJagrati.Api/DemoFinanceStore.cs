using System.Globalization;

namespace DhanJagrati.Api;

public sealed class DemoFinanceStore
{
    private readonly object gate = new();
    private readonly List<Member> members;
    private readonly List<Loan> loans;
    private readonly List<LoanApplication> applications;
    private readonly List<Collection> collections;
    private readonly List<LedgerActivity> activity;
    private int nextMemberId = 6;
    private int nextLoanId = 5;
    private int nextApplicationId = 4;
    private int nextCollectionId = 4;
    private int nextVoucherNo = 5;

    public DemoFinanceStore()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        members =
        [
            new(1, "DJ-24081", "Meera Devi", "98765 12001", "Rampur", "Shakti Mahila Group", "Central Branch", "Active", today.AddDays(-160)),
            new(2, "DJ-24082", "Sunita Kumari", "98765 12002", "Rampur", "Shakti Mahila Group", "Central Branch", "Active", today.AddDays(-142)),
            new(3, "DJ-24103", "Kavita Sharma", "98765 12003", "Sundarpur", "Nayi Disha Group", "Central Branch", "Active", today.AddDays(-98)),
            new(4, "DJ-24111", "Rekha Yadav", "98765 12004", "Sundarpur", "Nayi Disha Group", "Central Branch", "Active", today.AddDays(-76)),
            new(5, "DJ-24128", "Asha Verma", "98765 12005", "Kalyanpur", "Ujjwal Group", "Central Branch", "Active", today.AddDays(-40))
        ];

        loans =
        [
            new(1, "LN-250042", 1, "Meera Devi", "Sakhi Enterprise", 200000, 160000, 10000, today.AddDays(2), "Pooja Singh", "Active"),
            new(2, "LN-250051", 2, "Sunita Kumari", "Sakhi Enterprise", 180000, 150000, 9000, today.AddDays(-2), "Pooja Singh", "Active"),
            new(3, "LN-250063", 3, "Kavita Sharma", "Livelihood Plus", 120000, 100500, 7000, today.AddDays(4), "Ravi Kumar", "Active"),
            new(4, "LN-250011", 5, "Asha Verma", "Emergency Support", 50000, 0, 0, today.AddDays(-15), "Ravi Kumar", "Closed")
        ];

        applications =
        [
            new(1, "APP-260119", 4, "Rekha Yadav", "Sakhi Enterprise", 150000, 18, today.AddDays(-3), "Tailoring equipment", "Awaiting review", "Pooja Singh"),
            new(2, "APP-260117", 5, "Asha Verma", "Livelihood Plus", 90000, 12, today.AddDays(-5), "Dairy livestock", "Approved", "Ravi Kumar"),
            new(3, "APP-260113", 3, "Kavita Sharma", "Sakhi Enterprise", 100000, 18, today.AddDays(-8), "Seasonal inventory", "Disbursed", "Ravi Kumar")
        ];

        collections =
        [
            new(1, "RC-008421", "LN-250042", "Meera Devi", today.AddDays(-5), 59500, "Cash", ""),
            new(2, "RC-008422", "LN-250051", "Sunita Kumari", today.AddDays(-3), 38500, "UPI", "UPI-942661"),
            new(3, "RC-008423", "LN-250063", "Kavita Sharma", today.AddDays(-1), 11500, "Cash", "")
        ];

        activity =
        [
            new(today.AddDays(-80), "JV-00001", "Cash in Hand", "Opening branch funds", 800000, 0, "Opening", "", "Central Branch"),
            new(today.AddDays(-80), "JV-00001", "Share Capital", "Opening branch funds", 0, 800000, "Opening", "", "Central Branch"),
            new(today.AddDays(-12), "JV-00002", "Loan Portfolio", "Enterprise loan disbursements", 500000, 0, "Bank", "NEFT-61204", "Central Branch"),
            new(today.AddDays(-12), "JV-00002", "Cash in Hand", "Enterprise loan disbursements", 0, 500000, "Bank", "NEFT-61204", "Central Branch"),
            new(today.AddDays(-5), "RC-008421", "Cash in Hand", "Meera Devi · LN-250042", 59500, 0, "Cash", "", "Central Branch"),
            new(today.AddDays(-5), "RC-008421", "Loan Portfolio", "Meera Devi · LN-250042", 0, 55000, "Cash", "", "Central Branch"),
            new(today.AddDays(-5), "RC-008421", "Interest Income", "Meera Devi · LN-250042", 0, 4500, "Cash", "", "Central Branch"),
            new(today.AddDays(-3), "RC-008422", "Cash in Hand", "Sunita Kumari · LN-250051", 38500, 0, "UPI", "UPI-942661", "Central Branch"),
            new(today.AddDays(-3), "RC-008422", "Loan Portfolio", "Sunita Kumari · LN-250051", 0, 34500, "UPI", "UPI-942661", "Central Branch"),
            new(today.AddDays(-3), "RC-008422", "Interest Income", "Sunita Kumari · LN-250051", 0, 4000, "UPI", "UPI-942661", "Central Branch"),
            new(today.AddDays(-2), "JV-00003", "Operating Expenses", "Branch office and field travel", 12400, 0, "Cash", "", "Central Branch"),
            new(today.AddDays(-2), "JV-00003", "Cash in Hand", "Branch office and field travel", 0, 12400, "Cash", "", "Central Branch"),
            new(today.AddDays(-1), "RC-008423", "Cash in Hand", "Kavita Sharma · LN-250063", 11500, 0, "Cash", "", "Central Branch"),
            new(today.AddDays(-1), "RC-008423", "Loan Portfolio", "Kavita Sharma · LN-250063", 0, 11500, "Cash", "", "Central Branch")
        ];
    }

    public DashboardSummary GetDashboard()
    {
        lock (gate)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var monthCollections = collections.Where(item => item.PaidOn >= monthStart && item.PaidOn <= today).Sum(item => item.Amount);
            var trend = Enumerable.Range(0, 7)
                .Select(days => today.AddDays(days - 6))
                .Select(day => new DashboardCollectionDay(
                    day.ToString("ddd", CultureInfo.InvariantCulture),
                    collections.Where(item => item.PaidOn == day).Sum(item => item.Amount)))
                .ToArray();

            var dueLoans = loans.Where(loan => loan.Status == "Active" && loan.NextDue <= today)
                .OrderBy(loan => loan.NextDue).ToArray();

            return new DashboardSummary(
                members.Count(member => member.Status == "Active"),
                loans.Count(loan => loan.Status == "Active"),
                loans.Where(loan => loan.Status == "Active").Sum(loan => loan.Outstanding),
                monthCollections,
                dueLoans.Length,
                250000,
                monthCollections,
                trend,
                dueLoans);
        }
    }

    public IReadOnlyList<Member> GetMembers(string? search)
    {
        lock (gate)
        {
            var results = members.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                results = results.Where(member =>
                    member.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    member.MemberNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    member.Phone.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    member.Village.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return results.OrderByDescending(member => member.Id).ToArray();
        }
    }

    public Member AddMember(CreateMemberRequest request)
    {
        lock (gate)
        {
            var id = nextMemberId++;
            var member = new Member(
                id,
                $"DJ-{25000 + id}",
                request.FullName.Trim(),
                request.Phone.Trim(),
                request.Village?.Trim() ?? string.Empty,
                request.GroupName?.Trim() ?? string.Empty,
                "Central Branch",
                "Active",
                DateOnly.FromDateTime(DateTime.Today),
                request.DateOfBirth,
                request.GuardianName?.Trim(),
                request.MonthlyIncome,
                request.KycStatus?.ToLowerInvariant() == "verified" ? "Verified" : "Pending");
            members.Add(member);
            return member;
        }
    }

    public IReadOnlyList<LoanApplication> GetLoans()
    {
        lock (gate)
        {
            return applications.OrderByDescending(application => application.Id).ToArray();
        }
    }

    public IReadOnlyList<Loan> GetLoanAccounts()
    {
        lock (gate)
        {
            return loans.Where(item => item.Status == "Active").OrderBy(item => item.NextDue).ToArray();
        }
    }

    public Loan? DisburseLoan(string applicationNo, DisburseLoanRequest request)
    {
        lock (gate)
        {
            var applicationIndex = applications.FindIndex(application =>
                application.ApplicationNo == applicationNo && application.Status == "Approved");
            if (applicationIndex < 0)
            {
                return null;
            }

            var application = applications[applicationIndex];
            var id = nextLoanId++;
            var loanNo = $"LN-{260000 + id}";
            var installment = decimal.Round(application.AmountRequested / application.TenureMonths, 2);
            var loan = new Loan(
                id,
                loanNo,
                application.MemberId,
                application.MemberName,
                application.Product,
                application.AmountRequested,
                application.AmountRequested,
                installment,
                request.FirstInstallmentDate,
                application.RelationshipManager,
                "Active");

            loans.Add(loan);
            applications[applicationIndex] = application with { Status = "Disbursed" };
            activity.Add(new(request.DisbursementDate, $"DISB-{loanNo}", "Loan Portfolio",
                $"Loan disbursement · {application.MemberName}", application.AmountRequested, 0, request.Mode, request.Reference, "Central Branch"));
            activity.Add(new(request.DisbursementDate, $"DISB-{loanNo}", "Cash in Hand",
                $"Loan disbursement · {application.MemberName}", 0, application.AmountRequested, request.Mode, request.Reference, "Central Branch"));
            return loan;
        }
    }

    public LoanApplication? AddLoan(CreateLoanRequest request)
    {
        lock (gate)
        {
            var member = members.SingleOrDefault(item => item.Id == request.MemberId);
            if (member is null)
            {
                return null;
            }

            var id = nextApplicationId++;
            var application = new LoanApplication(
                id,
                $"APP-{DateTime.Today:yyMMdd}-{id:000}",
                member.Id,
                member.FullName,
                request.Product.Trim(),
                request.Amount,
                request.TenureMonths,
                DateOnly.FromDateTime(DateTime.Today),
                request.Purpose.Trim(),
                "Awaiting review",
                "Pooja Singh");
            applications.Add(application);
            return application;
        }
    }

    public bool DecideLoan(string applicationNo, bool approved)
    {
        lock (gate)
        {
            var index = applications.FindIndex(application => application.ApplicationNo == applicationNo);
            if (index < 0 || applications[index].Status != "Awaiting review")
            {
                return false;
            }

            applications[index] = applications[index] with { Status = approved ? "Approved" : "Declined" };
            return true;
        }
    }

    public IReadOnlyList<Collection> GetCollections()
    {
        lock (gate)
        {
            return collections.OrderByDescending(item => item.PaidOn).ThenByDescending(item => item.Id).ToArray();
        }
    }

    public Collection? RecordCollection(CreateCollectionRequest request)
    {
        lock (gate)
        {
            var loanIndex = loans.FindIndex(loan => loan.LoanNo == request.LoanNo && loan.Status == "Active");
            if (loanIndex < 0 || request.Amount > loans[loanIndex].Outstanding)
            {
                return null;
            }

            var loan = loans[loanIndex];
            var collectionId = nextCollectionId++;
            var collection = new Collection(
                collectionId,
                $"RC-{8420 + collectionId:000000}",
                loan.LoanNo,
                loan.MemberName,
                request.PaidOn,
                request.Amount,
                request.Mode,
                request.Reference.Trim());

            loans[loanIndex] = loan with
            {
                Outstanding = loan.Outstanding - request.Amount,
                Status = loan.Outstanding == request.Amount ? "Closed" : loan.Status
            };
            collections.Add(collection);
            activity.Add(new(request.PaidOn, collection.ReceiptNo, "Cash in Hand",
                $"{loan.MemberName} · {loan.LoanNo}", request.Amount, 0, request.Mode, request.Reference.Trim(), "Central Branch"));
            activity.Add(new(request.PaidOn, collection.ReceiptNo, "Loan Portfolio",
                $"{loan.MemberName} · {loan.LoanNo}", 0, request.Amount, request.Mode, request.Reference.Trim(), "Central Branch"));
            return collection;
        }
    }

    public IReadOnlyList<ReportDefinition> GetReportTypes() => ReportCatalog.All;

    public ReportData? BuildReport(string type, DateOnly from, DateOnly to, string branch)
    {
        lock (gate)
        {
            var title = ReportCatalog.All.SingleOrDefault(report => report.Id == type)?.Title;
            if (title is null)
            {
                return null;
            }

            var rows = type switch
            {
                "cashbook" => BuildCashbook(from, to, branch, detail: false),
                "cashbook-detail" => BuildCashbook(from, to, branch, detail: true),
                "trial-balance" => BuildTrialBalance(to, branch),
                "profit-loss" => BuildProfitLoss(from, to, branch),
                "balance-sheet" => BuildBalanceSheet(to, branch),
                "loan-applications" => BuildApplications(from, to),
                "recovery-due" => BuildRecoveryDue(to),
                "recovery-ledger" => BuildRecoveryLedger(from, to),
                "relationship-manager" => BuildRelationshipManagers(),
                "loan-closer" => BuildLoanClosures(from, to),
                _ => null
            };

            if (rows is null)
            {
                return null;
            }

            var reportArea = type switch
            {
                "cashbook" or "cashbook-detail" => "Cash management",
                "trial-balance" or "profit-loss" or "balance-sheet" => "Financial statements",
                _ => "Portfolio operations"
            };

            return new ReportData(
                type,
                title,
                "Dhan Jagrati Microfinance Ltd · " + reportArea,
                branch,
                from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateTimeOffset.Now.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture),
                rows.Metrics,
                rows.Columns,
                rows.Rows);
        }
    }

    public ReportData PostVoucher(CreateVoucherRequest request)
    {
        lock (gate)
        {
            var voucherNo = $"JV-{nextVoucherNo++:00000}";
            activity.Add(new(request.VoucherDate, voucherNo, request.DebitLedger, request.Narration.Trim(),
                request.Amount, 0, request.Mode, request.Reference.Trim(), "Central Branch"));
            activity.Add(new(request.VoucherDate, voucherNo, request.CreditLedger, request.Narration.Trim(),
                0, request.Amount, request.Mode, request.Reference.Trim(), "Central Branch"));
            return BuildReport("cashbook-detail", request.VoucherDate, request.VoucherDate, "Central Branch")!;
        }
    }

    private ReportRows BuildCashbook(DateOnly from, DateOnly to, string branch, bool detail)
    {
        var cashEntries = activity
            .Where(item => item.Ledger == "Cash in Hand" && item.Branch == branch)
            .OrderBy(item => item.Date).ThenBy(item => item.VoucherNo, StringComparer.Ordinal).ToArray();
        var opening = cashEntries.Where(item => item.Date < from).Sum(item => item.Debit - item.Credit);
        var period = cashEntries.Where(item => item.Date >= from && item.Date <= to).ToArray();
        var receipts = period.Sum(item => item.Debit);
        var payments = period.Sum(item => item.Credit);

        if (detail)
        {
            var detailRows = period.Select(item => Row(
                ("date", Date(item.Date)),
                ("voucher", item.VoucherNo),
                ("ledger", item.Ledger),
                ("particulars", item.Particulars),
                ("debit", Money(item.Debit)),
                ("credit", Money(item.Credit)),
                ("mode", item.Mode),
                ("reference", item.Reference))).ToArray();

            return new ReportRows(
                [Metric("Opening cash", Money(opening)), Metric("Total receipts", Money(receipts), "positive"), Metric("Total payments", Money(payments)), Metric("Closing cash", Money(opening + receipts - payments), "positive")],
                [Column("date", "Date"), Column("voucher", "Voucher no."), Column("ledger", "Account head"), Column("particulars", "Particulars"), Column("debit", "Debit", "right"), Column("credit", "Credit", "right"), Column("mode", "Mode"), Column("reference", "Cheque / reference")],
                detailRows);
        }

        var dailyRows = period.GroupBy(item => item.Date).OrderBy(group => group.Key).Select(group =>
        {
            var dayIn = group.Sum(item => item.Debit);
            var dayOut = group.Sum(item => item.Credit);
            return Row(("date", Date(group.Key)), ("receipts", Money(dayIn)), ("payments", Money(dayOut)), ("closing", Money(opening + period.Where(item => item.Date <= group.Key).Sum(item => item.Debit - item.Credit))));
        }).ToArray();

        return new ReportRows(
            [Metric("Opening cash", Money(opening)), Metric("Receipts", Money(receipts), "positive"), Metric("Payments", Money(payments)), Metric("Closing cash", Money(opening + receipts - payments), "positive")],
            [Column("date", "Voucher date"), Column("receipts", "Receipts", "right"), Column("payments", "Payments", "right"), Column("closing", "Closing balance", "right")],
            dailyRows);
    }

    private ReportRows BuildTrialBalance(DateOnly to, string branch)
    {
        var balances = activity.Where(item => item.Date <= to && item.Branch == branch)
            .GroupBy(item => item.Ledger)
            .Select(group =>
            {
                var debit = group.Sum(item => item.Debit);
                var credit = group.Sum(item => item.Credit);
                return new { Ledger = group.Key, Debit = Math.Max(debit - credit, 0), Credit = Math.Max(credit - debit, 0) };
            })
            .Where(item => item.Debit > 0 || item.Credit > 0)
            .OrderBy(item => item.Ledger)
            .ToArray();
        var debitTotal = balances.Sum(item => item.Debit);
        var creditTotal = balances.Sum(item => item.Credit);

        return new ReportRows(
            [Metric("Ledger accounts", balances.Length.ToString(CultureInfo.InvariantCulture)), Metric("Total debit", Money(debitTotal)), Metric("Total credit", Money(creditTotal), "positive")],
            [Column("ledger", "Ledger account"), Column("debit", "Debit balance", "right"), Column("credit", "Credit balance", "right")],
            balances.Select(item => Row(("ledger", item.Ledger), ("debit", Money(item.Debit)), ("credit", Money(item.Credit)))).ToArray());
    }

    private ReportRows BuildProfitLoss(DateOnly from, DateOnly to, string branch)
    {
        var income = activity.Where(item => item.Ledger == "Interest Income" && item.Date >= from && item.Date <= to && item.Branch == branch).Sum(item => item.Credit - item.Debit);
        var expenses = activity.Where(item => item.Ledger == "Operating Expenses" && item.Date >= from && item.Date <= to && item.Branch == branch).Sum(item => item.Debit - item.Credit);
        var net = income - expenses;
        return new ReportRows(
            [Metric("Total income", Money(income), "positive"), Metric("Total expenses", Money(expenses)), Metric(net >= 0 ? "Surplus" : "Deficit", Money(Math.Abs(net)), net >= 0 ? "positive" : "negative")],
            [Column("category", "Particulars"), Column("income", "Income", "right"), Column("expense", "Expenses", "right")],
            [
                Row(("category", "Interest income"), ("income", Money(income)), ("expense", "—")),
                Row(("category", "Operating expenses"), ("income", "—"), ("expense", Money(expenses))),
                Row(("category", net >= 0 ? "Net surplus" : "Net deficit"), ("income", net >= 0 ? Money(net) : "—"), ("expense", net < 0 ? Money(-net) : "—"))
            ]);
    }

    private ReportRows BuildBalanceSheet(DateOnly to, string branch)
    {
        var totals = activity.Where(item => item.Date <= to && item.Branch == branch)
            .GroupBy(item => item.Ledger)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Debit - item.Credit));
        var cash = totals.GetValueOrDefault("Cash in Hand");
        var portfolio = totals.GetValueOrDefault("Loan Portfolio");
        var capital = -totals.GetValueOrDefault("Share Capital");
        var income = -totals.GetValueOrDefault("Interest Income");
        var expenses = totals.GetValueOrDefault("Operating Expenses");
        var equity = capital + income - expenses;
        var assets = cash + portfolio;

        return new ReportRows(
            [Metric("Total assets", Money(assets), "positive"), Metric("Liabilities & equity", Money(equity)), Metric("Difference", Money(Math.Abs(assets - equity)), assets == equity ? "positive" : "negative")],
            [Column("section", "Statement of financial position"), Column("amount", "Amount", "right")],
            [
                Row(("section", "ASSETS · Cash in hand"), ("amount", Money(cash))),
                Row(("section", "ASSETS · Loan portfolio"), ("amount", Money(portfolio))),
                Row(("section", "TOTAL ASSETS"), ("amount", Money(assets))),
                Row(("section", "LIABILITIES & EQUITY · Share capital"), ("amount", Money(capital))),
                Row(("section", "LIABILITIES & EQUITY · Current year surplus"), ("amount", Money(income - expenses))),
                Row(("section", "TOTAL LIABILITIES & EQUITY"), ("amount", Money(equity)))
            ]);
    }

    private ReportRows BuildApplications(DateOnly from, DateOnly to)
    {
        var rows = applications.Where(item => item.AppliedOn >= from && item.AppliedOn <= to)
            .OrderByDescending(item => item.AppliedOn)
            .Select(item => Row(
                ("date", Date(item.AppliedOn)),
                ("application", item.ApplicationNo),
                ("member", item.MemberName),
                ("product", item.Product),
                ("amount", Money(item.AmountRequested)),
                ("tenure", $"{item.TenureMonths} months"),
                ("purpose", item.Purpose),
                ("status", item.Status),
                ("manager", item.RelationshipManager))).ToArray();
        return new ReportRows(
            [Metric("Applications", rows.Length.ToString(CultureInfo.InvariantCulture)), Metric("Under review", applications.Count(item => item.Status == "Awaiting review").ToString(CultureInfo.InvariantCulture)), Metric("Requested", Money(applications.Where(item => item.AppliedOn >= from && item.AppliedOn <= to).Sum(item => item.AmountRequested)))],
            [Column("date", "Applied on"), Column("application", "Application no."), Column("member", "Member"), Column("product", "Product"), Column("amount", "Requested", "right"), Column("tenure", "Tenure"), Column("purpose", "Purpose"), Column("status", "Status"), Column("manager", "Relationship manager")],
            rows);
    }

    private ReportRows BuildRecoveryDue(DateOnly to)
    {
        var due = loans.Where(item => item.Status == "Active" && item.NextDue <= to).OrderBy(item => item.NextDue).ToArray();
        return new ReportRows(
            [Metric("Accounts due", due.Length.ToString(CultureInfo.InvariantCulture)), Metric("Installments due", Money(due.Sum(item => item.Installment))), Metric("Outstanding", Money(due.Sum(item => item.Outstanding)))],
            [Column("loan", "Loan no."), Column("member", "Member"), Column("phone", "Contact"), Column("group", "Group"), Column("due", "Due date"), Column("installment", "Installment", "right"), Column("outstanding", "Outstanding", "right"), Column("manager", "Relationship manager")],
            due.Select(item =>
            {
                var member = members.Single(member => member.Id == item.MemberId);
                return Row(("loan", item.LoanNo), ("member", item.MemberName), ("phone", member.Phone), ("group", member.GroupName), ("due", Date(item.NextDue)), ("installment", Money(item.Installment)), ("outstanding", Money(item.Outstanding)), ("manager", item.RelationshipManager));
            }).ToArray());
    }

    private ReportRows BuildRecoveryLedger(DateOnly from, DateOnly to)
    {
        var results = collections.Where(item => item.PaidOn >= from && item.PaidOn <= to)
            .OrderByDescending(item => item.PaidOn).ToArray();
        return new ReportRows(
            [Metric("Receipts", results.Length.ToString(CultureInfo.InvariantCulture)), Metric("Collected", Money(results.Sum(item => item.Amount)), "positive")],
            [Column("date", "Receipt date"), Column("receipt", "Receipt no."), Column("loan", "Loan no."), Column("member", "Member"), Column("amount", "Amount", "right"), Column("mode", "Payment mode"), Column("reference", "Cheque / reference")],
            results.Select(item => Row(("date", Date(item.PaidOn)), ("receipt", item.ReceiptNo), ("loan", item.LoanNo), ("member", item.MemberName), ("amount", Money(item.Amount)), ("mode", item.Mode), ("reference", item.Reference))).ToArray());
    }

    private ReportRows BuildRelationshipManagers()
    {
        var results = loans.Where(item => item.Status == "Active").GroupBy(item => item.RelationshipManager).OrderBy(group => group.Key).ToArray();
        return new ReportRows(
            [Metric("Managers", results.Length.ToString(CultureInfo.InvariantCulture)), Metric("Portfolio", Money(results.Sum(group => group.Sum(item => item.Outstanding))))],
            [Column("manager", "Relationship manager"), Column("accounts", "Active accounts", "right"), Column("members", "Members"), Column("sanctioned", "Sanctioned", "right"), Column("outstanding", "Outstanding", "right")],
            results.Select(group => Row(
                ("manager", group.Key),
                ("accounts", group.Count().ToString(CultureInfo.InvariantCulture)),
                ("members", string.Join(", ", group.Select(item => item.MemberName))),
                ("sanctioned", Money(group.Sum(item => item.SanctionedAmount))),
                ("outstanding", Money(group.Sum(item => item.Outstanding))))).ToArray());
    }

    private ReportRows BuildLoanClosures(DateOnly from, DateOnly to)
    {
        var results = loans.Where(item => item.Status == "Closed" && item.NextDue >= from && item.NextDue <= to).ToArray();
        return new ReportRows(
            [Metric("Loans closed", results.Length.ToString(CultureInfo.InvariantCulture)), Metric("Settled amount", Money(results.Sum(item => item.SanctionedAmount)))],
            [Column("loan", "Loan no."), Column("member", "Member"), Column("product", "Product"), Column("amount", "Original amount", "right"), Column("closed", "Closure date"), Column("manager", "Relationship manager")],
            results.Select(item => Row(("loan", item.LoanNo), ("member", item.MemberName), ("product", item.Product), ("amount", Money(item.SanctionedAmount)), ("closed", Date(item.NextDue)), ("manager", item.RelationshipManager))).ToArray());
    }

    private static ReportMetric Metric(string label, string value, string tone = "") => new(label, value, tone);
    private static ReportColumn Column(string key, string label, string alignment = "left") => new(key, label, alignment);
    private static string Date(DateOnly value) => value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    private static string Money(decimal value) => "₹ " + value.ToString("#,##0.00", CultureInfo.GetCultureInfo("en-IN"));
    private static IReadOnlyDictionary<string, string> Row(params (string Key, string Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value);

    private sealed record ReportRows(
        IReadOnlyList<ReportMetric> Metrics,
        IReadOnlyList<ReportColumn> Columns,
        IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);
}
