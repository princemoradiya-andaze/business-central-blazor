SET NOCOUNT ON;
SET XACT_ABORT OFF;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51100, 'EF Core migration history is missing. Apply the initial migration first.', 1;

DECLARE @tenantId uniqueidentifier = NEWID();
DECLARE @companyId uniqueidentifier = NEWID();
DECLARE @otherCompanyId uniqueidentifier = NEWID();
DECLARE @periodId uniqueidentifier = NEWID();
DECLARE @accountId uniqueidentifier = NEWID();
DECLARE @otherAccountId uniqueidentifier = NEWID();
DECLARE @batchId uniqueidentifier = NEWID();
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
    (Id, CompanyId, Number, Name, Type, ParentAccountId, AllowsDirectPosting, IsBlocked)
VALUES
    (@accountId, @companyId, N'1000', N'Cash', N'Asset', NULL, 1, 0),
    (@otherAccountId, @otherCompanyId, N'1000', N'Cash', N'Asset', NULL, 1, 0);

INSERT dbo.AccountingPeriods (Id, CompanyId, Code, StartsOn, EndsOn, Status)
VALUES (@periodId, @companyId, N'FY26', '20260101', '20261231', N'Open');

INSERT dbo.GeneralJournalBatches
    (Id, CompanyId, AccountingPeriodId, Number, Status, CreatedAtUtc)
VALUES (@batchId, @companyId, @periodId, N'GEN', N'Open', @now);

INSERT dbo.GeneralJournalLines
    (Id, CompanyId, JournalBatchId, AccountId, LineNumber, PostingDate, DocumentNumber,
     Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
VALUES
    (@lineId, @companyId, @batchId, @accountId, 1, '20261012', N'SMOKE-1',
     N'Valid debit line', 10, 0, NULL, NULL, NULL);

BEGIN TRY
    INSERT dbo.GeneralJournalLines
        (Id, CompanyId, JournalBatchId, AccountId, LineNumber, PostingDate, DocumentNumber,
         Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
    VALUES
        (NEWID(), @companyId, @batchId, @accountId, 2, '20261012', N'SMOKE-BAD',
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
        (Id, CompanyId, JournalBatchId, AccountId, LineNumber, PostingDate, DocumentNumber,
         Description, DebitAmount, CreditAmount, CurrencyCode, CurrencyDebitAmount, CurrencyCreditAmount)
    VALUES
        (NEWID(), @otherCompanyId, @batchId, @otherAccountId, 2, '20261012', N'SMOKE-XCO',
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

PRINT 'PASS: valid tenant, company, currency, settings, period, account, batch, line, and entry persisted.';
ROLLBACK TRANSACTION;
PRINT 'PASS: all test rows rolled back; no sample records were retained.';
