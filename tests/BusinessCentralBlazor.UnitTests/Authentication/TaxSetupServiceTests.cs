using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class TaxSetupServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task TaxCodesAndRatesAreCompanyScopedAndRejectOverlappingOrInvalidDates()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("tax-rates-owner");
        var (otherPrincipal, otherCompanyId) = await CreateUserAndCompanyAsync("tax-rates-other");
        var liabilityAccountId = await AddAccountAsync(companyId, "2100", AccountCategory.Liability);
        var recoverableAccountId = await AddAccountAsync(companyId, "1200", AccountCategory.Asset);
        var otherLiabilityAccountId = await AddAccountAsync(otherCompanyId, "2100", AccountCategory.Liability);
        var otherRecoverableAccountId = await AddAccountAsync(otherCompanyId, "1200", AccountCategory.Asset);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxSetupService>();
        var taxCodeId = await service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "VAT",
            Description = "Value added tax",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = recoverableAccountId
        });

        var firstRateId = await service.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 5m,
            StartsOn = new DateOnly(2026, 1, 1),
            EndsOn = new DateOnly(2026, 6, 30)
        });
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 6, 30),
            EndsOn = new DateOnly(2026, 12, 31)
        }));

        var secondRateId = await service.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 7, 1),
            EndsOn = new DateOnly(2026, 12, 31)
        });
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 0m,
            StartsOn = new DateOnly(2027, 1, 1)
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 7m,
            StartsOn = new DateOnly(2027, 2, 1),
            EndsOn = new DateOnly(2027, 1, 31)
        }));

        var codes = await service.GetTaxCodesAsync(principal);
        var code = Assert.Single(codes);
        Assert.Equal("VAT", code.Code);
        Assert.Equal([firstRateId, secondRateId], code.Rates.Select(rate => rate.Id));
        Assert.Empty(await service.GetTaxCodesAsync(otherPrincipal));

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "CROSS",
            Description = "Cross-company accounts",
            LiabilityAccountId = otherLiabilityAccountId,
            RecoverableAccountId = otherRecoverableAccountId
        }));
    }

    [Fact]
    public async Task TaxMappingsRejectWrongCategoriesBlockedAndNonPostingAccounts()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("tax-mapping-rules");
        var liabilityAccountId = await AddAccountAsync(companyId, "2100", AccountCategory.Liability);
        var recoverableAccountId = await AddAccountAsync(companyId, "1200", AccountCategory.Asset);
        var blockedAccountId = await AddAccountAsync(companyId, "2110", AccountCategory.Liability, isBlocked: true);
        var headingAccountId = await AddAccountAsync(
            companyId,
            "2120",
            AccountCategory.Liability,
            type: AccountType.Heading);
        var wrongCategoryAccountId = await AddAccountAsync(companyId, "4000", AccountCategory.Income);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITaxSetupService>();

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "BLOCKED",
            Description = "Blocked mapping",
            LiabilityAccountId = blockedAccountId,
            RecoverableAccountId = recoverableAccountId
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "HEADING",
            Description = "Non-posting mapping",
            LiabilityAccountId = headingAccountId,
            RecoverableAccountId = recoverableAccountId
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "CATEGORY",
            Description = "Wrong category",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = wrongCategoryAccountId
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "SAME",
            Description = "Same account",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = liabilityAccountId
        }));
    }

    [Fact]
    public async Task EffectiveTaxRateCannotBeChangedAfterJournalUse()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("tax-rate-use");
        var liabilityAccountId = await AddAccountAsync(companyId, "2100", AccountCategory.Liability);
        var recoverableAccountId = await AddAccountAsync(companyId, "1200", AccountCategory.Asset);
        await using var scope = factory.Services.CreateAsyncScope();
        var taxSetup = scope.ServiceProvider.GetRequiredService<ITaxSetupService>();
        var taxCodeId = await taxSetup.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "SALES",
            Description = "Sales tax",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = recoverableAccountId
        });
        var rateId = await taxSetup.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 10m,
            StartsOn = new DateOnly(2026, 1, 1)
        });

        await using var dbScope = factory.Services.CreateAsyncScope();
        var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        dbContext.GeneralJournalLines.Add(new GeneralJournalLine
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            JournalBatchId = Guid.NewGuid(),
            AccountingPeriodId = Guid.NewGuid(),
            AccountId = liabilityAccountId,
            LineNumber = 10000,
            PostingDate = new DateOnly(2026, 10, 1),
            DocumentNumber = "TAX-1",
            Description = "Tax line",
            CreditAmount = 10m,
            TaxCodeId = taxCodeId,
            TaxRateId = rateId,
            TaxRatePercent = 10m,
            TaxBaseAmount = 100m,
            TaxPostingType = TaxPostingType.Liability
        });
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => taxSetup.UpdateRateAsync(principal, rateId, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 11m,
            StartsOn = new DateOnly(2026, 1, 1)
        }));
    }

    private async Task<(ClaimsPrincipal Principal, Guid CompanyId)> CreateUserAndCompanyAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(
            email,
            $"T{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            $"{prefix} company");
        return (await CreatePrincipalAsync(email, company.CompanyId), company.CompanyId);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
    }

    private async Task<Guid> AddAccountAsync(
        Guid companyId,
        string number,
        AccountCategory category,
        AccountType type = AccountType.Posting,
        bool isBlocked = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var account = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = number,
            Name = $"Account {number}",
            Category = category,
            Type = type,
            AllowsDirectPosting = type == AccountType.Posting,
            IsBlocked = isBlocked
        };
        dbContext.GeneralLedgerAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
