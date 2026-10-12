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

public sealed class ManualJournalTaxPostingTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";
    private static readonly DateOnly PostingDate = new(2026, 10, 12);

    [Fact]
    public async Task ManualLiabilityAndRecoverableTaxLinesPostAuditableEntriesAndReverseExactly()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-tax-post");
        var period = await CreatePeriodAsync(principal);
        var liabilityAccountId = await AddAccountAsync(companyId, "2100", AccountCategory.Liability);
        var recoverableAccountId = await AddAccountAsync(companyId, "1200", AccountCategory.Asset);
        var clearingAccountId = await AddAccountAsync(companyId, "1000", AccountCategory.Asset);
        await using var setupScope = factory.Services.CreateAsyncScope();
        var taxSetup = setupScope.ServiceProvider.GetRequiredService<ITaxSetupService>();
        var taxCodeId = await taxSetup.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "VAT",
            Description = "Value added tax",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = recoverableAccountId
        });
        var rateId = await taxSetup.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 1, 1)
        });

        await using var journalScope = factory.Services.CreateAsyncScope();
        var journal = journalScope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var liabilityBatchId = await journal.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "VAT-OUTPUT",
            DefaultAccountingPeriodId = period.Id
        });
        await journal.CreateLineAsync(principal, liabilityBatchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = clearingAccountId,
            PostingDate = PostingDate,
            DocumentNumber = "VAT-OUT-1",
            Description = "Tax balancing line",
            DebitAmount = 8.25m
        });
        var taxLineId = await journal.CreateLineAsync(principal, liabilityBatchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = period.Id,
            AccountId = liabilityAccountId,
            PostingDate = PostingDate,
            DocumentNumber = "VAT-OUT-1",
            Description = "Manual output tax",
            CreditAmount = 8.25m,
            TaxCodeId = taxCodeId,
            TaxBaseAmount = 100m
        });
        var openBatch = await journal.GetBatchAsync(principal, liabilityBatchId);
        var openTaxLine = Assert.Single(openBatch!.Lines, line => line.Id == taxLineId);
        Assert.Equal("VAT", openTaxLine.TaxCode);
        Assert.Equal(8.25m, openTaxLine.TaxRatePercent);
        Assert.Equal(100m, openTaxLine.TaxBaseAmount);
        Assert.Equal(TaxPostingType.Liability, openTaxLine.TaxPostingType);

        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var liabilityAccount = await dbContext.GeneralLedgerAccounts.SingleAsync(account =>
                account.CompanyId == companyId && account.Id == liabilityAccountId);
            liabilityAccount.IsBlocked = true;
            await dbContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ValidationException>(() => journal.PostBatchAsync(principal, liabilityBatchId));

        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var liabilityAccount = await dbContext.GeneralLedgerAccounts.SingleAsync(account =>
                account.CompanyId == companyId && account.Id == liabilityAccountId);
            liabilityAccount.IsBlocked = false;
            await dbContext.SaveChangesAsync();
        }

        await journal.PostBatchAsync(principal, liabilityBatchId);
        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var postedTaxEntry = await dbContext.GeneralLedgerEntries.SingleAsync(entry =>
                entry.CompanyId == companyId &&
                entry.JournalBatchId == liabilityBatchId &&
                entry.JournalLineId == taxLineId);
            Assert.Equal(liabilityAccountId, postedTaxEntry.AccountId);
            Assert.Equal(taxCodeId, postedTaxEntry.TaxCodeId);
            Assert.Equal(rateId, postedTaxEntry.TaxRateId);
            Assert.Equal(8.25m, postedTaxEntry.TaxRatePercent);
            Assert.Equal(100m, postedTaxEntry.TaxBaseAmount);
            Assert.Equal(TaxPostingType.Liability, postedTaxEntry.TaxPostingType);
            Assert.Equal(8.25m, postedTaxEntry.CreditAmount);
        }

        var reversalBatchId = await journal.ReverseBatchAsync(principal, liabilityBatchId, new GeneralJournalReversalInput
        {
            Name = "REV-VAT-OUTPUT",
            AccountingPeriodId = period.Id,
            PostingDate = new DateOnly(2026, 10, 13)
        });
        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var reversedTaxEntry = await dbContext.GeneralLedgerEntries.SingleAsync(entry =>
                entry.CompanyId == companyId &&
                entry.JournalBatchId == reversalBatchId &&
                entry.TaxCodeId == taxCodeId);
            Assert.Equal(liabilityAccountId, reversedTaxEntry.AccountId);
            Assert.Equal(rateId, reversedTaxEntry.TaxRateId);
            Assert.Equal(100m, reversedTaxEntry.TaxBaseAmount);
            Assert.Equal(8.25m, reversedTaxEntry.DebitAmount);
            Assert.Equal(0m, reversedTaxEntry.CreditAmount);
            Assert.Equal(TaxPostingType.Liability, reversedTaxEntry.TaxPostingType);
        }

        var recoverableBatchId = await journal.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "VAT-INPUT",
            DefaultAccountingPeriodId = period.Id
        });
        await journal.CreateLineAsync(principal, recoverableBatchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = recoverableAccountId,
            PostingDate = PostingDate,
            DocumentNumber = "VAT-IN-1",
            Description = "Manual recoverable tax",
            DebitAmount = 8.25m,
            TaxCodeId = taxCodeId,
            TaxBaseAmount = 100m
        });
        await journal.CreateLineAsync(principal, recoverableBatchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = period.Id,
            AccountId = clearingAccountId,
            PostingDate = PostingDate,
            DocumentNumber = "VAT-IN-1",
            Description = "Tax balancing line",
            CreditAmount = 8.25m
        });
        await journal.PostBatchAsync(principal, recoverableBatchId);
        await using (var dbScope = factory.Services.CreateAsyncScope())
        {
            var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var recoverableEntry = await dbContext.GeneralLedgerEntries.SingleAsync(entry =>
                entry.CompanyId == companyId &&
                entry.JournalBatchId == recoverableBatchId &&
                entry.TaxCodeId == taxCodeId);
            Assert.Equal(recoverableAccountId, recoverableEntry.AccountId);
            Assert.Equal(TaxPostingType.Recoverable, recoverableEntry.TaxPostingType);
            Assert.Equal(8.25m, recoverableEntry.DebitAmount);
        }
    }

    [Fact]
    public async Task ManualTaxLinesRejectWrongAmountsCrossCompanyCodesAndIneffectiveRates()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-tax-validation");
        var (otherPrincipal, otherCompanyId) = await CreateUserAndCompanyAsync("journal-tax-other");
        var period = await CreatePeriodAsync(principal);
        var liabilityAccountId = await AddAccountAsync(companyId, "2100", AccountCategory.Liability);
        var recoverableAccountId = await AddAccountAsync(companyId, "1200", AccountCategory.Asset);
        var otherLiabilityAccountId = await AddAccountAsync(otherCompanyId, "2100", AccountCategory.Liability);
        var otherRecoverableAccountId = await AddAccountAsync(otherCompanyId, "1200", AccountCategory.Asset);
        var creditAccountId = await AddAccountAsync(companyId, "4000", AccountCategory.Income);

        await using var setupScope = factory.Services.CreateAsyncScope();
        var taxSetup = setupScope.ServiceProvider.GetRequiredService<ITaxSetupService>();
        var taxCodeId = await taxSetup.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "LOCAL",
            Description = "Local tax",
            LiabilityAccountId = liabilityAccountId,
            RecoverableAccountId = recoverableAccountId
        });
        var otherTaxCodeId = await taxSetup.CreateTaxCodeAsync(otherPrincipal, new TaxCodeInput
        {
            Code = "PRIVATE",
            Description = "Other company tax",
            LiabilityAccountId = otherLiabilityAccountId,
            RecoverableAccountId = otherRecoverableAccountId
        });
        await taxSetup.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 11, 1)
        });

        await using var journalScope = factory.Services.CreateAsyncScope();
        var journal = journalScope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batchId = await journal.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "TAX-RULES",
            DefaultAccountingPeriodId = period.Id
        });
        var futureRateLine = new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = liabilityAccountId,
            PostingDate = PostingDate,
            DocumentNumber = "TAX-RULES-1",
            Description = "No effective rate",
            CreditAmount = 8.25m,
            TaxCodeId = taxCodeId,
            TaxBaseAmount = 100m
        };
        var noRate = await Assert.ThrowsAsync<ValidationException>(() =>
            journal.CreateLineAsync(principal, batchId, futureRateLine));
        Assert.Contains("effective", noRate.Message, StringComparison.OrdinalIgnoreCase);

        await taxSetup.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 1, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        var wrongAmount = await Assert.ThrowsAsync<ValidationException>(() =>
            journal.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
            {
                LineNumber = 10000,
                AccountingPeriodId = period.Id,
                AccountId = liabilityAccountId,
                PostingDate = PostingDate,
                DocumentNumber = "TAX-RULES-1",
                Description = "Wrong amount",
                CreditAmount = 8.24m,
                TaxCodeId = taxCodeId,
                TaxBaseAmount = 100m
            }));
        Assert.Contains("tax line amount", wrongAmount.Message, StringComparison.OrdinalIgnoreCase);

        var wrongMapping = await Assert.ThrowsAsync<ValidationException>(() =>
            journal.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
            {
                LineNumber = 10000,
                AccountingPeriodId = period.Id,
                AccountId = creditAccountId,
                PostingDate = PostingDate,
                DocumentNumber = "TAX-RULES-1",
                Description = "Wrong mapping",
                CreditAmount = 8.25m,
                TaxCodeId = taxCodeId,
                TaxBaseAmount = 100m
            }));
        Assert.Contains("configured posting account", wrongMapping.Message, StringComparison.OrdinalIgnoreCase);

        var crossCompany = await Assert.ThrowsAsync<ValidationException>(() =>
            journal.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
            {
                LineNumber = 10000,
                AccountingPeriodId = period.Id,
                AccountId = otherLiabilityAccountId,
                PostingDate = PostingDate,
                DocumentNumber = "TAX-RULES-1",
                Description = "Cross-company code",
                CreditAmount = 8.25m,
                TaxCodeId = otherTaxCodeId,
                TaxBaseAmount = 100m
            }));
        Assert.Contains("active company", crossCompany.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(ClaimsPrincipal Principal, Guid CompanyId)> CreateUserAndCompanyAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(
            email,
            $"J{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            $"{prefix} company");
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
        ], "test"));
        return (principal, company.CompanyId);
    }

    private async Task<AccountingPeriodListItem> CreatePeriodAsync(ClaimsPrincipal principal)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var id = await service.CreatePeriodAsync(principal, new AccountingPeriodInput
        {
            Name = "2026-10",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        return Assert.Single(await service.GetPeriodsAsync(principal), item => item.Id == id);
    }

    private async Task<Guid> AddAccountAsync(
        Guid companyId,
        string number,
        AccountCategory category)
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
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        dbContext.GeneralLedgerAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }
}
