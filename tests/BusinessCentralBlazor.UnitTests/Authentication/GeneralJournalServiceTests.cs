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

public sealed class GeneralJournalServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task BalancedBatchPostsLedgerEntriesAndLocksItsLines()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-post");
        var period = await CreatePeriodAsync(principal);
        var debitAccountId = await AddAccountAsync(companyId, "1000", "Cash");
        var creditAccountId = await AddAccountAsync(companyId, "4000", "Income");
        var (dimensionId, valueId) = await AddDimensionAsync(companyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batchId = await service.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "DAILY",
            Description = "Daily journal",
            DefaultAccountingPeriodId = period.Id
        });
        Assert.True(await service.UpdateBatchAsync(principal, batchId, new GeneralJournalBatchInput
        {
            Name = "DAILY-EDITED",
            Description = "Updated daily journal",
            DefaultAccountingPeriodId = period.Id
        }));

        var debitLineId = await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = debitAccountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "DOC-1",
            Description = "Cash received",
            DebitAmount = 125.50m,
            Dimensions = [new JournalDimensionAssignmentInput
            {
                DimensionId = dimensionId,
                DimensionValueId = valueId
            }]
        });
        await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = period.Id,
            AccountId = creditAccountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "DOC-1",
            Description = "Income",
            CreditAmount = 125.50m
        });

        await service.PostBatchAsync(principal, batchId);

        var postedBatch = await service.GetBatchAsync(principal, batchId);
        Assert.NotNull(postedBatch);
        Assert.Equal(JournalBatchStatus.Posted, postedBatch.Status);
        Assert.Equal(2, postedBatch.Lines.Count);
        Assert.Equal("DAILY-EDITED", postedBatch.Name);
        Assert.Equal("DEPT", Assert.Single(postedBatch.Lines.Single(line => line.Id == debitLineId).Dimensions)
            .DimensionCode);
        await using var dbScope = factory.Services.CreateAsyncScope();
        var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var entries = await dbContext.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == companyId && entry.JournalBatchId == batchId)
            .OrderBy(entry => entry.EntryNumber)
            .ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(125.50m, entries.Sum(entry => entry.DebitAmount));
        Assert.Equal(125.50m, entries.Sum(entry => entry.CreditAmount));
        Assert.Equal([1L, 2L], entries.Select(entry => entry.EntryNumber));

        await Assert.ThrowsAsync<ValidationException>(() => service.PostBatchAsync(principal, batchId));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateBatchAsync(
            principal,
            batchId,
            new GeneralJournalBatchInput
            {
                Name = "POSTED-EDIT",
                DefaultAccountingPeriodId = period.Id
            }));
        await Assert.ThrowsAsync<ValidationException>(() => service.DeleteLineAsync(principal, batchId, debitLineId));
    }

    [Fact]
    public async Task PostingRejectsUnbalancedLinesAndInvalidPostingPeriods()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-validation");
        var period = await CreatePeriodAsync(principal);
        var accountId = await AddAccountAsync(companyId, "1100", "Cash");
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batchId = await service.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "UNBALANCED",
            Description = "Needs balancing",
            DefaultAccountingPeriodId = period.Id
        });
        await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = accountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "DOC-2",
            Description = "Only a debit",
            DebitAmount = 10m
        });

        var exception = await Assert.ThrowsAsync<ValidationException>(() => service.PostBatchAsync(principal, batchId));
        Assert.Contains("balance", exception.Message, StringComparison.OrdinalIgnoreCase);

        await service.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = period.Id,
            AccountId = accountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "DOC-2",
            Description = "Balancing credit",
            CreditAmount = 10m
        });
        await service.UpdatePeriodStatusAsync(principal, period.Id, new AccountingPeriodStatusInput
        {
            IsClosed = true
        });
        var closedPeriodException = await Assert.ThrowsAsync<ValidationException>(
            () => service.PostBatchAsync(principal, batchId));
        Assert.Contains("closed", closedPeriodException.Message, StringComparison.OrdinalIgnoreCase);
        await service.UpdatePeriodStatusAsync(principal, period.Id, new AccountingPeriodStatusInput
        {
            DateLocked = true
        });
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBatchAsync(
            principal,
            new GeneralJournalBatchInput
            {
                Name = "LOCKED",
                DefaultAccountingPeriodId = period.Id
            }));
    }

    [Fact]
    public async Task JournalLinesRejectOutOfPeriodDatesBlockedAccountsAndInvalidDimensionAssignments()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-line-rules");
        var period = await CreatePeriodAsync(principal);
        var accountId = await AddAccountAsync(companyId, "1150", "Cash");
        var blockedAccountId = await AddAccountAsync(companyId, "1160", "Blocked");
        var (dimensionId, valueId) = await AddDimensionAsync(companyId);
        await using var setupScope = factory.Services.CreateAsyncScope();
        var dbContext = setupScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var blockedAccount = await dbContext.GeneralLedgerAccounts.SingleAsync(item => item.Id == blockedAccountId);
        blockedAccount.IsBlocked = true;
        var blockedValue = await dbContext.DimensionValues.SingleAsync(item => item.Id == valueId);
        blockedValue.IsBlocked = true;
        await dbContext.SaveChangesAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var batchId = await service.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "RULES",
            DefaultAccountingPeriodId = period.Id
        });
        var input = new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = accountId,
            PostingDate = new DateOnly(2026, 11, 1),
            DocumentNumber = "DOC-3",
            Description = "Invalid date",
            DebitAmount = 10m
        };
        var dateException = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateLineAsync(principal, batchId, input));
        Assert.Contains("posting date", dateException.Message, StringComparison.OrdinalIgnoreCase);

        input.PostingDate = new DateOnly(2026, 10, 12);
        input.Dimensions =
        [
            new JournalDimensionAssignmentInput { DimensionId = dimensionId, DimensionValueId = valueId },
            new JournalDimensionAssignmentInput { DimensionId = dimensionId, DimensionValueId = valueId }
        ];
        var duplicateDimensionException = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateLineAsync(principal, batchId, input));
        Assert.Contains("one value", duplicateDimensionException.Message, StringComparison.OrdinalIgnoreCase);

        input.Dimensions =
        [
            new JournalDimensionAssignmentInput { DimensionId = dimensionId, DimensionValueId = valueId }
        ];
        var blockedValueException = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateLineAsync(principal, batchId, input));
        Assert.Contains("unblocked", blockedValueException.Message, StringComparison.OrdinalIgnoreCase);

        input.Dimensions = [];
        input.AccountId = blockedAccountId;
        var blockedAccountException = await Assert.ThrowsAsync<ValidationException>(
            () => service.CreateLineAsync(principal, batchId, input));
        Assert.Contains("posting account", blockedAccountException.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReversalPostsEqualAndOppositeEntriesAndIsLinkedOnlyOnce()
    {
        var (principal, companyId) = await CreateUserAndCompanyAsync("journal-reversal");
        var period = await CreatePeriodAsync(principal);
        var debitAccountId = await AddAccountAsync(companyId, "1200", "Receivable");
        var creditAccountId = await AddAccountAsync(companyId, "4100", "Revenue");
        var (dimensionId, valueId) = await AddDimensionAsync(companyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var originalBatchId = await service.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "ORIGINAL",
            DefaultAccountingPeriodId = period.Id
        });
        await service.CreateLineAsync(principal, originalBatchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = period.Id,
            AccountId = debitAccountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "INV-1",
            Description = "Receivable",
            DebitAmount = 75m,
            Dimensions = [new JournalDimensionAssignmentInput
            {
                DimensionId = dimensionId,
                DimensionValueId = valueId
            }]
        });
        await service.CreateLineAsync(principal, originalBatchId, new GeneralJournalLineInput
        {
            LineNumber = 20000,
            AccountingPeriodId = period.Id,
            AccountId = creditAccountId,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "INV-1",
            Description = "Revenue",
            CreditAmount = 75m
        });
        await service.PostBatchAsync(principal, originalBatchId);

        var reversalBatchId = await service.ReverseBatchAsync(principal, originalBatchId, new GeneralJournalReversalInput
        {
            Name = "REV-ORIGINAL",
            Description = "Reverse original",
            AccountingPeriodId = period.Id,
            PostingDate = new DateOnly(2026, 10, 13)
        });

        var original = await service.GetBatchAsync(principal, originalBatchId);
        var reversal = await service.GetBatchAsync(principal, reversalBatchId);
        Assert.NotNull(original);
        Assert.NotNull(reversal);
        Assert.Equal(reversalBatchId, original.ReversedByBatchId);
        Assert.Equal(originalBatchId, reversal.ReversalOfBatchId);
        Assert.Equal(JournalBatchStatus.Posted, reversal.Status);
        Assert.Equal(75m, reversal.Lines.Sum(line => line.DebitAmount));
        Assert.Equal(75m, reversal.Lines.Sum(line => line.CreditAmount));
        Assert.Single(reversal.Lines.First(line => line.CreditAmount > 0).Dimensions);
        var listedBatches = await service.GetBatchesAsync(principal);
        var listedOriginal = Assert.Single(listedBatches, batch => batch.Id == originalBatchId);
        var listedReversal = Assert.Single(listedBatches, batch => batch.Id == reversalBatchId);
        Assert.Equal(reversalBatchId, listedOriginal.ReversedByBatchId);
        Assert.Null(listedOriginal.ReversalOfBatchId);
        Assert.Null(listedReversal.ReversedByBatchId);
        Assert.Equal(originalBatchId, listedReversal.ReversalOfBatchId);

        await Assert.ThrowsAsync<ValidationException>(() => service.ReverseBatchAsync(
            principal,
            originalBatchId,
            new GeneralJournalReversalInput
            {
                Name = "REV-AGAIN",
                AccountingPeriodId = period.Id,
                PostingDate = new DateOnly(2026, 10, 13)
            }));
        await using var dbScope = factory.Services.CreateAsyncScope();
        var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var entries = await dbContext.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == companyId)
            .OrderBy(entry => entry.EntryNumber)
            .ToListAsync();
        Assert.Equal(4, entries.Count);
        Assert.Equal(entries[0].DebitAmount, entries[2].CreditAmount);
        Assert.Equal(entries[0].CreditAmount, entries[2].DebitAmount);
        Assert.Equal(entries[1].DebitAmount, entries[3].CreditAmount);
        Assert.Equal(entries[1].CreditAmount, entries[3].DebitAmount);
    }

    [Fact]
    public async Task JournalBatchesAndPeriodsAreCompanyScopedAndPeriodsCannotOverlap()
    {
        var (ownerPrincipal, _) = await CreateUserAndCompanyAsync("journal-owner");
        var (otherPrincipal, _) = await CreateUserAndCompanyAsync("journal-other");
        var period = await CreatePeriodAsync(ownerPrincipal);
        var otherPeriod = await CreatePeriodAsync(otherPrincipal);
        Assert.NotEqual(period.Id, otherPeriod.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        await Assert.ThrowsAsync<ValidationException>(() => service.CreatePeriodAsync(
            ownerPrincipal,
            new AccountingPeriodInput
            {
                Name = "OVERLAP",
                StartsOn = new DateOnly(2026, 10, 1),
                EndsOn = new DateOnly(2026, 10, 31)
            }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateBatchAsync(
            ownerPrincipal,
            new GeneralJournalBatchInput
            {
                Name = "CROSS",
                DefaultAccountingPeriodId = otherPeriod.Id
            }));
        var batchId = await service.CreateBatchAsync(ownerPrincipal, new GeneralJournalBatchInput
        {
            Name = "PRIVATE",
            DefaultAccountingPeriodId = period.Id
        });
        Assert.Null(await service.GetBatchAsync(otherPrincipal, batchId));
        Assert.Equal(batchId, Assert.Single(await service.GetBatchesAsync(ownerPrincipal)).Id);
        Assert.Empty(await service.GetBatchesAsync(otherPrincipal));
    }

    private async Task<(ClaimsPrincipal Principal, Guid CompanyId)> CreateUserAndCompanyAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(
            email,
            $"J{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            $"{prefix} company");
        var userId = await GetUserIdAsync(email);
        return (
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
            ], "test")),
            company.CompanyId);
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
        return Assert.Single(await service.GetPeriodsAsync(principal), period => period.Id == id);
    }

    private async Task<Guid> AddAccountAsync(Guid companyId, string number, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var account = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = number,
            Name = name,
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        dbContext.GeneralLedgerAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    private async Task<(Guid DimensionId, Guid ValueId)> AddDimensionAsync(Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var dimension = new Dimension
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = "DEPT",
            Name = "Department"
        };
        var value = new DimensionValue
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DimensionId = dimension.Id,
            Code = "SALES",
            Name = "Sales"
        };
        dbContext.AddRange(dimension, value);
        await dbContext.SaveChangesAsync();
        return (dimension.Id, value.Id);
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
