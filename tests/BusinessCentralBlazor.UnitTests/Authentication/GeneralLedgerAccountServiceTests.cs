using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class GeneralLedgerAccountServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task AccountsCanBeCreatedListedAndUpdatedWithinActiveCompany()
    {
        var email = $"gl-accounts-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "GLACCT", "G/L Account Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();

        var parentId = await service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = " 1000 ",
            Name = " Cash ",
            Category = AccountCategory.Asset,
            Type = AccountType.Heading,
            AllowsDirectPosting = false
        });
        var childId = await service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "1010",
            Name = "Operating cash",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            ParentAccountId = parentId
        });

        var accounts = await service.GetAllAsync(principal);
        Assert.Equal(2, accounts.Count);
        Assert.Equal("1000", accounts[0].Number);
        Assert.Equal("Cash", accounts[0].Name);
        Assert.Equal("1000", accounts[1].ParentAccountNumber);

        Assert.True(await service.UpdateAsync(principal, childId, new GeneralLedgerAccountInput
        {
            Number = "1010",
            Name = "Operating cash",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            ParentAccountId = parentId,
            IsBlocked = true,
            AllowsDirectPosting = false
        }));

        var updated = await service.GetAsync(principal, childId);
        Assert.NotNull(updated);
        Assert.True(updated.IsBlocked);
        Assert.False(updated.AllowsDirectPosting);
    }

    [Fact]
    public async Task AccountsAndParentReferencesAreIsolatedBetweenCompanies()
    {
        var ownerEmail = $"gl-account-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"gl-account-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(ownerEmail, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownerCompany = await factory.AddCompanyMembershipAsync(ownerEmail, "GLOWNER", "Owner Company");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "GLOTHER", "Other Company");
        var ownerPrincipal = await CreatePrincipalAsync(ownerEmail, ownerCompany.CompanyId);
        var otherPrincipal = await CreatePrincipalAsync(otherEmail, otherCompany.CompanyId);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
        var ownerAccountId = await service.CreateAsync(ownerPrincipal, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Owner account",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting
        });
        var sameNumberInOtherCompanyId = await service.CreateAsync(
            otherPrincipal,
            new GeneralLedgerAccountInput
            {
                Number = "1000",
                Name = "Other company's account",
                Category = AccountCategory.Asset,
                Type = AccountType.Posting
            });

        Assert.Equal("1000", Assert.Single(await service.GetAllAsync(otherPrincipal)).Number);
        Assert.NotEqual(ownerAccountId, sameNumberInOtherCompanyId);
        Assert.Null(await service.GetAsync(otherPrincipal, ownerAccountId));
        Assert.False(await service.UpdateAsync(otherPrincipal, ownerAccountId, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Tampered account",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            otherPrincipal,
            new GeneralLedgerAccountInput
            {
                Number = "1001",
                Name = "Cross-company parent",
                Category = AccountCategory.Asset,
                Type = AccountType.Posting,
                ParentAccountId = ownerAccountId
            }));
        Assert.Equal("Owner account", (await service.GetAsync(ownerPrincipal, ownerAccountId))?.Name);
    }

    [Fact]
    public async Task DuplicateNumbersAndHierarchyCyclesAreRejectedWithoutChangingAccounts()
    {
        var email = $"gl-account-rules-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "GLRULES", "G/L Rules Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();

        var parentId = await service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Parent",
            Category = AccountCategory.Asset,
            Type = AccountType.Heading
        });
        var childId = await service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "1010",
            Name = "Child",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            ParentAccountId = parentId
        });

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Duplicate",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(principal, parentId, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Parent",
            Category = AccountCategory.Asset,
            Type = AccountType.Heading,
            ParentAccountId = parentId
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(principal, parentId, new GeneralLedgerAccountInput
        {
            Number = "1000",
            Name = "Parent",
            Category = AccountCategory.Asset,
            Type = AccountType.Heading,
            ParentAccountId = childId
        }));

        var parent = await service.GetAsync(principal, parentId);
        Assert.NotNull(parent);
        Assert.Null(parent.ParentAccountId);
    }

    [Fact]
    public async Task ServiceRejectsMissingCompanyAndInvalidAccountValues()
    {
        var email = $"gl-account-validation-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "GLVALID", "G/L Validation Company");
        var userId = await GetUserIdAsync(email);
        var noCompanyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAllAsync(noCompanyPrincipal));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "  ",
            Name = "Account",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(principal, new GeneralLedgerAccountInput
        {
            Number = "9999",
            Name = "Invalid category",
            Category = (AccountCategory)999,
            Type = AccountType.Posting
        }));
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        var userId = await GetUserIdAsync(email);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }
}
