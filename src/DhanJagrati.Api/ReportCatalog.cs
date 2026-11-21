namespace DhanJagrati.Api;

public static class ReportCatalog
{
    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new("cashbook", "Cash book summary", "Accounts", "Daily receipts, payments and closing cash."),
        new("cashbook-detail", "Cash book detail", "Accounts", "Voucher-level cash and bank activity."),
        new("trial-balance", "Trial balance", "Accounts", "Debit and credit balances by ledger."),
        new("profit-loss", "Profit & loss statement", "Accounts", "Income and expenses for a period."),
        new("balance-sheet", "Balance sheet", "Accounts", "Assets, liabilities and equity as of a date."),
        new("loan-applications", "Loan applications", "Loans", "Application pipeline and approval status."),
        new("recovery-due", "Loan recovery due", "Loans", "Installments due through the selected date."),
        new("recovery-ledger", "Loan recovery ledger", "Loans", "Receipt history for the selected period."),
        new("relationship-manager", "Relationship manager", "Loans", "Loan portfolio by assigned manager."),
        new("loan-closer", "Loan closure report", "Loans", "Closed accounts and settlement status.")
    ];
}
