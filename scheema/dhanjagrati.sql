/* =====================================================================================
   DHAN JAGRATI MICROFINANCE FOUNDATION  -  Database schema (SQL Server 2017+)
   -------------------------------------------------------------------------------------
   Derived from the legacy desktop screens in "screen shot/":

     MASTER       Salutation, Occupation, Religion, City, State, Country, District,
                  Tehsil, Khasra, Village, Center/Group, Vehicle, Dealer,
                  Interest Rate (loan plans), Daily Attendance, Date Closing
     EMPLOYEE     Advisor / Loan Relationship Manager entry (introducer chain, ranks)
     LOAN MASTER  Customer (member), Loan Application, Loan Approve, Single Recovery,
                  Bulk Recovery, Loan Settlement (close / re-open), loan reports,
                  Loan Card & Signature Card print
     ACCOUNT      Groups & Ledgers, Ledger Transaction Journal (vouchers), Cash Book,
                  Trial Balance, Profit & Loss, Balance Sheet
     HELP         Change Password, Select Branch (branch + login/business date)

   Schemas:  sec (security / company / branch / help)    mst (masters)
             emp (employees / advisors)                  loan (members, loans, recovery)
             acc (chart of accounts, vouchers, financial reports)

   Bootstrap script: run ONCE against a new server/database.
       sqlcmd -S <server> -d master -U <login> -C -b -i scheema\dhanjagrati.sql
   Business rules that change balances (EMI schedule generation, recovery posting,
   voucher creation) are executed by the Web API inside transactions; this script
   provides the tables, constraints, numbering, views and report procedures.
   ===================================================================================== */

USE master;
GO
IF DB_ID(N'DhanJagrati') IS NULL
    CREATE DATABASE DhanJagrati;
GO
USE DhanJagrati;
GO

IF OBJECT_ID(N'sec.Branch', N'U') IS NOT NULL
BEGIN
    RAISERROR('DhanJagrati schema already exists. This bootstrap script must only run on an empty database.', 16, 1);
    SET NOEXEC ON;
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE SCHEMA sec;
GO
CREATE SCHEMA mst;
GO
CREATE SCHEMA emp;
GO
CREATE SCHEMA loan;
GO
CREATE SCHEMA acc;
GO

/* =====================================================================================
   1. SECURITY, COMPANY, BRANCH  (HELP menu: Change Password, Select Branch)
   ===================================================================================== */

CREATE TABLE sec.Company
(
    CompanyId       INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Company PRIMARY KEY,
    CompanyName     NVARCHAR(150)       NOT NULL,
    AddressLine     NVARCHAR(300)       NULL,
    City            NVARCHAR(80)        NULL,
    PinCode         VARCHAR(10)         NULL,
    Phone           VARCHAR(20)         NULL,
    Email           NVARCHAR(120)       NULL,
    RegistrationNo  NVARCHAR(50)        NULL,
    LogoPath        NVARCHAR(400)       NULL,
    CreatedAt       DATETIME2(0)        NOT NULL CONSTRAINT DF_Company_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE sec.Branch
(
    BranchId        INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Branch PRIMARY KEY,
    CompanyId       INT                 NOT NULL CONSTRAINT FK_Branch_Company REFERENCES sec.Company(CompanyId),
    BranchCode      VARCHAR(10)         NOT NULL CONSTRAINT UQ_Branch_Code UNIQUE,
    BranchName      NVARCHAR(100)       NOT NULL CONSTRAINT UQ_Branch_Name UNIQUE,
    AddressLine     NVARCHAR(300)       NULL,
    Phone           VARCHAR(20)         NULL,
    CreditLimit     DECIMAL(18,2)       NULL,          -- "Set Branch Credit Limit"
    IsHeadOffice    BIT                 NOT NULL CONSTRAINT DF_Branch_IsHO DEFAULT 0,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Branch_IsActive DEFAULT 1,
    CreatedAt       DATETIME2(0)        NOT NULL CONSTRAINT DF_Branch_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE sec.AppUser
(
    UserId              INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_AppUser PRIMARY KEY,
    UserName            NVARCHAR(50)        NOT NULL CONSTRAINT UQ_AppUser_UserName UNIQUE,
    DisplayName         NVARCHAR(100)       NOT NULL,
    PasswordHash        NVARCHAR(500)       NULL,   -- ASP.NET Core PasswordHasher output; NULL = cannot log in yet
    RoleCode            VARCHAR(20)         NOT NULL CONSTRAINT DF_AppUser_Role DEFAULT 'OPERATOR'
                        CONSTRAINT CK_AppUser_Role CHECK (RoleCode IN ('ADMIN','MANAGER','ACCOUNTANT','OPERATOR','FIELD_OFFICER')),
    DefaultBranchId     INT                 NOT NULL CONSTRAINT FK_AppUser_Branch REFERENCES sec.Branch(BranchId),
    EmployeeId          INT                 NULL,   -- FK added after emp.Employee is created
    MustChangePassword  BIT                 NOT NULL CONSTRAINT DF_AppUser_MustChange DEFAULT 1,
    FailedLoginCount    INT                 NOT NULL CONSTRAINT DF_AppUser_Failed DEFAULT 0,
    LockedUntil         DATETIME2(0)        NULL,
    PasswordChangedAt   DATETIME2(0)        NULL,
    LastLoginAt         DATETIME2(0)        NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_AppUser_IsActive DEFAULT 1,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_AppUser_CreatedAt DEFAULT SYSUTCDATETIME()
);

-- Branches a user may switch to from HELP > Select Branch
CREATE TABLE sec.UserBranch
(
    UserId      INT NOT NULL CONSTRAINT FK_UserBranch_User REFERENCES sec.AppUser(UserId),
    BranchId    INT NOT NULL CONSTRAINT FK_UserBranch_Branch REFERENCES sec.Branch(BranchId),
    CONSTRAINT PK_UserBranch PRIMARY KEY (UserId, BranchId)
);

-- Login session: current branch + business (login) date chosen on Select Branch
CREATE TABLE sec.UserSession
(
    SessionId       UNIQUEIDENTIFIER    NOT NULL CONSTRAINT PK_UserSession PRIMARY KEY CONSTRAINT DF_UserSession_Id DEFAULT NEWID(),
    UserId          INT                 NOT NULL CONSTRAINT FK_UserSession_User REFERENCES sec.AppUser(UserId),
    BranchId        INT                 NOT NULL CONSTRAINT FK_UserSession_Branch REFERENCES sec.Branch(BranchId),
    BusinessDate    DATE                NOT NULL,
    LoginAt         DATETIME2(0)        NOT NULL CONSTRAINT DF_UserSession_LoginAt DEFAULT SYSUTCDATETIME(),
    LastSeenAt      DATETIME2(0)        NOT NULL CONSTRAINT DF_UserSession_LastSeen DEFAULT SYSUTCDATETIME(),
    LogoutAt        DATETIME2(0)        NULL,
    ClientIp        VARCHAR(45)         NULL
);
CREATE INDEX IX_UserSession_User ON sec.UserSession(UserId, LoginAt DESC);

CREATE TABLE sec.LoginAudit
(
    LoginAuditId    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoginAudit PRIMARY KEY,
    UserName        NVARCHAR(50)         NOT NULL,
    EventType       VARCHAR(20)          NOT NULL CONSTRAINT CK_LoginAudit_Event
                    CHECK (EventType IN ('LOGIN_OK','LOGIN_FAIL','LOGOUT','PASSWORD_CHANGE','BRANCH_CHANGE')),
    BranchId        INT                  NULL,
    ClientIp        VARCHAR(45)          NULL,
    EventAt         DATETIME2(0)         NOT NULL CONSTRAINT DF_LoginAudit_At DEFAULT SYSUTCDATETIME()
);

-- MASTER > Date Closing by User: once closed, no postings allowed on that branch/date
CREATE TABLE sec.DayClosing
(
    BranchId        INT             NOT NULL CONSTRAINT FK_DayClosing_Branch REFERENCES sec.Branch(BranchId),
    BusinessDate    DATE            NOT NULL,
    ClosedBy        INT             NOT NULL CONSTRAINT FK_DayClosing_User REFERENCES sec.AppUser(UserId),
    ClosedAt        DATETIME2(0)    NOT NULL CONSTRAINT DF_DayClosing_At DEFAULT SYSUTCDATETIME(),
    Remarks         NVARCHAR(250)   NULL,
    CONSTRAINT PK_DayClosing PRIMARY KEY (BranchId, BusinessDate)
);

-- Document numbering: M000000904, FO00000026, L002001096, LR22330, voucher 1004315 ...
CREATE TABLE sec.NumberSeries
(
    SeriesKey   VARCHAR(20)     NOT NULL,
    BranchId    INT             NOT NULL CONSTRAINT DF_NumberSeries_Branch DEFAULT 0,  -- 0 = company-wide
    Prefix      NVARCHAR(10)    NOT NULL CONSTRAINT DF_NumberSeries_Prefix DEFAULT N'',
    NextNo      BIGINT          NOT NULL CONSTRAINT CK_NumberSeries_Next CHECK (NextNo > 0),
    PadLength   TINYINT         NOT NULL CONSTRAINT DF_NumberSeries_Pad DEFAULT 0,
    CONSTRAINT PK_NumberSeries PRIMARY KEY (SeriesKey, BranchId)
);
GO

/* =====================================================================================
   2. MASTERS  (MASTER menu)
   ===================================================================================== */

CREATE TABLE mst.Salutation             -- MR, MRS, S/O, W/O, D/O
(
    SalutationId    INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Salutation PRIMARY KEY,
    SalutationName  NVARCHAR(20)        NOT NULL CONSTRAINT UQ_Salutation_Name UNIQUE,
    IsRelation      BIT                 NOT NULL CONSTRAINT DF_Salutation_IsRelation DEFAULT 0,  -- S/O, W/O, D/O
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Salutation_IsActive DEFAULT 1
);

CREATE TABLE mst.Occupation
(
    OccupationId    INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Occupation PRIMARY KEY,
    OccupationName  NVARCHAR(80)        NOT NULL CONSTRAINT UQ_Occupation_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Occupation_IsActive DEFAULT 1
);

CREATE TABLE mst.Religion
(
    ReligionId      INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Religion PRIMARY KEY,
    ReligionName    NVARCHAR(50)        NOT NULL CONSTRAINT UQ_Religion_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Religion_IsActive DEFAULT 1
);

CREATE TABLE mst.Relation              -- nominee relation: MOTHER, FATHER, WIFE ...
(
    RelationId      INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Relation PRIMARY KEY,
    RelationName    NVARCHAR(40)        NOT NULL CONSTRAINT UQ_Relation_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Relation_IsActive DEFAULT 1
);

CREATE TABLE mst.Designation           -- KYC designation in member group (PRESIDENT, SECRETARY, MEMBER)
(
    DesignationId   INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Designation PRIMARY KEY,
    DesignationName NVARCHAR(60)        NOT NULL CONSTRAINT UQ_Designation_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Designation_IsActive DEFAULT 1
);

CREATE TABLE mst.Bank                  -- bank names used in member bank details / cheque recovery
(
    BankId          INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Bank PRIMARY KEY,
    BankName        NVARCHAR(120)       NOT NULL CONSTRAINT UQ_Bank_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Bank_IsActive DEFAULT 1
);

-- Geography: Country > State > District > Tehsil > Village > Khasra ; City under State
CREATE TABLE mst.Country
(
    CountryId       INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Country PRIMARY KEY,
    CountryName     NVARCHAR(80)        NOT NULL CONSTRAINT UQ_Country_Name UNIQUE,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Country_IsActive DEFAULT 1
);

CREATE TABLE mst.State
(
    StateId         INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_State PRIMARY KEY,
    CountryId       INT                 NOT NULL CONSTRAINT FK_State_Country REFERENCES mst.Country(CountryId),
    StateName       NVARCHAR(80)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_State_IsActive DEFAULT 1,
    CONSTRAINT UQ_State_Name UNIQUE (CountryId, StateName)
);

CREATE TABLE mst.City
(
    CityId          INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_City PRIMARY KEY,
    StateId         INT                 NULL CONSTRAINT FK_City_State REFERENCES mst.State(StateId),
    CityName        NVARCHAR(80)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_City_IsActive DEFAULT 1
);
CREATE UNIQUE INDEX UQ_City_Name ON mst.City(StateId, CityName);

CREATE TABLE mst.District
(
    DistrictId      INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_District PRIMARY KEY,
    StateId         INT                 NOT NULL CONSTRAINT FK_District_State REFERENCES mst.State(StateId),
    DistrictName    NVARCHAR(80)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_District_IsActive DEFAULT 1,
    CONSTRAINT UQ_District_Name UNIQUE (StateId, DistrictName)
);

CREATE TABLE mst.Tehsil
(
    TehsilId        INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Tehsil PRIMARY KEY,
    DistrictId      INT                 NOT NULL CONSTRAINT FK_Tehsil_District REFERENCES mst.District(DistrictId),
    TehsilName      NVARCHAR(80)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Tehsil_IsActive DEFAULT 1,
    CONSTRAINT UQ_Tehsil_Name UNIQUE (DistrictId, TehsilName)
);

CREATE TABLE mst.Village
(
    VillageId       INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Village PRIMARY KEY,
    TehsilId        INT                 NOT NULL CONSTRAINT FK_Village_Tehsil REFERENCES mst.Tehsil(TehsilId),
    VillageName     NVARCHAR(100)       NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Village_IsActive DEFAULT 1,
    CONSTRAINT UQ_Village_Name UNIQUE (TehsilId, VillageName)
);

CREATE TABLE mst.Khasra                -- land record: Khasra No + area inside a village
(
    KhasraId        INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Khasra PRIMARY KEY,
    VillageId       INT                 NOT NULL CONSTRAINT FK_Khasra_Village REFERENCES mst.Village(VillageId),
    KhasraNo        NVARCHAR(30)        NOT NULL,
    TotalArea       DECIMAL(12,4)       NULL CONSTRAINT CK_Khasra_Area CHECK (TotalArea IS NULL OR TotalArea >= 0),
    AreaUnit        NVARCHAR(15)        NULL,     -- BIGHA / HECTARE / ACRE
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Khasra_IsActive DEFAULT 1,
    CONSTRAINT UQ_Khasra_No UNIQUE (VillageId, KhasraNo)
);

-- Read-only view mirroring the legacy grids (village / tehsil / district / state names in one row)
GO
CREATE OR ALTER VIEW mst.vw_Location
AS
SELECT  v.VillageId, v.VillageName,
        t.TehsilId, t.TehsilName,
        d.DistrictId, d.DistrictName,
        s.StateId, s.StateName,
        c.CountryId, c.CountryName
FROM    mst.Village v
JOIN    mst.Tehsil t   ON t.TehsilId = v.TehsilId
JOIN    mst.District d ON d.DistrictId = t.DistrictId
JOIN    mst.State s    ON s.StateId = d.StateId
JOIN    mst.Country c  ON c.CountryId = s.CountryId;
GO

CREATE TABLE mst.Center                -- Center Master (meeting centre of a branch)
(
    CenterId        INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Center PRIMARY KEY,
    BranchId        INT                 NOT NULL CONSTRAINT FK_Center_Branch REFERENCES sec.Branch(BranchId),
    CenterCode      VARCHAR(20)         NOT NULL,
    CenterName      NVARCHAR(100)       NOT NULL,
    VillageId       INT                 NULL CONSTRAINT FK_Center_Village REFERENCES mst.Village(VillageId),
    MeetingDay      TINYINT             NULL CONSTRAINT CK_Center_Day CHECK (MeetingDay BETWEEN 1 AND 31),
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Center_IsActive DEFAULT 1,
    CONSTRAINT UQ_Center_Code UNIQUE (BranchId, CenterCode)
);

CREATE TABLE mst.MemberGroup           -- Group Name on loan / bulk recovery (SHG001, "Single")
(
    GroupId         INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_MemberGroup PRIMARY KEY,
    BranchId        INT                 NOT NULL CONSTRAINT FK_MemberGroup_Branch REFERENCES sec.Branch(BranchId),
    CenterId        INT                 NULL CONSTRAINT FK_MemberGroup_Center REFERENCES mst.Center(CenterId),
    GroupCode       VARCHAR(20)         NOT NULL,
    GroupName       NVARCHAR(100)       NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_MemberGroup_IsActive DEFAULT 1,
    CONSTRAINT UQ_MemberGroup_Code UNIQUE (BranchId, GroupCode)
);

CREATE TABLE mst.Vehicle
(
    VehicleId       INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Vehicle PRIMARY KEY,
    VehicleName     NVARCHAR(100)       NOT NULL,
    Manufacturer    NVARCHAR(100)       NULL,
    Model           NVARCHAR(60)        NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Vehicle_IsActive DEFAULT 1
);

CREATE TABLE mst.Dealer
(
    DealerId        INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Dealer PRIMARY KEY,
    DealerName      NVARCHAR(120)       NOT NULL,
    ContactPerson   NVARCHAR(100)       NULL,
    MobileNo        VARCHAR(15)         NULL,
    AddressLine     NVARCHAR(300)       NULL,
    CityId          INT                 NULL CONSTRAINT FK_Dealer_City REFERENCES mst.City(CityId),
    GstNo           VARCHAR(20)         NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_Dealer_IsActive DEFAULT 1
);

-- MASTER > Interest Rate Set : loan plans (PLAN_CODE EPL-11, SPL-12 ...) with gender-wise rates by period
CREATE TABLE mst.LoanPlan
(
    PlanId              INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_LoanPlan PRIMARY KEY,
    PlanCode            NVARCHAR(20)        NOT NULL CONSTRAINT UQ_LoanPlan_Code UNIQUE,
    PlanName            NVARCHAR(100)       NULL,
    EmiMode             CHAR(1)             NOT NULL CONSTRAINT DF_LoanPlan_EmiMode DEFAULT 'M'
                        CONSTRAINT CK_LoanPlan_EmiMode CHECK (EmiMode IN ('D','W','F','M')),  -- Daily/Weekly/Fortnightly(By-weekly)/Monthly
    TenureMonths        SMALLINT            NOT NULL CONSTRAINT CK_LoanPlan_Tenure CHECK (TenureMonths > 0),
    NoOfInstallments    SMALLINT            NOT NULL CONSTRAINT CK_LoanPlan_Inst CHECK (NoOfInstallments > 0),
    ProcessingFeePct    DECIMAL(5,2)        NOT NULL CONSTRAINT DF_LoanPlan_ProcFee DEFAULT 0,
    LateFeePerInstallment DECIMAL(18,2)     NOT NULL CONSTRAINT DF_LoanPlan_LateFee DEFAULT 0,
    PreCloseChargesPct  DECIMAL(5,2)        NOT NULL CONSTRAINT DF_LoanPlan_PreClose DEFAULT 0,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_LoanPlan_IsActive DEFAULT 1,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_LoanPlan_CreatedAt DEFAULT SYSUTCDATETIME()
);

CREATE TABLE mst.InterestRate
(
    InterestRateId  INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_InterestRate PRIMARY KEY,
    PlanId          INT                 NOT NULL CONSTRAINT FK_InterestRate_Plan REFERENCES mst.LoanPlan(PlanId),
    PlanAmount      DECIMAL(18,2)       NOT NULL CONSTRAINT CK_InterestRate_Amount CHECK (PlanAmount > 0),
    RateMale        DECIMAL(9,4)        NOT NULL CONSTRAINT CK_InterestRate_Male CHECK (RateMale >= 0),
    RateFemale      DECIMAL(9,4)        NOT NULL CONSTRAINT CK_InterestRate_Female CHECK (RateFemale >= 0),
    EffectiveFrom   DATE                NOT NULL,
    EffectiveTo     DATE                NULL,
    CreatedAt       DATETIME2(0)        NOT NULL CONSTRAINT DF_InterestRate_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_InterestRate_Dates CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE INDEX IX_InterestRate_Plan ON mst.InterestRate(PlanId, EffectiveFrom);
GO

/* =====================================================================================
   3. EMPLOYEE  (Advisor / Field Officer / Loan Relationship Manager)
   ===================================================================================== */

CREATE TABLE emp.AdvisorRank
(
    RankId          INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_AdvisorRank PRIMARY KEY,
    RankNo          SMALLINT            NOT NULL CONSTRAINT UQ_AdvisorRank_No UNIQUE,
    RankCode        NVARCHAR(20)        NOT NULL CONSTRAINT UQ_AdvisorRank_Code UNIQUE,   -- 1-MEM
    RankName        NVARCHAR(60)        NOT NULL,
    IsActive        BIT                 NOT NULL CONSTRAINT DF_AdvisorRank_IsActive DEFAULT 1
);

CREATE TABLE emp.Employee
(
    EmployeeId          INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Employee PRIMARY KEY,
    EmployeeCode        VARCHAR(20)         NOT NULL CONSTRAINT UQ_Employee_Code UNIQUE,      -- FO00000026
    BranchId            INT                 NOT NULL CONSTRAINT FK_Employee_Branch REFERENCES sec.Branch(BranchId),
    JoiningDate         DATE                NOT NULL,
    -- Introducer / rank
    IntroducerId        INT                 NULL CONSTRAINT FK_Employee_Introducer REFERENCES emp.Employee(EmployeeId),
    IntroducerRankNo    SMALLINT            NULL,
    InitialRankId       INT                 NULL CONSTRAINT FK_Employee_InitialRank REFERENCES emp.AdvisorRank(RankId),
    CurrentRankId       INT                 NULL CONSTRAINT FK_Employee_CurrentRank REFERENCES emp.AdvisorRank(RankId),
    -- Personal details
    MemberId            INT                 NULL,       -- employee may also be a member (FK added later)
    EmployeeName        NVARCHAR(100)       NOT NULL,
    GuardianName        NVARCHAR(100)       NULL,
    DateOfBirth         DATE                NULL,
    Gender              CHAR(1)             NULL CONSTRAINT CK_Employee_Gender CHECK (Gender IN ('M','F','O')),
    -- Address & contact
    AddressLine         NVARCHAR(300)       NULL,
    CityId              INT                 NULL CONSTRAINT FK_Employee_City REFERENCES mst.City(CityId),
    StateId             INT                 NULL CONSTRAINT FK_Employee_State REFERENCES mst.State(StateId),
    CountryId           INT                 NULL CONSTRAINT FK_Employee_Country REFERENCES mst.Country(CountryId),
    PinCode             VARCHAR(10)         NULL,
    MobileNo            VARCHAR(15)         NULL,
    PhoneNo             VARCHAR(20)         NULL,
    Email               NVARCHAR(120)       NULL,
    PassportNo          VARCHAR(20)         NULL,
    -- Nominee
    NomineeName         NVARCHAR(100)       NULL,
    NomineeDob          DATE                NULL,
    NomineeRelationId   INT                 NULL CONSTRAINT FK_Employee_NomRel REFERENCES mst.Relation(RelationId),
    -- Joining fee
    JoiningFee          DECIMAL(18,2)       NOT NULL CONSTRAINT DF_Employee_JoinFee DEFAULT 0,
    JoiningFeeMode      VARCHAR(10)         NULL CONSTRAINT CK_Employee_FeeMode CHECK (JoiningFeeMode IN ('CASH','CHEQUE','NEFT','UPI')),
    JoiningFeeRefNo     VARCHAR(40)         NULL,
    PhotoPath           NVARCHAR(400)       NULL,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Employee_IsActive DEFAULT 1,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_Employee_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_Employee_CreatedBy REFERENCES sec.AppUser(UserId),
    ModifiedAt          DATETIME2(0)        NULL,
    ModifiedBy          INT                 NULL CONSTRAINT FK_Employee_ModifiedBy REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_Employee_NotOwnIntroducer CHECK (IntroducerId IS NULL OR IntroducerId <> EmployeeId)
);
CREATE INDEX IX_Employee_Branch ON emp.Employee(BranchId) INCLUDE (EmployeeCode, EmployeeName);
CREATE INDEX IX_Employee_Introducer ON emp.Employee(IntroducerId);

ALTER TABLE sec.AppUser ADD CONSTRAINT FK_AppUser_Employee FOREIGN KEY (EmployeeId) REFERENCES emp.Employee(EmployeeId);

-- MASTER > Daily Attendance
CREATE TABLE emp.Attendance
(
    EmployeeId      INT             NOT NULL CONSTRAINT FK_Attendance_Employee REFERENCES emp.Employee(EmployeeId),
    AttendanceDate  DATE            NOT NULL,
    Status          CHAR(1)         NOT NULL CONSTRAINT CK_Attendance_Status CHECK (Status IN ('P','A','L','H','W')), -- Present/Absent/Leave/Half-day/Week-off
    InTime          TIME(0)         NULL,
    OutTime         TIME(0)         NULL,
    Remarks         NVARCHAR(200)   NULL,
    MarkedBy        INT             NULL CONSTRAINT FK_Attendance_User REFERENCES sec.AppUser(UserId),
    CONSTRAINT PK_Attendance PRIMARY KEY (EmployeeId, AttendanceDate)
);
GO

/* =====================================================================================
   4. ACCOUNTS  (Groups & Ledgers, Journal vouchers)
   ===================================================================================== */

CREATE TABLE acc.AccountNature
(
    NatureId        TINYINT         NOT NULL CONSTRAINT PK_AccountNature PRIMARY KEY,
    NatureCode      VARCHAR(10)     NOT NULL CONSTRAINT UQ_AccountNature_Code UNIQUE,
    NatureName      NVARCHAR(30)    NOT NULL,
    ReportHead      CHAR(2)         NOT NULL CONSTRAINT CK_AccountNature_Report CHECK (ReportHead IN ('BS','PL')),
    NormalBalance   CHAR(1)         NOT NULL CONSTRAINT CK_AccountNature_Normal CHECK (NormalBalance IN ('D','C'))
);

CREATE TABLE acc.Ledger
(
    LedgerId            INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Ledger PRIMARY KEY,           -- LD_CODE
    LedgerName          NVARCHAR(150)       NOT NULL CONSTRAINT UQ_Ledger_Name UNIQUE,            -- LD_NAME
    NatureId            TINYINT             NOT NULL CONSTRAINT FK_Ledger_Nature REFERENCES acc.AccountNature(NatureId), -- LD_TYPE
    IsGroup             BIT                 NOT NULL,                                             -- LD_G_OR_L
    ParentLedgerId      INT                 NULL,                                                 -- LD_ROOT_CODE ("Under Group")
    ParentIsGroup       AS CAST(1 AS BIT) PERSISTED,
    ReportHead          CHAR(2)             NOT NULL CONSTRAINT CK_Ledger_Report CHECK (ReportHead IN ('BS','PL')), -- "Reports of Accounting"
    CashBankType        CHAR(1)             NOT NULL CONSTRAINT DF_Ledger_CashBank DEFAULT 'N'
                        CONSTRAINT CK_Ledger_CashBank CHECK (CashBankType IN ('C','B','N')),  -- LD_B_OR_C: Cash / Bank / None
    BankAccountNo       VARCHAR(30)         NULL,
    IfscCode            VARCHAR(15)         NULL,
    IsEditable          BIT                 NOT NULL CONSTRAINT DF_Ledger_Editable DEFAULT 1,
    IsVisibleInBranch   BIT                 NOT NULL CONSTRAINT DF_Ledger_Visible DEFAULT 1,
    IsDefaultGroup      BIT                 NOT NULL CONSTRAINT DF_Ledger_DefaultGroup DEFAULT 0,
    ApplySanction       BIT                 NOT NULL CONSTRAINT DF_Ledger_Sanction DEFAULT 0,      -- voucher needs sanction no.
    IsSystem            BIT                 NOT NULL CONSTRAINT DF_Ledger_System DEFAULT 0,        -- cannot be deleted
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Ledger_Active DEFAULT 1,        -- LD_ACTIVE
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_Ledger_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_Ledger_CreatedBy REFERENCES sec.AppUser(UserId),
    CONSTRAINT UQ_Ledger_IdGroup UNIQUE (LedgerId, IsGroup),
    CONSTRAINT FK_Ledger_Parent FOREIGN KEY (ParentLedgerId, ParentIsGroup) REFERENCES acc.Ledger(LedgerId, IsGroup),
    CONSTRAINT CK_Ledger_CashBankLeaf CHECK (CashBankType = 'N' OR IsGroup = 0)
);
CREATE INDEX IX_Ledger_Parent ON acc.Ledger(ParentLedgerId);

-- Opening balance per ledger per branch ("Opening Bal." + "Bal Type Cr/Dr" + "On Date")
CREATE TABLE acc.LedgerOpening
(
    LedgerId        INT             NOT NULL CONSTRAINT FK_LedgerOpening_Ledger REFERENCES acc.Ledger(LedgerId),
    BranchId        INT             NOT NULL CONSTRAINT FK_LedgerOpening_Branch REFERENCES sec.Branch(BranchId),
    OpeningDate     DATE            NOT NULL,
    OpeningBalance  DECIMAL(18,2)   NOT NULL CONSTRAINT CK_LedgerOpening_Amt CHECK (OpeningBalance >= 0),
    BalType         CHAR(1)         NOT NULL CONSTRAINT CK_LedgerOpening_Type CHECK (BalType IN ('D','C')),
    CONSTRAINT PK_LedgerOpening PRIMARY KEY (LedgerId, BranchId)
);

-- Ledgers the API posts automatically (loan disbursement, recovery, fees ...)
CREATE TABLE acc.LedgerMapping
(
    MappingKey      VARCHAR(30)     NOT NULL CONSTRAINT PK_LedgerMapping PRIMARY KEY,
    LedgerId        INT             NOT NULL CONSTRAINT FK_LedgerMapping_Ledger REFERENCES acc.Ledger(LedgerId),
    Description     NVARCHAR(200)   NULL
);

CREATE TABLE acc.Voucher
(
    VoucherId           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Voucher PRIMARY KEY,
    VoucherNo           VARCHAR(20)         NOT NULL CONSTRAINT UQ_Voucher_No UNIQUE,     -- Tran. No. 1004315
    VoucherDate         DATE                NOT NULL,
    BranchId            INT                 NOT NULL CONSTRAINT FK_Voucher_Branch REFERENCES sec.Branch(BranchId),
    VoucherType         VARCHAR(20)         NOT NULL CONSTRAINT CK_Voucher_Type CHECK (VoucherType IN
                        ('PAYMENT','RECEIPT','JOURNAL','CONTRA','BRANCH_TRANSFER',
                         'LOAN_DISBURSE','LOAN_RECOVERY','LOAN_CLOSE','JOINING_FEE')),
    PaymentMode         VARCHAR(10)         NOT NULL CONSTRAINT CK_Voucher_Mode CHECK (PaymentMode IN
                        ('CASH','CHEQUE','NEFT','RTGS','IMPS','UPI','TRANSFER','SAVING')),
    ChequeNo            VARCHAR(30)         NULL,
    ChequeDate          DATE                NULL,
    PayeeName           NVARCHAR(150)       NULL,
    Narration           NVARCHAR(500)       NULL,       -- Remarks / Particulars
    Amount              DECIMAL(18,2)       NOT NULL CONSTRAINT CK_Voucher_Amount CHECK (Amount > 0),
    TransferToBranchId  INT                 NULL CONSTRAINT FK_Voucher_TransferBranch REFERENCES sec.Branch(BranchId),
    SanctionNo          VARCHAR(30)         NULL,
    SourceModule        VARCHAR(20)         NULL,       -- LOAN / RECOVERY / SETTLEMENT / EMPLOYEE
    SourceId            BIGINT              NULL,
    IsCancelled         BIT                 NOT NULL CONSTRAINT DF_Voucher_Cancelled DEFAULT 0,
    CancelledAt         DATETIME2(0)        NULL,
    CancelledBy         INT                 NULL CONSTRAINT FK_Voucher_CancelledBy REFERENCES sec.AppUser(UserId),
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_Voucher_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_Voucher_CreatedBy REFERENCES sec.AppUser(UserId),
    ModifiedAt          DATETIME2(0)        NULL,
    ModifiedBy          INT                 NULL CONSTRAINT FK_Voucher_ModifiedBy REFERENCES sec.AppUser(UserId)
);
CREATE INDEX IX_Voucher_BranchDate ON acc.Voucher(BranchId, VoucherDate) INCLUDE (IsCancelled, VoucherNo);
CREATE INDEX IX_Voucher_Source ON acc.Voucher(SourceModule, SourceId);

CREATE TABLE acc.VoucherLine
(
    VoucherLineId   BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_VoucherLine PRIMARY KEY,
    VoucherId       BIGINT          NOT NULL CONSTRAINT FK_VoucherLine_Voucher REFERENCES acc.Voucher(VoucherId),
    LineNumber      SMALLINT        NOT NULL,
    BranchId        INT             NOT NULL CONSTRAINT FK_VoucherLine_Branch REFERENCES sec.Branch(BranchId), -- differs from header for branch transfer
    LedgerId        INT             NOT NULL,
    LedgerIsGroup   AS CAST(0 AS BIT) PERSISTED,
    Debit           DECIMAL(18,2)   NOT NULL CONSTRAINT DF_VoucherLine_Dr DEFAULT 0,
    Credit          DECIMAL(18,2)   NOT NULL CONSTRAINT DF_VoucherLine_Cr DEFAULT 0,
    Narration       NVARCHAR(500)   NULL,
    CONSTRAINT UQ_VoucherLine_No UNIQUE (VoucherId, LineNumber),
    CONSTRAINT FK_VoucherLine_Ledger FOREIGN KEY (LedgerId, LedgerIsGroup) REFERENCES acc.Ledger(LedgerId, IsGroup), -- postings only to leaf ledgers
    CONSTRAINT CK_VoucherLine_Amount CHECK (Debit >= 0 AND Credit >= 0 AND (Debit = 0 OR Credit = 0) AND Debit + Credit > 0)
);
CREATE INDEX IX_VoucherLine_Ledger ON acc.VoucherLine(LedgerId, BranchId) INCLUDE (VoucherId, Debit, Credit);
GO

/* =====================================================================================
   5. LOAN MASTER  (Customer, Application, Approval, Recovery, Settlement)
   ===================================================================================== */

CREATE TABLE loan.Member
(
    MemberId            INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_Member PRIMARY KEY,
    MemberNo            VARCHAR(20)         NOT NULL CONSTRAINT UQ_Member_No UNIQUE,         -- M000000904
    FileNo              VARCHAR(20)         NULL,
    AadhaarNo           CHAR(12)            NULL CONSTRAINT CK_Member_Aadhaar CHECK (AadhaarNo NOT LIKE '%[^0-9]%' AND LEN(AadhaarNo) = 12),
    RegistrationDate    DATE                NOT NULL,
    BranchId            INT                 NOT NULL CONSTRAINT FK_Member_Branch REFERENCES sec.Branch(BranchId),
    -- Customer information
    SalutationId        INT                 NULL CONSTRAINT FK_Member_Salutation REFERENCES mst.Salutation(SalutationId),
    MemberName          NVARCHAR(100)       NOT NULL,
    GuardianRelationId  INT                 NULL CONSTRAINT FK_Member_GuardianRel REFERENCES mst.Salutation(SalutationId), -- S/O, W/O, D/O
    GuardianName        NVARCHAR(100)       NULL,
    MotherName          NVARCHAR(100)       NULL,
    Gender              CHAR(1)             NOT NULL CONSTRAINT CK_Member_Gender CHECK (Gender IN ('M','F','O')),
    ReligionId          INT                 NULL CONSTRAINT FK_Member_Religion REFERENCES mst.Religion(ReligionId),
    DateOfBirth         DATE                NULL,
    MonthlyIncome       DECIMAL(18,2)       NULL CONSTRAINT CK_Member_Income CHECK (MonthlyIncome IS NULL OR MonthlyIncome >= 0),
    MaritalStatus       VARCHAR(10)         NULL CONSTRAINT CK_Member_Marital CHECK (MaritalStatus IN ('SINGLE','MARRIED','WIDOWED','DIVORCED')),
    IsSeniorCitizen     BIT                 NOT NULL CONSTRAINT DF_Member_Senior DEFAULT 0,
    IsHandicapped       BIT                 NOT NULL CONSTRAINT DF_Member_Handicap DEFAULT 0,
    IsSpecialCategory   BIT                 NOT NULL CONSTRAINT DF_Member_Special DEFAULT 0,
    OccupationId        INT                 NULL CONSTRAINT FK_Member_Occupation REFERENCES mst.Occupation(OccupationId),
    -- Bank details
    PanNo               VARCHAR(10)         NULL,
    BankId              INT                 NULL CONSTRAINT FK_Member_Bank REFERENCES mst.Bank(BankId),
    IfscCode            VARCHAR(15)         NULL,
    BankAccountNo       VARCHAR(30)         NULL,
    -- Contact details
    AddressLine         NVARCHAR(300)       NOT NULL,
    PermanentAddress    NVARCHAR(300)       NULL,
    VillageId           INT                 NULL CONSTRAINT FK_Member_Village REFERENCES mst.Village(VillageId),
    CityId              INT                 NULL CONSTRAINT FK_Member_City REFERENCES mst.City(CityId),
    StateId             INT                 NULL CONSTRAINT FK_Member_State REFERENCES mst.State(StateId),
    CountryId           INT                 NULL CONSTRAINT FK_Member_Country REFERENCES mst.Country(CountryId),
    PinCode             VARCHAR(10)         NULL,
    MobileNo            VARCHAR(15)         NULL,
    PhoneNo             VARCHAR(20)         NULL,
    Email               NVARCHAR(120)       NULL,
    PassportNo          VARCHAR(20)         NULL,
    -- Nominee details
    NomineeName         NVARCHAR(100)       NULL,
    NomineeGuardianName NVARCHAR(100)       NULL,
    NomineeDob          DATE                NULL,
    NomineeRelationId   INT                 NULL CONSTRAINT FK_Member_NomRel REFERENCES mst.Relation(RelationId),
    RefMemberId         INT                 NULL CONSTRAINT FK_Member_RefMember REFERENCES loan.Member(MemberId),
    -- Know Your Customer
    KycAadhaar          BIT                 NOT NULL CONSTRAINT DF_Member_KycA DEFAULT 0,
    KycVoterId          BIT                 NOT NULL CONSTRAINT DF_Member_KycV DEFAULT 0,
    KycDrivingLicense   BIT                 NOT NULL CONSTRAINT DF_Member_KycD DEFAULT 0,
    KycOtherProof       BIT                 NOT NULL CONSTRAINT DF_Member_KycO DEFAULT 0,
    IdProofNo           VARCHAR(30)         NULL,
    GroupId             INT                 NULL CONSTRAINT FK_Member_Group REFERENCES mst.MemberGroup(GroupId),
    DesignationId       INT                 NULL CONSTRAINT FK_Member_Designation REFERENCES mst.Designation(DesignationId),
    -- Photo / signature (Photo/Sign Verify)
    PhotoPath           NVARCHAR(400)       NULL,
    SignaturePath       NVARCHAR(400)       NULL,
    IsPhotoVerified     BIT                 NOT NULL CONSTRAINT DF_Member_PhotoVer DEFAULT 0,
    IsActive            BIT                 NOT NULL CONSTRAINT DF_Member_IsActive DEFAULT 1,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_Member_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_Member_CreatedBy REFERENCES sec.AppUser(UserId),
    ModifiedAt          DATETIME2(0)        NULL,
    ModifiedBy          INT                 NULL CONSTRAINT FK_Member_ModifiedBy REFERENCES sec.AppUser(UserId)
);
CREATE UNIQUE INDEX UQ_Member_Aadhaar ON loan.Member(AadhaarNo) WHERE AadhaarNo IS NOT NULL;
CREATE INDEX IX_Member_Branch ON loan.Member(BranchId) INCLUDE (MemberNo, MemberName);
CREATE INDEX IX_Member_Name ON loan.Member(MemberName);
CREATE INDEX IX_Member_Mobile ON loan.Member(MobileNo);

ALTER TABLE emp.Employee ADD CONSTRAINT FK_Employee_Member FOREIGN KEY (MemberId) REFERENCES loan.Member(MemberId);

CREATE TABLE loan.LoanApplication
(
    LoanId              INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_LoanApplication PRIMARY KEY,
    LoanNo              VARCHAR(20)         NOT NULL CONSTRAINT UQ_Loan_No UNIQUE,           -- L002001096
    ApplicationDate     DATE                NOT NULL,
    BranchId            INT                 NOT NULL CONSTRAINT FK_Loan_Branch REFERENCES sec.Branch(BranchId),
    MemberId            INT                 NOT NULL CONSTRAINT FK_Loan_Member REFERENCES loan.Member(MemberId),
    -- Loan calculator
    PlanId              INT                 NOT NULL CONSTRAINT FK_Loan_Plan REFERENCES mst.LoanPlan(PlanId),
    EmiMode             CHAR(1)             NOT NULL CONSTRAINT CK_Loan_EmiMode CHECK (EmiMode IN ('D','W','F','M')),
    LoanAmount          DECIMAL(18,2)       NOT NULL CONSTRAINT CK_Loan_Amount CHECK (LoanAmount > 0),
    InterestRate        DECIMAL(9,4)        NOT NULL CONSTRAINT CK_Loan_Rate CHECK (InterestRate >= 0),
    TenureYears         SMALLINT            NOT NULL CONSTRAINT DF_Loan_Years DEFAULT 0,
    TenureMonths        SMALLINT            NOT NULL CONSTRAINT DF_Loan_Months DEFAULT 0,
    NoOfInstallments    SMALLINT            NOT NULL CONSTRAINT CK_Loan_Inst CHECK (NoOfInstallments > 0),
    EmiAmount           DECIMAL(18,2)       NOT NULL CONSTRAINT CK_Loan_Emi CHECK (EmiAmount > 0),
    NoOfUnits           SMALLINT            NOT NULL CONSTRAINT DF_Loan_Units DEFAULT 1,
    EmiStartDate        DATE                NOT NULL,
    -- Disbursement
    DisbursementMode    VARCHAR(10)         NOT NULL CONSTRAINT CK_Loan_DisbMode CHECK (DisbursementMode IN ('CASH','CHEQUE','NEFT','RTGS','IMPS','SAVING')),
    ChequeNo            VARCHAR(30)         NULL,
    ChequeAmount        DECIMAL(18,2)       NULL,
    DisbursementLedgerId INT                NULL CONSTRAINT FK_Loan_DisbLedger REFERENCES acc.Ledger(LedgerId),  -- cash / company bank ledger
    Remark              NVARCHAR(500)       NULL,
    -- Relationship
    EmployeeId          INT                 NOT NULL CONSTRAINT FK_Loan_Employee REFERENCES emp.Employee(EmployeeId),
    GroupId             INT                 NULL CONSTRAINT FK_Loan_Group REFERENCES mst.MemberGroup(GroupId),
    -- Deductions
    ProcessingFeePct    DECIMAL(5,2)        NOT NULL CONSTRAINT DF_Loan_ProcPct DEFAULT 0,
    ProcessingFee       DECIMAL(18,2)       NOT NULL CONSTRAINT DF_Loan_ProcFee DEFAULT 0,
    GstAmount           DECIMAL(18,2)       NOT NULL CONSTRAINT DF_Loan_Gst DEFAULT 0,
    ShareAmount         DECIMAL(18,2)       NOT NULL CONSTRAINT DF_Loan_Share DEFAULT 0,
    InsuranceAmount     DECIMAL(18,2)       NOT NULL CONSTRAINT DF_Loan_Insurance DEFAULT 0,
    NetDisbursedAmount  AS CAST(LoanAmount - ProcessingFee - GstAmount - ShareAmount - InsuranceAmount AS DECIMAL(18,2)) PERSISTED,
    SavingAccountNo     VARCHAR(30)         NULL,
    IsSavingTransfer    BIT                 NOT NULL CONSTRAINT DF_Loan_SavingTr DEFAULT 0,
    -- Workflow
    Status              VARCHAR(12)         NOT NULL CONSTRAINT DF_Loan_Status DEFAULT 'PENDING'
                        CONSTRAINT CK_Loan_Status CHECK (Status IN ('PENDING','APPROVED','REJECTED','CLOSED')),
    ApprovedOn          DATE                NULL,
    ApprovedBy          INT                 NULL CONSTRAINT FK_Loan_ApprovedBy REFERENCES sec.AppUser(UserId),
    DisbursedOn         DATE                NULL,
    DisbursementVoucherId BIGINT            NULL CONSTRAINT FK_Loan_DisbVoucher REFERENCES acc.Voucher(VoucherId),
    ClosedOn            DATE                NULL,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_Loan_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_Loan_CreatedBy REFERENCES sec.AppUser(UserId),
    ModifiedAt          DATETIME2(0)        NULL,
    ModifiedBy          INT                 NULL CONSTRAINT FK_Loan_ModifiedBy REFERENCES sec.AppUser(UserId),
    CONSTRAINT CK_Loan_Deductions CHECK (ProcessingFee >= 0 AND GstAmount >= 0 AND ShareAmount >= 0 AND InsuranceAmount >= 0
                                         AND ProcessingFee + GstAmount + ShareAmount + InsuranceAmount <= LoanAmount),
    CONSTRAINT CK_Loan_ApprovedOn CHECK (Status NOT IN ('APPROVED','CLOSED') OR ApprovedOn IS NOT NULL)
);
CREATE INDEX IX_Loan_BranchStatus ON loan.LoanApplication(BranchId, Status, ApplicationDate);
CREATE INDEX IX_Loan_Member ON loan.LoanApplication(MemberId);
CREATE INDEX IX_Loan_Employee ON loan.LoanApplication(EmployeeId, Status);

CREATE TABLE loan.LoanGuarantor        -- "Guarantor Info." tab
(
    GuarantorId     INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_LoanGuarantor PRIMARY KEY,
    LoanId          INT                 NOT NULL CONSTRAINT FK_LoanGuarantor_Loan REFERENCES loan.LoanApplication(LoanId),
    MemberId        INT                 NULL CONSTRAINT FK_LoanGuarantor_Member REFERENCES loan.Member(MemberId),
    GuarantorName   NVARCHAR(100)       NOT NULL,
    GuardianName    NVARCHAR(100)       NULL,
    AddressLine     NVARCHAR(300)       NULL,
    MobileNo        VARCHAR(15)         NULL,
    RelationId      INT                 NULL CONSTRAINT FK_LoanGuarantor_Relation REFERENCES mst.Relation(RelationId),
    IdProofNo       VARCHAR(30)         NULL
);
CREATE INDEX IX_LoanGuarantor_Loan ON loan.LoanGuarantor(LoanId);

CREATE TABLE loan.LoanCollateralDeposit  -- "Loan Against Deposit A/c" tab
(
    CollateralId    INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_LoanCollateralDeposit PRIMARY KEY,
    LoanId          INT                 NOT NULL CONSTRAINT FK_LoanCollateral_Loan REFERENCES loan.LoanApplication(LoanId),
    DepositAccountNo VARCHAR(30)        NOT NULL,
    DepositAmount   DECIMAL(18,2)       NOT NULL CONSTRAINT CK_LoanCollateral_Amt CHECK (DepositAmount > 0),
    MaturityDate    DATE                NULL,
    LienAmount      DECIMAL(18,2)       NULL
);

CREATE TABLE loan.LoanStatusHistory     -- Loan Approve: Approve / Dis-Approve / Reject; Settlement: Close / Re-open
(
    HistoryId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoanStatusHistory PRIMARY KEY,
    LoanId          INT                 NOT NULL CONSTRAINT FK_LoanStatusHistory_Loan REFERENCES loan.LoanApplication(LoanId),
    ActionType      VARCHAR(12)         NOT NULL CONSTRAINT CK_LoanStatusHistory_Action CHECK (ActionType IN ('CREATE','APPROVE','DISAPPROVE','REJECT','CLOSE','REOPEN')),
    FromStatus      VARCHAR(12)         NULL,
    ToStatus        VARCHAR(12)         NOT NULL,
    ActionDate      DATE                NOT NULL,
    Remarks         NVARCHAR(300)       NULL,
    ActionBy        INT                 NULL CONSTRAINT FK_LoanStatusHistory_User REFERENCES sec.AppUser(UserId),
    ActionAt        DATETIME2(0)        NOT NULL CONSTRAINT DF_LoanStatusHistory_At DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_LoanStatusHistory_Loan ON loan.LoanStatusHistory(LoanId, ActionAt);

-- Repayment schedule, generated by the API on approval
CREATE TABLE loan.EmiSchedule
(
    LoanId          INT             NOT NULL CONSTRAINT FK_EmiSchedule_Loan REFERENCES loan.LoanApplication(LoanId),
    InstallmentNo   SMALLINT        NOT NULL CONSTRAINT CK_EmiSchedule_No CHECK (InstallmentNo > 0),
    DueDate         DATE            NOT NULL,
    EmiAmount       DECIMAL(18,2)   NOT NULL CONSTRAINT CK_EmiSchedule_Emi CHECK (EmiAmount > 0),
    PrincipalAmount DECIMAL(18,2)   NOT NULL,
    InterestAmount  DECIMAL(18,2)   NOT NULL,
    PrincipalPaid   DECIMAL(18,2)   NOT NULL CONSTRAINT DF_EmiSchedule_PrinPaid DEFAULT 0,
    InterestPaid    DECIMAL(18,2)   NOT NULL CONSTRAINT DF_EmiSchedule_IntPaid DEFAULT 0,
    LateFeePaid     DECIMAL(18,2)   NOT NULL CONSTRAINT DF_EmiSchedule_LateFee DEFAULT 0,
    LastPaidDate    DATE            NULL,
    Status          VARCHAR(8)      NOT NULL CONSTRAINT DF_EmiSchedule_Status DEFAULT 'DUE'
                    CONSTRAINT CK_EmiSchedule_Status CHECK (Status IN ('DUE','PARTIAL','PAID','WAIVED')),
    CONSTRAINT PK_EmiSchedule PRIMARY KEY (LoanId, InstallmentNo),
    CONSTRAINT CK_EmiSchedule_Split CHECK (PrincipalAmount >= 0 AND InterestAmount >= 0 AND PrincipalAmount + InterestAmount = EmiAmount),
    CONSTRAINT CK_EmiSchedule_Paid CHECK (PrincipalPaid BETWEEN 0 AND PrincipalAmount AND InterestPaid BETWEEN 0 AND InterestAmount)
);
CREATE INDEX IX_EmiSchedule_Due ON loan.EmiSchedule(DueDate, Status) INCLUDE (EmiAmount, PrincipalPaid, InterestPaid);

-- Loan Bulk Recovery batch (advisor-wise / group-wise)
CREATE TABLE loan.BulkRecoveryBatch
(
    BatchId         INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_BulkRecoveryBatch PRIMARY KEY,
    BranchId        INT                 NOT NULL CONSTRAINT FK_BulkBatch_Branch REFERENCES sec.Branch(BranchId),      -- branch of the loans
    PaidBranchId    INT                 NOT NULL CONSTRAINT FK_BulkBatch_PaidBranch REFERENCES sec.Branch(BranchId),  -- "Paid Branch"
    EmployeeId      INT                 NULL CONSTRAINT FK_BulkBatch_Employee REFERENCES emp.Employee(EmployeeId),
    GroupId         INT                 NULL CONSTRAINT FK_BulkBatch_Group REFERENCES mst.MemberGroup(GroupId),
    DueFrom         DATE                NOT NULL,
    DueTo           DATE                NOT NULL,
    PaidDate        DATE                NOT NULL,
    PaymentMode     VARCHAR(10)         NOT NULL CONSTRAINT CK_BulkBatch_Mode CHECK (PaymentMode IN ('CASH','SAVING')),
    SelectionMode   VARCHAR(10)         NOT NULL CONSTRAINT CK_BulkBatch_Sel CHECK (SelectionMode IN ('LOAN','ADVISOR','GROUP')),
    TotalAmount     DECIMAL(18,2)       NOT NULL CONSTRAINT DF_BulkBatch_Total DEFAULT 0,
    CreatedAt       DATETIME2(0)        NOT NULL CONSTRAINT DF_BulkBatch_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       INT                 NULL CONSTRAINT FK_BulkBatch_CreatedBy REFERENCES sec.AppUser(UserId)
);

-- Loan Recovery (single) and each row of a bulk recovery
CREATE TABLE loan.LoanRecovery
(
    RecoveryId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoanRecovery PRIMARY KEY,
    TransNo             VARCHAR(20)         NOT NULL CONSTRAINT UQ_LoanRecovery_TransNo UNIQUE,  -- LR22330
    TransDate           DATE                NOT NULL,
    BranchId            INT                 NOT NULL CONSTRAINT FK_LoanRecovery_Branch REFERENCES sec.Branch(BranchId),   -- collecting branch
    LoanId              INT                 NOT NULL CONSTRAINT FK_LoanRecovery_Loan REFERENCES loan.LoanApplication(LoanId),
    InstallmentNo       SMALLINT            NOT NULL,                       -- EMI No
    EmiAmount           DECIMAL(18,2)       NOT NULL,
    PrincipalAmount     DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_Prin DEFAULT 0,
    InterestAmount      DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_Int DEFAULT 0,
    PreviousLateFee     DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_PrevLF DEFAULT 0,
    CurrentLateFee      DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_CurLF DEFAULT 0,
    LateFeePaid         DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_LFPaid DEFAULT 0,
    ChequeBounceCharges DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LoanRecovery_Bounce DEFAULT 0,
    NetAmount           DECIMAL(18,2)       NOT NULL CONSTRAINT CK_LoanRecovery_Net CHECK (NetAmount > 0),  -- amount received
    PaymentMode         VARCHAR(10)         NOT NULL CONSTRAINT CK_LoanRecovery_Mode CHECK (PaymentMode IN ('CASH','CHEQUE','NEFT','RTGS','IMPS','UPI','SAVING')),
    RefTransNo          VARCHAR(40)         NULL,
    ChequeNo            VARCHAR(30)         NULL,
    ChequeDate          DATE                NULL,
    BankName            NVARCHAR(120)       NULL,
    ReceiptNo           VARCHAR(20)         NULL,
    Remarks             NVARCHAR(300)       NULL,
    EmployeeId          INT                 NULL CONSTRAINT FK_LoanRecovery_Employee REFERENCES emp.Employee(EmployeeId),  -- FO who collected
    BulkBatchId         INT                 NULL CONSTRAINT FK_LoanRecovery_Batch REFERENCES loan.BulkRecoveryBatch(BatchId),
    VoucherId           BIGINT              NULL CONSTRAINT FK_LoanRecovery_Voucher REFERENCES acc.Voucher(VoucherId),
    IsReversed          BIT                 NOT NULL CONSTRAINT DF_LoanRecovery_Reversed DEFAULT 0,
    CreatedAt           DATETIME2(0)        NOT NULL CONSTRAINT DF_LoanRecovery_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT                 NULL CONSTRAINT FK_LoanRecovery_CreatedBy REFERENCES sec.AppUser(UserId),
    CONSTRAINT FK_LoanRecovery_Schedule FOREIGN KEY (LoanId, InstallmentNo) REFERENCES loan.EmiSchedule(LoanId, InstallmentNo),
    CONSTRAINT CK_LoanRecovery_Parts CHECK (PrincipalAmount >= 0 AND InterestAmount >= 0 AND LateFeePaid >= 0 AND ChequeBounceCharges >= 0)
);
CREATE INDEX IX_LoanRecovery_Loan ON loan.LoanRecovery(LoanId, TransDate);
CREATE INDEX IX_LoanRecovery_BranchDate ON loan.LoanRecovery(BranchId, TransDate) INCLUDE (NetAmount, IsReversed);
CREATE INDEX IX_LoanRecovery_Employee ON loan.LoanRecovery(EmployeeId, TransDate);

-- Loan Settlement: pre-closure / re-open
CREATE TABLE loan.LoanSettlement
(
    SettlementId            INT IDENTITY(1,1)   NOT NULL CONSTRAINT PK_LoanSettlement PRIMARY KEY,
    LoanId                  INT                 NOT NULL CONSTRAINT FK_LoanSettlement_Loan REFERENCES loan.LoanApplication(LoanId),
    ActionType              VARCHAR(6)          NOT NULL CONSTRAINT CK_LoanSettlement_Action CHECK (ActionType IN ('CLOSE','REOPEN')),
    ActionDate              DATE                NOT NULL,
    TotalPrincipalReceived  DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_PrinRec DEFAULT 0,
    TotalInterestReceived   DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_IntRec DEFAULT 0,
    BalancePrincipal        DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_BalPrin DEFAULT 0,
    BalanceInterest         DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_BalInt DEFAULT 0,
    LateFeeBalance          DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_LateFee DEFAULT 0,
    BalanceInstallmentAmt   DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_BalInst DEFAULT 0,
    AmountToBeReceived      DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_ToRec DEFAULT 0,
    LateFeeReceived         DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_LFRec DEFAULT 0,
    PreCloseCharges         DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_PreClose DEFAULT 0,
    NetCloseAmount          DECIMAL(18,2)       NOT NULL CONSTRAINT DF_LS_Net DEFAULT 0,
    PaymentMode             VARCHAR(10)         NULL CONSTRAINT CK_LoanSettlement_Mode CHECK (PaymentMode IN ('CASH','CHEQUE','NEFT','RTGS','IMPS','UPI','SAVING')),
    Remarks                 NVARCHAR(300)       NULL,
    VoucherId               BIGINT              NULL CONSTRAINT FK_LoanSettlement_Voucher REFERENCES acc.Voucher(VoucherId),
    CreatedAt               DATETIME2(0)        NOT NULL CONSTRAINT DF_LoanSettlement_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy               INT                 NULL CONSTRAINT FK_LoanSettlement_CreatedBy REFERENCES sec.AppUser(UserId)
);
CREATE INDEX IX_LoanSettlement_Loan ON loan.LoanSettlement(LoanId, ActionDate);

-- Card print log (Loan Card / Signature Card)
CREATE TABLE loan.CardPrintLog
(
    PrintId         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CardPrintLog PRIMARY KEY,
    LoanId          INT             NOT NULL CONSTRAINT FK_CardPrintLog_Loan REFERENCES loan.LoanApplication(LoanId),
    CardType        VARCHAR(10)     NOT NULL CONSTRAINT CK_CardPrintLog_Type CHECK (CardType IN ('LOAN','SIGNATURE')),
    PrintDate       DATE            NOT NULL,
    PrintedBy       INT             NULL CONSTRAINT FK_CardPrintLog_User REFERENCES sec.AppUser(UserId),
    PrintedAt       DATETIME2(0)    NOT NULL CONSTRAINT DF_CardPrintLog_At DEFAULT SYSUTCDATETIME()
);
GO

/* =====================================================================================
   6. NUMBERING PROCEDURE
   ===================================================================================== */

CREATE OR ALTER PROCEDURE sec.usp_NextNumber
    @SeriesKey  VARCHAR(20),
    @BranchId   INT = 0,
    @Number     VARCHAR(30) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @n BIGINT, @prefix NVARCHAR(10), @pad TINYINT;

    UPDATE sec.NumberSeries WITH (UPDLOCK, HOLDLOCK)
    SET    @n = NextNo, @prefix = Prefix, @pad = PadLength, NextNo = NextNo + 1
    WHERE  SeriesKey = @SeriesKey AND BranchId = @BranchId;

    IF @@ROWCOUNT = 0
        THROW 50001, 'Number series not configured for this key/branch.', 1;

    SET @Number = @prefix + CASE WHEN @pad = 0 OR LEN(CAST(@n AS VARCHAR(20))) >= @pad
                                 THEN CAST(@n AS VARCHAR(20))
                                 ELSE RIGHT(REPLICATE('0', @pad) + CAST(@n AS VARCHAR(20)), @pad) END;
END
GO

/* =====================================================================================
   7. VIEWS
   ===================================================================================== */

-- Loan outstanding position (used by recovery, settlement, employee & closure reports)
CREATE OR ALTER VIEW loan.vw_LoanOutstanding
AS
SELECT  s.LoanId,
        COUNT(*)                                                      AS TotalInstallments,
        SUM(CASE WHEN s.Status IN ('PAID','WAIVED') THEN 1 ELSE 0 END) AS PaidInstallments,
        SUM(s.EmiAmount)                                              AS TotalRepayable,
        SUM(s.PrincipalAmount)                                        AS TotalPrincipal,
        SUM(s.InterestAmount)                                         AS TotalInterest,
        SUM(s.PrincipalPaid)                                          AS PrincipalReceived,
        SUM(s.InterestPaid)                                           AS InterestReceived,
        SUM(s.LateFeePaid)                                            AS LateFeeReceived,
        SUM(CASE WHEN s.Status = 'WAIVED' THEN 0 ELSE s.PrincipalAmount - s.PrincipalPaid END) AS BalancePrincipal,
        SUM(CASE WHEN s.Status = 'WAIVED' THEN 0 ELSE s.InterestAmount - s.InterestPaid END)   AS BalanceInterest,
        MIN(CASE WHEN s.Status IN ('DUE','PARTIAL') THEN s.DueDate END)                        AS NextDueDate,
        MIN(CASE WHEN s.Status IN ('DUE','PARTIAL') THEN s.InstallmentNo END)                  AS NextInstallmentNo
FROM    loan.EmiSchedule s
GROUP BY s.LoanId;
GO

-- Loan Card print (8.1)
CREATE OR ALTER VIEW loan.vw_LoanCard
AS
SELECT  l.LoanId, l.LoanNo, l.BranchId, m.FileNo, m.MemberNo, m.MemberName, m.GuardianName,
        m.AddressLine, l.ApplicationDate, l.DisbursedOn, l.EmiStartDate,
        l.NoOfInstallments, l.EmiAmount, l.EmiMode, DAY(l.EmiStartDate) AS EmiDayOfMonth,
        e.EmployeeCode, e.EmployeeName
FROM    loan.LoanApplication l
JOIN    loan.Member m   ON m.MemberId = l.MemberId
JOIN    emp.Employee e  ON e.EmployeeId = l.EmployeeId;
GO

-- Signature Card print (8.2)
CREATE OR ALTER VIEW loan.vw_SignatureCard
AS
SELECT  l.LoanId, l.LoanNo, l.BranchId, l.ApplicationDate, l.LoanAmount,
        m.FileNo, m.MemberNo, m.MemberName, m.GuardianName, m.RegistrationDate AS DateOfJoining,
        DATEDIFF(YEAR, m.DateOfBirth, l.ApplicationDate)
          - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, m.DateOfBirth, l.ApplicationDate), m.DateOfBirth) > l.ApplicationDate THEN 1 ELSE 0 END AS Age,
        m.AddressLine, m.Gender, m.MobileNo, o.OccupationName, m.PhotoPath, m.SignaturePath
FROM    loan.LoanApplication l
JOIN    loan.Member m          ON m.MemberId = l.MemberId
LEFT JOIN mst.Occupation o     ON o.OccupationId = m.OccupationId;
GO

-- Vouchers whose lines do not balance (should always be empty)
CREATE OR ALTER VIEW acc.vw_UnbalancedVouchers
AS
SELECT  v.VoucherId, v.VoucherNo, v.VoucherDate, SUM(l.Debit) AS TotalDebit, SUM(l.Credit) AS TotalCredit
FROM    acc.Voucher v
LEFT JOIN acc.VoucherLine l ON l.VoucherId = v.VoucherId
WHERE   v.IsCancelled = 0
GROUP BY v.VoucherId, v.VoucherNo, v.VoucherDate
HAVING  ISNULL(SUM(l.Debit), 0) <> ISNULL(SUM(l.Credit), 0) OR COUNT(l.VoucherLineId) < 2;
GO

/* =====================================================================================
   8. ACCOUNT REPORTS
   ===================================================================================== */

-- Ledger balances for a branch (NULL = all branches / H.O.) over a period
CREATE OR ALTER FUNCTION acc.fn_LedgerBalances (@BranchId INT, @FromDate DATE, @ToDate DATE)
RETURNS TABLE
AS
RETURN
WITH Openings AS
(
    SELECT  o.LedgerId, SUM(CASE o.BalType WHEN 'D' THEN o.OpeningBalance ELSE -o.OpeningBalance END) AS Amount
    FROM    acc.LedgerOpening o
    WHERE   (@BranchId IS NULL OR o.BranchId = @BranchId)
    GROUP BY o.LedgerId
),
Moves AS
(
    SELECT  l.LedgerId,
            SUM(CASE WHEN v.VoucherDate <  @FromDate THEN l.Debit - l.Credit ELSE 0 END) AS PriorNet,
            SUM(CASE WHEN v.VoucherDate >= @FromDate THEN l.Debit  ELSE 0 END)           AS PeriodDebit,
            SUM(CASE WHEN v.VoucherDate >= @FromDate THEN l.Credit ELSE 0 END)           AS PeriodCredit
    FROM    acc.VoucherLine l
    JOIN    acc.Voucher v ON v.VoucherId = l.VoucherId
    WHERE   v.IsCancelled = 0
      AND   v.VoucherDate <= @ToDate
      AND   (@BranchId IS NULL OR l.BranchId = @BranchId)
    GROUP BY l.LedgerId
)
SELECT  g.LedgerId, g.LedgerName, g.NatureId, g.ReportHead, g.ParentLedgerId, g.CashBankType,
        CAST(ISNULL(op.Amount, 0) + ISNULL(m.PriorNet, 0) AS DECIMAL(18,2))  AS OpeningNet,
        CAST(ISNULL(m.PeriodDebit, 0)  AS DECIMAL(18,2))                     AS PeriodDebit,
        CAST(ISNULL(m.PeriodCredit, 0) AS DECIMAL(18,2))                     AS PeriodCredit,
        CAST(ISNULL(op.Amount, 0) + ISNULL(m.PriorNet, 0)
             + ISNULL(m.PeriodDebit, 0) - ISNULL(m.PeriodCredit, 0) AS DECIMAL(18,2)) AS ClosingNet   -- +Dr / -Cr
FROM    acc.Ledger g
LEFT JOIN Openings op ON op.LedgerId = g.LedgerId
LEFT JOIN Moves m     ON m.LedgerId  = g.LedgerId
WHERE   g.IsGroup = 0;
GO

-- Cash Book: result set 1 = opening / receipts / payments / closing,
--            result set 2 = SUMMARY (by counter ledger) or DETAIL / LEDGER (each counter line)
CREATE OR ALTER PROCEDURE acc.usp_CashBook
    @BranchId       INT,
    @FromDate       DATE,
    @ToDate         DATE,
    @CashLedgerId   INT = NULL,          -- "Ledger Name": NULL = ALL cash ledgers
    @GroupLedgerId  INT = NULL,          -- "Group Ledger": restrict counter ledgers to this group
    @Mode           VARCHAR(10) = 'SUMMARY'   -- SUMMARY (Consolidate) | DETAIL | LEDGER (Ledger Wise)
AS
BEGIN
    SET NOCOUNT ON;
    IF @Mode NOT IN ('SUMMARY','DETAIL','LEDGER') THROW 50010, 'Mode must be SUMMARY, DETAIL or LEDGER.', 1;

    DECLARE @Cash TABLE (LedgerId INT PRIMARY KEY);
    INSERT @Cash (LedgerId)
    SELECT LedgerId FROM acc.Ledger
    WHERE  CashBankType = 'C' AND IsGroup = 0 AND (@CashLedgerId IS NULL OR LedgerId = @CashLedgerId);

    DECLARE @Opening DECIMAL(18,2), @Receipts DECIMAL(18,2), @Payments DECIMAL(18,2);

    SELECT @Opening = ISNULL(SUM(b.OpeningNet), 0)
    FROM   acc.fn_LedgerBalances(@BranchId, @FromDate, @ToDate) b
    JOIN   @Cash c ON c.LedgerId = b.LedgerId;

    SELECT @Receipts = ISNULL(SUM(l.Debit), 0), @Payments = ISNULL(SUM(l.Credit), 0)
    FROM   acc.VoucherLine l
    JOIN   acc.Voucher v ON v.VoucherId = l.VoucherId
    JOIN   @Cash c ON c.LedgerId = l.LedgerId
    WHERE  v.IsCancelled = 0 AND l.BranchId = @BranchId AND v.VoucherDate BETWEEN @FromDate AND @ToDate;

    SELECT @Opening AS OpeningBalance, @Receipts AS TotalReceipts, @Payments AS TotalPayments,
           @Opening + @Receipts - @Payments AS ClosingBalance;

    -- Counter-entries of every cash voucher in the period
    ;WITH CashVouchers AS
    (
        SELECT DISTINCT l.VoucherId
        FROM   acc.VoucherLine l
        JOIN   acc.Voucher v ON v.VoucherId = l.VoucherId
        JOIN   @Cash c ON c.LedgerId = l.LedgerId
        WHERE  v.IsCancelled = 0 AND l.BranchId = @BranchId AND v.VoucherDate BETWEEN @FromDate AND @ToDate
    ),
    Counter AS
    (
        SELECT v.VoucherId, v.VoucherNo, v.VoucherDate, v.PaymentMode, v.ChequeNo, v.Narration AS VoucherNarration,
               l.LedgerId, g.LedgerName, g.ParentLedgerId, l.Narration,
               l.Credit AS Receipt,      -- counter ledger credited => cash received
               l.Debit  AS Payment       -- counter ledger debited  => cash paid
        FROM   CashVouchers cv
        JOIN   acc.Voucher v      ON v.VoucherId = cv.VoucherId
        JOIN   acc.VoucherLine l  ON l.VoucherId = cv.VoucherId
        JOIN   acc.Ledger g       ON g.LedgerId = l.LedgerId
        WHERE  NOT EXISTS (SELECT 1 FROM @Cash c WHERE c.LedgerId = l.LedgerId)
          AND  (@GroupLedgerId IS NULL OR g.ParentLedgerId = @GroupLedgerId)
    )
    SELECT  *
    INTO    #Counter
    FROM    Counter;

    IF @Mode = 'SUMMARY'
        SELECT  LedgerId, LedgerName, SUM(Receipt) AS Receipt, SUM(Payment) AS Payment
        FROM    #Counter
        GROUP BY LedgerId, LedgerName
        ORDER BY LedgerName;
    ELSE
        SELECT  ROW_NUMBER() OVER (PARTITION BY CASE WHEN @Mode = 'LEDGER' THEN LedgerId END ORDER BY VoucherDate, VoucherNo) AS SrNo,
                VoucherDate, VoucherNo, LedgerId, LedgerName AS AccountHead,
                COALESCE(Narration, VoucherNarration) AS Particulars,
                Payment AS Debit, Receipt AS Credit, PaymentMode, ChequeNo
        FROM    #Counter
        ORDER BY CASE WHEN @Mode = 'LEDGER' THEN LedgerName END, VoucherDate, VoucherNo;
END
GO

CREATE OR ALTER PROCEDURE acc.usp_TrialBalance
    @BranchId   INT = NULL,
    @FromDate   DATE,
    @ToDate     DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  ROW_NUMBER() OVER (ORDER BY b.NatureId, b.LedgerName) AS SrNo,
            b.LedgerId, b.LedgerName, n.NatureName,
            b.OpeningNet, b.PeriodDebit, b.PeriodCredit,
            CASE WHEN b.ClosingNet > 0 THEN b.ClosingNet ELSE 0 END  AS Debit,
            CASE WHEN b.ClosingNet < 0 THEN -b.ClosingNet ELSE 0 END AS Credit
    FROM    acc.fn_LedgerBalances(@BranchId, @FromDate, @ToDate) b
    JOIN    acc.AccountNature n ON n.NatureId = b.NatureId
    WHERE   b.ClosingNet <> 0 OR b.PeriodDebit <> 0 OR b.PeriodCredit <> 0
    ORDER BY b.NatureId, b.LedgerName;
END
GO

CREATE OR ALTER PROCEDURE acc.usp_ProfitAndLoss
    @BranchId   INT = NULL,
    @FromDate   DATE,
    @ToDate     DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  b.LedgerId, b.LedgerName,
            CASE WHEN b.NatureId = 4 THEN 'INCOME' ELSE 'EXPENSE' END AS Side,
            CASE WHEN b.NatureId = 4 THEN b.PeriodCredit - b.PeriodDebit
                 ELSE b.PeriodDebit - b.PeriodCredit END              AS Amount
    INTO    #PL
    FROM    acc.fn_LedgerBalances(@BranchId, @FromDate, @ToDate) b
    WHERE   b.ReportHead = 'PL' AND (b.PeriodDebit <> 0 OR b.PeriodCredit <> 0);

    SELECT LedgerId, LedgerName, Side, Amount FROM #PL ORDER BY Side, LedgerName;

    DECLARE @Income DECIMAL(18,2)  = (SELECT ISNULL(SUM(Amount), 0) FROM #PL WHERE Side = 'INCOME');
    DECLARE @Expense DECIMAL(18,2) = (SELECT ISNULL(SUM(Amount), 0) FROM #PL WHERE Side = 'EXPENSE');
    SELECT  @Expense AS TotalExpense, @Income AS TotalIncome,
            CASE WHEN @Income > @Expense THEN @Income - @Expense ELSE 0 END AS NetProfit,
            CASE WHEN @Expense > @Income THEN @Expense - @Income ELSE 0 END AS NetLoss;
END
GO

-- Balance sheet as on @ToDate; P&L to date is carried to the liabilities side.
CREATE OR ALTER PROCEDURE acc.usp_BalanceSheet
    @BranchId   INT = NULL,
    @ToDate     DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  b.LedgerId, b.LedgerName, b.NatureId, b.ReportHead, b.ClosingNet
    INTO    #B
    FROM    acc.fn_LedgerBalances(@BranchId, '19000101', @ToDate) b;

    SELECT  LedgerId, LedgerName,
            CASE WHEN NatureId = 1 THEN 'ASSET' ELSE 'LIABILITY' END AS Side,
            CASE WHEN NatureId = 1 THEN ClosingNet ELSE -ClosingNet END AS Amount
    FROM    #B
    WHERE   ReportHead = 'BS' AND ClosingNet <> 0
    UNION ALL
    SELECT  NULL, N'PROFIT & LOSS A/C', 'LIABILITY', -ISNULL(SUM(ClosingNet), 0)
    FROM    #B
    WHERE   ReportHead = 'PL'
    HAVING  ISNULL(SUM(ClosingNet), 0) <> 0
    ORDER BY Side, LedgerName;
END
GO

-- Ledger statement (account-wise detail with running balance)
CREATE OR ALTER PROCEDURE acc.usp_LedgerStatement
    @BranchId   INT = NULL,
    @LedgerId   INT,
    @FromDate   DATE,
    @ToDate     DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Opening DECIMAL(18,2) =
        (SELECT OpeningNet FROM acc.fn_LedgerBalances(@BranchId, @FromDate, @ToDate) WHERE LedgerId = @LedgerId);

    SELECT ISNULL(@Opening, 0) AS OpeningNet;

    SELECT  v.VoucherDate, v.VoucherNo, v.VoucherType, v.PaymentMode, v.ChequeNo,
            COALESCE(l.Narration, v.Narration) AS Particulars, l.Debit, l.Credit,
            ISNULL(@Opening, 0) + SUM(l.Debit - l.Credit) OVER (ORDER BY v.VoucherDate, v.VoucherId, l.LineNumber
                                                               ROWS UNBOUNDED PRECEDING) AS RunningBalance
    FROM    acc.VoucherLine l
    JOIN    acc.Voucher v ON v.VoucherId = l.VoucherId
    WHERE   l.LedgerId = @LedgerId AND v.IsCancelled = 0
      AND   v.VoucherDate BETWEEN @FromDate AND @ToDate
      AND   (@BranchId IS NULL OR l.BranchId = @BranchId)
    ORDER BY v.VoucherDate, v.VoucherId, l.LineNumber;
END
GO

/* =====================================================================================
   9. LOAN REPORTS
   ===================================================================================== */

-- Loan Approve grid (Approved / Pending)
CREATE OR ALTER PROCEDURE loan.usp_LoanApprovalList
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @Status     VARCHAR(12) = 'PENDING',
    @LoanNo     VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  l.LoanId, l.LoanNo, l.ApplicationDate, m.MemberNo, m.MemberName, m.GuardianName,
            p.PlanCode, l.LoanAmount, l.InterestRate, l.NoOfInstallments, l.EmiAmount,
            e.EmployeeCode, e.EmployeeName, l.Status, l.ApprovedOn
    FROM    loan.LoanApplication l
    JOIN    loan.Member m   ON m.MemberId = l.MemberId
    JOIN    mst.LoanPlan p  ON p.PlanId = l.PlanId
    JOIN    emp.Employee e  ON e.EmployeeId = l.EmployeeId
    WHERE   l.BranchId = @BranchId
      AND   l.Status = @Status
      AND   l.ApplicationDate BETWEEN @FromDate AND @ToDate
      AND   (@LoanNo IS NULL OR l.LoanNo = @LoanNo)
    ORDER BY l.ApplicationDate, l.LoanNo;
END
GO

-- 7.1 Loan Application report: APPLICATION | SANCTION | PAYMENT | WITH_SAVING
CREATE OR ALTER PROCEDURE loan.usp_LoanApplicationReport
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @ReportType VARCHAR(12) = 'APPLICATION',
    @LoanNo     VARCHAR(20) = NULL,
    @MemberNo   VARCHAR(20) = NULL,
    @PlanId     INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  l.LoanNo, l.ApplicationDate, l.ApprovedOn, l.DisbursedOn, l.Status,
            m.MemberNo, m.FileNo, m.MemberName, m.GuardianName, m.MobileNo,
            p.PlanCode, l.LoanAmount, l.InterestRate, l.NoOfInstallments, l.EmiAmount,
            l.ProcessingFee, l.GstAmount, l.ShareAmount, l.InsuranceAmount, l.NetDisbursedAmount,
            l.DisbursementMode, l.ChequeNo, l.SavingAccountNo,
            e.EmployeeCode, e.EmployeeName, g.GroupName
    FROM    loan.LoanApplication l
    JOIN    loan.Member m         ON m.MemberId = l.MemberId
    JOIN    mst.LoanPlan p        ON p.PlanId = l.PlanId
    JOIN    emp.Employee e        ON e.EmployeeId = l.EmployeeId
    LEFT JOIN mst.MemberGroup g   ON g.GroupId = l.GroupId
    WHERE   l.BranchId = @BranchId
      AND   (@LoanNo IS NULL OR l.LoanNo = @LoanNo)
      AND   (@MemberNo IS NULL OR m.MemberNo = @MemberNo)
      AND   (@PlanId IS NULL OR l.PlanId = @PlanId)
      AND   (   (@ReportType = 'APPLICATION' AND l.ApplicationDate BETWEEN @FromDate AND @ToDate)
             OR (@ReportType = 'SANCTION'    AND l.ApprovedOn      BETWEEN @FromDate AND @ToDate)
             OR (@ReportType = 'PAYMENT'     AND l.DisbursedOn     BETWEEN @FromDate AND @ToDate)
             OR (@ReportType = 'WITH_SAVING' AND l.SavingAccountNo IS NOT NULL AND l.ApplicationDate BETWEEN @FromDate AND @ToDate))
    ORDER BY l.LoanNo;
END
GO

-- 7.2 Loan Instalment Reminder / Recovery Due report (also feeds Bulk Recovery grid)
CREATE OR ALTER PROCEDURE loan.usp_RecoveryDueReport
    @BranchId       INT,
    @FromDate       DATE,
    @ToDate         DATE,
    @EmployeeCode   VARCHAR(20) = NULL,
    @GroupId        INT = NULL,
    @EmiMode        CHAR(1) = NULL       -- D / W / F / M
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  l.LoanId, l.LoanNo, l.SavingAccountNo, m.MemberNo, m.MemberName, m.MobileNo,
            s.InstallmentNo, s.DueDate, s.EmiAmount,
            s.EmiAmount - s.PrincipalPaid - s.InterestPaid AS DueAmount,
            l.EmiMode, e.EmployeeCode, e.EmployeeName, g.GroupName
    FROM    loan.EmiSchedule s
    JOIN    loan.LoanApplication l ON l.LoanId = s.LoanId
    JOIN    loan.Member m          ON m.MemberId = l.MemberId
    JOIN    emp.Employee e         ON e.EmployeeId = l.EmployeeId
    LEFT JOIN mst.MemberGroup g    ON g.GroupId = l.GroupId
    WHERE   l.BranchId = @BranchId
      AND   l.Status = 'APPROVED'
      AND   s.Status IN ('DUE','PARTIAL')
      AND   s.DueDate BETWEEN @FromDate AND @ToDate
      AND   (@EmployeeCode IS NULL OR e.EmployeeCode = @EmployeeCode)
      AND   (@GroupId IS NULL OR l.GroupId = @GroupId)
      AND   (@EmiMode IS NULL OR l.EmiMode = @EmiMode)
    ORDER BY e.EmployeeCode, s.DueDate, l.LoanNo;
END
GO

-- 7.3 Employee (Relationship Manager) wise loan report
CREATE OR ALTER PROCEDURE loan.usp_EmployeeWiseLoanReport
    @EmployeeCode   VARCHAR(20) = NULL,
    @EmployeeName   NVARCHAR(100) = NULL,
    @RankId         INT = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  e.EmployeeCode, e.EmployeeName, r.RankCode, b.BranchName,
            COUNT(l.LoanId)                                                 AS TotalLoans,
            SUM(CASE WHEN l.Status = 'APPROVED' THEN 1 ELSE 0 END)          AS ActiveLoans,
            SUM(CASE WHEN l.Status = 'CLOSED' THEN 1 ELSE 0 END)            AS ClosedLoans,
            ISNULL(SUM(CASE WHEN l.Status IN ('APPROVED','CLOSED') THEN l.LoanAmount END), 0) AS DisbursedAmount,
            ISNULL(SUM(CASE WHEN l.Status = 'APPROVED' THEN o.BalancePrincipal END), 0)       AS OutstandingPrincipal
    FROM    emp.Employee e
    JOIN    sec.Branch b                ON b.BranchId = e.BranchId
    LEFT JOIN emp.AdvisorRank r         ON r.RankId = e.CurrentRankId
    LEFT JOIN loan.LoanApplication l    ON l.EmployeeId = e.EmployeeId
    LEFT JOIN loan.vw_LoanOutstanding o ON o.LoanId = l.LoanId
    WHERE   (@EmployeeCode IS NULL OR e.EmployeeCode = @EmployeeCode)
      AND   (@EmployeeName IS NULL OR e.EmployeeName LIKE N'%' + @EmployeeName + N'%')
      AND   (@RankId IS NULL OR e.CurrentRankId = @RankId)
    GROUP BY e.EmployeeCode, e.EmployeeName, r.RankCode, b.BranchName
    ORDER BY e.EmployeeCode;
END
GO

-- 7.4 Loan Recovery ledger (daily recovery list)
CREATE OR ALTER PROCEDURE loan.usp_LoanRecoveryLedger
    @BranchId       INT,
    @FromDate       DATE,
    @ToDate         DATE,
    @EmployeeCode   VARCHAR(20) = NULL,     -- "BDA No"
    @LoanNo         VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  r.TransNo, r.TransDate, l.LoanNo, m.MemberNo, m.MemberName,
            r.InstallmentNo, r.EmiAmount, r.PrincipalAmount, r.InterestAmount, r.LateFeePaid,
            r.ChequeBounceCharges, r.NetAmount, r.PaymentMode, r.ChequeNo, r.ReceiptNo,
            e.EmployeeCode, e.EmployeeName
    FROM    loan.LoanRecovery r
    JOIN    loan.LoanApplication l ON l.LoanId = r.LoanId
    JOIN    loan.Member m          ON m.MemberId = l.MemberId
    LEFT JOIN emp.Employee e       ON e.EmployeeId = r.EmployeeId
    WHERE   r.BranchId = @BranchId
      AND   r.IsReversed = 0
      AND   r.TransDate BETWEEN @FromDate AND @ToDate
      AND   (@EmployeeCode IS NULL OR e.EmployeeCode = @EmployeeCode)
      AND   (@LoanNo IS NULL OR l.LoanNo = @LoanNo)
    ORDER BY r.TransDate, r.TransNo;
END
GO

-- 7.5 Loan closure report
CREATE OR ALTER PROCEDURE loan.usp_LoanClosureReport
    @BranchId   INT,
    @FromDate   DATE,
    @ToDate     DATE,
    @LoanNo     VARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  s.ActionDate AS CloseDate, l.LoanNo, l.ApplicationDate, m.MemberNo, m.MemberName,
            l.LoanAmount, l.InterestRate, l.NoOfInstallments, l.EmiAmount,
            s.TotalPrincipalReceived, s.TotalInterestReceived, s.AmountToBeReceived,
            s.LateFeeReceived, s.PreCloseCharges, s.NetCloseAmount,
            e.EmployeeCode, e.EmployeeName
    FROM    loan.LoanSettlement s
    JOIN    loan.LoanApplication l ON l.LoanId = s.LoanId
    JOIN    loan.Member m          ON m.MemberId = l.MemberId
    JOIN    emp.Employee e         ON e.EmployeeId = l.EmployeeId
    WHERE   l.BranchId = @BranchId
      AND   s.ActionType = 'CLOSE'
      AND   s.ActionDate BETWEEN @FromDate AND @ToDate
      AND   (@LoanNo IS NULL OR l.LoanNo = @LoanNo)
      AND   NOT EXISTS (SELECT 1 FROM loan.LoanSettlement r
                        WHERE r.LoanId = s.LoanId AND r.ActionType = 'REOPEN' AND r.SettlementId > s.SettlementId)
    ORDER BY s.ActionDate, l.LoanNo;
END
GO

-- Advisor "Parent Chain" (introducer hierarchy up to the root)
CREATE OR ALTER PROCEDURE emp.usp_EmployeeParentChain
    @EmployeeId INT
AS
BEGIN
    SET NOCOUNT ON;
    WITH Chain AS
    (
        SELECT e.EmployeeId, e.IntroducerId, 0 AS Level
        FROM   emp.Employee e WHERE e.EmployeeId = @EmployeeId
        UNION ALL
        SELECT p.EmployeeId, p.IntroducerId, c.Level + 1
        FROM   Chain c JOIN emp.Employee p ON p.EmployeeId = c.IntroducerId
        WHERE  c.Level < 50
    )
    SELECT  c.Level, e.EmployeeCode AS AdvisorCode, r.RankCode AS AdvisorRank, e.EmployeeName AS AdvisorName
    FROM    Chain c
    JOIN    emp.Employee e ON e.EmployeeId = c.EmployeeId
    LEFT JOIN emp.AdvisorRank r ON r.RankId = e.CurrentRankId
    ORDER BY c.Level
    OPTION (MAXRECURSION 50);
END
GO

/* =====================================================================================
   10. SEED DATA
   ===================================================================================== */

SET IDENTITY_INSERT sec.Company ON;
INSERT sec.Company (CompanyId, CompanyName, AddressLine, City, PinCode)
VALUES (1, N'DHAN JAGRATI MICROFINANCE FOUNDATION',
        N'PLOT NO:-7 SECOND FLOOR ADAN BAGH EXTENSION-DAYALBAGH', N'AGRA', '282005');
SET IDENTITY_INSERT sec.Company OFF;

SET IDENTITY_INSERT sec.Branch ON;
INSERT sec.Branch (BranchId, CompanyId, BranchCode, BranchName, IsHeadOffice)
VALUES (1, 1, '001', N'HEAD OFFICE', 1),
       (2, 1, '002', N'AGRA', 0);
SET IDENTITY_INSERT sec.Branch OFF;

-- Administrator account. PasswordHash is NULL: the Web API sets the initial password
-- on first start-up (from configuration/user-secrets) and forces a change at first login.
INSERT sec.AppUser (UserName, DisplayName, RoleCode, DefaultBranchId, MustChangePassword)
VALUES (N'SYSTEM', N'SYSTEM (ADMIN)', 'ADMIN', 2, 1);
INSERT sec.UserBranch (UserId, BranchId) SELECT UserId, BranchId FROM sec.AppUser CROSS JOIN sec.Branch WHERE UserName = N'SYSTEM';

INSERT sec.NumberSeries (SeriesKey, BranchId, Prefix, NextNo, PadLength) VALUES
    ('MEMBER',     0, N'M',    1,       9),     -- M000000001
    ('EMPLOYEE',   0, N'FO',   1,       8),     -- FO00000001
    ('LOAN',       1, N'L001', 1,       6),     -- L001000001
    ('LOAN',       2, N'L002', 1,       6),     -- L002000001
    ('RECOVERY',   0, N'LR',   1,       0),     -- LR1
    ('SETTLEMENT', 0, N'LS',   1,       0),
    ('VOUCHER',    0, N'',     1000001, 0),     -- 1000001
    ('RECEIPT',    0, N'R',    1,       0);

INSERT mst.Salutation (SalutationName, IsRelation) VALUES
    (N'MR', 0), (N'MRS', 0), (N'MS', 0), (N'S/O', 1), (N'W/O', 1), (N'D/O', 1);

INSERT mst.Religion (ReligionName) VALUES (N'HINDU'), (N'MUSLIM'), (N'CHRISTIAN'), (N'SIKH'), (N'BUDDHIST'), (N'JAIN'), (N'OTHER');

INSERT mst.Occupation (OccupationName) VALUES
    (N'SELF EMPLOYED'), (N'SALARIED'), (N'BUSINESS'), (N'AGRICULTURE'), (N'DAILY WAGE'), (N'HOUSEWIFE'), (N'STUDENT'), (N'OTHER');

INSERT mst.Relation (RelationName) VALUES
    (N'FATHER'), (N'MOTHER'), (N'HUSBAND'), (N'WIFE'), (N'SON'), (N'DAUGHTER'), (N'BROTHER'), (N'SISTER'), (N'OTHER');

INSERT mst.Designation (DesignationName) VALUES (N'PRESIDENT'), (N'SECRETARY'), (N'TREASURER'), (N'MEMBER');

INSERT mst.Bank (BankName) VALUES
    (N'STATE BANK OF INDIA'), (N'ICICI BANK'), (N'HDFC BANK'), (N'PUNJAB NATIONAL BANK'),
    (N'BANK OF BARODA'), (N'UTKARSH SMALL FINANCE BANK'), (N'OTHER');

INSERT mst.Country (CountryName) VALUES (N'INDIA');

INSERT mst.State (CountryId, StateName)
SELECT 1, v.Name FROM (VALUES
    (N'ANDHRA PRADESH'), (N'ARUNACHAL PRADESH'), (N'ASSAM'), (N'BIHAR'), (N'CHHATTISGARH'), (N'GOA'),
    (N'GUJARAT'), (N'HARYANA'), (N'HIMACHAL PRADESH'), (N'JHARKHAND'), (N'KARNATAKA'), (N'KERALA'),
    (N'MADHYA PRADESH'), (N'MAHARASHTRA'), (N'MANIPUR'), (N'MEGHALAYA'), (N'MIZORAM'), (N'NAGALAND'),
    (N'ODISHA'), (N'PUNJAB'), (N'RAJASTHAN'), (N'SIKKIM'), (N'TAMIL NADU'), (N'TELANGANA'), (N'TRIPURA'),
    (N'UTTAR PRADESH'), (N'UTTARAKHAND'), (N'WEST BENGAL'),
    (N'ANDAMAN AND NICOBAR ISLANDS'), (N'CHANDIGARH'), (N'DADRA AND NAGAR HAVELI AND DAMAN AND DIU'),
    (N'DELHI'), (N'JAMMU AND KASHMIR'), (N'LADAKH'), (N'LAKSHADWEEP'), (N'PUDUCHERRY')) v(Name);

DECLARE @UP INT = (SELECT StateId FROM mst.State WHERE StateName = N'UTTAR PRADESH');
INSERT mst.District (StateId, DistrictName) VALUES (@UP, N'AGRA'), (@UP, N'FIROZABAD'), (@UP, N'MATHURA'), (@UP, N'MEERUT');
INSERT mst.City (StateId, CityName) VALUES (@UP, N'AGRA'), (@UP, N'FIROZABAD'), (@UP, N'MATHURA'), (@UP, N'MEERUT');

INSERT mst.MemberGroup (BranchId, GroupCode, GroupName) VALUES (2, 'SINGLE', N'Single'), (2, 'SHG001', N'SHG001');

INSERT emp.AdvisorRank (RankNo, RankCode, RankName)
SELECT n, CAST(n AS NVARCHAR(5)) + N'-' + CASE WHEN n = 1 THEN N'MEM' ELSE N'R' + CAST(n AS NVARCHAR(5)) END,
       CASE WHEN n = 1 THEN N'MEMBER' ELSE N'RANK ' + CAST(n AS NVARCHAR(5)) END
FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14)) r(n);

INSERT mst.LoanPlan (PlanCode, PlanName, EmiMode, TenureMonths, NoOfInstallments, ProcessingFeePct, LateFeePerInstallment, PreCloseChargesPct) VALUES
    (N'EPL-11', N'Easy Personal Loan - 11 Months', 'M', 11, 11, 3, 4, 1),
    (N'EPL-12', N'Easy Personal Loan - 12 Months', 'M', 12, 12, 3, 4, 1),
    (N'SPL-12', N'Special Loan - 12 Months',       'M', 12, 12, 3, 4, 1);
INSERT mst.InterestRate (PlanId, PlanAmount, RateMale, RateFemale, EffectiveFrom)
SELECT PlanId, CASE WHEN PlanCode = N'SPL-12' THEN 10000 ELSE 5000 END, 41, 41, '20200101' FROM mst.LoanPlan;

INSERT acc.AccountNature (NatureId, NatureCode, NatureName, ReportHead, NormalBalance) VALUES
    (1, 'ASSET',     N'Asset',     'BS', 'D'),
    (2, 'LIABILITY', N'Liability', 'BS', 'C'),
    (3, 'CAPITAL',   N'Capital',   'BS', 'C'),
    (4, 'INCOME',    N'Income',    'PL', 'C'),
    (5, 'EXPENSE',   N'Expense',   'PL', 'D');

-- Primary groups
INSERT acc.Ledger (LedgerName, NatureId, IsGroup, ParentLedgerId, ReportHead, IsDefaultGroup, IsSystem, IsEditable) VALUES
    (N'CAPITAL ACCOUNT',          3, 1, NULL, 'BS', 1, 1, 0),
    (N'RESERVES & SURPLUS',       3, 1, NULL, 'BS', 1, 1, 0),
    (N'LOANS (LIABILITY)',        2, 1, NULL, 'BS', 1, 1, 0),
    (N'CURRENT LIABILITIES',      2, 1, NULL, 'BS', 1, 1, 0),
    (N'DUTIES & TAXES',           2, 1, NULL, 'BS', 1, 1, 0),
    (N'FIXED ASSETS',             1, 1, NULL, 'BS', 1, 1, 0),
    (N'CURRENT ASSETS',           1, 1, NULL, 'BS', 1, 1, 0),
    (N'CASH-IN-HAND',             1, 1, NULL, 'BS', 1, 1, 0),
    (N'BANK ACCOUNTS',            1, 1, NULL, 'BS', 1, 1, 0),
    (N'LOANS & ADVANCES (ASSET)', 1, 1, NULL, 'BS', 1, 1, 0),
    (N'DIRECT INCOMES',           4, 1, NULL, 'PL', 1, 1, 0),
    (N'INDIRECT INCOMES',         4, 1, NULL, 'PL', 1, 1, 0),
    (N'INDIRECT EXPENSES',        5, 1, NULL, 'PL', 1, 1, 0);

-- Core ledgers (names follow the legacy reports)
INSERT acc.Ledger (LedgerName, NatureId, IsGroup, ParentLedgerId, ReportHead, CashBankType, IsSystem)
SELECT v.LedgerName, v.NatureId, 0, g.LedgerId, v.ReportHead, v.CashBankType, v.IsSystem
FROM (VALUES
    (N'CASH A/C',                       1, 'BS', 'C', 1, N'CASH-IN-HAND'),
    (N'LOAN PAYMENT A/C',               1, 'BS', 'N', 1, N'LOANS & ADVANCES (ASSET)'),
    (N'SALARY ADVANCE A/C',             1, 'BS', 'N', 0, N'LOANS & ADVANCES (ASSET)'),
    (N'SECURITY DEPOSIT- RENT A/C',     1, 'BS', 'N', 0, N'LOANS & ADVANCES (ASSET)'),
    (N'COMPUTER A/C',                   1, 'BS', 'N', 0, N'FIXED ASSETS'),
    (N'FURNITURE & FIXTURE A/C',        1, 'BS', 'N', 0, N'FIXED ASSETS'),
    (N'ELECTRICAL EQUIPMENT A/C',       1, 'BS', 'N', 0, N'FIXED ASSETS'),
    (N'CAPITAL A/C',                    3, 'BS', 'N', 0, N'CAPITAL ACCOUNT'),
    (N'RESERVE & SURPLUS A/C',          3, 'BS', 'N', 0, N'RESERVES & SURPLUS'),
    (N'SHARE CAPITAL A/C',              3, 'BS', 'N', 1, N'CAPITAL ACCOUNT'),
    (N'LOAN FROM DIRECTOR A/C',         2, 'BS', 'N', 0, N'LOANS (LIABILITY)'),
    (N'INSURANCE PAYABLE A/C',          2, 'BS', 'N', 1, N'CURRENT LIABILITIES'),
    (N'AUDIT FEE PAYABLE',              2, 'BS', 'N', 0, N'CURRENT LIABILITIES'),
    (N'GST PAYABLE A/C',                2, 'BS', 'N', 1, N'DUTIES & TAXES'),
    (N'TDS PAYABLE',                    2, 'BS', 'N', 0, N'DUTIES & TAXES'),
    (N'PROVISION FOR TAX',              2, 'BS', 'N', 0, N'DUTIES & TAXES'),
    (N'LOAN RECOVER INTEREST A/C',      4, 'PL', 'N', 1, N'DIRECT INCOMES'),
    (N'LOAN PROCESSING FEE A/C',        4, 'PL', 'N', 1, N'DIRECT INCOMES'),
    (N'LATE FEE A/C',                   4, 'PL', 'N', 1, N'DIRECT INCOMES'),
    (N'LOAN PRE CLOSING CHARGES A/C',   4, 'PL', 'N', 1, N'DIRECT INCOMES'),
    (N'CHEQUE BOUNCE CHARGES A/C',      4, 'PL', 'N', 1, N'INDIRECT INCOMES'),
    (N'ADVISOR JOINING FEE A/C',        4, 'PL', 'N', 1, N'INDIRECT INCOMES'),
    (N'SALARY A/C',                     5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'RENT A/C',                       5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'STAFF WELFARE EXPENSES A/C',     5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'OFFICE EXPENSES A/C',            5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'ELECTRICITY EXPENSES A/C',       5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'PRINTING & STATIONERY EXPENSES A/C', 5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'TELEPHONE EXPENSES A/C',         5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'TRAVELLING EXPENSES - STAFF A/C',5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'LEGAL EXPENSES A/C',             5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'BANK CHARGES A/C',               5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'AUDIT FEE',                      5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'DEPRECIATION A/C',               5, 'PL', 'N', 0, N'INDIRECT EXPENSES'),
    (N'INTEREST ON LOAN FROM DIRECTOR', 5, 'PL', 'N', 0, N'INDIRECT EXPENSES')
) v(LedgerName, NatureId, ReportHead, CashBankType, IsSystem, GroupName)
JOIN acc.Ledger g ON g.LedgerName = v.GroupName AND g.IsGroup = 1;

INSERT acc.LedgerMapping (MappingKey, LedgerId, Description)
SELECT v.MappingKey, l.LedgerId, v.Description
FROM (VALUES
    ('CASH',             N'CASH A/C',                     N'Default cash ledger for cash vouchers'),
    ('LOAN_PORTFOLIO',   N'LOAN PAYMENT A/C',             N'Loan principal: Dr on disbursement, Cr on recovery'),
    ('LOAN_INTEREST',    N'LOAN RECOVER INTEREST A/C',    N'Interest portion of EMI'),
    ('PROCESSING_FEE',   N'LOAN PROCESSING FEE A/C',      N'Processing fee deducted at disbursement'),
    ('LATE_FEE',         N'LATE FEE A/C',                 N'Late fee collected'),
    ('PRECLOSE_CHARGES', N'LOAN PRE CLOSING CHARGES A/C', N'Pre-closure charges on settlement'),
    ('BOUNCE_CHARGES',   N'CHEQUE BOUNCE CHARGES A/C',    N'Cheque bounce charges'),
    ('GST_PAYABLE',      N'GST PAYABLE A/C',              N'GST on processing fee'),
    ('INSURANCE',        N'INSURANCE PAYABLE A/C',        N'Insurance deducted at disbursement'),
    ('SHARE_CAPITAL',    N'SHARE CAPITAL A/C',            N'Share amount deducted at disbursement'),
    ('JOINING_FEE',      N'ADVISOR JOINING FEE A/C',      N'Advisor joining fee')
) v(MappingKey, LedgerName, Description)
JOIN acc.Ledger l ON l.LedgerName = v.LedgerName;
GO

SET NOEXEC OFF;
GO
PRINT 'DhanJagrati schema created.';
GO
