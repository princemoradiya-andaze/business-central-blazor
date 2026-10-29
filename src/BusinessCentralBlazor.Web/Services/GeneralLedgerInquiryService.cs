using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed class GeneralLedgerEntryFilter
{
    public DateOnly? FromDate { get; set; }
    public DateOnly? ThroughDate { get; set; }
    public Guid? AccountId { get; set; }

    [StringLength(64)]
    public string? DocumentNumber { get; set; }
}

public sealed record GeneralLedgerAccountOption(Guid Id, string Number, string Name);

public sealed record GeneralLedgerEntryListItem(
    Guid Id,
    long EntryNumber,
    DateOnly PostingDate,
    Guid AccountId,
    string AccountNumber,
    string AccountName,
    string DocumentNumber,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    Guid JournalBatchId = default);

public sealed record GeneralLedgerEntryReport(
    IReadOnlyList<GeneralLedgerEntryListItem> Entries,
    decimal TotalDebit,
    decimal TotalCredit);

public sealed record GeneralLedgerAccountActivityReport(
    Guid AccountId,
    string AccountNumber,
    string AccountName,
    DateOnly? FromDate,
    DateOnly? ThroughDate,
    decimal OpeningDebit,
    decimal OpeningCredit,
    decimal PeriodDebit,
    decimal PeriodCredit,
    decimal ClosingDebit,
    decimal ClosingCredit);

public sealed record TrialBalanceLine(
    Guid AccountId,
    string AccountNumber,
    string AccountName,
    AccountCategory Category,
    decimal DebitBalance,
    decimal CreditBalance);

public sealed record TrialBalanceReport(
    DateOnly AsOfDate,
    IReadOnlyList<TrialBalanceLine> Lines,
    decimal TotalDebit,
    decimal TotalCredit);

public interface IGeneralLedgerInquiryService
{
    Task<IReadOnlyList<GeneralLedgerAccountOption>> GetAccountsAsync(ClaimsPrincipal user);
    Task<GeneralLedgerEntryReport> GetEntriesAsync(
        ClaimsPrincipal user,
        GeneralLedgerEntryFilter filter);
    Task<GeneralLedgerAccountActivityReport?> GetAccountActivityAsync(
        ClaimsPrincipal user,
        Guid accountId,
        DateOnly? fromDate,
        DateOnly? throughDate);
    Task<TrialBalanceReport> GetTrialBalanceAsync(ClaimsPrincipal user, DateOnly asOfDate);
    Task<TrialBalanceReport> GetTrialBalanceAsync(ClaimsPrincipal user, TrialBalanceRequest request);
}

public sealed class GeneralLedgerInquiryService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IGeneralLedgerInquiryService
{
    public async Task<IReadOnlyList<GeneralLedgerAccountOption>> GetAccountsAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId)
            .OrderBy(account => account.Number)
            .Select(account => new GeneralLedgerAccountOption(account.Id, account.Number, account.Name))
            .ToListAsync();
    }

    public async Task<GeneralLedgerEntryReport> GetEntriesAsync(
        ClaimsPrincipal user,
        GeneralLedgerEntryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ValidateDateRange(filter.FromDate, filter.ThroughDate);
        filter.DocumentNumber = filter.DocumentNumber?.Trim();
        Validator.ValidateObject(filter, new ValidationContext(filter), validateAllProperties: true);

        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var query =
            from entry in dbContext.GeneralLedgerEntries.AsNoTracking()
            join account in dbContext.GeneralLedgerAccounts.AsNoTracking()
                on new { entry.CompanyId, Id = entry.AccountId } equals new { account.CompanyId, account.Id }
            where entry.CompanyId == company.CompanyId
            select new { Entry = entry, Account = account };

        if (filter.FromDate is { } fromDate)
        {
            query = query.Where(item => item.Entry.PostingDate >= fromDate);
        }

        if (filter.ThroughDate is { } throughDate)
        {
            query = query.Where(item => item.Entry.PostingDate <= throughDate);
        }

        if (filter.AccountId is { } accountId)
        {
            query = query.Where(item => item.Entry.AccountId == accountId);
        }

        if (!string.IsNullOrEmpty(filter.DocumentNumber))
        {
            query = query.Where(item => item.Entry.DocumentNumber.Contains(filter.DocumentNumber));
        }

        var entries = await query
            .OrderByDescending(item => item.Entry.PostingDate)
            .ThenByDescending(item => item.Entry.EntryNumber)
            .Select(item => new GeneralLedgerEntryListItem(
                item.Entry.Id,
                item.Entry.EntryNumber,
                item.Entry.PostingDate,
                item.Entry.AccountId,
                item.Account.Number,
                item.Account.Name,
                item.Entry.DocumentNumber,
                item.Entry.Description,
                item.Entry.DebitAmount,
                item.Entry.CreditAmount,
                item.Entry.JournalBatchId))
            .ToListAsync();

        return new GeneralLedgerEntryReport(
            entries,
            entries.Sum(entry => entry.DebitAmount),
            entries.Sum(entry => entry.CreditAmount));
    }

    public async Task<GeneralLedgerAccountActivityReport?> GetAccountActivityAsync(
        ClaimsPrincipal user,
        Guid accountId,
        DateOnly? fromDate,
        DateOnly? throughDate)
    {
        ValidateDateRange(fromDate, throughDate);
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var account = await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(item => item.CompanyId == company.CompanyId && item.Id == accountId)
            .Select(item => new { item.Id, item.Number, item.Name })
            .SingleOrDefaultAsync();
        if (account is null)
        {
            return null;
        }

        var entries = dbContext.GeneralLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == company.CompanyId && entry.AccountId == accountId);
        var openingEntries = fromDate is { } startDate
            ? entries.Where(entry => entry.PostingDate < startDate)
            : entries.Where(_ => false);
        var periodEntries = entries;
        if (fromDate is { } periodStart)
        {
            periodEntries = periodEntries.Where(entry => entry.PostingDate >= periodStart);
        }

        if (throughDate is { } periodEnd)
        {
            periodEntries = periodEntries.Where(entry => entry.PostingDate <= periodEnd);
        }

        var openingDebit = await openingEntries.SumAsync(entry => entry.DebitAmount);
        var openingCredit = await openingEntries.SumAsync(entry => entry.CreditAmount);
        var periodDebit = await periodEntries.SumAsync(entry => entry.DebitAmount);
        var periodCredit = await periodEntries.SumAsync(entry => entry.CreditAmount);
        var closingBalance = openingDebit - openingCredit + periodDebit - periodCredit;

        return new GeneralLedgerAccountActivityReport(
            account.Id,
            account.Number,
            account.Name,
            fromDate,
            throughDate,
            Math.Max(openingDebit - openingCredit, 0),
            Math.Max(openingCredit - openingDebit, 0),
            periodDebit,
            periodCredit,
            Math.Max(closingBalance, 0),
            Math.Max(-closingBalance, 0));
    }

    public Task<TrialBalanceReport> GetTrialBalanceAsync(ClaimsPrincipal user, DateOnly asOfDate) =>
        GetTrialBalanceAsync(user, new TrialBalanceRequest { AsOfDate = asOfDate });

    public async Task<TrialBalanceReport> GetTrialBalanceAsync(ClaimsPrincipal user, TrialBalanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var entries = dbContext.GeneralLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == company.CompanyId && entry.PostingDate <= request.AsOfDate);
        var document = request.DocumentNumber?.Trim();
        if (!string.IsNullOrEmpty(document))
        {
            entries = entries.Where(entry => entry.DocumentNumber.Contains(document));
        }

        var accountBalances = await entries
            .GroupBy(entry => entry.AccountId)
            .Select(group => new
            {
                AccountId = group.Key,
                Debit = group.Sum(entry => entry.DebitAmount),
                Credit = group.Sum(entry => entry.CreditAmount)
            })
            .ToDictionaryAsync(balance => balance.AccountId);
        var accountQuery = dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId && account.Type == AccountType.Posting);
        if (request.AccountId is { } accountId)
        {
            accountQuery = accountQuery.Where(account => account.Id == accountId);
        }

        var name = request.AccountName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            accountQuery = accountQuery.Where(account => account.Name.Contains(name));
        }

        if (request.Category is { } category)
        {
            accountQuery = accountQuery.Where(account => account.Category == category);
        }

        var accounts = await accountQuery
            .OrderBy(account => account.Number)
            .Select(account => new
            {
                account.Id,
                account.Number,
                account.Name,
                account.Category
            })
            .ToListAsync();
        var lines = accounts.Select(account =>
        {
            accountBalances.TryGetValue(account.Id, out var balance);
            var netBalance = (balance?.Debit ?? 0) - (balance?.Credit ?? 0);
            return new TrialBalanceLine(
                account.Id,
                account.Number,
                account.Name,
                account.Category,
                Math.Max(netBalance, 0),
                Math.Max(-netBalance, 0));
        }).Where(line => request.IncludeZeroBalances || line.DebitBalance != 0 || line.CreditBalance != 0).ToArray();

        if (lines.Length > request.MaximumRows)
        {
            throw new ValidationException(
                $"Report contains {lines.Length} rows, exceeding the maximum of {request.MaximumRows}. Increase the limit or narrow the filters. No partial totals are generated.");
        }

        return new TrialBalanceReport(
            request.AsOfDate,
            lines,
            lines.Sum(line => line.DebitBalance),
            lines.Sum(line => line.CreditBalance));
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required for general-ledger inquiry.");

    private static void ValidateDateRange(DateOnly? fromDate, DateOnly? throughDate)
    {
        if (fromDate is { } startDate && throughDate is { } endDate && startDate > endDate)
        {
            throw new ValidationException("The start date must be on or before the end date.");
        }
    }
}
