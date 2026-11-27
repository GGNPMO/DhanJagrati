/* =====================================================================================
   DhanJagrati  -  Patch 001
   -------------------------------------------------------------------------------------
   A. App settings + schema version tracking
   B. Configurable EMI calculation per loan plan (formula to be confirmed by business;
      plans default to MANUAL until then) and an EMI snapshot on every loan
   C. Saving module (schema "sav"), self-contained and linked to loans only through
      nullable columns + ledger mappings, so it can be switched on / merged later
   D. Inter-branch clearing ledger + per-branch voucher balance check

   Prerequisite: scheema\dhanjagrati.sql has been applied.
   Run with -b so any error aborts and the transaction is rolled back:
       sqlcmd -S <server> -d DhanJagrati -U <login> -C -b -i scheema\dhanjagrati_patches\001_emi_config_saving_interbranch.sql
   Re-running is safe: the patch is skipped once recorded in sec.SchemaVersion.
   ===================================================================================== */

USE DhanJagrati;
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'sec.Branch', N'U') IS NULL
BEGIN
    RAISERROR('Base schema (dhanjagrati.sql) not found. Apply it before this patch.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID(N'sec.SchemaVersion', N'U') IS NULL
    CREATE TABLE sec.SchemaVersion
    (
        Version     VARCHAR(20)     NOT NULL CONSTRAINT PK_SchemaVersion PRIMARY KEY,
        Description NVARCHAR(200)   NOT NULL,
        AppliedAt   DATETIME2(0)    NOT NULL CONSTRAINT DF_SchemaVersion_At DEFAULT SYSUTCDATETIME()
    );
GO

IF EXISTS (SELECT 1 FROM sec.SchemaVersion WHERE Version = '001')
BEGIN
    PRINT 'Patch 001 already applied - skipping.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

/* =====================================================================================
   A. APPLICATION SETTINGS
   ===================================================================================== */

CREATE TABLE sec.AppSetting
(
    SettingKey      VARCHAR(60)     NOT NULL CONSTRAINT PK_AppSetting PRIMARY KEY,
    SettingValue    NVARCHAR(400)   NOT NULL,
    DataType        VARCHAR(10)     NOT NULL CONSTRAINT CK_AppSetting_Type CHECK (DataType IN ('STRING','INT','DECIMAL','BOOL','DATE')),
    Description     NVARCHAR(300)   NULL,
    ModifiedAt      DATETIME2(0)    NULL,
    ModifiedBy      INT             NULL CONSTRAINT FK_AppSetting_User REFERENCES sec.AppUser(UserId)
);

INSERT sec.AppSetting (SettingKey, SettingValue, DataType, Description) VALUES
    ('MODULE.SAVING.ENABLED',      N'false',  'BOOL',   N'Show the Saving module and allow SAVING as a payment mode in loan screens'),
    ('LOAN.EMI.DEFAULT_METHOD',    N'MANUAL', 'STRING', N'EMI method pre-selected for new loan plans (MANUAL | FLAT | REDUCING | FACTOR)'),
    ('LOAN.EMI.ALLOW_OVERRIDE',    N'true',   'BOOL',   N'Allow users to overwrite the calculated EMI on a loan application');
GO

/* =====================================================================================
   B. CONFIGURABLE EMI CALCULATION
   -------------------------------------------------------------------------------------
   EmiCalcMethod
     MANUAL    EMI typed by the user (no formula). Default until the business confirms.
     FLAT      Interest = P x Rate% x (months/12) when RateBasis = ANNUAL,
               or P x Rate% when RateBasis = TENURE;  EMI = (P + Interest) / installments
     REDUCING  Standard amortised EMI: P x r / (1 - (1 + r)^-n),
               r = annual rate / installments per year
     FACTOR    EMI = P x EmiFactor  (legacy screens fit EmiFactor = 0.111 for EPL-11:
               30000 -> 3330, 40000 -> 4440, 50000 -> 5550; unconfirmed)
   EmiRounding / EmiRoundTo   rounding of the final EMI (e.g. UP to the next 10)
   InterestSplit              how each EMI is split into principal and interest
                              in loan.EmiSchedule: FLAT (equal) or REDUCING (amortised)
   ===================================================================================== */

ALTER TABLE mst.LoanPlan ADD
    EmiCalcMethod     VARCHAR(10)   NOT NULL CONSTRAINT DF_LoanPlan_EmiMethod DEFAULT 'MANUAL'
                      CONSTRAINT CK_LoanPlan_EmiMethod CHECK (EmiCalcMethod IN ('MANUAL','FLAT','REDUCING','FACTOR')),
    EmiFactor         DECIMAL(12,8) NULL CONSTRAINT CK_LoanPlan_EmiFactor CHECK (EmiFactor IS NULL OR EmiFactor > 0),
    RateBasis         VARCHAR(8)    NOT NULL CONSTRAINT DF_LoanPlan_RateBasis DEFAULT 'ANNUAL'
                      CONSTRAINT CK_LoanPlan_RateBasis CHECK (RateBasis IN ('ANNUAL','TENURE')),
    EmiRounding       VARCHAR(8)    NOT NULL CONSTRAINT DF_LoanPlan_EmiRounding DEFAULT 'NEAREST'
                      CONSTRAINT CK_LoanPlan_EmiRounding CHECK (EmiRounding IN ('NONE','NEAREST','UP','DOWN')),
    EmiRoundTo        DECIMAL(9,2)  NOT NULL CONSTRAINT DF_LoanPlan_EmiRoundTo DEFAULT 1
                      CONSTRAINT CK_LoanPlan_EmiRoundTo CHECK (EmiRoundTo > 0),
    InterestSplit     VARCHAR(8)    NOT NULL CONSTRAINT DF_LoanPlan_InterestSplit DEFAULT 'FLAT'
                      CONSTRAINT CK_LoanPlan_InterestSplit CHECK (InterestSplit IN ('FLAT','REDUCING')),
    AllowEmiOverride  BIT           NOT NULL CONSTRAINT DF_LoanPlan_AllowOverride DEFAULT 1;
GO

ALTER TABLE mst.LoanPlan ADD CONSTRAINT CK_LoanPlan_FactorRequired
    CHECK (EmiCalcMethod <> 'FACTOR' OR EmiFactor IS NOT NULL);
GO

-- Snapshot on the loan so later plan changes never alter an existing loan's EMI
ALTER TABLE loan.LoanApplication ADD
    EmiCalcMethod     VARCHAR(10)   NULL CONSTRAINT CK_Loan_EmiMethod CHECK (EmiCalcMethod IN ('MANUAL','FLAT','REDUCING','FACTOR')),
    EmiFactor         DECIMAL(12,8) NULL,
    IsEmiOverridden   BIT           NOT NULL CONSTRAINT DF_Loan_EmiOverridden DEFAULT 0;
GO

/* =====================================================================================
   C. SAVING MODULE  (schema sav)
   -------------------------------------------------------------------------------------
   Kept self-contained: loans reference it only through nullable SavingAccountId /
   SavingTxnId columns, and accounting goes through acc.LedgerMapping. Enabling it is
   a setting (MODULE.SAVING.ENABLED); merging with an external saving system means
   re-pointing those two columns and the SAVING_DEPOSIT ledger mapping.
   ===================================================================================== */
GO
CREATE SCHEMA sav;
GO

CREATE TABLE sav.SavingProduct
(
    ProductId               INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_SavingProduct PRIMARY KEY,
    ProductCode             VARCHAR(20)         NOT NULL CONSTRAINT UQ_SavingProduct_Code UNIQUE,
    ProductName             NVARCHAR(100)       NOT NULL,
    InterestRate            DECIMAL(9,4)        NOT NULL CONSTRAINT DF_SavingProduct_Rate DEFAULT 0
                            CONSTRAINT CK_SavingProduct_Rate CHECK (InterestRate >= 0),
    InterestPosting         CHAR(1)             NOT NULL CONSTRAINT DF_SavingProduct_Posting DEFAULT 'Q'
                            CONSTRAINT CK_SavingProduct_Posting CHECK (InterestPosting IN ('M','Q','H','Y')),  -- monthly/quarterly/half-yearly/yearly
    MinOpeningDeposit       DECIMAL(18,2)       NOT NULL CONSTRAINT DF_SavingProduct_MinOpen DEFAULT 0,
    MinBalance              DECIMAL(18,2)       NOT NULL CONSTRAINT DF_SavingProduct_MinBal DEFAULT 0,
    AllowWithdrawal         BIT                 NOT NULL CONSTRAINT DF_SavingProduct_Withdraw DEFAULT 1,
    DepositLedgerId         INT                 NOT NULL CONSTRAINT FK_SavingProduct_DepLedger REFERENCES acc.Ledger(LedgerId),
    InterestLedgerId        INT                 NOT NULL CONSTRAINT FK_SavingProduct_IntLedger REFERENCES acc.Ledger(LedgerId),
    IsActive                BIT                 NOT NULL CONSTRAINT DF_SavingProduct_IsActive DEFAULT 1,
    CreatedAt               DATETIME2(0)        NOT NULL CONSTRAINT DF_SavingProduct_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE sav.SavingAccount
(
    AccountId           INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_SavingAccount PRIMARY KEY,
    AccountNo           VARCHAR(20)         NOT NULL CONSTRAINT UQ_SavingAccount_No UNIQUE,     -- S002000001
    BranchId            INT                 NOT NULL CONSTRAINT FK_SavingAccount_Branch REFERENCES sec.Branch(BranchId),
    MemberId            INT                 NOT NULL CONSTRAINT FK_SavingAccount_Member REFERENCES loan.Member(MemberId),
    JointMemberId       INT                 NULL CONSTRAINT FK_SavingAccount_Joint REFERENCES loan.Member(MemberId),
    ProductId           INT                 NOT NULL CONSTRAINT FK_SavingAccount_Product REFERENCES sav.SavingProduct(ProductId),
    OpenDate            DATE                NOT NULL,
    EmployeeId          INT                 NULL CONSTRAINT FK_SavingAccount_Employee REFERENCES emp.Employee(EmployeeId),
    NomineeName         NVARCHAR(100)       NULL,
    NomineeRelationId   INT                 NULL CONSTRAINT FK_SavingAccount_NomRel REFERENCES mst.Relation(RelationId),
    CurrentBalance      DECIMAL(18,2)       NOT NULL CONSTRAINT DF_SavingAccount_Balance DEFAULT 0
                        CONSTRAINT CK_SavingAccount_Balance CHECK (CurrentBalance >= 0),
    Status              VARCHAR(8)          NOT NULL CONSTRAINT DF_SavingAccount_Status DEFAULT 'ACTIVE'
                        CONSTRAINT CK_SavingAccount_Status CHECK (Status IN ('ACTIVE','DORMANT','FROZEN','CLOSED')),
    ClosedOn            DATE                NULL,
    RowVer              ROWVERSION          NOT NULL,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_SavingAccount_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_SavingAccount_CreatedBy REFERENCES sec.AppUser(UserId),
    ModifiedAt          DATETIME2(0)        NULL,
    ModifiedBy          INT                 NULL CONSTRAINT FK_SavingAccount_ModifiedBy REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_SavingAccount_Closed CHECK (Status <> 'CLOSED' OR ClosedOn IS NOT NULL),
    CONSTRAINT CK_SavingAccount_Joint CHECK (JointMemberId IS NULL OR JointMemberId <> MemberId)
);
CREATE INDEX IX_SavingAccount_Member ON sav.SavingAccount(MemberId);
CREATE INDEX IX_SavingAccount_Branch ON sav.SavingAccount(BranchId, Status);

CREATE TABLE sav.SavingTransaction
(
    TxnId           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SavingTransaction PRIMARY KEY,
    TxnNo           VARCHAR(20)         NOT NULL CONSTRAINT UQ_SavingTransaction_No UNIQUE,
    AccountId       INT                 NOT NULL CONSTRAINT FK_SavingTxn_Account REFERENCES sav.SavingAccount(AccountId),
    TxnDate         DATE                NOT NULL,
    BranchId        INT                 NOT NULL CONSTRAINT FK_SavingTxn_Branch REFERENCES sec.Branch(BranchId),  -- branch where it happened
    TxnType         VARCHAR(16)         NOT NULL CONSTRAINT CK_SavingTxn_Type CHECK (TxnType IN
                    ('OPENING','DEPOSIT','WITHDRAWAL','INTEREST','CHARGE',
                     'LOAN_CREDIT','LOAN_RECOVERY','TRANSFER_IN','TRANSFER_OUT','REVERSAL')),
    Direction       CHAR(1)             NOT NULL CONSTRAINT CK_SavingTxn_Direction CHECK (Direction IN ('C','D')),  -- C = adds to balance
    Amount          DECIMAL(18,2)       NOT NULL CONSTRAINT CK_SavingTxn_Amount CHECK (Amount > 0),
    BalanceAfter    DECIMAL(18,2)       NOT NULL,
    PaymentMode     VARCHAR(10)         NOT NULL CONSTRAINT CK_SavingTxn_Mode CHECK (PaymentMode IN ('CASH','CHEQUE','NEFT','RTGS','IMPS','UPI','TRANSFER','SYSTEM')),
    ChequeNo        VARCHAR(30)         NULL,
    ChequeDate      DATE                NULL,
    Narration       NVARCHAR(300)       NULL,
    SourceModule    VARCHAR(20)         NULL,       -- LOAN / RECOVERY / SETTLEMENT
    SourceId        BIGINT              NULL,
    VoucherId       BIGINT              NULL CONSTRAINT FK_SavingTxn_Voucher REFERENCES acc.Voucher(VoucherId),
    ReversalOfTxnId BIGINT              NULL CONSTRAINT FK_SavingTxn_Reversal REFERENCES sav.SavingTransaction(TxnId),
    IsReversed      BIT                 NOT NULL CONSTRAINT DF_SavingTxn_Reversed DEFAULT 0,
    CreatedAt       DATETIME2(0)        NOT NULL CONSTRAINT DF_SavingTxn_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT                 NULL CONSTRAINT FK_SavingTxn_CreatedBy REFERENCES sec.AppUser(UserId)
);
CREATE INDEX IX_SavingTxn_Account ON sav.SavingTransaction(AccountId, TxnDate, TxnId);
CREATE INDEX IX_SavingTxn_BranchDate ON sav.SavingTransaction(BranchId, TxnDate);
GO

-- Loan <-> saving links (all nullable: loans work with the module switched off)
ALTER TABLE loan.LoanApplication ADD
    SavingAccountId INT NULL CONSTRAINT FK_Loan_SavingAccount REFERENCES sav.SavingAccount(AccountId);
ALTER TABLE loan.LoanRecovery ADD
    SavingTxnId BIGINT NULL CONSTRAINT FK_LoanRecovery_SavingTxn REFERENCES sav.SavingTransaction(TxnId);
ALTER TABLE loan.LoanSettlement ADD
    SavingTxnId BIGINT NULL CONSTRAINT FK_LoanSettlement_SavingTxn REFERENCES sav.SavingTransaction(TxnId);
GO

-- Voucher types for saving postings
ALTER TABLE acc.Voucher DROP CONSTRAINT CK_Voucher_Type;
ALTER TABLE acc.Voucher ADD CONSTRAINT CK_Voucher_Type CHECK (VoucherType IN
    ('PAYMENT','RECEIPT','JOURNAL','CONTRA','BRANCH_TRANSFER',
     'LOAN_DISBURSE','LOAN_RECOVERY','LOAN_CLOSE','JOINING_FEE',
     'SAVING_DEPOSIT','SAVING_WITHDRAWAL','SAVING_INTEREST','SAVING_TRANSFER'));
GO

/* =====================================================================================
   D. LEDGERS, MAPPINGS, NUMBERING
   -------------------------------------------------------------------------------------
   Branch transfer (A -> B) posts one voucher with lines tagged per branch:
       Branch A:  Dr INTER BRANCH A/C   Cr Cash/Bank
       Branch B:  Dr Cash/Bank          Cr INTER BRANCH A/C
   Each branch balances on its own; consolidated INTER BRANCH A/C nets to zero.
   ===================================================================================== */

INSERT acc.Ledger (LedgerName, NatureId, IsGroup, ParentLedgerId, ReportHead, CashBankType, IsSystem, IsEditable)
SELECT v.LedgerName, v.NatureId, 0, g.LedgerId, v.ReportHead, 'N', 1, 0
FROM (VALUES
    (N'SAVING DEPOSIT A/C',     2, 'BS', N'CURRENT LIABILITIES'),
    (N'INTEREST ON SAVING A/C', 5, 'PL', N'INDIRECT EXPENSES'),
    (N'INTER BRANCH A/C',       1, 'BS', N'CURRENT ASSETS')
) v(LedgerName, NatureId, ReportHead, GroupName)
JOIN acc.Ledger g ON g.LedgerName = v.GroupName AND g.IsGroup = 1;

INSERT acc.LedgerMapping (MappingKey, LedgerId, Description)
SELECT v.MappingKey, l.LedgerId, v.Description
FROM (VALUES
    ('SAVING_DEPOSIT',  N'SAVING DEPOSIT A/C',     N'Member saving balances (liability)'),
    ('SAVING_INTEREST', N'INTEREST ON SAVING A/C', N'Interest credited to saving accounts'),
    ('INTER_BRANCH',    N'INTER BRANCH A/C',       N'Clearing ledger for branch-to-branch transfers')
) v(MappingKey, LedgerName, Description)
JOIN acc.Ledger l ON l.LedgerName = v.LedgerName;

-- Default product; rate / minimum balance to be configured by the business
INSERT sav.SavingProduct (ProductCode, ProductName, InterestRate, InterestPosting, DepositLedgerId, InterestLedgerId)
SELECT 'SB', N'REGULAR SAVING', 0, 'Q',
       (SELECT LedgerId FROM acc.Ledger WHERE LedgerName = N'SAVING DEPOSIT A/C'),
       (SELECT LedgerId FROM acc.Ledger WHERE LedgerName = N'INTEREST ON SAVING A/C');

INSERT sec.NumberSeries (SeriesKey, BranchId, Prefix, NextNo, PadLength)
SELECT 'SAVING_ACCOUNT', BranchId, N'S' + BranchCode, 1, 6 FROM sec.Branch;
INSERT sec.NumberSeries (SeriesKey, BranchId, Prefix, NextNo, PadLength) VALUES ('SAVING_TXN', 0, N'ST', 1, 0);
GO

/* =====================================================================================
   E. VIEWS / PROCEDURES
   ===================================================================================== */

-- Replaces the base view: a voucher must balance for every branch it touches
CREATE OR ALTER VIEW acc.vw_UnbalancedVouchers
AS
WITH PerBranch AS
(
    SELECT l.VoucherId, l.BranchId, SUM(l.Debit) AS TotalDebit, SUM(l.Credit) AS TotalCredit
    FROM   acc.VoucherLine l
    GROUP BY l.VoucherId, l.BranchId
)
SELECT  v.VoucherId, v.VoucherNo, v.VoucherDate, p.BranchId, p.TotalDebit, p.TotalCredit
FROM    acc.Voucher v
LEFT JOIN PerBranch p ON p.VoucherId = v.VoucherId
WHERE   v.IsCancelled = 0
  AND   (p.VoucherId IS NULL OR p.TotalDebit <> p.TotalCredit);
GO

-- Inter-branch position per branch; the column total across branches should be zero
CREATE OR ALTER VIEW acc.vw_InterBranchPosition
AS
SELECT  b.BranchId, b.BranchName,
        ISNULL(SUM(l.Debit - l.Credit), 0) AS NetDebit
FROM    sec.Branch b
LEFT JOIN acc.VoucherLine l
       ON l.BranchId = b.BranchId
      AND l.LedgerId = (SELECT LedgerId FROM acc.LedgerMapping WHERE MappingKey = 'INTER_BRANCH')
      AND EXISTS (SELECT 1 FROM acc.Voucher v WHERE v.VoucherId = l.VoucherId AND v.IsCancelled = 0)
GROUP BY b.BranchId, b.BranchName;
GO

-- Saving balance check: cached balance vs. sum of transactions
CREATE OR ALTER VIEW sav.vw_SavingAccountBalance
AS
SELECT  a.AccountId, a.AccountNo, a.BranchId, a.Status, a.OpenDate,
        m.MemberNo, m.MemberName, p.ProductCode,
        a.CurrentBalance,
        ISNULL(t.TxnBalance, 0) AS TxnBalance,
        CAST(CASE WHEN a.CurrentBalance <> ISNULL(t.TxnBalance, 0) THEN 1 ELSE 0 END AS BIT) AS IsMismatch
FROM    sav.SavingAccount a
JOIN    loan.Member m         ON m.MemberId = a.MemberId
JOIN    sav.SavingProduct p   ON p.ProductId = a.ProductId
OUTER APPLY (SELECT SUM(CASE WHEN x.Direction = 'C' THEN x.Amount ELSE -x.Amount END) AS TxnBalance
             FROM sav.SavingTransaction x WHERE x.AccountId = a.AccountId) t;
GO

CREATE OR ALTER PROCEDURE sav.usp_SavingStatement
    @AccountId  INT,
    @FromDate   DATE,
    @ToDate     DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT ISNULL(SUM(CASE WHEN Direction = 'C' THEN Amount ELSE -Amount END), 0) AS OpeningBalance
    FROM   sav.SavingTransaction
    WHERE  AccountId = @AccountId AND TxnDate < @FromDate;

    SELECT  TxnNo, TxnDate, TxnType, PaymentMode, ChequeNo, Narration,
            CASE WHEN Direction = 'D' THEN Amount ELSE 0 END AS Withdrawal,
            CASE WHEN Direction = 'C' THEN Amount ELSE 0 END AS Deposit,
            BalanceAfter
    FROM    sav.SavingTransaction
    WHERE   AccountId = @AccountId AND TxnDate BETWEEN @FromDate AND @ToDate
    ORDER BY TxnDate, TxnId;
END
GO

INSERT sec.SchemaVersion (Version, Description)
VALUES ('001', N'Configurable EMI, saving module, inter-branch ledger');
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
