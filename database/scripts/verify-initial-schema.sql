SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT OFF;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51100, 'EF Core migration history is missing. Apply the schema migrations first.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20261012103444_AddJournalBatchReversal'
)
    THROW 51104, 'Apply the journal-reversal migration before running this smoke test.', 1;

DECLARE @tenantId uniqueidentifier = NEWID();
DECLARE @companyId uniqueidentifier = NEWID();
DECLARE @otherCompanyId uniqueidentifier = NEWID();
DECLARE @periodId uniqueidentifier = NEWID();
DECLARE @accountId uniqueidentifier = NEWID();
DECLARE @otherAccountId uniqueidentifier = NEWID();
DECLARE @templateId uniqueidentifier = NEWID();
DECLARE @batchId uniqueidentifier = NEWID();
DECLARE @otherBatchId uniqueidentifier = NEWID();
DECLARE @reversalBatchId uniqueidentifier = NEWID();
DECLARE @lineId uniqueidentifier = NEWID();
DECLARE @ledgerEntryId uniqueidentifier = NEWID();
DECLARE @now datetimeoffset = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00');
DECLARE @currencyCode char(3) = RIGHT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 3);

WHILE EXISTS (SELECT 1 FROM dbo.Currencies WHERE Code = @currencyCode)
    SET @currencyCode = RIGHT(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''), 3);

BEGIN TRANSACTION;

INSERT dbo.Tenants (Id, Name, CreatedAtUtc)
VALUES (@tenantId, N'Phase 0 SQL smoke test', @now);

INSERT dbo.Currencies (Code, Name, MinorUnit, IsActive)
VALUES (@currencyCode, N'Test currency', 2, 1);

INSERT dbo.Companies (Id, TenantId, Code, Name, IsActive, CreatedAtUtc)
VALUES
    (@companyId, @tenantId, N'TEST', N'Smoke Test Company', 1, @now),
    (@otherCompanyId, @tenantId, N'OTHER', N'Other Smoke Test Company', 1, @now);

INSERT dbo.CompanySettings (CompanyId, BaseCurrencyCode, TimeZoneId, FirstDayOfWeek)
VALUES
    (@companyId, @currencyCode, N'UTC', 1),
    (@otherCompanyId, @currencyCode, N'UTC', 1);

INSERT dbo.GeneralLedgerAccounts
    (Id, CompanyId, Number, Name, Category, Type, ParentAccountId, AllowsDirectPosting, IsBlocked)
VALUES
    (@accountId, @companyId, N'1000', N'Cash', N'Asset', N'Posting', NULL, 1, 0),
    (@otherAccountId, @otherCompanyId, N'1000', N'Cash', N'Asset', N'Posting', NULL, 1, 0);

INSERT dbo.AccountingPeriods (Id, CompanyId, Name, StartsOn, EndsOn, IsClosed, DateLocked)
VALUES (@periodId, @companyId, N'FY26', '20260101', '20261231', 0, 0);

INSERT dbo.GeneralJournalTemplates (Id, CompanyId, Code, Description, Type)
VALUES (@templateId, @companyId, N'GENERAL', N'General journal', N'General');

INSERT dbo.GeneralJournalBatches
    (Id, CompanyId, JournalTemplateId, DefaultAccountingPeriodId, Name, Description, Status, CreatedAtUtc)
VALUES (@batchId, @companyId, @templateId, @periodId, N'DEFAULT', N'Default journal batch', N'Open', @now);

INSERT dbo.GeneralJournalBatches
    (Id, CompanyId, JournalTemplateId, DefaultAccountingPeriodId, Name, Description, Status, CreatedAtUtc)
VALUES
    (@otherBatchId, @companyId, @templateId, @periodId, N'OTHER', N'Other journal batch', N'Open', @now),
    (@reversalBatchId, @companyId, @templateId, @periodId, N'REVERSAL', N'Reversing journal batch', N'Posted', @now);

UPDATE dbo.GeneralJournalBatches
SET ReversedByBatchId = @reversalBatchId
WHERE CompanyId = @companyId AND Id = @batchId;

BEGIN TRY
    UPDATE dbo.GeneralJournalBatches
    SET ReversedByBatchId = @reversalBatchId
    WHERE CompanyId = @companyId AND Id = @otherBatchId;
    THROW 51105, 'Unique reversal constraint did not reject a duplicate reversal.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627)
        THROW;

    PRINT 'PASS: unique reversal index rejected a second reversal for the same batch.';
END CATCH;

INSERT dbo.GeneralJournalLines
    (Id, CompanyId, JournalBatchId, AccountingPeriodId, AccountId, LineNumber, PostingDate, DocumentNumber,
     Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
VALUES
    (@lineId, @companyId, @batchId, @periodId, @accountId, 1, '20261012', N'SMOKE-1',
     N'Valid debit line', 10, 0, NULL, NULL, NULL);

BEGIN TRY
    INSERT dbo.GeneralJournalLines
        (Id, CompanyId, JournalBatchId, AccountingPeriodId, AccountId, LineNumber, PostingDate, DocumentNumber,
         Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
    VALUES
        (NEWID(), @companyId, @batchId, @periodId, @accountId, 2, '20261012', N'SMOKE-BAD',
         N'Invalid zero line', 0, 0, NULL, NULL, NULL);
    THROW 51101, 'Debit/credit check constraint did not reject an invalid line.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547
        THROW;

    PRINT 'PASS: debit/credit check constraint rejected an invalid line.';
END CATCH;

BEGIN TRY
    INSERT dbo.GeneralJournalLines
        (Id, CompanyId, JournalBatchId, AccountingPeriodId, AccountId, LineNumber, PostingDate, DocumentNumber,
         Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
    VALUES
        (NEWID(), @otherCompanyId, @batchId, @periodId, @otherAccountId, 2, '20261012', N'SMOKE-XCO',
         N'Cross-company batch reference', 10, 0, NULL, NULL, NULL);
    THROW 51102, 'Composite company FK did not reject a cross-company batch reference.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() <> 547
        THROW;

    PRINT 'PASS: company-scoped composite FK rejected a cross-company journal line.';
END CATCH;

INSERT dbo.GeneralLedgerEntries
    (Id, CompanyId, JournalBatchId, JournalLineId, AccountId, EntryNumber, PostingDate,
     DocumentNumber, Description, DebitAmount, CreditAmount, CurrencyCode,
     CurrencyDebitAmount, CurrencyCreditAmount, CreatedAtUtc)
VALUES
    (@ledgerEntryId, @companyId, @batchId, @lineId, @accountId, 1, '20261012',
     N'SMOKE-1', N'Valid ledger entry', 10, 0, NULL, NULL, NULL, @now);

BEGIN TRY
    INSERT dbo.GeneralLedgerEntries
        (Id, CompanyId, JournalBatchId, JournalLineId, AccountId, EntryNumber, PostingDate,
         DocumentNumber, Description, DebitAmount, CreditAmount, CurrencyCode,
         CurrencyDebitAmount, CurrencyCreditAmount, CreatedAtUtc)
    VALUES
        (NEWID(), @companyId, @batchId, @lineId, @accountId, 2, '20261012',
         N'SMOKE-2', N'Duplicate source line', 10, 0, NULL, NULL, NULL, @now);
    THROW 51103, 'Unique posting constraint did not reject a duplicate source line.', 1;
END TRY
BEGIN CATCH
    IF ERROR_NUMBER() NOT IN (2601, 2627)
        THROW;

    PRINT 'PASS: unique source-line index rejected duplicate ledger posting.';
END CATCH;

PRINT 'PASS: valid tenant, company, currency, settings, period, account, template, batch, line, and entry persisted.';
ROLLBACK TRANSACTION;
PRINT 'PASS: all test rows rolled back; no sample records were retained.';
