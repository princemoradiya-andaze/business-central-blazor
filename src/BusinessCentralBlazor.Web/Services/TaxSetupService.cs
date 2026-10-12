using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessCentralBlazor.Web.Services;

public sealed class TaxCodeInput
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Description { get; set; } = string.Empty;

    public Guid LiabilityAccountId { get; set; }
    public Guid RecoverableAccountId { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class TaxRateInput
{
    public Guid TaxCodeId { get; set; }

    [Range(typeof(decimal), "0.0001", "99999.9999")]
    public decimal RatePercent { get; set; }

    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
}

public sealed record TaxRateListItem(
    Guid Id,
    decimal RatePercent,
    DateOnly StartsOn,
    DateOnly? EndsOn);

public sealed record TaxCodeListItem(
    Guid Id,
    string Code,
    string Description,
    Guid LiabilityAccountId,
    string LiabilityAccountNumber,
    string LiabilityAccountName,
    Guid RecoverableAccountId,
    string RecoverableAccountNumber,
    string RecoverableAccountName,
    bool IsBlocked,
    IReadOnlyList<TaxRateListItem> Rates);

public sealed record TaxAccountOption(
    Guid Id,
    string Number,
    string Name,
    AccountCategory Category,
    AccountType Type,
    bool AllowsDirectPosting,
    bool IsBlocked);

public interface ITaxSetupService
{
    Task<IReadOnlyList<TaxCodeListItem>> GetTaxCodesAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<TaxAccountOption>> GetAccountsAsync(ClaimsPrincipal user);
    Task<Guid> CreateTaxCodeAsync(ClaimsPrincipal user, TaxCodeInput input);
    Task<bool> UpdateTaxCodeAsync(ClaimsPrincipal user, Guid taxCodeId, TaxCodeInput input);
    Task<Guid> CreateRateAsync(ClaimsPrincipal user, TaxRateInput input);
    Task<bool> UpdateRateAsync(ClaimsPrincipal user, Guid rateId, TaxRateInput input);
}

public sealed class TaxSetupService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : ITaxSetupService
{
    public async Task<IReadOnlyList<TaxCodeListItem>> GetTaxCodesAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var codes = await dbContext.TaxCodes
            .AsNoTracking()
            .Where(code => code.CompanyId == company.CompanyId)
            .OrderBy(code => code.Code)
            .ToListAsync();
        if (codes.Count == 0)
        {
            return [];
        }

        var codeIds = codes.Select(code => code.Id).ToArray();
        var rates = await dbContext.TaxRates
            .AsNoTracking()
            .Where(rate => rate.CompanyId == company.CompanyId && codeIds.Contains(rate.TaxCodeId))
            .OrderBy(rate => rate.StartsOn)
            .ToListAsync();
        var accountIds = codes
            .SelectMany(code => new[] { code.LiabilityAccountId, code.RecoverableAccountId })
            .Distinct()
            .ToArray();
        var accounts = await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId && accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id);

        return codes.Select(code =>
        {
            var liability = accounts[code.LiabilityAccountId];
            var recoverable = accounts[code.RecoverableAccountId];
            return new TaxCodeListItem(
                code.Id,
                code.Code,
                code.Description,
                liability.Id,
                liability.Number,
                liability.Name,
                recoverable.Id,
                recoverable.Number,
                recoverable.Name,
                code.IsBlocked,
                rates.Where(rate => rate.TaxCodeId == code.Id)
                    .Select(rate => new TaxRateListItem(
                        rate.Id,
                        rate.RatePercent,
                        rate.StartsOn,
                        rate.EndsOn))
                    .ToArray());
        }).ToArray();
    }

    public async Task<IReadOnlyList<TaxAccountOption>> GetAccountsAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId)
            .OrderBy(account => account.Number)
            .Select(account => new TaxAccountOption(
                account.Id,
                account.Number,
                account.Name,
                account.Category,
                account.Type,
                account.AllowsDirectPosting,
                account.IsBlocked))
            .ToListAsync();
    }

    public async Task<Guid> CreateTaxCodeAsync(ClaimsPrincipal user, TaxCodeInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await ValidateCodeAsync(dbContext, company.CompanyId, input, null);
        var taxCode = new TaxCode
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Code = input.Code,
            Description = input.Description,
            LiabilityAccountId = input.LiabilityAccountId,
            RecoverableAccountId = input.RecoverableAccountId,
            IsBlocked = input.IsBlocked
        };
        dbContext.TaxCodes.Add(taxCode);
        await dbContext.SaveChangesAsync();
        return taxCode.Id;
    }

    public async Task<bool> UpdateTaxCodeAsync(
        ClaimsPrincipal user,
        Guid taxCodeId,
        TaxCodeInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var taxCode = await dbContext.TaxCodes.SingleOrDefaultAsync(code =>
            code.CompanyId == company.CompanyId && code.Id == taxCodeId);
        if (taxCode is null)
        {
            return false;
        }

        if (!string.Equals(taxCode.Code, input.Code, StringComparison.Ordinal) &&
            await dbContext.GeneralJournalLines.AnyAsync(line =>
                line.CompanyId == company.CompanyId && line.TaxCodeId == taxCodeId))
        {
            throw new ValidationException("A tax code referenced by journal lines cannot be renamed.");
        }

        await ValidateCodeAsync(dbContext, company.CompanyId, input, taxCodeId);
        taxCode.Code = input.Code;
        taxCode.Description = input.Description;
        taxCode.LiabilityAccountId = input.LiabilityAccountId;
        taxCode.RecoverableAccountId = input.RecoverableAccountId;
        taxCode.IsBlocked = input.IsBlocked;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<Guid> CreateRateAsync(ClaimsPrincipal user, TaxRateInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        await EnsureNoOverlappingRateAsync(dbContext, company.CompanyId, input, null);

        var taxRate = new TaxRate
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            TaxCodeId = input.TaxCodeId,
            RatePercent = input.RatePercent,
            StartsOn = input.StartsOn,
            EndsOn = input.EndsOn
        };
        dbContext.TaxRates.Add(taxRate);
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return taxRate.Id;
    }

    public async Task<bool> UpdateRateAsync(
        ClaimsPrincipal user,
        Guid rateId,
        TaxRateInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        var rate = await dbContext.TaxRates.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == rateId);
        if (rate is null)
        {
            return false;
        }

        if (rate.TaxCodeId != input.TaxCodeId)
        {
            throw new ValidationException("A tax rate cannot be moved to another tax code.");
        }

        if (await dbContext.GeneralJournalLines.AnyAsync(line =>
                line.CompanyId == company.CompanyId && line.TaxRateId == rateId))
        {
            throw new ValidationException("A rate referenced by a journal line cannot be changed; add a new effective-dated rate.");
        }

        await EnsureNoOverlappingRateAsync(dbContext, company.CompanyId, input, rateId);
        rate.RatePercent = input.RatePercent;
        rate.StartsOn = input.StartsOn;
        rate.EndsOn = input.EndsOn;
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return true;
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required to manage tax setup.");

    private static void NormalizeAndValidate(TaxCodeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Code = input.Code?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        ValidateFields(input);
    }

    private static void NormalizeAndValidate(TaxRateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateFields(input);
        if (input.TaxCodeId == Guid.Empty ||
            input.StartsOn == default ||
            input.EndsOn is { } endDate && endDate == default ||
            input.EndsOn is { } end && end < input.StartsOn ||
            decimal.Round(input.RatePercent, 4) != input.RatePercent)
        {
            throw new ValidationException("Enter a valid tax rate and effective-date range.");
        }
    }

    private static void ValidateFields(object input) =>
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

    private static async Task ValidateCodeAsync(
        ErpDbContext dbContext,
        Guid companyId,
        TaxCodeInput input,
        Guid? taxCodeId)
    {
        if (await dbContext.TaxCodes.AnyAsync(code =>
                code.CompanyId == companyId &&
                code.Code == input.Code &&
                (taxCodeId == null || code.Id != taxCodeId)))
        {
            throw new ValidationException("A tax code with this code already exists in the active company.");
        }

        if (input.LiabilityAccountId == input.RecoverableAccountId)
        {
            throw new ValidationException("Liability and recoverable tax mappings must use different accounts.");
        }

        await ValidateMappingAccountAsync(
            dbContext,
            companyId,
            input.LiabilityAccountId,
            AccountCategory.Liability,
            "liability");
        await ValidateMappingAccountAsync(
            dbContext,
            companyId,
            input.RecoverableAccountId,
            AccountCategory.Asset,
            "recoverable");
    }

    private static async Task ValidateMappingAccountAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid accountId,
        AccountCategory category,
        string mapping)
    {
        if (!await dbContext.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId &&
                account.Id == accountId &&
                account.Category == category &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked))
        {
            throw new ValidationException(
                $"Select an unblocked, directly-postable {category} posting account from the active company for the {mapping} tax mapping.");
        }
    }

    private static async Task EnsureNoOverlappingRateAsync(
        ErpDbContext dbContext,
        Guid companyId,
        TaxRateInput input,
        Guid? excludedRateId)
    {
        if (!await dbContext.TaxCodes.AnyAsync(code =>
                code.CompanyId == companyId && code.Id == input.TaxCodeId))
        {
            throw new ValidationException("Select a tax code from the active company.");
        }

        var endsOn = input.EndsOn ?? DateOnly.MaxValue;
        if (await dbContext.TaxRates.AnyAsync(rate =>
                rate.CompanyId == companyId &&
                rate.TaxCodeId == input.TaxCodeId &&
                (excludedRateId == null || rate.Id != excludedRateId) &&
                rate.StartsOn <= endsOn &&
                (rate.EndsOn == null || rate.EndsOn >= input.StartsOn)))
        {
            throw new ValidationException("Tax-rate effective dates cannot overlap for this code.");
        }
    }

    private static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(ErpDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    }
}
