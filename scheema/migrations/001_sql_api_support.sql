/* Run after microfinance_schema.sql. Safe to re-run. */
IF COL_LENGTH('lon.LoanApplication', 'PurposeDescription') IS NULL
    ALTER TABLE lon.LoanApplication ADD PurposeDescription NVARCHAR(250) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM gl.AccountGroup WHERE GroupCode = 'ASSETS')
    INSERT gl.AccountGroup(GroupCode, GroupName, AccountClass) VALUES ('ASSETS', N'Assets', 'ASSET');
IF NOT EXISTS (SELECT 1 FROM gl.AccountGroup WHERE GroupCode = 'EQUITY')
    INSERT gl.AccountGroup(GroupCode, GroupName, AccountClass) VALUES ('EQUITY', N'Equity', 'EQUITY');
IF NOT EXISTS (SELECT 1 FROM gl.AccountGroup WHERE GroupCode = 'INCOME')
    INSERT gl.AccountGroup(GroupCode, GroupName, AccountClass) VALUES ('INCOME', N'Income', 'INCOME');
IF NOT EXISTS (SELECT 1 FROM gl.AccountGroup WHERE GroupCode = 'EXPENSES')
    INSERT gl.AccountGroup(GroupCode, GroupName, AccountClass) VALUES ('EXPENSES', N'Expenses', 'EXPENSE');
GO

DECLARE @Assets INT = (SELECT AccountGroupId FROM gl.AccountGroup WHERE GroupCode = 'ASSETS');
DECLARE @Equity INT = (SELECT AccountGroupId FROM gl.AccountGroup WHERE GroupCode = 'EQUITY');
DECLARE @Income INT = (SELECT AccountGroupId FROM gl.AccountGroup WHERE GroupCode = 'INCOME');
DECLARE @Expenses INT = (SELECT AccountGroupId FROM gl.AccountGroup WHERE GroupCode = 'EXPENSES');

IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'CASH')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass, IsCash, IsBank) VALUES ('CASH', N'Cash in Hand', @Assets, 'ASSET', 1, 0);
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'BANK')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass, IsCash, IsBank) VALUES ('BANK', N'Bank Clearing', @Assets, 'ASSET', 0, 1);
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'LOAN_PORTFOLIO')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('LOAN_PORTFOLIO', N'Loan Portfolio', @Assets, 'ASSET');
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'SHARE_CAPITAL')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('SHARE_CAPITAL', N'Share Capital', @Equity, 'EQUITY');
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'INTEREST_INCOME')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('INTEREST_INCOME', N'Interest Income', @Income, 'INCOME');
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'SERVICE_FEE_INCOME')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('SERVICE_FEE_INCOME', N'Service Fee Income', @Income, 'INCOME');
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'PENALTY_INCOME')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('PENALTY_INCOME', N'Penalty Income', @Income, 'INCOME');
IF NOT EXISTS (SELECT 1 FROM gl.Ledger WHERE LedgerCode = 'OPERATING_EXPENSES')
    INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass) VALUES ('OPERATING_EXPENSES', N'Operating Expenses', @Expenses, 'EXPENSE');
GO
