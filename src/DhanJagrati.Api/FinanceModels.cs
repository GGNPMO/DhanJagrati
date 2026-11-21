namespace DhanJagrati.Api;

public sealed record Member(
    int Id,
    string MemberNo,
    string FullName,
    string Phone,
    string Village,
    string GroupName,
    string Branch,
    string Status,
    DateOnly JoinedOn,
    DateOnly? DateOfBirth = null,
    string? GuardianName = null,
    decimal? MonthlyIncome = null,
    string KycStatus = "Verified");

public sealed record Loan(
    int Id,
    string LoanNo,
    int MemberId,
    string MemberName,
    string Product,
    decimal SanctionedAmount,
    decimal Outstanding,
    decimal Installment,
    DateOnly NextDue,
    string RelationshipManager,
    string Status);

public sealed record LoanApplication(
    int Id,
    string ApplicationNo,
    int MemberId,
    string MemberName,
    string Product,
    decimal AmountRequested,
    int TenureMonths,
    DateOnly AppliedOn,
    string Purpose,
    string Status,
    string RelationshipManager);

public sealed record Collection(
    int Id,
    string ReceiptNo,
    string LoanNo,
    string MemberName,
    DateOnly PaidOn,
    decimal Amount,
    string Mode,
    string Reference);

public sealed record CreateMemberRequest(
    string FullName,
    string Phone,
    string Village,
    string GroupName,
    DateOnly? DateOfBirth = null,
    string? GuardianName = null,
    decimal? MonthlyIncome = null,
    string? KycStatus = null);

public sealed record CreateLoanRequest(
    int MemberId,
    string Product,
    decimal Amount,
    int TenureMonths,
    string Purpose);

public sealed record LoanDecisionRequest(bool Approved);

public sealed record DisburseLoanRequest(DateOnly DisbursementDate, DateOnly FirstInstallmentDate, string Mode, string Reference);

public sealed record CreateCollectionRequest(
    string LoanNo,
    decimal Amount,
    DateOnly PaidOn,
    string Mode,
    string Reference);

public sealed record CreateVoucherRequest(
    DateOnly VoucherDate,
    string Narration,
    string DebitLedger,
    string CreditLedger,
    decimal Amount,
    string Mode,
    string Reference);

public sealed record DashboardSummary(
    int ActiveMembers,
    int ActiveLoans,
    decimal PortfolioOutstanding,
    decimal CollectionsThisMonth,
    int OverdueAccounts,
    decimal CollectionTarget,
    decimal CollectionAchieved,
    IReadOnlyList<DashboardCollectionDay> CollectionTrend,
    IReadOnlyList<Loan> DueLoans);

public sealed record DashboardCollectionDay(string Day, decimal Amount);

public sealed record ReportDefinition(string Id, string Title, string Category, string Description);

public sealed record ReportMetric(string Label, string Value, string Tone);

public sealed record ReportColumn(string Key, string Label, string Alignment = "left");

public sealed record ReportData(
    string Type,
    string Title,
    string Subtitle,
    string Branch,
    string DateFrom,
    string DateTo,
    string GeneratedAt,
    IReadOnlyList<ReportMetric> Metrics,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed record LedgerActivity(
    DateOnly Date,
    string VoucherNo,
    string Ledger,
    string Particulars,
    decimal Debit,
    decimal Credit,
    string Mode,
    string Reference,
    string Branch);
