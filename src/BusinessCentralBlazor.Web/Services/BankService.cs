using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessCentralBlazor.Web.Services;

public sealed class BankAccountInput
{
    [Required, StringLength(32)]
    public string Code { get; set; } = string.Empty;
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;
    [StringLength(100)]
    public string AccountIdentifier { get; set; } = string.Empty;
    public Guid GeneralLedgerAccountId { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class BankJournalInput
{
    public Guid AccountingPeriodId { get; set; }
    public Guid OffsetAccountId { get; set; }
    public DateOnly PostingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, StringLength(64)]
    public string DocumentNumber { get; set; } = string.Empty;
    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsReceipt { get; set; } = true;
}

public sealed class BankStatementInput
{
    [Required, StringLength(64)]
    public string Reference { get; set; } = string.Empty;
    public DateOnly StartsOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly EndsOn { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
}

public sealed class BankStatementLineInput
{
    [Required, StringLength(64)]
    public string Reference { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed record BankAccountDetail(Guid Id, string Code, string Name, string AccountIdentifier,
    Guid GeneralLedgerAccountId, string CurrencyCode, bool IsBlocked, int Revision);
public sealed record BankJournalDetail(Guid BatchId, string DocumentNumber, bool IsReceipt, JournalBatchStatus Status);
public sealed record BankStatementSummary(Guid Id, string Reference, DateOnly EndsOn, bool IsCompleted);
public sealed record BankActivity(Guid Id, long EntryNumber, Guid BatchId, DateOnly PostingDate,
    string DocumentNumber, string Description, decimal Amount, Guid? MatchedLineId);
public sealed record BankStatementLineDetail(Guid Id, string Reference, DateOnly TransactionDate,
    string Description, decimal Amount, Guid? EntryId);
public sealed record BankStatementDetail(Guid Id, Guid BankAccountId, string Reference, DateOnly StartsOn,
    DateOnly EndsOn, decimal OpeningBalance, decimal ClosingBalance, bool IsCompleted, int Revision,
    decimal StatementDifference, decimal BookDifference, IReadOnlyList<BankStatementLineDetail> Lines,
    IReadOnlyList<BankActivity> Activity);

public interface IBankService
{
    Task<IReadOnlyList<BankAccountDetail>> GetAccountsAsync(ClaimsPrincipal user);
    Task<Guid> CreateAccountAsync(ClaimsPrincipal user, BankAccountInput input);
    Task UpdateAccountAsync(ClaimsPrincipal user, Guid bankId, int revision, BankAccountInput input);
    Task<Guid> CreateJournalAsync(ClaimsPrincipal user, Guid bankId, BankJournalInput input);
    Task<IReadOnlyList<BankJournalDetail>> GetJournalsAsync(ClaimsPrincipal user, Guid bankId);
    Task<IReadOnlyList<BankStatementSummary>> GetStatementsAsync(ClaimsPrincipal user, Guid bankId);
    Task<Guid> CreateStatementAsync(ClaimsPrincipal user, Guid bankId, BankStatementInput input);
    Task UpdateStatementAsync(ClaimsPrincipal user, Guid statementId, int revision, BankStatementInput input);
    Task<BankStatementDetail> GetStatementAsync(ClaimsPrincipal user, Guid statementId);
    Task AddLinesAsync(ClaimsPrincipal user, Guid statementId, int revision, IReadOnlyList<BankStatementLineInput> inputs);
    Task ImportLinesAsync(ClaimsPrincipal user, Guid statementId, int revision, string text);
    Task RemoveLineAsync(ClaimsPrincipal user, Guid statementId, int revision, Guid lineId);
    Task MatchAsync(ClaimsPrincipal user, Guid statementId, int revision, Guid lineId, Guid? entryId);
    Task CompleteAsync(ClaimsPrincipal user, Guid statementId, int revision);
}

public sealed class BankService(IDbContextFactory<ErpDbContext> factory, ICompanyScopeAuthorizer authorizer) : IBankService
{
    private const decimal MaximumAmount = 999999999999999.9999m;

    public async Task<IReadOnlyList<BankAccountDetail>> GetAccountsAsync(ClaimsPrincipal user)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        return await db.BankAccounts.AsNoTracking().Where(bank => bank.CompanyId == company)
            .OrderBy(bank => bank.Code).Select(bank => new BankAccountDetail(bank.Id, bank.Code, bank.Name,
                bank.AccountIdentifier, bank.GeneralLedgerAccountId, bank.CurrencyCode, bank.IsBlocked, bank.Revision))
            .ToListAsync();
    }

    public async Task<Guid> CreateAccountAsync(ClaimsPrincipal user, BankAccountInput input)
    {
        var company = await CompanyAsync(user);
        Normalize(input);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        await EligibleAccountAsync(db, company, input.GeneralLedgerAccountId, true);
        var currency = await db.CompanySettings.Where(settings => settings.CompanyId == company)
            .Select(settings => settings.BaseCurrencyCode).SingleOrDefaultAsync();
        if (currency is null || !await db.Currencies.AnyAsync(item => item.Code == currency && item.IsActive))
            throw new ValidationException("Configure an active company base currency before creating a bank account.");
        await UniqueAccountAsync(db, company, Guid.Empty, input);
        var bank = new BankAccount { Id = Guid.NewGuid(), CompanyId = company, CurrencyCode = currency };
        Apply(bank, input);
        db.BankAccounts.Add(bank);
        Audit(db, user, company, "BankAccountCreated", bank.Id);
        await SaveAsync(db, transaction);
        return bank.Id;
    }

    public async Task UpdateAccountAsync(ClaimsPrincipal user, Guid bankId, int revision, BankAccountInput input)
    {
        var company = await CompanyAsync(user);
        Normalize(input);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var bank = await BankAsync(db, company, bankId);
        CheckRevision(bank.Revision, revision);
        if (!input.IsBlocked || bank.GeneralLedgerAccountId != input.GeneralLedgerAccountId)
            await EligibleAccountAsync(db, company, input.GeneralLedgerAccountId, true);
        else if (!await db.GeneralLedgerAccounts.AnyAsync(account => account.CompanyId == company && account.Id == input.GeneralLedgerAccountId))
            throw new ValidationException("Select an account in the active company.");
        if (bank.GeneralLedgerAccountId != input.GeneralLedgerAccountId &&
            (await db.BankJournals.AnyAsync(item => item.CompanyId == company && item.BankAccountId == bankId) ||
             await db.BankStatements.AnyAsync(item => item.CompanyId == company && item.BankAccountId == bankId) ||
             await db.GeneralLedgerEntries.AnyAsync(item => item.CompanyId == company && item.AccountId == bank.GeneralLedgerAccountId)))
            throw new ValidationException("The G/L mapping cannot change after journal, statement, or posted activity exists.");
        await UniqueAccountAsync(db, company, bankId, input);
        Apply(bank, input);
        bank.Revision++;
        Audit(db, user, company, "BankAccountUpdated", bank.Id);
        await SaveAsync(db, transaction);
    }

    public async Task<Guid> CreateJournalAsync(ClaimsPrincipal user, Guid bankId, BankJournalInput input)
    {
        var company = await CompanyAsync(user);
        input.DocumentNumber = input.DocumentNumber?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        Validate(input);
        Amount(input.Amount, false);
        if (input.Amount <= 0) throw new ValidationException("Enter a positive receipt/payment amount.");
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var bank = await BankAsync(db, company, bankId);
        await OperationalBankAsync(db, company, bank);
        await EligibleAccountAsync(db, company, input.OffsetAccountId, false);
        if (input.OffsetAccountId == bank.GeneralLedgerAccountId)
            throw new ValidationException("The offset account must differ from the bank G/L account.");
        var period = await db.AccountingPeriods.SingleOrDefaultAsync(item =>
            item.CompanyId == company && item.Id == input.AccountingPeriodId);
        if (period is null || period.IsClosed || period.DateLocked || input.PostingDate == default ||
            input.PostingDate < period.StartsOn || input.PostingDate > period.EndsOn)
            throw new ValidationException("Select an open, unlocked period containing the posting date.");
        if (await db.BankJournals.AnyAsync(item => item.CompanyId == company &&
            item.BankAccountId == bankId && item.DocumentNumber == input.DocumentNumber))
            throw new ValidationException("This bank document already has a journal.");
        var type = input.IsReceipt ? JournalTemplateType.CashReceipts : JournalTemplateType.Payments;
        var template = await db.GeneralJournalTemplates.SingleOrDefaultAsync(item => item.CompanyId == company && item.Type == type);
        if (template is null)
        {
            template = new GeneralJournalTemplate { Id = Guid.NewGuid(), CompanyId = company,
                Code = input.IsReceipt ? "RECEIPTS" : "PAYMENTS", Description = "Bank journal", Type = type };
            db.GeneralJournalTemplates.Add(template);
        }
        var batch = new GeneralJournalBatch { Id = Guid.NewGuid(), CompanyId = company,
            JournalTemplateId = template.Id, DefaultAccountingPeriodId = period.Id,
            Name = Guid.NewGuid().ToString("N"), Description = input.Description.Length <= 100 ? input.Description : input.Description[..100],
            Status = JournalBatchStatus.Open, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.GeneralJournalBatches.Add(batch);
        foreach (var bankSide in new[] { true, false })
        {
            var debit = bankSide == input.IsReceipt;
            db.GeneralJournalLines.Add(new GeneralJournalLine { Id = Guid.NewGuid(), CompanyId = company,
                JournalBatchId = batch.Id, AccountingPeriodId = period.Id,
                AccountId = bankSide ? bank.GeneralLedgerAccountId : input.OffsetAccountId,
                LineNumber = bankSide ? 10000 : 20000, PostingDate = input.PostingDate,
                DocumentNumber = input.DocumentNumber, Description = input.Description,
                DebitAmount = debit ? input.Amount : 0, CreditAmount = debit ? 0 : input.Amount });
        }
        db.BankJournals.Add(new BankJournal { Id = Guid.NewGuid(), CompanyId = company, BankAccountId = bankId,
            JournalBatchId = batch.Id, DocumentNumber = input.DocumentNumber, IsReceipt = input.IsReceipt });
        Audit(db, user, company, "BankJournalCreated", batch.Id,
            new { BankAccountId = bankId, input.DocumentNumber, input.IsReceipt, input.Amount });
        await SaveAsync(db, transaction);
        return batch.Id;
    }

    public async Task<IReadOnlyList<BankJournalDetail>> GetJournalsAsync(ClaimsPrincipal user, Guid bankId)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        await BankAsync(db, company, bankId);
        return await (from journal in db.BankJournals.AsNoTracking()
                      join batch in db.GeneralJournalBatches.AsNoTracking()
                          on new { journal.CompanyId, Id = journal.JournalBatchId } equals new { batch.CompanyId, batch.Id }
                      where journal.CompanyId == company && journal.BankAccountId == bankId
                      orderby batch.CreatedAtUtc descending
                      select new BankJournalDetail(batch.Id, journal.DocumentNumber, journal.IsReceipt, batch.Status)).ToListAsync();
    }

    public async Task<IReadOnlyList<BankStatementSummary>> GetStatementsAsync(ClaimsPrincipal user, Guid bankId)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        await BankAsync(db, company, bankId);
        return await db.BankStatements.AsNoTracking().Where(item => item.CompanyId == company && item.BankAccountId == bankId)
            .OrderByDescending(item => item.EndsOn)
            .Select(item => new BankStatementSummary(item.Id, item.Reference, item.EndsOn, item.IsCompleted)).ToListAsync();
    }

    public async Task<Guid> CreateStatementAsync(ClaimsPrincipal user, Guid bankId, BankStatementInput input)
    {
        var company = await CompanyAsync(user);
        NormalizeStatement(input);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var bank = await BankAsync(db, company, bankId);
        await OperationalBankAsync(db, company, bank);
        if (await db.BankStatements.AnyAsync(item => item.CompanyId == company && item.BankAccountId == bankId &&
            (item.Reference == input.Reference || (item.StartsOn <= input.EndsOn && input.StartsOn <= item.EndsOn))))
            throw new ValidationException("A statement with this reference or an overlapping date range already exists.");
        var statement = new BankStatement { Id = Guid.NewGuid(), CompanyId = company, BankAccountId = bankId,
            Reference = input.Reference, StartsOn = input.StartsOn, EndsOn = input.EndsOn,
            OpeningBalance = input.OpeningBalance, ClosingBalance = input.ClosingBalance };
        db.BankStatements.Add(statement);
        Audit(db, user, company, "BankStatementCreated", statement.Id);
        await SaveAsync(db, transaction);
        return statement.Id;
    }

    public async Task UpdateStatementAsync(ClaimsPrincipal user, Guid statementId, int revision, BankStatementInput input)
    {
        var company = await CompanyAsync(user);
        NormalizeStatement(input);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var statement = await EditableStatementAsync(db, company, statementId, revision);
        if (await db.BankStatements.AnyAsync(item => item.CompanyId == company && item.BankAccountId == statement.BankAccountId &&
            item.Id != statementId && (item.Reference == input.Reference ||
            (item.StartsOn <= input.EndsOn && input.StartsOn <= item.EndsOn))))
            throw new ValidationException("A statement with this reference or an overlapping date range already exists.");
        if (await db.BankStatementLines.AnyAsync(item => item.CompanyId == company && item.BankStatementId == statementId &&
            (item.TransactionDate < input.StartsOn || item.TransactionDate > input.EndsOn)) ||
            await (from line in db.BankStatementLines
                   join entry in db.GeneralLedgerEntries on new { line.CompanyId, Id = line.GeneralLedgerEntryId }
                       equals new { entry.CompanyId, Id = (Guid?)entry.Id }
                   where line.CompanyId == company && line.BankStatementId == statementId && entry.PostingDate > input.EndsOn
                   select line.Id).AnyAsync())
            throw new ValidationException("The revised date range must contain all lines and include all matched posting dates.");
        statement.Reference = input.Reference;
        statement.StartsOn = input.StartsOn;
        statement.EndsOn = input.EndsOn;
        statement.OpeningBalance = input.OpeningBalance;
        statement.ClosingBalance = input.ClosingBalance;
        statement.Revision++;
        Audit(db, user, company, "BankStatementUpdated", statement.Id);
        await SaveAsync(db, transaction);
    }

    public async Task<BankStatementDetail> GetStatementAsync(ClaimsPrincipal user, Guid statementId)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        var statement = await StatementAsync(db, company, statementId);
        var bank = await BankAsync(db, company, statement.BankAccountId);
        var lines = await db.BankStatementLines.AsNoTracking().Where(item => item.CompanyId == company && item.BankStatementId == statementId)
            .OrderBy(item => item.TransactionDate).ThenBy(item => item.Reference)
            .Select(item => new BankStatementLineDetail(item.Id, item.Reference, item.TransactionDate,
                item.Description, item.Amount, item.GeneralLedgerEntryId)).ToListAsync();
        var entries = await db.GeneralLedgerEntries.AsNoTracking().Where(item => item.CompanyId == company &&
            item.AccountId == bank.GeneralLedgerAccountId && item.PostingDate <= statement.EndsOn)
            .OrderBy(item => item.EntryNumber).ToListAsync();
        var matches = await db.BankStatementLines.AsNoTracking()
            .Where(item => item.CompanyId == company && item.GeneralLedgerEntryId != null)
            .ToDictionaryAsync(item => item.GeneralLedgerEntryId!.Value);
        var activity = entries.Where(entry => !matches.TryGetValue(entry.Id, out var match) || match.BankStatementId == statementId)
            .Select(entry => new BankActivity(entry.Id, entry.EntryNumber, entry.JournalBatchId, entry.PostingDate,
                entry.DocumentNumber, entry.Description, entry.DebitAmount - entry.CreditAmount,
                matches.TryGetValue(entry.Id, out var match) ? match.Id : null)).ToArray();
        return new BankStatementDetail(statement.Id, bank.Id, statement.Reference, statement.StartsOn, statement.EndsOn,
            statement.OpeningBalance, statement.ClosingBalance, statement.IsCompleted, statement.Revision,
            statement.ClosingBalance - statement.OpeningBalance - lines.Sum(line => line.Amount),
            statement.ClosingBalance - entries.Sum(entry => entry.DebitAmount - entry.CreditAmount), lines, activity);
    }

    public async Task AddLinesAsync(ClaimsPrincipal user, Guid statementId, int revision, IReadOnlyList<BankStatementLineInput> inputs)
    {
        var company = await CompanyAsync(user);
        if (inputs.Count is < 1 or > 1000) throw new ValidationException("Enter between 1 and 1000 statement lines.");
        foreach (var input in inputs)
        {
            input.Reference = input.Reference?.Trim() ?? string.Empty;
            input.Description = input.Description?.Trim() ?? string.Empty;
            Validate(input);
            Amount(input.Amount, false);
        }
        if (inputs.Select(item => item.Reference).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inputs.Count)
            throw new ValidationException("Statement line references must be unique.");
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var statement = await EditableStatementAsync(db, company, statementId, revision);
        var references = await db.BankStatementLines.Where(item => item.CompanyId == company && item.BankStatementId == statementId)
            .Select(item => item.Reference).ToListAsync();
        foreach (var input in inputs)
        {
            if (input.TransactionDate < statement.StartsOn || input.TransactionDate > statement.EndsOn)
                throw new ValidationException("Every transaction date must fall within the statement date range.");
            if (references.Contains(input.Reference, StringComparer.OrdinalIgnoreCase))
                throw new ValidationException("A statement line with this reference already exists.");
            db.BankStatementLines.Add(new BankStatementLine { Id = Guid.NewGuid(), CompanyId = company,
                BankStatementId = statementId, Reference = input.Reference, TransactionDate = input.TransactionDate,
                Description = input.Description, Amount = input.Amount });
        }
        statement.Revision++;
        Audit(db, user, company, "BankStatementLinesAdded", statement.Id);
        await SaveAsync(db, transaction);
    }

    public async Task ImportLinesAsync(ClaimsPrincipal user, Guid statementId, int revision, string text)
    {
        await CompanyAsync(user);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 400000)
            throw new ValidationException("Enter statement text (maximum 400000 characters).");
        var rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var inputs = new List<BankStatementLineInput>();
        foreach (var row in rows)
        {
            var fields = row.TrimEnd('\r').Split('\t');
            if (fields.Length != 4 ||
                !DateOnly.TryParseExact(fields[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                !decimal.TryParse(fields[3], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var amount))
                throw new ValidationException($"Invalid import row {inputs.Count + 1}. Use date, reference, description, signed amount separated by tabs.");
            inputs.Add(new BankStatementLineInput { TransactionDate = date, Reference = fields[1], Description = fields[2], Amount = amount });
        }
        await AddLinesAsync(user, statementId, revision, inputs);
    }

    public async Task RemoveLineAsync(ClaimsPrincipal user, Guid statementId, int revision, Guid lineId)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var statement = await EditableStatementAsync(db, company, statementId, revision);
        var line = await LineAsync(db, company, statementId, lineId);
        if (line.GeneralLedgerEntryId is not null) throw new ValidationException("Unmatch the line before removing it.");
        db.BankStatementLines.Remove(line);
        statement.Revision++;
        Audit(db, user, company, "BankStatementLineRemoved", lineId);
        await SaveAsync(db, transaction);
    }

    public async Task MatchAsync(ClaimsPrincipal user, Guid statementId, int revision, Guid lineId, Guid? entryId)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var statement = await EditableStatementAsync(db, company, statementId, revision);
        var line = await LineAsync(db, company, statementId, lineId);
        if (entryId is not null)
        {
            if (line.GeneralLedgerEntryId is not null) throw new ValidationException("Unmatch this line before selecting another entry.");
            var bank = await BankAsync(db, company, statement.BankAccountId);
            var entry = await db.GeneralLedgerEntries.SingleOrDefaultAsync(item => item.CompanyId == company &&
                item.Id == entryId && item.AccountId == bank.GeneralLedgerAccountId);
            if (entry is null || entry.PostingDate > statement.EndsOn ||
                entry.DebitAmount - entry.CreditAmount != line.Amount ||
                !await db.GeneralJournalBatches.AnyAsync(batch => batch.CompanyId == company &&
                    batch.Id == entry.JournalBatchId && batch.Status == JournalBatchStatus.Posted))
                throw new ValidationException("Select posted bank activity through the statement end date with the same signed amount.");
            if (await db.BankStatementLines.AnyAsync(item => item.CompanyId == company && item.GeneralLedgerEntryId == entryId))
                throw new ValidationException("This ledger entry is already matched.");
        }
        else if (line.GeneralLedgerEntryId is null) throw new ValidationException("This line is not matched.");
        var previousEntryId = line.GeneralLedgerEntryId;
        line.GeneralLedgerEntryId = entryId;
        statement.Revision++;
        Audit(db, user, company, entryId is null ? "BankLineUnmatched" : "BankLineMatched", lineId,
            new { BankStatementId = statementId, PreviousEntryId = previousEntryId, EntryId = entryId });
        await SaveAsync(db, transaction);
    }

    public async Task CompleteAsync(ClaimsPrincipal user, Guid statementId, int revision)
    {
        var company = await CompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await TransactionAsync(db);
        var statement = await EditableStatementAsync(db, company, statementId, revision);
        var lines = await db.BankStatementLines.Where(item => item.CompanyId == company && item.BankStatementId == statementId).ToListAsync();
        if (lines.Count == 0 || lines.Any(line => line.GeneralLedgerEntryId is null) ||
            statement.OpeningBalance + lines.Sum(line => line.Amount) != statement.ClosingBalance)
            throw new ValidationException("Match every statement line and resolve the statement balance difference before completing.");
        statement.IsCompleted = true;
        statement.Revision++;
        Audit(db, user, company, "BankStatementCompleted", statement.Id);
        await SaveAsync(db, transaction);
    }

    private async Task<Guid> CompanyAsync(ClaimsPrincipal user) =>
        (await authorizer.ResolveActiveCompanyAsync(user) ??
         throw new UnauthorizedAccessException("An active company is required to manage banking.")).CompanyId;

    private static async Task<BankAccount> BankAsync(ErpDbContext db, Guid company, Guid id) =>
        await db.BankAccounts.SingleOrDefaultAsync(item => item.CompanyId == company && item.Id == id)
        ?? throw new ValidationException("Bank account not found in the active company.");
    private static async Task<BankStatement> StatementAsync(ErpDbContext db, Guid company, Guid id) =>
        await db.BankStatements.SingleOrDefaultAsync(item => item.CompanyId == company && item.Id == id)
        ?? throw new ValidationException("Statement not found in the active company.");
    private static async Task<BankStatementLine> LineAsync(ErpDbContext db, Guid company, Guid statementId, Guid id) =>
        await db.BankStatementLines.SingleOrDefaultAsync(item => item.CompanyId == company && item.BankStatementId == statementId && item.Id == id)
        ?? throw new ValidationException("Statement line not found.");
    private static async Task<BankStatement> EditableStatementAsync(ErpDbContext db, Guid company, Guid id, int revision)
    {
        var statement = await StatementAsync(db, company, id);
        CheckRevision(statement.Revision, revision);
        if (statement.IsCompleted) throw new ValidationException("Completed statements cannot be changed.");
        return statement;
    }
    private static void CheckRevision(int actual, int expected)
    {
        if (actual != expected) throw new ValidationException("This record changed. Reload before trying again.");
    }
    private static async Task EligibleAccountAsync(ErpDbContext db, Guid company, Guid id, bool bank)
    {
        if (!await db.GeneralLedgerAccounts.AnyAsync(account => account.CompanyId == company && account.Id == id &&
            account.Type == AccountType.Posting && account.AllowsDirectPosting && !account.IsBlocked &&
            (!bank || account.Category == AccountCategory.Asset)))
            throw new ValidationException(bank
                ? "Select an unblocked, directly-postable Asset G/L account dedicated to bank/cash."
                : "Select an unblocked, directly-postable offset G/L account.");
    }
    private static async Task OperationalBankAsync(ErpDbContext db, Guid company, BankAccount bank)
    {
        if (bank.IsBlocked) throw new ValidationException("The bank account is blocked.");
        await EligibleAccountAsync(db, company, bank.GeneralLedgerAccountId, true);
        if (!await db.CompanySettings.AnyAsync(item => item.CompanyId == company && item.BaseCurrencyCode == bank.CurrencyCode) ||
            !await db.Currencies.AnyAsync(item => item.Code == bank.CurrencyCode && item.IsActive))
            throw new ValidationException("Bank currency must be the active company base currency. FX is not supported.");
    }
    internal static async Task ValidateJournalForPostingAsync(
        ErpDbContext db, Guid company, Guid batchId, IReadOnlyList<GeneralJournalLine> lines)
    {
        var journal = await db.BankJournals.SingleOrDefaultAsync(item => item.CompanyId == company && item.JournalBatchId == batchId);
        if (journal is null) return;
        var bank = await BankAsync(db, company, journal.BankAccountId);
        await OperationalBankAsync(db, company, bank);
        var bankLines = lines.Where(line => line.AccountId == bank.GeneralLedgerAccountId).ToArray();
        if (bankLines.Length != 1 || lines.Count < 2 ||
            lines.Any(line => line.DocumentNumber != journal.DocumentNumber) ||
            (journal.IsReceipt ? bankLines[0].DebitAmount : bankLines[0].CreditAmount) <= 0)
            throw new ValidationException("The linked bank journal must retain its document number and one correctly signed bank line.");
    }
    private static async Task UniqueAccountAsync(ErpDbContext db, Guid company, Guid id, BankAccountInput input)
    {
        if (await db.BankAccounts.AnyAsync(item => item.CompanyId == company && item.Id != id &&
            (item.Code == input.Code || item.GeneralLedgerAccountId == input.GeneralLedgerAccountId)))
            throw new ValidationException("Bank code and dedicated G/L account must be unique in the active company.");
    }
    private static void Normalize(BankAccountInput input)
    {
        input.Code = input.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.AccountIdentifier = input.AccountIdentifier?.Trim() ?? string.Empty;
        Validate(input);
    }
    private static void NormalizeStatement(BankStatementInput input)
    {
        input.Reference = input.Reference?.Trim() ?? string.Empty;
        Validate(input);
        Amount(input.OpeningBalance, true);
        Amount(input.ClosingBalance, true);
        if (input.StartsOn == default || input.EndsOn == default || input.StartsOn > input.EndsOn)
            throw new ValidationException("Enter a valid statement date range.");
    }
    private static void Apply(BankAccount bank, BankAccountInput input)
    {
        bank.Code = input.Code;
        bank.Name = input.Name;
        bank.AccountIdentifier = input.AccountIdentifier;
        bank.GeneralLedgerAccountId = input.GeneralLedgerAccountId;
        bank.IsBlocked = input.IsBlocked;
    }
    private static void Validate(object input) => Validator.ValidateObject(input, new ValidationContext(input), true);
    private static void Amount(decimal amount, bool allowZero)
    {
        if ((!allowZero && amount == 0) || amount > MaximumAmount || amount < -MaximumAmount || decimal.Round(amount, 4) != amount)
            throw new ValidationException("Amounts must fit decimal(19,4), with no more than four decimals and a nonzero line amount.");
    }
    private static void Audit(ErpDbContext db, ClaimsPrincipal user, Guid company, string action, Guid id, object? details = null) =>
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), CompanyId = company,
            ActorUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null,
            Action = action, EntityType = "Banking", EntityId = id.ToString(), OccurredAtUtc = DateTimeOffset.UtcNow,
            MetadataJson = details is null ? null : JsonSerializer.Serialize(details) });
    private static async Task<IDbContextTransaction?> TransactionAsync(ErpDbContext db) =>
        db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
    private static async Task SaveAsync(ErpDbContext db, IDbContextTransaction? transaction)
    {
        try
        {
            await db.SaveChangesAsync();
            if (transaction is not null) await transaction.CommitAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException("This record changed. Reload before trying again.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 1205 })
        {
            throw new ValidationException("A duplicate or concurrent banking operation occurred. Reload before trying again.", exception);
        }
    }
}
