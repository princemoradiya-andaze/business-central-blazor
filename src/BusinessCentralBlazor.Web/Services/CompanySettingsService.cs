using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed record CompanyCurrencyOption(string Code, string Name, int MinorUnit);

public sealed record CompanySettingsDetail(string? BaseCurrencyCode, IReadOnlyList<CompanyCurrencyOption> Currencies);

public interface ICompanySettingsService
{
    Task<CompanySettingsDetail> GetAsync(ClaimsPrincipal user);
    Task<string> CreateCurrencyAsync(ClaimsPrincipal user, string code, string name, int minorUnit);
    Task SaveBaseCurrencyAsync(ClaimsPrincipal user, string currencyCode);
}

public sealed class CompanySettingsService(
    IDbContextFactory<ErpDbContext> factory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : ICompanySettingsService
{
    public async Task<CompanySettingsDetail> GetAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var db = await factory.CreateDbContextAsync();
        var settings = await db.CompanySettings.AsNoTracking()
            .Where(item => item.CompanyId == company.CompanyId)
            .Select(item => item.BaseCurrencyCode)
            .SingleOrDefaultAsync();
        var currencies = await db.Currencies.AsNoTracking()
            .Where(currency => currency.IsActive)
            .OrderBy(currency => currency.Code)
            .Select(currency => new CompanyCurrencyOption(currency.Code, currency.Name, currency.MinorUnit))
            .ToListAsync();
        return new CompanySettingsDetail(settings, currencies);
    }

    public async Task<string> CreateCurrencyAsync(ClaimsPrincipal user, string code, string name, int minorUnit)
    {
        var company = await GetRequiredCompanyAsync(user);
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedCode.Length != 3 || normalizedCode.Any(character => !char.IsAsciiLetter(character)) ||
            normalizedName.Length is < 1 or > 80 || minorUnit is < 0 or > 4)
        {
            throw new ValidationException("Enter a three-letter code, a name of 1–80 characters, and 0–4 minor units.");
        }

        await using var db = await factory.CreateDbContextAsync();
        if (await db.Currencies.AnyAsync(currency => currency.Code == normalizedCode))
        {
            throw new ValidationException("A currency with this code already exists in the shared catalog.");
        }

        db.Currencies.Add(new Currency
        {
            Code = normalizedCode,
            Name = normalizedName,
            MinorUnit = minorUnit,
            IsActive = true
        });
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            ActorUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null,
            Action = "CurrencyCreated",
            EntityType = "Currency",
            EntityId = normalizedCode,
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return normalizedCode;
    }

    public async Task SaveBaseCurrencyAsync(ClaimsPrincipal user, string currencyCode)
    {
        var company = await GetRequiredCompanyAsync(user);
        var normalizedCode = currencyCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalizedCode.Length != 3 || normalizedCode.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ValidationException("Select an active three-letter currency code.");
        }

        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Currencies.AnyAsync(currency => currency.Code == normalizedCode && currency.IsActive))
        {
            throw new ValidationException("Select an active currency.");
        }

        var settings = await db.CompanySettings.SingleOrDefaultAsync(item => item.CompanyId == company.CompanyId);
        if (settings is null)
        {
            settings = new CompanySettings
            {
                CompanyId = company.CompanyId,
                BaseCurrencyCode = normalizedCode
            };
            db.CompanySettings.Add(settings);
        }
        else if (!string.Equals(settings.BaseCurrencyCode, normalizedCode, StringComparison.Ordinal))
        {
            if (await db.BankAccounts.AnyAsync(item => item.CompanyId == company.CompanyId) ||
                await db.GeneralJournalLines.AnyAsync(item => item.CompanyId == company.CompanyId) ||
                await db.GeneralLedgerEntries.AnyAsync(item => item.CompanyId == company.CompanyId) ||
                await db.CurrencyRates.AnyAsync(item => item.CompanyId == company.CompanyId))
            {
                throw new ValidationException(
                    "The base currency cannot be changed after journal lines, bank accounts, ledger entries, or currency rates exist.");
            }

            settings.BaseCurrencyCode = normalizedCode;
        }
        else
        {
            return;
        }

        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            ActorUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null,
            Action = "BaseCurrencyConfigured",
            EntityType = "CompanySettings",
            EntityId = company.CompanyId.ToString(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { BaseCurrencyCode = normalizedCode })
        });
        await db.SaveChangesAsync();
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required to manage company settings.");
}
