/* =====================================================================
   MICROFINANCE LMS - SQL SERVER SCHEMA  (run on an empty database)
   Modules : ref (reference) | sec (users/roles) | cust (customer)
             lon (product, application, loan, schedule, settlement)
             col (payments, penalties, recovery) | gl (accounting)
             aud (audit, notify)
   Conventions
     - BIGINT identity keys on transactional tables, INT on reference tables
     - Money DECIMAL(18,2), rates DECIMAL(9,4), UTC timestamps
     - ROWVERSION column (RowVer) = optimistic concurrency token for screens
     - Soft delete (IsDeleted) on customer data, never hard delete
     - Status columns use CHECK constraints (map to enums in .NET)
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF DB_ID('DhanJagrati_MFI') IS NULL CREATE DATABASE DhanJagrati_MFI;
GO
USE DhanJagrati_MFI;
GO
CREATE SCHEMA ref;
GO
CREATE SCHEMA sec;
GO
CREATE SCHEMA cust;
GO
CREATE SCHEMA lon;
GO
CREATE SCHEMA col;
GO
CREATE SCHEMA gl;
GO
CREATE SCHEMA aud;
GO

/* ---------- Sequences for business numbers ---------- */
CREATE SEQUENCE ref.SeqCustomerNo    AS BIGINT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE ref.SeqApplicationNo AS BIGINT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE ref.SeqLoanNo        AS BIGINT START WITH 1 INCREMENT BY 1;
CREATE SEQUENCE ref.SeqReceiptNo     AS BIGINT START WITH 1 INCREMENT BY 1;
GO

/* =====================================================================
   REFERENCE
   ===================================================================== */
CREATE TABLE ref.Branch (
    BranchId     INT IDENTITY(1,1) PRIMARY KEY,
    BranchCode   VARCHAR(10)   NOT NULL UNIQUE,
    BranchName   NVARCHAR(100) NOT NULL,
    AddressLine  NVARCHAR(250) NULL,
    City         NVARCHAR(80)  NULL,
    Phone        VARCHAR(20)   NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CreatedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE ref.Company (
    CompanyId    INT IDENTITY(1,1) PRIMARY KEY,
    CompanyName  NVARCHAR(150) NOT NULL,
    AddressLine  NVARCHAR(250) NULL,
    City         NVARCHAR(80) NULL,
    State        NVARCHAR(80) NULL,
    PostalCode   VARCHAR(10) NULL,
    Phone        VARCHAR(20) NULL,
    Email        NVARCHAR(150) NULL,
    LogoPath     NVARCHAR(300) NULL,
    IsActive     BIT NOT NULL DEFAULT 1
);

CREATE TABLE ref.Country (
    CountryId   INT IDENTITY(1,1) PRIMARY KEY,
    CountryName NVARCHAR(100) NOT NULL UNIQUE,
    CountryCode VARCHAR(3) NULL,
    IsActive    BIT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UX_Country_Code ON ref.Country(CountryCode) WHERE CountryCode IS NOT NULL;
CREATE TABLE ref.State (
    StateId   INT IDENTITY(1,1) PRIMARY KEY,
    CountryId INT NOT NULL REFERENCES ref.Country(CountryId),
    StateName NVARCHAR(100) NOT NULL,
    IsActive  BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_State_Name UNIQUE (CountryId, StateName)
);
CREATE TABLE ref.District (
    DistrictId   INT IDENTITY(1,1) PRIMARY KEY,
    StateId      INT NOT NULL REFERENCES ref.State(StateId),
    DistrictName NVARCHAR(100) NOT NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_District_Name UNIQUE (StateId, DistrictName)
);
CREATE TABLE ref.City (
    CityId     INT IDENTITY(1,1) PRIMARY KEY,
    DistrictId INT NULL REFERENCES ref.District(DistrictId),
    StateId    INT NULL REFERENCES ref.State(StateId),
    CityName   NVARCHAR(100) NOT NULL,
    IsActive   BIT NOT NULL DEFAULT 1
);
CREATE INDEX IX_City_Name ON ref.City(CityName);
CREATE TABLE ref.Tehsil (
    TehsilId   INT IDENTITY(1,1) PRIMARY KEY,
    DistrictId INT NOT NULL REFERENCES ref.District(DistrictId),
    TehsilName NVARCHAR(100) NOT NULL,
    IsActive   BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Tehsil_Name UNIQUE (DistrictId, TehsilName)
);
CREATE TABLE ref.Village (
    VillageId   INT IDENTITY(1,1) PRIMARY KEY,
    TehsilId    INT NOT NULL REFERENCES ref.Tehsil(TehsilId),
    VillageName NVARCHAR(100) NOT NULL,
    IsActive    BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Village_Name UNIQUE (TehsilId, VillageName)
);
CREATE TABLE ref.Khasra (
    KhasraId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    VillageId   INT NOT NULL REFERENCES ref.Village(VillageId),
    KhasraNo    NVARCHAR(40) NOT NULL,
    TotalArea   DECIMAL(18,4) NULL CHECK (TotalArea >= 0),
    AreaUnit    VARCHAR(20) NULL,
    IsActive    BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Khasra_Village UNIQUE (VillageId, KhasraNo)
);

/* One generic lookup table: GENDER, MARITAL_STATUS, OCCUPATION, ID_TYPE,
   LOAN_PURPOSE, DOC_TYPE, RELATION, COLLATERAL_TYPE, FOLLOWUP_OUTCOME,
   SALUTATION, RELIGION, DESIGNATION */
CREATE TABLE ref.Lookup (
    LookupId  INT IDENTITY(1,1) PRIMARY KEY,
    Category  VARCHAR(40)   NOT NULL,
    Code      VARCHAR(40)   NOT NULL,
    Name      NVARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    IsActive  BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Lookup UNIQUE (Category, Code)
);

CREATE TABLE ref.Holiday (
    HolidayId   INT IDENTITY(1,1) PRIMARY KEY,
    HolidayDate DATE NOT NULL,
    BranchId    INT NULL REFERENCES ref.Branch(BranchId),   -- NULL = all branches
    Description NVARCHAR(100) NULL
);
CREATE UNIQUE INDEX UX_Holiday ON ref.Holiday(HolidayDate, BranchId);

/* =====================================================================
   SECURITY
   ===================================================================== */
CREATE TABLE sec.AppUser (
    UserId       INT IDENTITY(1,1) PRIMARY KEY,
    Username     NVARCHAR(60)  NOT NULL UNIQUE,
    FullName     NVARCHAR(120) NOT NULL,
    Email        NVARCHAR(150) NULL,
    Phone        VARCHAR(20)   NULL,
    PasswordHash NVARCHAR(500) NULL,            -- or external IdP (Entra ID / Identity)
    BranchId     INT NULL REFERENCES ref.Branch(BranchId),
    PasswordChangedAt DATETIME2(0) NULL,
    IsActive     BIT NOT NULL DEFAULT 1,
    LastLoginAt  DATETIME2(0) NULL,
    CreatedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVer       ROWVERSION NOT NULL
);
CREATE TABLE sec.UserBranch (
    UserId   INT NOT NULL REFERENCES sec.AppUser(UserId),
    BranchId INT NOT NULL REFERENCES ref.Branch(BranchId),
    IsDefault BIT NOT NULL DEFAULT 0,
    IsActive  BIT NOT NULL DEFAULT 1,
    PRIMARY KEY (UserId, BranchId)
);
CREATE TABLE sec.UserSession (
    SessionId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWSEQUENTIALID() PRIMARY KEY,
    UserId          INT NOT NULL REFERENCES sec.AppUser(UserId),
    BranchId        INT NOT NULL REFERENCES ref.Branch(BranchId),
    WorkingDate     DATE NOT NULL,
    LoggedInAt      DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    LoggedOutAt     DATETIME2(0) NULL
);
CREATE TABLE sec.Role (
    RoleId   INT IDENTITY(1,1) PRIMARY KEY,
    RoleCode VARCHAR(30)   NOT NULL UNIQUE,
    RoleName NVARCHAR(80)  NOT NULL
);
CREATE TABLE sec.UserRole (
    UserId INT NOT NULL REFERENCES sec.AppUser(UserId),
    RoleId INT NOT NULL REFERENCES sec.Role(RoleId),
    PRIMARY KEY (UserId, RoleId)
);
CREATE TABLE sec.Employee (
    EmployeeId      INT IDENTITY(1,1) PRIMARY KEY,
    EmployeeCode    VARCHAR(20) NOT NULL UNIQUE,
    UserId          INT NULL REFERENCES sec.AppUser(UserId),
    EmployeeName    NVARCHAR(120) NOT NULL,
    BranchId        INT NOT NULL REFERENCES ref.Branch(BranchId),
    DesignationId   INT NULL REFERENCES ref.Lookup(LookupId),
    JoiningDate     DATE NOT NULL,
    IntroductoryRank NVARCHAR(40) NULL,
    InitialRank     NVARCHAR(40) NULL,
    CurrentRank     NVARCHAR(40) NULL,
    JoiningFee      DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (JoiningFee >= 0),
    Status          VARCHAR(12) NOT NULL DEFAULT 'ACTIVE' CHECK (Status IN ('ACTIVE','INACTIVE','LEFT')),
    IsDeleted       BIT NOT NULL DEFAULT 0,
    CreatedAt       DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_Employee_User ON sec.Employee(UserId) WHERE UserId IS NOT NULL;
CREATE TABLE sec.EmployeeIntroducer (
    EmployeeId    INT NOT NULL REFERENCES sec.Employee(EmployeeId),
    IntroducerId  INT NOT NULL REFERENCES sec.Employee(EmployeeId),
    EffectiveFrom DATE NOT NULL,
    EffectiveTo   DATE NULL,
    PRIMARY KEY (EmployeeId, EffectiveFrom),
    CHECK (EmployeeId <> IntroducerId),
    CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);
CREATE TABLE sec.EmployeeRankHistory (
    RankHistoryId BIGINT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId    INT NOT NULL REFERENCES sec.Employee(EmployeeId),
    RankName      NVARCHAR(40) NOT NULL,
    EffectiveFrom DATE NOT NULL,
    EffectiveTo   DATE NULL,
    ChangedBy     INT NULL REFERENCES sec.AppUser(UserId)
);
CREATE INDEX IX_EmployeeRankHistory_Employee ON sec.EmployeeRankHistory(EmployeeId, EffectiveFrom);

CREATE TABLE ref.Center (
    CenterId   INT IDENTITY(1,1) PRIMARY KEY,
    CenterCode VARCHAR(20) NOT NULL UNIQUE,
    CenterName NVARCHAR(120) NOT NULL,
    BranchId   INT NOT NULL REFERENCES ref.Branch(BranchId),
    VillageId  INT NULL REFERENCES ref.Village(VillageId),
    MeetingDay TINYINT NULL CHECK (MeetingDay BETWEEN 1 AND 7),
    IsActive   BIT NOT NULL DEFAULT 1
);
CREATE TABLE ref.Dealer (
    DealerId    INT IDENTITY(1,1) PRIMARY KEY,
    DealerCode  VARCHAR(20) NULL,
    DealerName  NVARCHAR(120) NOT NULL,
    Phone       VARCHAR(20) NULL,
    AddressLine NVARCHAR(250) NULL,
    BranchId    INT NULL REFERENCES ref.Branch(BranchId),
    IsActive    BIT NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UX_Dealer_Code ON ref.Dealer(DealerCode) WHERE DealerCode IS NOT NULL;
CREATE TABLE ref.Vehicle (
    VehicleId       INT IDENTITY(1,1) PRIMARY KEY,
    RegistrationNo  VARCHAR(20) NOT NULL UNIQUE,
    VehicleType     NVARCHAR(50) NULL,
    MakeModel       NVARCHAR(100) NULL,
    DealerId        INT NULL REFERENCES ref.Dealer(DealerId),
    AssignedEmployeeId INT NULL REFERENCES sec.Employee(EmployeeId),
    IsActive        BIT NOT NULL DEFAULT 1
);
CREATE TABLE sec.EmployeeAttendance (
    AttendanceId BIGINT IDENTITY(1,1) PRIMARY KEY,
    EmployeeId   INT NOT NULL REFERENCES sec.Employee(EmployeeId),
    AttendanceDate DATE NOT NULL,
    Status       VARCHAR(10) NOT NULL CHECK (Status IN ('PRESENT','ABSENT','LEAVE','HOLIDAY')),
    CheckInAt    DATETIME2(0) NULL,
    CheckOutAt   DATETIME2(0) NULL,
    Remarks      NVARCHAR(250) NULL,
    RecordedBy   INT NULL REFERENCES sec.AppUser(UserId),
    CONSTRAINT UQ_EmployeeAttendance UNIQUE (EmployeeId, AttendanceDate)
);
CREATE TABLE sec.UserDateClose (
    DateCloseId BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId      INT NOT NULL REFERENCES sec.AppUser(UserId),
    BranchId    INT NOT NULL REFERENCES ref.Branch(BranchId),
    ClosedDate  DATE NOT NULL,
    ClosedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsReopened  BIT NOT NULL DEFAULT 0,
    ReopenedAt  DATETIME2(0) NULL,
    ReopenedBy  INT NULL REFERENCES sec.AppUser(UserId),
    CONSTRAINT UQ_UserDateClose UNIQUE (UserId, BranchId, ClosedDate)
);

/* =====================================================================
   CUSTOMER
   ===================================================================== */
CREATE TABLE cust.Customer (
    CustomerId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerNo        VARCHAR(20)  NOT NULL UNIQUE,
    FirstName         NVARCHAR(80) NOT NULL,
    MiddleName        NVARCHAR(80) NULL,
    LastName          NVARCHAR(80) NULL,
    FullName          AS (LTRIM(RTRIM(FirstName + ISNULL(' ' + MiddleName,'') + ISNULL(' ' + LastName,'')))) PERSISTED,
    FileNo            VARCHAR(30) NULL,
    RegistrationDate  DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    SalutationId      INT NULL REFERENCES ref.Lookup(LookupId),
    DateOfBirth       DATE NOT NULL,
    GenderId          INT NULL REFERENCES ref.Lookup(LookupId),
    MaritalStatusId   INT NULL REFERENCES ref.Lookup(LookupId),
    GuardianName      NVARCHAR(120) NULL,                    -- father / spouse
    MotherName        NVARCHAR(120) NULL,
    ReligionId        INT NULL REFERENCES ref.Lookup(LookupId),
    PrimaryPhone      VARCHAR(20)   NOT NULL,
    AlternatePhone    VARCHAR(20)   NULL,
    Email             NVARCHAR(150) NULL,
    OccupationId      INT NULL REFERENCES ref.Lookup(LookupId),
    EmployerOrBusiness NVARCHAR(150) NULL,
    MonthlyIncome     DECIMAL(18,2) NULL CHECK (MonthlyIncome  >= 0),
    MonthlyExpense    DECIMAL(18,2) NULL CHECK (MonthlyExpense >= 0),
    HouseholdSize     TINYINT NULL,
    CreditScore       SMALLINT NULL,
    RiskCategory      VARCHAR(10) NULL CHECK (RiskCategory IN ('LOW','MEDIUM','HIGH')),
    KycStatus         VARCHAR(12) NOT NULL DEFAULT 'PENDING'  CHECK (KycStatus IN ('PENDING','VERIFIED','REJECTED')),
    Status            VARCHAR(12) NOT NULL DEFAULT 'ACTIVE'   CHECK (Status IN ('ACTIVE','INACTIVE','BLACKLISTED','DECEASED')),
    BranchId          INT NOT NULL REFERENCES ref.Branch(BranchId),
    RelationshipOfficerId INT NULL REFERENCES sec.AppUser(UserId),
    PhotoPath         NVARCHAR(300) NULL,
    SignaturePath     NVARCHAR(300) NULL,
    SeniorCitizen     BIT NOT NULL DEFAULT 0,
    Handicapped       BIT NOT NULL DEFAULT 0,
    SpecialCategory   BIT NOT NULL DEFAULT 0,
    Remarks           NVARCHAR(500) NULL,
    IsDeleted         BIT NOT NULL DEFAULT 0,
    CreatedAt         DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy         INT NULL,
    UpdatedAt         DATETIME2(0) NULL,
    UpdatedBy         INT NULL,
    RowVer            ROWVERSION NOT NULL
);
CREATE UNIQUE INDEX UX_Customer_FileNo ON cust.Customer(FileNo) WHERE FileNo IS NOT NULL;
ALTER TABLE sec.Employee
    ADD CustomerId BIGINT NULL REFERENCES cust.Customer(CustomerId);
CREATE INDEX IX_Customer_Name   ON cust.Customer(FullName) INCLUDE (CustomerNo, PrimaryPhone, Status);
CREATE INDEX IX_Customer_Phone  ON cust.Customer(PrimaryPhone);
CREATE INDEX IX_Customer_Branch ON cust.Customer(BranchId, Status) WHERE IsDeleted = 0;

CREATE TABLE cust.CustomerAddress (
    AddressId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    AddressType VARCHAR(12) NOT NULL CHECK (AddressType IN ('RESIDENTIAL','PERMANENT','BUSINESS')),
    Line1       NVARCHAR(150) NOT NULL,
    Line2       NVARCHAR(150) NULL,
    Landmark    NVARCHAR(100) NULL,
    City        NVARCHAR(80)  NULL,
    District    NVARCHAR(80)  NULL,
    State       NVARCHAR(80)  NULL,
    CountryId   INT NULL REFERENCES ref.Country(CountryId),
    StateId     INT NULL REFERENCES ref.State(StateId),
    DistrictId  INT NULL REFERENCES ref.District(DistrictId),
    CityId      INT NULL REFERENCES ref.City(CityId),
    PostalCode  VARCHAR(10)   NULL,
    Latitude    DECIMAL(9,6)  NULL,
    Longitude   DECIMAL(9,6)  NULL,
    IsPrimary   BIT NOT NULL DEFAULT 0,
    IsDeleted   BIT NOT NULL DEFAULT 0,
    CreatedAt   DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt   DATETIME2(0) NULL
);
CREATE INDEX IX_CustAddr_Customer ON cust.CustomerAddress(CustomerId) WHERE IsDeleted = 0;

CREATE TABLE cust.CustomerIdentity (
    IdentityId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId   BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    IdTypeId     INT NOT NULL REFERENCES ref.Lookup(LookupId),
    IdNumber     VARCHAR(40) NOT NULL,                  -- encrypt / mask Aadhaar-type numbers at app layer
    IssueDate    DATE NULL,
    ExpiryDate   DATE NULL,
    IsVerified   BIT NOT NULL DEFAULT 0,
    VerifiedBy   INT NULL,
    VerifiedAt   DATETIME2(0) NULL,
    DocumentPath NVARCHAR(300) NULL,
    IsDeleted    BIT NOT NULL DEFAULT 0,
    CreatedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_CustIdentity ON cust.CustomerIdentity(IdTypeId, IdNumber) WHERE IsDeleted = 0;  -- duplicate-customer guard
CREATE INDEX IX_CustIdentity_Customer ON cust.CustomerIdentity(CustomerId);

CREATE TABLE cust.CustomerBankAccount (
    BankAccountId BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId    BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    BankName      NVARCHAR(100) NOT NULL,
    AccountHolder NVARCHAR(120) NOT NULL,
    AccountNumber VARCHAR(40)   NOT NULL,
    IfscOrRouting VARCHAR(20)   NULL,
    PanNumber     VARCHAR(20)   NULL,
    UpiId         NVARCHAR(80)  NULL,
    IsPrimary     BIT NOT NULL DEFAULT 0,
    IsDeleted     BIT NOT NULL DEFAULT 0
);
CREATE INDEX IX_CustBank_Customer ON cust.CustomerBankAccount(CustomerId);

CREATE TABLE cust.CustomerContactPerson (              -- nominee / reference / emergency contact
    ContactId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId  BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    ContactType VARCHAR(12) NOT NULL CHECK (ContactType IN ('NOMINEE','REFERENCE','EMERGENCY')),
    FullName    NVARCHAR(120) NOT NULL,
    RelationId  INT NULL REFERENCES ref.Lookup(LookupId),
    DateOfBirth DATE NULL,
    GuardianName NVARCHAR(120) NULL,
    SameAsCustomerAddress BIT NOT NULL DEFAULT 0,
    ReferenceCustomerId BIGINT NULL REFERENCES cust.Customer(CustomerId),
    Phone       VARCHAR(20) NULL,
    Address     NVARCHAR(250) NULL,
    IsDeleted   BIT NOT NULL DEFAULT 0
);
CREATE INDEX IX_CustContact_Customer ON cust.CustomerContactPerson(CustomerId);

CREATE TABLE cust.CustomerGroup (                      -- joint-liability / self-help group
    GroupId          BIGINT IDENTITY(1,1) PRIMARY KEY,
    GroupNo          VARCHAR(20)   NOT NULL UNIQUE,
    GroupName        NVARCHAR(120) NOT NULL,
    BranchId         INT NOT NULL REFERENCES ref.Branch(BranchId),
    CenterId         INT NULL REFERENCES ref.Center(CenterId),
    LoanOfficerId    INT NULL REFERENCES sec.AppUser(UserId),
    MeetingDay       TINYINT NULL CHECK (MeetingDay BETWEEN 1 AND 7),
    LeaderCustomerId BIGINT NULL REFERENCES cust.Customer(CustomerId),
    Status           VARCHAR(10) NOT NULL DEFAULT 'ACTIVE' CHECK (Status IN ('ACTIVE','INACTIVE')),
    CreatedAt        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE TABLE cust.GroupMember (
    GroupId    BIGINT NOT NULL REFERENCES cust.CustomerGroup(GroupId),
    CustomerId BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    JoinedOn   DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    DesignationId INT NULL REFERENCES ref.Lookup(LookupId),
    LeftOn     DATE NULL,
    PRIMARY KEY (GroupId, CustomerId)
);

CREATE TABLE cust.SavingsAccount (
    SavingsAccountId BIGINT IDENTITY(1,1) PRIMARY KEY,
    AccountNo        VARCHAR(25) NOT NULL UNIQUE,
    CustomerId       BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    BranchId         INT NOT NULL REFERENCES ref.Branch(BranchId),
    OpenedDate       DATE NOT NULL,
    Status           VARCHAR(10) NOT NULL DEFAULT 'ACTIVE' CHECK (Status IN ('ACTIVE','CLOSED','FROZEN')),
    Balance          DECIMAL(18,2) NOT NULL DEFAULT 0,
    IsDeleted        BIT NOT NULL DEFAULT 0,
    CreatedAt        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_SavingsAccount_Customer ON cust.SavingsAccount(CustomerId, Status);
CREATE TABLE cust.SavingsTransaction (
    SavingsTransactionId BIGINT IDENTITY(1,1) PRIMARY KEY,
    SavingsAccountId     BIGINT NOT NULL REFERENCES cust.SavingsAccount(SavingsAccountId),
    TransactionDate      DATE NOT NULL,
    TransactionType      VARCHAR(20) NOT NULL CHECK (TransactionType IN ('DEPOSIT','WITHDRAWAL','LOAN_TRANSFER','ADJUSTMENT')),
    Amount               DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
    Reference             NVARCHAR(60) NULL,
    Remarks               NVARCHAR(300) NULL,
    LoanId                BIGINT NULL,
    CreatedBy             INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt             DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);

/* =====================================================================
   LOAN PRODUCT  (drives the calculator & policy; all rates are data, not code)
   ===================================================================== */
CREATE TABLE lon.InterestRatePlan (
    InterestRatePlanId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PlanCode           VARCHAR(30) NOT NULL,
    LoanAmount         DECIMAL(18,2) NOT NULL CHECK (LoanAmount >= 0),
    MaleRatePct        DECIMAL(9,4) NOT NULL CHECK (MaleRatePct >= 0),
    FemaleRatePct      DECIMAL(9,4) NOT NULL CHECK (FemaleRatePct >= 0),
    EffectiveFrom      DATE NOT NULL,
    EffectiveTo        DATE NULL,
    IsActive           BIT NOT NULL DEFAULT 1,
    CreatedAt          DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_InterestRatePlan UNIQUE (PlanCode, EffectiveFrom),
    CHECK (EffectiveTo IS NULL OR EffectiveTo >= EffectiveFrom)
);

CREATE TABLE lon.LoanProduct (
    ProductId            INT IDENTITY(1,1) PRIMARY KEY,
    ProductCode          VARCHAR(20)   NOT NULL UNIQUE,
    ProductName          NVARCHAR(100) NOT NULL,
    InterestRatePlanId   BIGINT NULL REFERENCES lon.InterestRatePlan(InterestRatePlanId),
    InterestMethod       VARCHAR(10) NOT NULL CHECK (InterestMethod IN ('FLAT','REDUCING')),
    Frequency            VARCHAR(10) NOT NULL CHECK (Frequency IN ('WEEKLY','BIWEEKLY','MONTHLY')),
    MinAmount            DECIMAL(18,2) NOT NULL,
    MaxAmount            DECIMAL(18,2) NOT NULL,
    MinInstallments      INT NOT NULL,
    MaxInstallments      INT NOT NULL,
    DefaultRatePct       DECIMAL(9,4) NOT NULL,           -- annual
    MinRatePct           DECIMAL(9,4) NOT NULL,
    MaxRatePct           DECIMAL(9,4) NOT NULL,
    ProcessingFeePct     DECIMAL(9,4) NOT NULL DEFAULT 0,
    ProcessingFeeFlat    DECIMAL(18,2) NOT NULL DEFAULT 0,
    InsurancePct         DECIMAL(9,4) NOT NULL DEFAULT 0,
    PenaltyRatePct       DECIMAL(9,4) NOT NULL DEFAULT 0, -- annual, on overdue amount, accrued daily
    PenaltyGraceDays     INT NOT NULL DEFAULT 0,
    ForeclosureChargePct DECIMAL(9,4) NOT NULL DEFAULT 0, -- on principal outstanding
    MinAge               TINYINT NOT NULL DEFAULT 18,
    MaxAge               TINYINT NOT NULL DEFAULT 65,
    RequiresGuarantor    BIT NOT NULL DEFAULT 0,
    RequiresCollateral   BIT NOT NULL DEFAULT 0,
    IsGroupLoan          BIT NOT NULL DEFAULT 0,
    IsActive             BIT NOT NULL DEFAULT 1,
    CreatedAt            DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Product_Ranges CHECK (MinAmount <= MaxAmount AND MinInstallments <= MaxInstallments AND MinRatePct <= MaxRatePct)
);

/* =====================================================================
   LOAN APPLICATION
   ===================================================================== */
CREATE TABLE lon.LoanApplication (
    ApplicationId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationNo        VARCHAR(20) NOT NULL UNIQUE,
    CustomerId           BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    ProductId            INT    NOT NULL REFERENCES lon.LoanProduct(ProductId),
    InterestRatePlanId   BIGINT NULL REFERENCES lon.InterestRatePlan(InterestRatePlanId),
    SavingsAccountId     BIGINT NULL REFERENCES cust.SavingsAccount(SavingsAccountId),
    BranchId             INT    NOT NULL REFERENCES ref.Branch(BranchId),
    GroupId              BIGINT NULL REFERENCES cust.CustomerGroup(GroupId),
    EmployeeId           INT NULL REFERENCES sec.Employee(EmployeeId),
    ApplicationDate      DATE   NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    FirstInstallmentDate DATE NULL,
    PurposeId            INT NULL REFERENCES ref.Lookup(LookupId),
    PurposeDescription   NVARCHAR(250) NULL,
    RequestedAmount      DECIMAL(18,2) NOT NULL CHECK (RequestedAmount > 0),
    RequestedInstallments INT NOT NULL CHECK (RequestedInstallments > 0),
    TermYears            SMALLINT NULL CHECK (TermYears >= 0),
    TermMonths           SMALLINT NULL CHECK (TermMonths >= 0),
    Frequency            VARCHAR(10) NOT NULL CHECK (Frequency IN ('WEEKLY','BIWEEKLY','MONTHLY')),
    InterestMethod       VARCHAR(10) NOT NULL CHECK (InterestMethod IN ('FLAT','REDUCING')),
    InterestRatePct      DECIMAL(9,4) NOT NULL,           -- snapshot from product at submission
    ProposedInstallmentAmount DECIMAL(18,2) NULL,
    ProcessingFeePct     DECIMAL(9,4) NULL,
    ProcessingFee        DECIMAL(18,2) NOT NULL DEFAULT 0,
    InsuranceFee         DECIMAL(18,2) NOT NULL DEFAULT 0,
    GstAmount            DECIMAL(18,2) NOT NULL DEFAULT 0,
    ShareAmount          DECIMAL(18,2) NOT NULL DEFAULT 0,
    DisbursementMode     VARCHAR(20) NULL CHECK (DisbursementMode IN ('CASH','BANK_TRANSFER','UPI','CHEQUE','NEFT','SAVINGS_TRANSFER')),
    DisbursementBankAccountId BIGINT NULL REFERENCES cust.CustomerBankAccount(BankAccountId),
    ChequeNo             VARCHAR(30) NULL,
    ChequeDate           DATE NULL,
    ChequeAmount         DECIMAL(18,2) NULL,
    BankName             NVARCHAR(100) NULL,
    TransactionNo        NVARCHAR(60) NULL,
    MonthlyIncome        DECIMAL(18,2) NULL,              -- snapshot for credit decision
    MonthlyObligations   DECIMAL(18,2) NULL,
    Status               VARCHAR(12) NOT NULL DEFAULT 'DRAFT'
                         CHECK (Status IN ('DRAFT','SUBMITTED','UNDER_REVIEW','APPROVED','REJECTED','CANCELLED','DISBURSED')),
    ApprovedAmount       DECIMAL(18,2) NULL,
    ApprovedInstallments INT NULL,
    ApprovedRatePct      DECIMAL(9,4) NULL,
    ApprovedAt           DATETIME2(0) NULL,
    ApprovedBy           INT NULL,
    RejectionReason      NVARCHAR(300) NULL,
    LoanOfficerId        INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt            DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy            INT NULL,
    UpdatedAt            DATETIME2(0) NULL,
    UpdatedBy            INT NULL,
    RowVer               ROWVERSION NOT NULL
);
CREATE INDEX IX_App_Customer ON lon.LoanApplication(CustomerId);
CREATE INDEX IX_App_Queue    ON lon.LoanApplication(BranchId, Status, ApplicationDate);

CREATE TABLE lon.ApplicationStatusHistory (
    HistoryId     BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId BIGINT NOT NULL REFERENCES lon.LoanApplication(ApplicationId),
    FromStatus    VARCHAR(12) NULL,
    ToStatus      VARCHAR(12) NOT NULL,
    Comments      NVARCHAR(300) NULL,
    ChangedBy     INT NULL,
    ChangedAt     DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_AppHist_App ON lon.ApplicationStatusHistory(ApplicationId);

CREATE TABLE lon.ApplicationApproval (                -- multi-level maker/checker
    ApprovalId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId BIGINT NOT NULL REFERENCES lon.LoanApplication(ApplicationId),
    LevelNo       TINYINT NOT NULL,
    ApproverId    INT NOT NULL REFERENCES sec.AppUser(UserId),
    Decision      VARCHAR(10) NOT NULL CHECK (Decision IN ('APPROVED','REJECTED','SENT_BACK')),
    Comments      NVARCHAR(300) NULL,
    DecidedAt     DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_AppApproval_App ON lon.ApplicationApproval(ApplicationId);

CREATE TABLE lon.ApplicationDocument (
    DocumentId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId BIGINT NOT NULL REFERENCES lon.LoanApplication(ApplicationId),
    DocTypeId     INT NOT NULL REFERENCES ref.Lookup(LookupId),
    FilePath      NVARCHAR(300) NOT NULL,              -- blob storage path
    IsVerified    BIT NOT NULL DEFAULT 0,
    UploadedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_AppDoc_App ON lon.ApplicationDocument(ApplicationId);

CREATE TABLE lon.Guarantor (
    GuarantorId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId BIGINT NOT NULL REFERENCES lon.LoanApplication(ApplicationId),
    CustomerId    BIGINT NULL REFERENCES cust.Customer(CustomerId),   -- existing customer, or...
    FullName      NVARCHAR(120) NOT NULL,                              -- ...external person
    Phone         VARCHAR(20) NULL,
    RelationId    INT NULL REFERENCES ref.Lookup(LookupId),
    IdTypeId      INT NULL REFERENCES ref.Lookup(LookupId),
    IdNumber      VARCHAR(40) NULL,
    Address       NVARCHAR(250) NULL,
    MonthlyIncome DECIMAL(18,2) NULL
);
CREATE INDEX IX_Guarantor_App ON lon.Guarantor(ApplicationId);

CREATE TABLE lon.Collateral (
    CollateralId   BIGINT IDENTITY(1,1) PRIMARY KEY,
    ApplicationId  BIGINT NOT NULL REFERENCES lon.LoanApplication(ApplicationId),
    CollateralTypeId INT NULL REFERENCES ref.Lookup(LookupId),
    Description    NVARCHAR(300) NOT NULL,
    EstimatedValue DECIMAL(18,2) NULL,
    DocumentRef    NVARCHAR(100) NULL,
    ReleasedOn     DATE NULL
);
CREATE INDEX IX_Collateral_App ON lon.Collateral(ApplicationId);

/* =====================================================================
   LOAN, SCHEDULE, DISBURSEMENT
   ===================================================================== */
CREATE TABLE lon.Loan (
    LoanId               BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanAccountNo        VARCHAR(20) NOT NULL UNIQUE,
    ApplicationId        BIGINT NOT NULL UNIQUE REFERENCES lon.LoanApplication(ApplicationId),
    CustomerId           BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    EmployeeId           INT NULL REFERENCES sec.Employee(EmployeeId),
    ProductId            INT    NOT NULL REFERENCES lon.LoanProduct(ProductId),
    BranchId             INT    NOT NULL REFERENCES ref.Branch(BranchId),
    PrincipalAmount      DECIMAL(18,2) NOT NULL,
    InterestRatePct      DECIMAL(9,4)  NOT NULL,
    InterestMethod       VARCHAR(10)   NOT NULL CHECK (InterestMethod IN ('FLAT','REDUCING')),
    Frequency            VARCHAR(10)   NOT NULL CHECK (Frequency IN ('WEEKLY','BIWEEKLY','MONTHLY')),
    NumberOfInstallments INT NOT NULL,
    TermYears            SMALLINT NULL,
    TermMonths           SMALLINT NULL,
    InstallmentAmount    DECIMAL(18,2) NOT NULL,
    ProcessingFee        DECIMAL(18,2) NOT NULL DEFAULT 0,
    DisbursementDate     DATE NOT NULL,
    FirstInstallmentDate DATE NOT NULL,
    MaturityDate         DATE NOT NULL,
    Status               VARCHAR(12) NOT NULL DEFAULT 'ACTIVE'
                         CHECK (Status IN ('ACTIVE','CLOSED','FORECLOSED','SETTLED','WRITTEN_OFF')),
    -- denormalised balances (maintained transactionally by procs; reconcilable from lon.LoanSchedule)
    PrincipalOutstanding DECIMAL(18,2) NOT NULL DEFAULT 0,
    InterestOutstanding  DECIMAL(18,2) NOT NULL DEFAULT 0,
    FeesOutstanding      DECIMAL(18,2) NOT NULL DEFAULT 0,
    PenaltyOutstanding   DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalPaid            DECIMAL(18,2) NOT NULL DEFAULT 0,
    DaysPastDue          INT NOT NULL DEFAULT 0,
    DelinquencyBucket    VARCHAR(12) NOT NULL DEFAULT 'CURRENT'
                         CHECK (DelinquencyBucket IN ('CURRENT','DPD_1_30','DPD_31_60','DPD_61_90','DPD_90_PLUS')),
    LastPaymentDate      DATE NULL,
    NextDueDate          DATE NULL,
    ClosedDate           DATE NULL,
    CreatedAt            DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy            INT NULL,
    UpdatedAt            DATETIME2(0) NULL,
    UpdatedBy            INT NULL,
    RowVer               ROWVERSION NOT NULL
);
CREATE INDEX IX_Loan_Customer ON lon.Loan(CustomerId);
CREATE INDEX IX_Loan_Recovery ON lon.Loan(BranchId, DelinquencyBucket, DaysPastDue) WHERE Status = 'ACTIVE';

ALTER TABLE cust.SavingsTransaction
    ADD CONSTRAINT FK_SavingsTransaction_Loan FOREIGN KEY (LoanId) REFERENCES lon.Loan(LoanId);

CREATE TABLE lon.LoanStatusHistory (
    LoanStatusHistoryId BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId              BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    FromStatus          VARCHAR(12) NULL,
    ToStatus            VARCHAR(12) NOT NULL,
    EffectiveDate       DATE NOT NULL,
    Reason              NVARCHAR(300) NULL,
    ChangedBy           INT NULL REFERENCES sec.AppUser(UserId),
    ChangedAt           DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_LoanStatusHistory_Loan ON lon.LoanStatusHistory(LoanId, EffectiveDate);

CREATE TABLE lon.LoanSchedule (
    ScheduleId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId           BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    ScheduleVersion  INT NOT NULL DEFAULT 1,              -- bump on restructure / reschedule
    IsActive         BIT NOT NULL DEFAULT 1,
    InstallmentNo    INT NOT NULL,
    DueDate          DATE NOT NULL,
    OpeningBalance   DECIMAL(18,2) NOT NULL,
    PrincipalDue     DECIMAL(18,2) NOT NULL,
    InterestDue      DECIMAL(18,2) NOT NULL,
    FeeDue           DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalDue         AS (PrincipalDue + InterestDue + FeeDue) PERSISTED,
    PrincipalPaid    DECIMAL(18,2) NOT NULL DEFAULT 0,
    InterestPaid     DECIMAL(18,2) NOT NULL DEFAULT 0,
    FeePaid          DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status           VARCHAR(8) NOT NULL DEFAULT 'PENDING'
                     CHECK (Status IN ('PENDING','PARTIAL','PAID','OVERDUE','CLOSED')),
    PaidDate         DATE NULL,
    CONSTRAINT UQ_Schedule UNIQUE (LoanId, ScheduleVersion, InstallmentNo)
);
CREATE INDEX IX_Schedule_Due ON lon.LoanSchedule(DueDate, Status) INCLUDE (LoanId) WHERE IsActive = 1;

CREATE TABLE lon.Disbursement (
    DisbursementId BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId         BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    GrossAmount    DECIMAL(18,2) NOT NULL,
    FeesDeducted   DECIMAL(18,2) NOT NULL DEFAULT 0,
    NetAmount      DECIMAL(18,2) NOT NULL,
    InstrumentAmount DECIMAL(18,2) NULL,
    Mode           VARCHAR(20) NOT NULL CHECK (Mode IN ('CASH','BANK_TRANSFER','UPI','CHEQUE','NEFT','SAVINGS_TRANSFER')),
    BankAccountId  BIGINT NULL REFERENCES cust.CustomerBankAccount(BankAccountId),
    SavingsAccountId BIGINT NULL REFERENCES cust.SavingsAccount(SavingsAccountId),
    ChequeNo       VARCHAR(30) NULL,
    ChequeDate     DATE NULL,
    BankName       NVARCHAR(100) NULL,
    TransactionNo  NVARCHAR(60) NULL,
    Reference      NVARCHAR(60) NULL,
    DisbursedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    DisbursedBy    INT NULL
);
CREATE INDEX IX_Disb_Loan ON lon.Disbursement(LoanId);

/* =====================================================================
   COLLECTIONS: PENALTY, PAYMENT, ALLOCATION, RECOVERY
   ===================================================================== */
CREATE TABLE col.LoanPenalty (
    PenaltyId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId       BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    ScheduleId   BIGINT NOT NULL REFERENCES lon.LoanSchedule(ScheduleId),
    AsOfDate     DATE NOT NULL,
    DaysOverdue  INT NOT NULL,
    Amount       DECIMAL(18,2) NOT NULL,
    PaidAmount   DECIMAL(18,2) NOT NULL DEFAULT 0,
    WaivedAmount DECIMAL(18,2) NOT NULL DEFAULT 0,
    Outstanding  AS (Amount - PaidAmount - WaivedAmount),
    CONSTRAINT UQ_Penalty_Day UNIQUE (ScheduleId, AsOfDate)    -- makes daily batch idempotent
);
CREATE INDEX IX_Penalty_Loan ON col.LoanPenalty(LoanId);

CREATE TABLE col.LoanCharge (
    LoanChargeId BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId       BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    PaymentId    BIGINT NULL,
    ChargeType   VARCHAR(20) NOT NULL CHECK (ChargeType IN ('CHEQUE_BOUNCE','SERVICE_FEE','OTHER')),
    ChargeDate   DATE NOT NULL,
    Description  NVARCHAR(250) NULL,
    Amount       DECIMAL(18,2) NOT NULL CHECK (Amount >= 0),
    PaidAmount   DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (PaidAmount >= 0),
    WaivedAmount DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (WaivedAmount >= 0),
    Outstanding  AS (Amount - PaidAmount - WaivedAmount),
    CreatedBy    INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (PaidAmount + WaivedAmount <= Amount)
);
CREATE INDEX IX_LoanCharge_Loan ON col.LoanCharge(LoanId, ChargeDate);

CREATE TABLE col.CollectionBatch (
    BatchId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    BatchNo        VARCHAR(30) NOT NULL UNIQUE,
    BranchId       INT NOT NULL REFERENCES ref.Branch(BranchId),
    PaidBranchId   INT NULL REFERENCES ref.Branch(BranchId),
    CollectedBy    INT NULL REFERENCES sec.AppUser(UserId),
    CollectorEmployeeId INT NULL REFERENCES sec.Employee(EmployeeId),
    GroupId        BIGINT NULL REFERENCES cust.CustomerGroup(GroupId),
    PaymentDate    DATE NOT NULL,
    DueFrom        DATE NULL,
    DueTo          DATE NULL,
    CollectionMode VARCHAR(20) NULL CHECK (CollectionMode IN ('CASH','CHEQUE','UPI','BANK_TRANSFER','NEFT','SAVINGS_TRANSFER')),
    SelectionMode  VARCHAR(20) NULL CHECK (SelectionMode IN ('LOAN_WISE','ADVISOR_WISE','GROUP_WISE')),
    TotalAmount    DECIMAL(18,2) NOT NULL DEFAULT 0,
    TotalLateFee   DECIMAL(18,2) NOT NULL DEFAULT 0,
    Status         VARCHAR(10) NOT NULL DEFAULT 'OPEN' CHECK (Status IN ('OPEN','POSTED','CANCELLED')),
    Remarks        NVARCHAR(300) NULL,
    CreatedBy      INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt      DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE col.Payment (
    PaymentId      BIGINT IDENTITY(1,1) PRIMARY KEY,
    ReceiptNo      VARCHAR(25) NOT NULL UNIQUE,
    LoanId         BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    CustomerId     BIGINT NOT NULL REFERENCES cust.Customer(CustomerId),
    PaymentDate    DATE NOT NULL,
    Amount         DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
    Purpose        VARCHAR(12) NOT NULL DEFAULT 'INSTALLMENT' CHECK (Purpose IN ('INSTALLMENT','SETTLEMENT')),
    Mode           VARCHAR(20) NOT NULL CHECK (Mode IN ('CASH','UPI','BANK_TRANSFER','CHEQUE','NACH','NEFT','SAVINGS_TRANSFER')),
    BatchId        BIGINT NULL REFERENCES col.CollectionBatch(BatchId),
    SavingsAccountId BIGINT NULL REFERENCES cust.SavingsAccount(SavingsAccountId),
    ChequeNo       VARCHAR(30) NULL,
    ChequeDate     DATE NULL,
    BankName       NVARCHAR(100) NULL,
    TransactionNo  NVARCHAR(60) NULL,
    TransferBranchId INT NULL REFERENCES ref.Branch(BranchId),
    Reference      NVARCHAR(60) NULL,
    CollectedBy    INT NULL REFERENCES sec.AppUser(UserId),
    Status         VARCHAR(10) NOT NULL DEFAULT 'POSTED' CHECK (Status IN ('POSTED','REVERSED','BOUNCED')),
    ReversalOfPaymentId BIGINT NULL REFERENCES col.Payment(PaymentId),
    IdempotencyKey UNIQUEIDENTIFIER NOT NULL UNIQUE,         -- safe retries from mobile/offline collection apps
    Remarks        NVARCHAR(300) NULL,
    CreatedAt      DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
ALTER TABLE col.LoanCharge
    ADD CONSTRAINT FK_LoanCharge_Payment FOREIGN KEY (PaymentId) REFERENCES col.Payment(PaymentId);
CREATE INDEX IX_Payment_Loan ON col.Payment(LoanId, PaymentDate);
CREATE INDEX IX_Payment_Daily ON col.Payment(PaymentDate, CollectedBy);

CREATE TABLE col.PaymentAllocation (
    AllocationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    PaymentId    BIGINT NOT NULL REFERENCES col.Payment(PaymentId),
    ScheduleId   BIGINT NULL REFERENCES lon.LoanSchedule(ScheduleId),
    PenaltyId    BIGINT NULL REFERENCES col.LoanPenalty(PenaltyId),
    ChargeId     BIGINT NULL REFERENCES col.LoanCharge(LoanChargeId),
    Component    VARCHAR(10) NOT NULL CHECK (Component IN ('PRINCIPAL','INTEREST','FEE','PENALTY','CHARGE','SETTLEMENT')),
    Amount       DECIMAL(18,2) NOT NULL CHECK (Amount > 0)
);
CREATE INDEX IX_Alloc_Payment ON col.PaymentAllocation(PaymentId);

CREATE TABLE col.CollectionAssignment (
    AssignmentId  BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId        BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    AgentId       INT NOT NULL REFERENCES sec.AppUser(UserId),
    AssignedFrom  DATE NOT NULL DEFAULT CAST(SYSUTCDATETIME() AS DATE),
    AssignedTo    DATE NULL,
    BucketAtAssign VARCHAR(12) NULL
);
CREATE INDEX IX_CollAssign_Agent ON col.CollectionAssignment(AgentId, AssignedTo);

CREATE TABLE col.CollectionFollowUp (
    FollowUpId       BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId           BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    AgentId          INT NOT NULL REFERENCES sec.AppUser(UserId),
    FollowUpAt       DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    ContactType      VARCHAR(12) NOT NULL CHECK (ContactType IN ('CALL','VISIT','SMS','WHATSAPP','LEGAL_NOTICE')),
    OutcomeId        INT NULL REFERENCES ref.Lookup(LookupId),
    Remarks          NVARCHAR(500) NULL,
    Latitude         DECIMAL(9,6) NULL,
    Longitude        DECIMAL(9,6) NULL,
    NextFollowUpDate DATE NULL
);
CREATE INDEX IX_FollowUp_Loan ON col.CollectionFollowUp(LoanId, FollowUpAt DESC);
CREATE INDEX IX_FollowUp_Next ON col.CollectionFollowUp(NextFollowUpDate, AgentId) WHERE NextFollowUpDate IS NOT NULL;

CREATE TABLE col.PromiseToPay (
    PromiseId      BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId         BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    FollowUpId     BIGINT NULL REFERENCES col.CollectionFollowUp(FollowUpId),
    PromisedDate   DATE NOT NULL,
    PromisedAmount DECIMAL(18,2) NOT NULL,
    Status         VARCHAR(8) NOT NULL DEFAULT 'OPEN' CHECK (Status IN ('OPEN','KEPT','BROKEN'))
);
CREATE INDEX IX_PTP_Open ON col.PromiseToPay(PromisedDate) WHERE Status = 'OPEN';

/* =====================================================================
   SETTLEMENT / FORECLOSURE / WRITE-OFF
   ===================================================================== */
CREATE TABLE lon.LoanSettlement (
    SettlementId        BIGINT IDENTITY(1,1) PRIMARY KEY,
    LoanId              BIGINT NOT NULL REFERENCES lon.Loan(LoanId),
    SettlementType      VARCHAR(20) NOT NULL CHECK (SettlementType IN ('FORECLOSURE','ONE_TIME_SETTLEMENT','WRITE_OFF')),
    AsOfDate            DATE NOT NULL,
    PrincipalOutstanding DECIMAL(18,2) NOT NULL,
    InterestAccrued     DECIMAL(18,2) NOT NULL,          -- earned & unpaid up to AsOfDate
    FeesOutstanding     DECIMAL(18,2) NOT NULL,
    PenaltyOutstanding  DECIMAL(18,2) NOT NULL,
    ForeclosureCharge   DECIMAL(18,2) NOT NULL DEFAULT 0,
    InterestWaived      DECIMAL(18,2) NOT NULL DEFAULT 0,
    PenaltyWaived       DECIMAL(18,2) NOT NULL DEFAULT 0,
    SettlementAmount    DECIMAL(18,2) NOT NULL,          -- amount customer must pay
    PaymentId           BIGINT NULL REFERENCES col.Payment(PaymentId),
    Status              VARCHAR(10) NOT NULL DEFAULT 'REQUESTED'
                        CHECK (Status IN ('REQUESTED','APPROVED','REJECTED','COMPLETED')),
    Reason              NVARCHAR(300) NULL,
    RequestedBy         INT NULL,
    ApprovedBy          INT NULL,
    ApprovedAt          DATETIME2(0) NULL,
    CompletedAt         DATETIME2(0) NULL,
    CreatedAt           DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVer              ROWVERSION NOT NULL
);
CREATE INDEX IX_Settlement_Loan ON lon.LoanSettlement(LoanId);

/* =====================================================================
   GENERAL LEDGER / ACCOUNTING
   All financial statements are derived from posted, balanced journal entries.
   ===================================================================== */
CREATE TABLE gl.AccountGroup (
    AccountGroupId       INT IDENTITY(1,1) PRIMARY KEY,
    GroupCode            VARCHAR(30) NOT NULL UNIQUE,
    GroupName            NVARCHAR(120) NOT NULL,
    ParentAccountGroupId INT NULL REFERENCES gl.AccountGroup(AccountGroupId),
    AccountClass         VARCHAR(12) NOT NULL CHECK (AccountClass IN ('ASSET','LIABILITY','EQUITY','INCOME','EXPENSE')),
    IsActive             BIT NOT NULL DEFAULT 1
);

CREATE TABLE gl.Ledger (
    LedgerId          BIGINT IDENTITY(1,1) PRIMARY KEY,
    LedgerCode        VARCHAR(30) NOT NULL UNIQUE,
    LedgerName        NVARCHAR(150) NOT NULL,
    LedgerType        NVARCHAR(40) NULL,
    AccountGroupId    INT NULL REFERENCES gl.AccountGroup(AccountGroupId),
    ParentLedgerId    BIGINT NULL REFERENCES gl.Ledger(LedgerId),
    AccountClass      VARCHAR(12) NOT NULL CHECK (AccountClass IN ('ASSET','LIABILITY','EQUITY','INCOME','EXPENSE')),
    IsGroupLedger     BIT NOT NULL DEFAULT 0,
    IsCash            BIT NOT NULL DEFAULT 0,
    IsBank            BIT NOT NULL DEFAULT 0,
    BankName          NVARCHAR(100) NULL,
    BankAccountNo     VARCHAR(40) NULL,
    IfscCode          VARCHAR(20) NULL,
    Editable          BIT NOT NULL DEFAULT 1,
    VisibleInBranch   BIT NOT NULL DEFAULT 1,
    IsDefaultGroup    BIT NOT NULL DEFAULT 0,
    LoadToBranch      BIT NOT NULL DEFAULT 1,
    ApplySanction     BIT NOT NULL DEFAULT 0,
    IsActive          BIT NOT NULL DEFAULT 1,
    CreatedAt         DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CHECK (IsCash = 0 OR IsBank = 0)
);
CREATE INDEX IX_Ledger_Group ON gl.Ledger(AccountGroupId, LedgerName) WHERE IsActive = 1;
CREATE INDEX IX_Ledger_CashBank ON gl.Ledger(IsCash, IsBank) WHERE IsActive = 1;

CREATE TABLE gl.LedgerBranch (
    LedgerId BIGINT NOT NULL REFERENCES gl.Ledger(LedgerId),
    BranchId INT NOT NULL REFERENCES ref.Branch(BranchId),
    IsVisible BIT NOT NULL DEFAULT 1,
    PRIMARY KEY (LedgerId, BranchId)
);

CREATE TABLE gl.LedgerOpeningBalance (
    OpeningBalanceId BIGINT IDENTITY(1,1) PRIMARY KEY,
    LedgerId         BIGINT NOT NULL REFERENCES gl.Ledger(LedgerId),
    BranchId         INT NOT NULL REFERENCES ref.Branch(BranchId),
    AsOfDate         DATE NOT NULL,
    DebitAmount      DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DebitAmount >= 0),
    CreditAmount     DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (CreditAmount >= 0),
    CreatedBy        INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_LedgerOpening UNIQUE (LedgerId, BranchId, AsOfDate),
    CHECK ((DebitAmount = 0 AND CreditAmount > 0) OR (CreditAmount = 0 AND DebitAmount >= 0))
);

CREATE TABLE gl.JournalEntry (
    JournalEntryId  BIGINT IDENTITY(1,1) PRIMARY KEY,
    VoucherNo       VARCHAR(30) NOT NULL UNIQUE,
    VoucherType     VARCHAR(15) NOT NULL CHECK (VoucherType IN ('PAYMENT','RECEIPT','CONTRA','JOURNAL','OPENING','LOAN','RECOVERY')),
    VoucherDate     DATE NOT NULL,
    BranchId        INT NOT NULL REFERENCES ref.Branch(BranchId),
    PayeeName       NVARCHAR(150) NULL,
    PaymentMode     VARCHAR(20) NULL CHECK (PaymentMode IN ('CASH','CHEQUE','UPI','BANK_TRANSFER','NEFT','NACH','SAVINGS_TRANSFER')),
    ChequeNo        VARCHAR(30) NULL,
    ChequeDate      DATE NULL,
    BankName        NVARCHAR(100) NULL,
    TransactionNo   NVARCHAR(60) NULL,
    TransferToBranchId INT NULL REFERENCES ref.Branch(BranchId),
    SanctionNo      VARCHAR(30) NULL,
    LoanId          BIGINT NULL REFERENCES lon.Loan(LoanId),
    PaymentId       BIGINT NULL REFERENCES col.Payment(PaymentId),
    Remarks         NVARCHAR(500) NULL,
    Status          VARCHAR(10) NOT NULL DEFAULT 'DRAFT' CHECK (Status IN ('DRAFT','POSTED','REVERSED')),
    CreatedBy       INT NULL REFERENCES sec.AppUser(UserId),
    CreatedAt       DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    PostedBy        INT NULL REFERENCES sec.AppUser(UserId),
    PostedAt        DATETIME2(0) NULL,
    ReversalOfId    BIGINT NULL REFERENCES gl.JournalEntry(JournalEntryId),
    RowVer          ROWVERSION NOT NULL
);
CREATE INDEX IX_JournalEntry_BranchDate ON gl.JournalEntry(BranchId, VoucherDate, Status);
CREATE INDEX IX_JournalEntry_Loan ON gl.JournalEntry(LoanId, VoucherDate) WHERE LoanId IS NOT NULL;

CREATE TABLE gl.JournalLine (
    JournalLineId  BIGINT IDENTITY(1,1) PRIMARY KEY,
    JournalEntryId BIGINT NOT NULL REFERENCES gl.JournalEntry(JournalEntryId),
    [LineNo]       SMALLINT NOT NULL,
    LedgerId       BIGINT NOT NULL REFERENCES gl.Ledger(LedgerId),
    Particulars    NVARCHAR(300) NULL,
    DebitAmount    DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (DebitAmount >= 0),
    CreditAmount   DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (CreditAmount >= 0),
    CONSTRAINT UQ_JournalLine_No UNIQUE (JournalEntryId, [LineNo]),
    CHECK ((DebitAmount > 0 AND CreditAmount = 0) OR (CreditAmount > 0 AND DebitAmount = 0))
);
CREATE INDEX IX_JournalLine_Ledger ON gl.JournalLine(LedgerId, JournalEntryId);

GO
CREATE OR ALTER PROCEDURE gl.usp_PostJournalEntry
    @JournalEntryId BIGINT,
    @UserId INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRAN;
    DECLARE @status VARCHAR(10), @debit DECIMAL(38,2), @credit DECIMAL(38,2), @lineCount INT;
    SELECT @status = Status
      FROM gl.JournalEntry WITH (UPDLOCK, HOLDLOCK)
     WHERE JournalEntryId = @JournalEntryId;
    IF @status IS NULL OR @status <> 'DRAFT'
        THROW 50050, 'Journal entry not found or not in DRAFT status.', 1;

    SELECT @debit = COALESCE(SUM(DebitAmount), 0),
           @credit = COALESCE(SUM(CreditAmount), 0),
           @lineCount = COUNT(*)
      FROM gl.JournalLine
     WHERE JournalEntryId = @JournalEntryId;
    IF @lineCount < 2 OR @debit <> @credit
        THROW 50051, 'Journal entry must contain at least two balanced lines before posting.', 1;

    UPDATE gl.JournalEntry
       SET Status = 'POSTED', PostedBy = @UserId, PostedAt = SYSUTCDATETIME()
     WHERE JournalEntryId = @JournalEntryId;
    COMMIT;
END
GO

CREATE OR ALTER VIEW gl.vw_PostedJournalActivity
AS
SELECT h.JournalEntryId, h.VoucherNo, h.VoucherType, h.VoucherDate, h.BranchId,
       h.PaymentMode, h.ChequeNo, h.ChequeDate, h.BankName, h.TransactionNo,
       h.TransferToBranchId, h.SanctionNo, h.LoanId, h.PaymentId, h.PayeeName,
       h.Remarks AS VoucherRemarks, l.JournalLineId, l.[LineNo], l.LedgerId,
       a.LedgerCode, a.LedgerName, a.AccountClass, a.IsCash, a.IsBank,
       l.Particulars, l.DebitAmount, l.CreditAmount
  FROM gl.JournalEntry h
  JOIN gl.JournalLine l ON l.JournalEntryId = h.JournalEntryId
  JOIN gl.Ledger a ON a.LedgerId = l.LedgerId
 WHERE h.Status = 'POSTED';
GO

/* =====================================================================
   AUDIT & NOTIFICATIONS
   ===================================================================== */
CREATE TABLE aud.AuditLog (
    AuditId    BIGINT IDENTITY(1,1) PRIMARY KEY,
    TableName  VARCHAR(80) NOT NULL,
    RecordId   VARCHAR(40) NOT NULL,
    Action     VARCHAR(10) NOT NULL CHECK (Action IN ('INSERT','UPDATE','DELETE')),
    OldValues  NVARCHAR(MAX) NULL,                      -- JSON
    NewValues  NVARCHAR(MAX) NULL,                      -- JSON
    ChangedBy  INT NULL,
    ChangedAt  DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Audit_Record ON aud.AuditLog(TableName, RecordId, ChangedAt DESC);

CREATE TABLE aud.Notification (
    NotificationId BIGINT IDENTITY(1,1) PRIMARY KEY,
    CustomerId     BIGINT NULL REFERENCES cust.Customer(CustomerId),
    LoanId         BIGINT NULL REFERENCES lon.Loan(LoanId),
    Channel        VARCHAR(10) NOT NULL CHECK (Channel IN ('SMS','WHATSAPP','EMAIL','PUSH')),
    TemplateCode   VARCHAR(40) NOT NULL,                -- DUE_REMINDER, OVERDUE, RECEIPT, ...
    Message        NVARCHAR(500) NOT NULL,
    Status         VARCHAR(10) NOT NULL DEFAULT 'QUEUED' CHECK (Status IN ('QUEUED','SENT','FAILED')),
    ScheduledAt    DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    SentAt         DATETIME2(0) NULL
);
CREATE INDEX IX_Notif_Queue ON aud.Notification(Status, ScheduledAt) WHERE Status = 'QUEUED';
GO

/* =====================================================================
   SEED DATA
   ===================================================================== */
INSERT ref.Branch(BranchCode, BranchName, City) VALUES ('HO','Head Office',N'Noida');

INSERT sec.Role(RoleCode, RoleName) VALUES
 ('ADMIN','Administrator'),('BRANCH_MANAGER','Branch Manager'),('LOAN_OFFICER','Loan Officer'),
 ('CREDIT_APPROVER','Credit Approver'),('COLLECTION_AGENT','Collection Agent'),
 ('CASHIER','Cashier'),('AUDITOR','Auditor');

INSERT ref.Lookup(Category, Code, Name, SortOrder) VALUES
 ('GENDER','M','Male',1),('GENDER','F','Female',2),('GENDER','O','Other',3),
 ('MARITAL_STATUS','SINGLE','Single',1),('MARITAL_STATUS','MARRIED','Married',2),('MARITAL_STATUS','WIDOWED','Widowed',3),('MARITAL_STATUS','DIVORCED','Divorced',4),
 ('ID_TYPE','NATIONAL_ID','National ID',1),('ID_TYPE','PAN','PAN',2),('ID_TYPE','VOTER_ID','Voter ID',3),('ID_TYPE','DRIVING_LICENSE','Driving License',4),('ID_TYPE','PASSPORT','Passport',5),
 ('OCCUPATION','FARMER','Farmer',1),('OCCUPATION','SHOP_OWNER','Shop Owner',2),('OCCUPATION','VENDOR','Street Vendor',3),('OCCUPATION','ARTISAN','Artisan',4),('OCCUPATION','SALARIED','Salaried',5),('OCCUPATION','OTHER','Other',9),
 ('OCCUPATION','SELF_EMPLOYED','Self employed',6),
 ('SALUTATION','MR','Mr',1),('SALUTATION','MRS','Mrs',2),('SALUTATION','SO','S/O',3),('SALUTATION','WO','W/O',4),('SALUTATION','DO','D/O',5),
 ('RELIGION','HINDU','Hindu',1),('RELIGION','MUSLIM','Muslim',2),('RELIGION','CHRISTIAN','Christian',3),('RELIGION','SIKH','Sikh',4),('RELIGION','OTHER','Other',9),
 ('DESIGNATION','MEMBER','Member',1),('DESIGNATION','GROUP_LEADER','Group leader',2),
 ('LOAN_PURPOSE','BUSINESS','Business working capital',1),('LOAN_PURPOSE','AGRICULTURE','Agriculture',2),('LOAN_PURPOSE','EDUCATION','Education',3),('LOAN_PURPOSE','MEDICAL','Medical',4),('LOAN_PURPOSE','HOME_IMPROVE','Home improvement',5),
 ('DOC_TYPE','ID_PROOF','ID proof',1),('DOC_TYPE','ADDRESS_PROOF','Address proof',2),('DOC_TYPE','INCOME_PROOF','Income proof',3),('DOC_TYPE','PHOTO','Photo',4),('DOC_TYPE','SIGNED_AGREEMENT','Signed agreement',5),
 ('RELATION','SPOUSE','Spouse',1),('RELATION','PARENT','Parent',2),('RELATION','CHILD','Child',3),('RELATION','SIBLING','Sibling',4),('RELATION','FRIEND','Friend',5),('RELATION','NEIGHBOUR','Neighbour',6),
 ('RELATION','MOTHER','Mother',7),('RELATION','FATHER','Father',8),('RELATION','GUARDIAN','Guardian',9),
 ('COLLATERAL_TYPE','GOLD','Gold',1),('COLLATERAL_TYPE','VEHICLE','Vehicle',2),('COLLATERAL_TYPE','PROPERTY','Property',3),('COLLATERAL_TYPE','EQUIPMENT','Equipment',4),
 ('FOLLOWUP_OUTCOME','PAID','Paid',1),('FOLLOWUP_OUTCOME','PROMISED','Promised to pay',2),('FOLLOWUP_OUTCOME','NOT_REACHABLE','Not reachable',3),('FOLLOWUP_OUTCOME','REFUSED','Refused',4),('FOLLOWUP_OUTCOME','DISPUTE','Dispute',5);

INSERT lon.LoanProduct(ProductCode, ProductName, InterestMethod, Frequency, MinAmount, MaxAmount, MinInstallments, MaxInstallments,
                       DefaultRatePct, MinRatePct, MaxRatePct, ProcessingFeePct, PenaltyRatePct, PenaltyGraceDays, ForeclosureChargePct, IsGroupLoan)
VALUES ('JLG-WEEKLY','Group Loan - Weekly (Flat)','FLAT','WEEKLY', 5000, 50000, 26, 52, 22.0, 18.0, 26.0, 1.0, 24.0, 3, 2.0, 1),
       ('IND-MONTHLY','Individual Loan - Monthly (Reducing)','REDUCING','MONTHLY', 10000, 300000, 6, 36, 24.0, 18.0, 30.0, 1.5, 24.0, 5, 2.0, 0);

INSERT gl.AccountGroup(GroupCode, GroupName, AccountClass) VALUES
    ('ASSETS', N'Assets', 'ASSET'),
    ('EQUITY', N'Equity', 'EQUITY'),
    ('INCOME', N'Income', 'INCOME'),
    ('EXPENSES', N'Expenses', 'EXPENSE');

INSERT gl.Ledger(LedgerCode, LedgerName, AccountGroupId, AccountClass, IsCash, IsBank)
SELECT 'CASH', N'Cash in Hand', AccountGroupId, 'ASSET', 1, 0 FROM gl.AccountGroup WHERE GroupCode = 'ASSETS'
UNION ALL
SELECT 'BANK', N'Bank Clearing', AccountGroupId, 'ASSET', 0, 1 FROM gl.AccountGroup WHERE GroupCode = 'ASSETS'
UNION ALL
SELECT 'LOAN_PORTFOLIO', N'Loan Portfolio', AccountGroupId, 'ASSET', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'ASSETS'
UNION ALL
SELECT 'SHARE_CAPITAL', N'Share Capital', AccountGroupId, 'EQUITY', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'EQUITY'
UNION ALL
SELECT 'INTEREST_INCOME', N'Interest Income', AccountGroupId, 'INCOME', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'INCOME'
UNION ALL
SELECT 'SERVICE_FEE_INCOME', N'Service Fee Income', AccountGroupId, 'INCOME', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'INCOME'
UNION ALL
SELECT 'PENALTY_INCOME', N'Penalty Income', AccountGroupId, 'INCOME', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'INCOME'
UNION ALL
SELECT 'OPERATING_EXPENSES', N'Operating Expenses', AccountGroupId, 'EXPENSE', 0, 0 FROM gl.AccountGroup WHERE GroupCode = 'EXPENSES';
GO

/* =====================================================================
   TABLE TYPES FOR CUSTOMER SAVE SCREEN
   ===================================================================== */
CREATE TYPE cust.AddressTVP AS TABLE (
    AddressId  BIGINT NULL,                 -- NULL = new row
    AddressType VARCHAR(12) NOT NULL,
    Line1 NVARCHAR(150) NOT NULL, Line2 NVARCHAR(150) NULL, Landmark NVARCHAR(100) NULL,
    City NVARCHAR(80) NULL, District NVARCHAR(80) NULL, State NVARCHAR(80) NULL,
    PostalCode VARCHAR(10) NULL, IsPrimary BIT NOT NULL DEFAULT 0,
    CountryId INT NULL, StateId INT NULL, DistrictId INT NULL, CityId INT NULL
);
CREATE TYPE cust.IdentityTVP AS TABLE (
    IdentityId BIGINT NULL,                 -- NULL = new row
    IdTypeId INT NOT NULL, IdNumber VARCHAR(40) NOT NULL,
    IssueDate DATE NULL, ExpiryDate DATE NULL
);
GO

/* =====================================================================
   CALCULATOR  - pure, no writes. Same proc feeds the calculator screen,
   the application screen and the disbursement (single source of truth).
   FLAT     : interest = P * rate * (N / periodsPerYear), spread equally
   REDUCING : EMI formula, interest on opening balance each period
   ===================================================================== */
CREATE OR ALTER PROCEDURE lon.usp_GenerateSchedule
    @Principal       DECIMAL(18,2),
    @AnnualRatePct   DECIMAL(9,4),
    @NumInstallments INT,
    @Frequency       VARCHAR(10),          -- WEEKLY | BIWEEKLY | MONTHLY
    @Method          VARCHAR(10),          -- FLAT | REDUCING
    @FirstDueDate    DATE
AS
BEGIN
    SET NOCOUNT ON;
    IF @Principal <= 0 OR @NumInstallments <= 0 THROW 50020, 'Principal and installments must be positive.', 1;

    DECLARE @ppy INT = CASE @Frequency WHEN 'WEEKLY' THEN 52 WHEN 'BIWEEKLY' THEN 26 ELSE 12 END;
    DECLARE @r DECIMAL(18,10) = CAST(@AnnualRatePct / 100.0 / @ppy AS DECIMAL(18,10));
    DECLARE @emi DECIMAL(18,2) = 0, @flatInt DECIMAL(18,2) = 0, @flatPrin DECIMAL(18,2) = 0;

    IF @Method = 'FLAT'
    BEGIN
        SET @flatPrin = ROUND(@Principal / @NumInstallments, 2);
        SET @flatInt  = ROUND(@Principal * (@AnnualRatePct / 100.0) * (CAST(@NumInstallments AS FLOAT) / @ppy) / @NumInstallments, 2);
    END
    ELSE
    BEGIN
        DECLARE @rf FLOAT = CAST(@r AS FLOAT);
        SET @emi = CASE WHEN @rf = 0 THEN ROUND(@Principal / @NumInstallments, 2)
                        ELSE CAST(ROUND(@Principal * @rf * POWER(1 + @rf, @NumInstallments) / (POWER(1 + @rf, @NumInstallments) - 1), 2) AS DECIMAL(18,2)) END;
    END

    ;WITH s AS (
        SELECT CAST(1 AS INT) AS n,
               CAST(@Principal AS DECIMAL(18,2)) AS Opening,
               CAST(CASE WHEN @Method = 'FLAT' THEN @flatInt ELSE ROUND(@Principal * @r, 2) END AS DECIMAL(18,2)) AS Interest,
               CAST(CASE WHEN @NumInstallments = 1 THEN @Principal
                         WHEN @Method = 'FLAT' THEN @flatPrin
                         ELSE @emi - ROUND(@Principal * @r, 2) END AS DECIMAL(18,2)) AS Principal
        UNION ALL
        SELECT s.n + 1,
               CAST(s.Opening - s.Principal AS DECIMAL(18,2)),
               CAST(CASE WHEN @Method = 'FLAT' THEN @flatInt ELSE ROUND((s.Opening - s.Principal) * @r, 2) END AS DECIMAL(18,2)),
               CAST(CASE WHEN s.n + 1 = @NumInstallments THEN s.Opening - s.Principal    -- last row clears rounding drift
                         WHEN @Method = 'FLAT' THEN @flatPrin
                         ELSE @emi - ROUND((s.Opening - s.Principal) * @r, 2) END AS DECIMAL(18,2))
        FROM s
        WHERE s.n < @NumInstallments
    )
    SELECT n AS InstallmentNo,
           CASE @Frequency WHEN 'WEEKLY'   THEN DATEADD(DAY, 7  * (n - 1), @FirstDueDate)
                           WHEN 'BIWEEKLY' THEN DATEADD(DAY, 14 * (n - 1), @FirstDueDate)
                           ELSE DATEADD(MONTH, n - 1, @FirstDueDate) END AS DueDate,
           Opening        AS OpeningBalance,
           Principal      AS PrincipalDue,
           Interest       AS InterestDue,
           Principal + Interest AS TotalDue,
           Opening - Principal  AS ClosingBalance
    FROM s
    ORDER BY n
    OPTION (MAXRECURSION 1000);
END
GO

/* =====================================================================
   CUSTOMER SCREEN: search / get / save (add + update in one proc)
   Update requires the RowVer the screen loaded (optimistic concurrency).
   Child lists (addresses, identities) are sent in full: missing = removed.
   ===================================================================== */
CREATE OR ALTER PROCEDURE cust.usp_Customer_Search
    @Search NVARCHAR(100) = NULL, @BranchId INT = NULL, @Status VARCHAR(12) = NULL, @KycStatus VARCHAR(12) = NULL,
    @Page INT = 1, @PageSize INT = 25
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.CustomerId, c.CustomerNo, c.FullName, c.PrimaryPhone, c.Status, c.KycStatus, c.BranchId,
           (SELECT COUNT(*) FROM lon.Loan l WHERE l.CustomerId = c.CustomerId AND l.Status = 'ACTIVE') AS ActiveLoans,
           COUNT(*) OVER () AS TotalRows
    FROM cust.Customer c
    WHERE c.IsDeleted = 0
      AND (@BranchId  IS NULL OR c.BranchId  = @BranchId)
      AND (@Status    IS NULL OR c.Status    = @Status)
      AND (@KycStatus IS NULL OR c.KycStatus = @KycStatus)
      AND (@Search IS NULL OR c.CustomerNo = @Search OR c.PrimaryPhone LIKE @Search + '%' OR c.FullName LIKE '%' + @Search + '%'
           OR EXISTS (SELECT 1 FROM cust.CustomerIdentity i WHERE i.CustomerId = c.CustomerId AND i.IsDeleted = 0 AND i.IdNumber = @Search))
    ORDER BY c.FullName
    OFFSET (@Page - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE cust.usp_Customer_Get @CustomerId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM cust.Customer WHERE CustomerId = @CustomerId AND IsDeleted = 0;                     -- 1 header (+RowVer)
    SELECT * FROM cust.CustomerAddress       WHERE CustomerId = @CustomerId AND IsDeleted = 0;        -- 2 addresses
    SELECT * FROM cust.CustomerIdentity      WHERE CustomerId = @CustomerId AND IsDeleted = 0;        -- 3 identities
    SELECT * FROM cust.CustomerBankAccount   WHERE CustomerId = @CustomerId AND IsDeleted = 0;        -- 4 bank accounts
    SELECT * FROM cust.CustomerContactPerson WHERE CustomerId = @CustomerId AND IsDeleted = 0;        -- 5 nominee/references
    SELECT LoanId, LoanAccountNo, Status, PrincipalAmount, PrincipalOutstanding, DaysPastDue, DelinquencyBucket, NextDueDate
    FROM lon.Loan WHERE CustomerId = @CustomerId ORDER BY DisbursementDate DESC;                      -- 6 loan history
    SELECT ApplicationId, ApplicationNo, Status, RequestedAmount, ApplicationDate
    FROM lon.LoanApplication WHERE CustomerId = @CustomerId ORDER BY ApplicationDate DESC;            -- 7 applications
END
GO

CREATE OR ALTER PROCEDURE cust.usp_Customer_Save
    @CustomerId       BIGINT OUTPUT,              -- NULL on add; set on return
    @FirstName        NVARCHAR(80), @MiddleName NVARCHAR(80) = NULL, @LastName NVARCHAR(80) = NULL,
    @DateOfBirth      DATE, @GenderId INT = NULL, @MaritalStatusId INT = NULL, @GuardianName NVARCHAR(120) = NULL,
    @PrimaryPhone     VARCHAR(20), @AlternatePhone VARCHAR(20) = NULL, @Email NVARCHAR(150) = NULL,
    @OccupationId     INT = NULL, @EmployerOrBusiness NVARCHAR(150) = NULL,
    @MonthlyIncome    DECIMAL(18,2) = NULL, @MonthlyExpense DECIMAL(18,2) = NULL, @HouseholdSize TINYINT = NULL,
    @Status           VARCHAR(12) = 'ACTIVE', @KycStatus VARCHAR(12) = 'PENDING',
    @BranchId         INT, @RelationshipOfficerId INT = NULL, @Remarks NVARCHAR(500) = NULL,
    @RowVer           BINARY(8) = NULL,           -- required on update
    @UserId           INT,
    @Addresses        cust.AddressTVP  READONLY,
    @Identities       cust.IdentityTVP READONLY,
    @FileNo           VARCHAR(30) = NULL,
    @RegistrationDate DATE = NULL,
    @SalutationId     INT = NULL,
    @MotherName       NVARCHAR(120) = NULL,
    @ReligionId       INT = NULL,
    @PhotoPath        NVARCHAR(300) = NULL,
    @SignaturePath    NVARCHAR(300) = NULL,
    @SeniorCitizen    BIT = NULL,
    @Handicapped      BIT = NULL,
    @SpecialCategory  BIT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF DATEDIFF(YEAR, @DateOfBirth, SYSUTCDATETIME()) < 18 THROW 50001, 'Customer must be at least 18 years old.', 1;

    BEGIN TRAN;
    DECLARE @old NVARCHAR(MAX) = NULL, @action VARCHAR(10);

    IF @CustomerId IS NULL
    BEGIN
        DECLARE @seq BIGINT = NEXT VALUE FOR ref.SeqCustomerNo;
        DECLARE @no VARCHAR(20) = 'C' + RIGHT('0000000' + CAST(@seq AS VARCHAR(10)), 7);
        INSERT cust.Customer(CustomerNo, FileNo, RegistrationDate, SalutationId, FirstName, MiddleName, LastName, DateOfBirth,
                             GenderId, MaritalStatusId, GuardianName, MotherName, ReligionId,
                             PrimaryPhone, AlternatePhone, Email, OccupationId, EmployerOrBusiness, MonthlyIncome, MonthlyExpense,
                             HouseholdSize, Status, KycStatus, BranchId, RelationshipOfficerId, PhotoPath, SignaturePath,
                             SeniorCitizen, Handicapped, SpecialCategory, Remarks, CreatedBy)
        VALUES (@no, @FileNo, COALESCE(@RegistrationDate, CAST(SYSUTCDATETIME() AS DATE)), @SalutationId,
                @FirstName, @MiddleName, @LastName, @DateOfBirth, @GenderId, @MaritalStatusId, @GuardianName, @MotherName, @ReligionId,
                @PrimaryPhone, @AlternatePhone, @Email, @OccupationId, @EmployerOrBusiness, @MonthlyIncome, @MonthlyExpense,
                @HouseholdSize, @Status, @KycStatus, @BranchId, @RelationshipOfficerId, @PhotoPath, @SignaturePath,
                COALESCE(@SeniorCitizen, 0), COALESCE(@Handicapped, 0), COALESCE(@SpecialCategory, 0), @Remarks, @UserId);
        SET @CustomerId = SCOPE_IDENTITY();
        SET @action = 'INSERT';
    END
    ELSE
    BEGIN
        SET @old = (SELECT * FROM cust.Customer WHERE CustomerId = @CustomerId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);
        UPDATE cust.Customer
           SET FileNo = COALESCE(@FileNo, FileNo), RegistrationDate = COALESCE(@RegistrationDate, RegistrationDate),
               SalutationId = COALESCE(@SalutationId, SalutationId), FirstName = @FirstName, MiddleName = @MiddleName, LastName = @LastName,
               DateOfBirth = @DateOfBirth, GenderId = @GenderId, MaritalStatusId = @MaritalStatusId,
               GuardianName = @GuardianName, MotherName = COALESCE(@MotherName, MotherName), ReligionId = COALESCE(@ReligionId, ReligionId),
               PrimaryPhone = @PrimaryPhone, AlternatePhone = @AlternatePhone, Email = @Email,
               OccupationId = @OccupationId, EmployerOrBusiness = @EmployerOrBusiness,
               MonthlyIncome = @MonthlyIncome, MonthlyExpense = @MonthlyExpense, HouseholdSize = @HouseholdSize,
               Status = @Status, KycStatus = @KycStatus, BranchId = @BranchId,
               RelationshipOfficerId = @RelationshipOfficerId, PhotoPath = COALESCE(@PhotoPath, PhotoPath),
               SignaturePath = COALESCE(@SignaturePath, SignaturePath),
               SeniorCitizen = COALESCE(@SeniorCitizen, SeniorCitizen), Handicapped = COALESCE(@Handicapped, Handicapped),
               SpecialCategory = COALESCE(@SpecialCategory, SpecialCategory),
               Remarks = @Remarks,
               UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
         WHERE CustomerId = @CustomerId AND IsDeleted = 0 AND RowVer = @RowVer;
        IF @@ROWCOUNT = 0 THROW 50002, 'Customer not found, or it was changed by another user. Reload and retry.', 1;
        SET @action = 'UPDATE';
    END

    /* ---- addresses: soft-delete missing, update existing, insert new ---- */
    UPDATE a SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME()
      FROM cust.CustomerAddress a
     WHERE a.CustomerId = @CustomerId AND a.IsDeleted = 0
       AND NOT EXISTS (SELECT 1 FROM @Addresses s WHERE s.AddressId = a.AddressId);

    UPDATE a SET AddressType = s.AddressType, Line1 = s.Line1, Line2 = s.Line2, Landmark = s.Landmark, City = s.City,
                 District = s.District, State = s.State, CountryId = s.CountryId, StateId = s.StateId,
                 DistrictId = s.DistrictId, CityId = s.CityId, PostalCode = s.PostalCode,
                 IsPrimary = s.IsPrimary, UpdatedAt = SYSUTCDATETIME()
      FROM cust.CustomerAddress a JOIN @Addresses s ON s.AddressId = a.AddressId
     WHERE a.CustomerId = @CustomerId;

    INSERT cust.CustomerAddress(CustomerId, AddressType, Line1, Line2, Landmark, City, District, State,
                                CountryId, StateId, DistrictId, CityId, PostalCode, IsPrimary)
    SELECT @CustomerId, AddressType, Line1, Line2, Landmark, City, District, State,
           CountryId, StateId, DistrictId, CityId, PostalCode, IsPrimary
      FROM @Addresses WHERE AddressId IS NULL;

    /* ---- identities (unique index blocks duplicate ID numbers across customers) ---- */
    UPDATE i SET IsDeleted = 1
      FROM cust.CustomerIdentity i
     WHERE i.CustomerId = @CustomerId AND i.IsDeleted = 0
       AND NOT EXISTS (SELECT 1 FROM @Identities s WHERE s.IdentityId = i.IdentityId);

    UPDATE i SET IdTypeId = s.IdTypeId, IdNumber = s.IdNumber, IssueDate = s.IssueDate, ExpiryDate = s.ExpiryDate
      FROM cust.CustomerIdentity i JOIN @Identities s ON s.IdentityId = i.IdentityId
     WHERE i.CustomerId = @CustomerId;

    INSERT cust.CustomerIdentity(CustomerId, IdTypeId, IdNumber, IssueDate, ExpiryDate)
    SELECT @CustomerId, IdTypeId, IdNumber, IssueDate, ExpiryDate FROM @Identities WHERE IdentityId IS NULL;

    /* ---- audit ---- */
    INSERT aud.AuditLog(TableName, RecordId, Action, OldValues, NewValues, ChangedBy)
    VALUES ('cust.Customer', CAST(@CustomerId AS VARCHAR(40)), @action, @old,
            (SELECT * FROM cust.Customer WHERE CustomerId = @CustomerId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER), @UserId);

    COMMIT;
    SELECT CustomerId, CustomerNo, RowVer FROM cust.Customer WHERE CustomerId = @CustomerId;   -- fresh token for the screen
END
GO

/* =====================================================================
   DISBURSE: approved application -> Loan + Schedule + Disbursement (atomic)
   ===================================================================== */
CREATE OR ALTER PROCEDURE lon.usp_Loan_Disburse
    @ApplicationId BIGINT, @DisbursementDate DATE, @FirstInstallmentDate DATE,
    @Mode VARCHAR(20) = NULL, @BankAccountId BIGINT = NULL, @Reference NVARCHAR(60) = NULL,
    @UserId INT, @LoanId BIGINT OUTPUT, @SavingsAccountId BIGINT = NULL,
    @ChequeNo VARCHAR(30) = NULL, @ChequeDate DATE = NULL, @BankName NVARCHAR(100) = NULL,
    @TransactionNo NVARCHAR(60) = NULL, @InstrumentAmount DECIMAL(18,2) = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @cust BIGINT, @prod INT, @br INT, @emp INT, @termY SMALLINT, @termM SMALLINT, @amt DECIMAL(18,2), @n INT, @rate DECIMAL(9,4),
            @method VARCHAR(10), @freq VARCHAR(10), @fee DECIMAL(18,2), @ins DECIMAL(18,2),
            @gst DECIMAL(18,2), @share DECIMAL(18,2), @status VARCHAR(12);

    SELECT @cust = CustomerId, @prod = ProductId, @br = BranchId, @emp = EmployeeId,
           @termY = TermYears, @termM = TermMonths,
           @Mode = COALESCE(@Mode, DisbursementMode),
           @BankAccountId = COALESCE(@BankAccountId, DisbursementBankAccountId),
           @SavingsAccountId = COALESCE(@SavingsAccountId, SavingsAccountId),
           @ChequeNo = COALESCE(@ChequeNo, ChequeNo), @ChequeDate = COALESCE(@ChequeDate, ChequeDate),
           @BankName = COALESCE(@BankName, BankName), @TransactionNo = COALESCE(@TransactionNo, TransactionNo),
           @InstrumentAmount = COALESCE(@InstrumentAmount, ChequeAmount),
           @amt = ApprovedAmount, @n = ApprovedInstallments, @rate = COALESCE(ApprovedRatePct, InterestRatePct),
           @method = InterestMethod, @freq = Frequency, @fee = ProcessingFee, @ins = InsuranceFee,
           @gst = GstAmount, @share = ShareAmount, @status = Status
      FROM lon.LoanApplication WITH (UPDLOCK, HOLDLOCK) WHERE ApplicationId = @ApplicationId;
    IF @status IS NULL OR @status <> 'APPROVED' THROW 50030, 'Application is not in APPROVED status.', 1;

    CREATE TABLE #sched (InstallmentNo INT, DueDate DATE, OpeningBalance DECIMAL(18,2), PrincipalDue DECIMAL(18,2),
                         InterestDue DECIMAL(18,2), TotalDue DECIMAL(18,2), ClosingBalance DECIMAL(18,2));
    INSERT #sched EXEC lon.usp_GenerateSchedule @amt, @rate, @n, @freq, @method, @FirstInstallmentDate;

    BEGIN TRAN;
    DECLARE @seq BIGINT = NEXT VALUE FOR ref.SeqLoanNo;
    INSERT lon.Loan(LoanAccountNo, ApplicationId, CustomerId, ProductId, BranchId, EmployeeId, PrincipalAmount, InterestRatePct, InterestMethod,
                    Frequency, NumberOfInstallments, TermYears, TermMonths, InstallmentAmount, ProcessingFee, DisbursementDate, FirstInstallmentDate,
                    MaturityDate, PrincipalOutstanding, InterestOutstanding, NextDueDate, CreatedBy)
    SELECT 'L' + RIGHT('00000000' + CAST(@seq AS VARCHAR(10)), 8), @ApplicationId, @cust, @prod, @br, @emp, @amt, @rate, @method,
           @freq, @n, @termY, @termM, (SELECT TOP 1 TotalDue FROM #sched ORDER BY InstallmentNo), @fee, @DisbursementDate, @FirstInstallmentDate,
           (SELECT MAX(DueDate) FROM #sched), @amt, (SELECT SUM(InterestDue) FROM #sched), @FirstInstallmentDate, @UserId;
    SET @LoanId = SCOPE_IDENTITY();

    INSERT lon.LoanSchedule(LoanId, InstallmentNo, DueDate, OpeningBalance, PrincipalDue, InterestDue)
    SELECT @LoanId, InstallmentNo, DueDate, OpeningBalance, PrincipalDue, InterestDue FROM #sched;

    INSERT lon.Disbursement(LoanId, GrossAmount, FeesDeducted, NetAmount, InstrumentAmount, Mode, BankAccountId, SavingsAccountId,
                            ChequeNo, ChequeDate, BankName, TransactionNo, Reference, DisbursedBy)
    VALUES (@LoanId, @amt, @fee + @ins + @gst + @share, @amt - @fee - @ins - @gst - @share, @InstrumentAmount, @Mode,
            @BankAccountId, @SavingsAccountId, @ChequeNo, @ChequeDate, @BankName, @TransactionNo, @Reference, @UserId);

    UPDATE lon.LoanApplication SET Status = 'DISBURSED', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId WHERE ApplicationId = @ApplicationId;
    INSERT lon.ApplicationStatusHistory(ApplicationId, FromStatus, ToStatus, ChangedBy) VALUES (@ApplicationId, 'APPROVED', 'DISBURSED', @UserId);
    COMMIT;
END
GO

/* =====================================================================
   RECOVERY: post a payment. Waterfall = penalty -> fee -> interest -> principal,
   oldest installment first. Idempotent on @IdempotencyKey.
   ===================================================================== */
CREATE OR ALTER PROCEDURE col.usp_PostPayment
    @LoanId BIGINT, @PaymentDate DATE, @Amount DECIMAL(18,2), @Mode VARCHAR(20),
    @Reference NVARCHAR(60) = NULL, @IdempotencyKey UNIQUEIDENTIFIER, @UserId INT, @PaymentId BIGINT OUTPUT,
    @BatchId BIGINT = NULL, @SavingsAccountId BIGINT = NULL, @ChequeNo VARCHAR(30) = NULL,
    @ChequeDate DATE = NULL, @BankName NVARCHAR(100) = NULL, @TransactionNo NVARCHAR(60) = NULL,
    @TransferBranchId INT = NULL
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    SELECT @PaymentId = PaymentId FROM col.Payment WHERE IdempotencyKey = @IdempotencyKey;
    IF @PaymentId IS NOT NULL RETURN;                                     -- retry of an already-posted payment
    IF @Amount <= 0 THROW 50010, 'Amount must be positive.', 1;

    BEGIN TRAN;
    DECLARE @cust BIGINT, @status VARCHAR(12);
    SELECT @cust = CustomerId, @status = Status FROM lon.Loan WITH (UPDLOCK, HOLDLOCK) WHERE LoanId = @LoanId;
    IF @status IS NULL OR @status <> 'ACTIVE' THROW 50011, 'Loan not found or not ACTIVE.', 1;

    DECLARE @rcpt VARCHAR(25) = 'R' + FORMAT(@PaymentDate, 'yyMMdd') + RIGHT('000000' + CAST(NEXT VALUE FOR ref.SeqReceiptNo AS VARCHAR(10)), 6);
    INSERT col.Payment(ReceiptNo, LoanId, CustomerId, PaymentDate, Amount, Mode, Reference, CollectedBy, IdempotencyKey,
                       BatchId, SavingsAccountId, ChequeNo, ChequeDate, BankName, TransactionNo, TransferBranchId)
    VALUES (@rcpt, @LoanId, @cust, @PaymentDate, @Amount, @Mode, @Reference, @UserId, @IdempotencyKey,
            @BatchId, @SavingsAccountId, @ChequeNo, @ChequeDate, @BankName, @TransactionNo, @TransferBranchId);
    SET @PaymentId = SCOPE_IDENTITY();

    DECLARE @rem DECIMAL(18,2) = @Amount, @id BIGINT, @a DECIMAL(18,2), @b DECIMAL(18,2), @c DECIMAL(18,2),
            @pf DECIMAL(18,2), @pi DECIMAL(18,2), @pp DECIMAL(18,2);

    /* 1) penalties */
    DECLARE cpen CURSOR LOCAL FAST_FORWARD FOR
        SELECT PenaltyId, Outstanding FROM col.LoanPenalty WHERE LoanId = @LoanId AND Outstanding > 0 ORDER BY AsOfDate, PenaltyId;
    OPEN cpen; FETCH NEXT FROM cpen INTO @id, @a;
    WHILE @@FETCH_STATUS = 0 AND @rem > 0
    BEGIN
        SET @pp = CASE WHEN @rem < @a THEN @rem ELSE @a END;
        UPDATE col.LoanPenalty SET PaidAmount = PaidAmount + @pp WHERE PenaltyId = @id;
        INSERT col.PaymentAllocation(PaymentId, PenaltyId, Component, Amount) VALUES (@PaymentId, @id, 'PENALTY', @pp);
        SET @rem -= @pp;
        FETCH NEXT FROM cpen INTO @id, @a;
    END
    CLOSE cpen; DEALLOCATE cpen;

    /* 2) installments, oldest first: fee -> interest -> principal */
    DECLARE csch CURSOR LOCAL FAST_FORWARD FOR
        SELECT ScheduleId, FeeDue - FeePaid, InterestDue - InterestPaid, PrincipalDue - PrincipalPaid
          FROM lon.LoanSchedule
         WHERE LoanId = @LoanId AND IsActive = 1 AND Status IN ('PENDING','PARTIAL','OVERDUE') ORDER BY InstallmentNo;
    OPEN csch; FETCH NEXT FROM csch INTO @id, @a, @b, @c;
    WHILE @@FETCH_STATUS = 0 AND @rem > 0
    BEGIN
        SET @pf = CASE WHEN @rem < @a THEN @rem ELSE @a END;  SET @rem -= @pf;
        SET @pi = CASE WHEN @rem < @b THEN @rem ELSE @b END;  SET @rem -= @pi;
        SET @pp = CASE WHEN @rem < @c THEN @rem ELSE @c END;  SET @rem -= @pp;

        UPDATE lon.LoanSchedule
           SET FeePaid = FeePaid + @pf, InterestPaid = InterestPaid + @pi, PrincipalPaid = PrincipalPaid + @pp,
               Status   = CASE WHEN (FeeDue - FeePaid - @pf) + (InterestDue - InterestPaid - @pi) + (PrincipalDue - PrincipalPaid - @pp) <= 0
                               THEN 'PAID' ELSE 'PARTIAL' END,
               PaidDate = CASE WHEN (FeeDue - FeePaid - @pf) + (InterestDue - InterestPaid - @pi) + (PrincipalDue - PrincipalPaid - @pp) <= 0
                               THEN @PaymentDate ELSE PaidDate END
         WHERE ScheduleId = @id;

        INSERT col.PaymentAllocation(PaymentId, ScheduleId, Component, Amount)
        SELECT @PaymentId, @id, v.Component, v.Amt
          FROM (VALUES ('FEE', @pf), ('INTEREST', @pi), ('PRINCIPAL', @pp)) v(Component, Amt) WHERE v.Amt > 0;

        FETCH NEXT FROM csch INTO @id, @a, @b, @c;
    END
    CLOSE csch; DEALLOCATE csch;

    IF @rem > 0 THROW 50012, 'Payment exceeds total outstanding. Use the settlement/foreclosure flow.', 1;

    /* 3) refresh loan balances from the schedule */
    UPDATE l
       SET PrincipalOutstanding = ISNULL(x.P, 0), InterestOutstanding = ISNULL(x.I, 0), FeesOutstanding = ISNULL(x.F, 0),
           PenaltyOutstanding   = ISNULL((SELECT SUM(Outstanding) FROM col.LoanPenalty p WHERE p.LoanId = l.LoanId), 0),
           TotalPaid = TotalPaid + @Amount, LastPaymentDate = @PaymentDate,
           NextDueDate = (SELECT MIN(DueDate) FROM lon.LoanSchedule s WHERE s.LoanId = l.LoanId AND s.IsActive = 1 AND s.Status IN ('PENDING','PARTIAL','OVERDUE')),
           UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
      FROM lon.Loan l
      CROSS APPLY (SELECT SUM(PrincipalDue - PrincipalPaid) AS P, SUM(InterestDue - InterestPaid) AS I, SUM(FeeDue - FeePaid) AS F
                     FROM lon.LoanSchedule s WHERE s.LoanId = l.LoanId AND s.IsActive = 1 AND s.Status <> 'CLOSED') x
     WHERE l.LoanId = @LoanId;

    IF NOT EXISTS (SELECT 1 FROM lon.LoanSchedule WHERE LoanId = @LoanId AND IsActive = 1 AND Status = 'OVERDUE')
        UPDATE lon.Loan SET DaysPastDue = 0, DelinquencyBucket = 'CURRENT' WHERE LoanId = @LoanId;

    UPDATE lon.Loan SET Status = 'CLOSED', ClosedDate = @PaymentDate, NextDueDate = NULL
     WHERE LoanId = @LoanId AND PrincipalOutstanding + InterestOutstanding + FeesOutstanding + PenaltyOutstanding <= 0;

    COMMIT;
END
GO

/* =====================================================================
   RECOVERY: nightly batch (SQL Agent / Hangfire). Safe to re-run for a date.
   ===================================================================== */
CREATE OR ALTER PROCEDURE col.usp_RunDailyDelinquency @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    BEGIN TRAN;

    UPDATE s SET Status = 'OVERDUE'
      FROM lon.LoanSchedule s JOIN lon.Loan l ON l.LoanId = s.LoanId
     WHERE s.IsActive = 1 AND s.Status IN ('PENDING','PARTIAL') AND s.DueDate < @AsOfDate AND l.Status = 'ACTIVE';

    INSERT col.LoanPenalty(LoanId, ScheduleId, AsOfDate, DaysOverdue, Amount)
    SELECT s.LoanId, s.ScheduleId, @AsOfDate, DATEDIFF(DAY, s.DueDate, @AsOfDate),
           CAST(ROUND((s.PrincipalDue + s.InterestDue + s.FeeDue - s.PrincipalPaid - s.InterestPaid - s.FeePaid) * p.PenaltyRatePct / 100.0 / 365.0, 2) AS DECIMAL(18,2))
      FROM lon.LoanSchedule s
      JOIN lon.Loan l        ON l.LoanId = s.LoanId
      JOIN lon.LoanProduct p ON p.ProductId = l.ProductId
     WHERE s.IsActive = 1 AND s.Status = 'OVERDUE' AND l.Status = 'ACTIVE' AND p.PenaltyRatePct > 0
       AND DATEDIFF(DAY, s.DueDate, @AsOfDate) > p.PenaltyGraceDays
       AND NOT EXISTS (SELECT 1 FROM col.LoanPenalty x WHERE x.ScheduleId = s.ScheduleId AND x.AsOfDate = @AsOfDate);

    UPDATE l
       SET DaysPastDue = ISNULL(d.Dpd, 0),
           DelinquencyBucket = CASE WHEN ISNULL(d.Dpd, 0) = 0  THEN 'CURRENT'
                                    WHEN d.Dpd <= 30 THEN 'DPD_1_30'
                                    WHEN d.Dpd <= 60 THEN 'DPD_31_60'
                                    WHEN d.Dpd <= 90 THEN 'DPD_61_90' ELSE 'DPD_90_PLUS' END,
           PenaltyOutstanding = ISNULL((SELECT SUM(Outstanding) FROM col.LoanPenalty p WHERE p.LoanId = l.LoanId), 0)
      FROM lon.Loan l
      OUTER APPLY (SELECT DATEDIFF(DAY, MIN(s.DueDate), @AsOfDate) AS Dpd
                     FROM lon.LoanSchedule s WHERE s.LoanId = l.LoanId AND s.IsActive = 1 AND s.Status = 'OVERDUE') d
     WHERE l.Status = 'ACTIVE';

    COMMIT;
END
GO

/* =====================================================================
   SETTLEMENT: quote, create request, complete
   Policy here: interest earned up to AsOfDate + principal + fees + penalty
   + foreclosure charge. Change the formula in ONE place as policy evolves.
   ===================================================================== */
CREATE OR ALTER PROCEDURE lon.usp_Settlement_Quote @LoanId BIGINT, @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;
    SELECT l.LoanId, l.LoanAccountNo, @AsOfDate AS AsOfDate,
           x.Princ AS PrincipalOutstanding,
           x.IntEarned AS InterestAccrued,
           x.FutureInt AS FutureInterestNotCharged,
           x.Fees AS FeesOutstanding,
           ISNULL(pn.Pen, 0) AS PenaltyOutstanding,
           CAST(ROUND(x.Princ * p.ForeclosureChargePct / 100.0, 2) AS DECIMAL(18,2)) AS ForeclosureCharge,
           x.Princ + x.IntEarned + x.Fees + ISNULL(pn.Pen, 0)
             + CAST(ROUND(x.Princ * p.ForeclosureChargePct / 100.0, 2) AS DECIMAL(18,2)) AS TotalPayable
      FROM lon.Loan l
      JOIN lon.LoanProduct p ON p.ProductId = l.ProductId
      CROSS APPLY (SELECT SUM(PrincipalDue - PrincipalPaid) AS Princ,
                          SUM(CASE WHEN DueDate <= @AsOfDate THEN InterestDue - InterestPaid ELSE 0 END) AS IntEarned,
                          SUM(CASE WHEN DueDate >  @AsOfDate THEN InterestDue - InterestPaid ELSE 0 END) AS FutureInt,
                          SUM(FeeDue - FeePaid) AS Fees
                     FROM lon.LoanSchedule s WHERE s.LoanId = l.LoanId AND s.IsActive = 1 AND s.Status <> 'CLOSED') x
      OUTER APPLY (SELECT SUM(Outstanding) AS Pen FROM col.LoanPenalty q WHERE q.LoanId = l.LoanId) pn
     WHERE l.LoanId = @LoanId AND l.Status = 'ACTIVE';
END
GO

CREATE OR ALTER PROCEDURE lon.usp_Settlement_Create
    @LoanId BIGINT, @SettlementType VARCHAR(20), @AsOfDate DATE,
    @InterestWaived DECIMAL(18,2) = 0, @PenaltyWaived DECIMAL(18,2) = 0, @Reason NVARCHAR(300) = NULL,
    @UserId INT, @SettlementId BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    CREATE TABLE #q (LoanId BIGINT, LoanAccountNo VARCHAR(20), AsOfDate DATE, PrincipalOutstanding DECIMAL(18,2), InterestAccrued DECIMAL(18,2),
                     FutureInterestNotCharged DECIMAL(18,2), FeesOutstanding DECIMAL(18,2), PenaltyOutstanding DECIMAL(18,2),
                     ForeclosureCharge DECIMAL(18,2), TotalPayable DECIMAL(18,2));
    INSERT #q EXEC lon.usp_Settlement_Quote @LoanId, @AsOfDate;
    IF NOT EXISTS (SELECT 1 FROM #q) THROW 50040, 'Loan not found or not ACTIVE.', 1;

    INSERT lon.LoanSettlement(LoanId, SettlementType, AsOfDate, PrincipalOutstanding, InterestAccrued, FeesOutstanding, PenaltyOutstanding,
                              ForeclosureCharge, InterestWaived, PenaltyWaived, SettlementAmount, Reason, RequestedBy)
    SELECT @LoanId, @SettlementType, @AsOfDate, PrincipalOutstanding, InterestAccrued, FeesOutstanding, PenaltyOutstanding,
           ForeclosureCharge, @InterestWaived, @PenaltyWaived,
           CASE WHEN @SettlementType = 'WRITE_OFF' THEN 0 ELSE TotalPayable - @InterestWaived - @PenaltyWaived END,
           @Reason, @UserId
      FROM #q;
    SET @SettlementId = SCOPE_IDENTITY();
END
GO

CREATE OR ALTER PROCEDURE lon.usp_Settlement_Complete
    @SettlementId BIGINT, @PaymentDate DATE, @Mode VARCHAR(12) = 'CASH', @Reference NVARCHAR(60) = NULL,
    @IdempotencyKey UNIQUEIDENTIFIER, @UserId INT
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    DECLARE @loan BIGINT, @type VARCHAR(20), @amt DECIMAL(18,2), @status VARCHAR(10), @cust BIGINT, @pay BIGINT = NULL;
    SELECT @loan = LoanId, @type = SettlementType, @amt = SettlementAmount, @status = Status
      FROM lon.LoanSettlement WITH (UPDLOCK, HOLDLOCK) WHERE SettlementId = @SettlementId;
    IF @status IS NULL OR @status <> 'APPROVED' THROW 50041, 'Settlement must be APPROVED before completion.', 1;

    BEGIN TRAN;
    SELECT @cust = CustomerId FROM lon.Loan WHERE LoanId = @loan;

    IF @amt > 0
    BEGIN
        INSERT col.Payment(ReceiptNo, LoanId, CustomerId, PaymentDate, Amount, Purpose, Mode, Reference, CollectedBy, IdempotencyKey)
        VALUES ('R' + FORMAT(@PaymentDate, 'yyMMdd') + RIGHT('000000' + CAST(NEXT VALUE FOR ref.SeqReceiptNo AS VARCHAR(10)), 6),
                @loan, @cust, @PaymentDate, @amt, 'SETTLEMENT', @Mode, @Reference, @UserId, @IdempotencyKey);
        SET @pay = SCOPE_IDENTITY();
        INSERT col.PaymentAllocation(PaymentId, Component, Amount) VALUES (@pay, 'SETTLEMENT', @amt);
    END

    -- penalties: collected in a foreclosure, waived otherwise
    UPDATE col.LoanPenalty
       SET PaidAmount   = CASE WHEN @type =  'FORECLOSURE' THEN Amount - WaivedAmount ELSE PaidAmount END,
           WaivedAmount = CASE WHEN @type <> 'FORECLOSURE' THEN Amount - PaidAmount   ELSE WaivedAmount END
     WHERE LoanId = @loan AND Outstanding > 0;

    UPDATE lon.LoanSchedule SET Status = 'CLOSED'
     WHERE LoanId = @loan AND IsActive = 1 AND Status IN ('PENDING','PARTIAL','OVERDUE');

    UPDATE lon.Loan
       SET Status = CASE @type WHEN 'FORECLOSURE' THEN 'FORECLOSED' WHEN 'ONE_TIME_SETTLEMENT' THEN 'SETTLED' ELSE 'WRITTEN_OFF' END,
           ClosedDate = @PaymentDate, NextDueDate = NULL,
           PrincipalOutstanding = CASE WHEN @type = 'WRITE_OFF' THEN PrincipalOutstanding ELSE 0 END,   -- keep balance visible for write-off recovery reporting
           InterestOutstanding  = CASE WHEN @type = 'WRITE_OFF' THEN InterestOutstanding  ELSE 0 END,
           FeesOutstanding      = CASE WHEN @type = 'WRITE_OFF' THEN FeesOutstanding      ELSE 0 END,
           PenaltyOutstanding   = CASE WHEN @type = 'WRITE_OFF' THEN PenaltyOutstanding   ELSE 0 END,
           TotalPaid = TotalPaid + @amt, LastPaymentDate = CASE WHEN @amt > 0 THEN @PaymentDate ELSE LastPaymentDate END,
           UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserId
     WHERE LoanId = @loan;

    UPDATE lon.LoanSettlement SET Status = 'COMPLETED', PaymentId = @pay, CompletedAt = SYSUTCDATETIME() WHERE SettlementId = @SettlementId;
    COMMIT;
END
GO

/* =====================================================================
   REPORTING VIEWS
   ===================================================================== */
CREATE OR ALTER VIEW lon.vw_LoanSummary AS
SELECT l.LoanId, l.LoanAccountNo, c.CustomerNo, c.FullName AS CustomerName, c.PrimaryPhone,
       p.ProductName, b.BranchName, l.Status, l.PrincipalAmount, l.InterestRatePct, l.InstallmentAmount,
       l.PrincipalOutstanding, l.InterestOutstanding, l.FeesOutstanding, l.PenaltyOutstanding,
       l.PrincipalOutstanding + l.InterestOutstanding + l.FeesOutstanding + l.PenaltyOutstanding AS TotalOutstanding,
       l.DaysPastDue, l.DelinquencyBucket, l.NextDueDate, l.LastPaymentDate, l.DisbursementDate, l.MaturityDate
  FROM lon.Loan l
  JOIN cust.Customer c   ON c.CustomerId = l.CustomerId
  JOIN lon.LoanProduct p ON p.ProductId  = l.ProductId
  JOIN ref.Branch b      ON b.BranchId   = l.BranchId;
GO

CREATE OR ALTER VIEW col.vw_PortfolioAtRisk AS
SELECT l.BranchId, l.DelinquencyBucket,
       COUNT(*) AS LoanCount,
       SUM(l.PrincipalOutstanding) AS PrincipalOutstanding
  FROM lon.Loan l
 WHERE l.Status = 'ACTIVE'
 GROUP BY l.BranchId, l.DelinquencyBucket;
GO

CREATE OR ALTER VIEW col.vw_CollectionWorklist AS      -- agent's daily call/visit list
SELECT a.AgentId, l.LoanId, l.LoanAccountNo, c.FullName, c.PrimaryPhone, l.DaysPastDue, l.DelinquencyBucket,
       l.PrincipalOutstanding + l.InterestOutstanding + l.FeesOutstanding + l.PenaltyOutstanding AS TotalOutstanding,
       fu.NextFollowUpDate, ptp.PromisedDate, ptp.PromisedAmount
  FROM col.CollectionAssignment a
  JOIN lon.Loan l     ON l.LoanId = a.LoanId AND l.Status = 'ACTIVE' AND l.DaysPastDue > 0
  JOIN cust.Customer c ON c.CustomerId = l.CustomerId
  OUTER APPLY (SELECT TOP 1 NextFollowUpDate FROM col.CollectionFollowUp f WHERE f.LoanId = l.LoanId ORDER BY f.FollowUpAt DESC) fu
  OUTER APPLY (SELECT TOP 1 PromisedDate, PromisedAmount FROM col.PromiseToPay t WHERE t.LoanId = l.LoanId AND t.Status = 'OPEN' ORDER BY t.PromisedDate) ptp
 WHERE a.AssignedTo IS NULL;
GO

/* Quick test of the calculator:
   EXEC lon.usp_GenerateSchedule @Principal=50000, @AnnualRatePct=24, @NumInstallments=12,
        @Frequency='MONTHLY', @Method='REDUCING', @FirstDueDate='2026-12-20';
*/
