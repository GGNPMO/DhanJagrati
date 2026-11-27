/* =====================================================================================
   DhanJagrati  -  Patch 002  (Phase 4: recovery & settlement)
   -------------------------------------------------------------------------------------
   A. loan.LoanSettlementLine - per-installment amounts applied by a settlement, so a
      re-open can restore the schedule exactly.
   B. Receipt lookup index on loan.LoanRecovery (one receipt may pay several EMIs).
   C. Late-fee settings (grace days, mode).

   Prerequisite: patch 001. Run with -b -I:
       sqlcmd -S <server> -d DhanJagrati -U <login> -C -b -I -i scheema\dhanjagrati_patches\002_recovery_settlement.sql
   ===================================================================================== */

USE DhanJagrati;
GO
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sec.SchemaVersion WHERE Version = '001')
BEGIN
    RAISERROR('Patch 001 must be applied before patch 002.', 16, 1);
    SET NOEXEC ON;
END
GO

IF EXISTS (SELECT 1 FROM sec.SchemaVersion WHERE Version = '002')
BEGIN
    PRINT 'Patch 002 already applied - skipping.';
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
GO

CREATE TABLE loan.LoanSettlementLine
(
    SettlementId    INT             NOT NULL CONSTRAINT FK_LoanSettlementLine_Settlement REFERENCES loan.LoanSettlement(SettlementId),
    InstallmentNo   SMALLINT        NOT NULL,
    PrincipalPaid   DECIMAL(18,2)   NOT NULL CONSTRAINT CK_LoanSettlementLine_Prin CHECK (PrincipalPaid >= 0),
    InterestPaid    DECIMAL(18,2)   NOT NULL CONSTRAINT CK_LoanSettlementLine_Int CHECK (InterestPaid >= 0),
    PreviousStatus  VARCHAR(8)      NOT NULL,
    CONSTRAINT PK_LoanSettlementLine PRIMARY KEY (SettlementId, InstallmentNo)
);
GO

CREATE INDEX IX_LoanRecovery_Receipt ON loan.LoanRecovery(ReceiptNo) INCLUDE (LoanId, IsReversed);
GO

INSERT sec.AppSetting (SettingKey, SettingValue, DataType, Description) VALUES
    ('LOAN.LATEFEE.MODE',       N'PER_INSTALLMENT', 'STRING', N'PER_INSTALLMENT: plan late fee for each overdue EMI; NONE: no automatic late fee'),
    ('LOAN.LATEFEE.GRACE_DAYS', N'0',               'INT',    N'Days after the due date before the late fee applies');
GO

INSERT sec.SchemaVersion (Version, Description) VALUES ('002', N'Settlement lines, receipt index, late-fee settings');
GO

COMMIT TRANSACTION;
GO

SET NOEXEC OFF;
GO
