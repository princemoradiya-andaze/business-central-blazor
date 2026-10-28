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

public sealed class InventoryServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";
    private static readonly DateOnly PostingDate = new(2026, 10, 12);

    [Fact]
    public async Task ReceiptsAndIssuesPostWeightedAverageValuationAndBalancedLedgerEntries()
    {
        var data = await SetupAsync("inventory-post");
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var journals = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();

        var firstReceipt = await inventory.CreateMovementDraftAsync(data.User,
            Movement(data, "OPEN-1", 10m, true, 2m));
        await journals.PostBatchAsync(data.User, firstReceipt);
        var item = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal(10m, item.QuantityOnHand);
        Assert.Equal(2m, item.UnitCost);
        Assert.Equal(20m, item.InventoryValue);
        await Assert.ThrowsAsync<ValidationException>(() => journals.ReverseBatchAsync(
            data.User,
            firstReceipt,
            new GeneralJournalReversalInput
            {
                Name = "INVALID-REVERSAL",
                AccountingPeriodId = data.Period,
                PostingDate = PostingDate
            }));
        Assert.Equal(10m, Assert.Single(await inventory.GetItemsAsync(data.User)).QuantityOnHand);

        var secondReceipt = await inventory.CreateMovementDraftAsync(data.User,
            Movement(data, "RECEIPT-2", 5m, true, 4m));
        await journals.PostBatchAsync(data.User, secondReceipt);
        item = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal(15m, item.QuantityOnHand);
        Assert.Equal(2.6667m, item.UnitCost);
        Assert.Equal(40m, item.InventoryValue);

        var issue = await inventory.CreateMovementDraftAsync(data.User,
            Movement(data, "ISSUE-3", 6m, false));
        await journals.PostBatchAsync(data.User, issue);
        item = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal(9m, item.QuantityOnHand);
        Assert.Equal(2.6667m, item.UnitCost);
        Assert.Equal(23.9998m, item.InventoryValue);

        var movements = await inventory.GetMovementsAsync(data.User);
        Assert.Equal(3, movements.Count);
        Assert.All(movements, movement => Assert.True(movement.IsPosted));
        Assert.Equal(-6m, Assert.Single(movements, movement => movement.DocumentNumber == "ISSUE-3").Quantity);

        await using var dbContext = await ContextAsync();
        var postedEntries = await dbContext.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == data.Company)
            .ToListAsync();
        Assert.Equal(6, postedEntries.Count);
        foreach (var batchId in new[] { firstReceipt, secondReceipt, issue })
        {
            var batchEntries = postedEntries.Where(entry => entry.JournalBatchId == batchId).ToArray();
            Assert.Equal(batchEntries.Sum(entry => entry.DebitAmount), batchEntries.Sum(entry => entry.CreditAmount));
        }

        var issueEntries = postedEntries.Where(entry => entry.JournalBatchId == issue).ToArray();
        Assert.Contains(issueEntries, entry => entry.AccountId == data.IssueExpense && entry.DebitAmount == 16.0002m);
        Assert.Contains(issueEntries, entry => entry.AccountId == data.InventoryAccount && entry.CreditAmount == 16.0002m);
        Assert.Contains(await dbContext.AuditEntries.ToListAsync(),
            entry => entry.CompanyId == data.Company && entry.Action == "InventoryMovementPosted");
    }

    [Fact]
    public async Task InsufficientStockAndForeignCompanyItemsAreRejectedWithoutDrafts()
    {
        var data = await SetupAsync("inventory-isolation");
        var other = await SetupAsync("inventory-other");
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();

        await Assert.ThrowsAsync<ValidationException>(() => inventory.CreateMovementDraftAsync(
            data.User, Movement(data, "NO-STOCK", 1m, false)));
        await Assert.ThrowsAsync<ValidationException>(() => inventory.CreateMovementDraftAsync(
            data.User,
            new InventoryMovementInput
            {
                ItemId = other.Item,
                AccountingPeriodId = data.Period,
                PostingDate = PostingDate,
                DocumentNumber = "FOREIGN-ITEM",
                Description = "Foreign company item",
                Quantity = 1m,
                IsReceipt = true,
                UnitCost = 1m
            }));

        Assert.Empty(await inventory.GetMovementsAsync(data.User));
    }

    [Fact]
    public async Task PostingRevalidatesDraftAndStockAndDoesNotLeavePartialLedgerOrBalanceChanges()
    {
        var data = await SetupAsync("inventory-posting-validation");
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var journals = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();

        var alteredDraft = await inventory.CreateMovementDraftAsync(
            data.User, Movement(data, "ALTERED", 10m, true, 2m));
        await using (var dbContext = await ContextAsync())
        {
            var line = await dbContext.GeneralJournalLines
                .SingleAsync(candidate => candidate.CompanyId == data.Company &&
                                          candidate.JournalBatchId == alteredDraft &&
                                          candidate.LineNumber == 10000);
            line.DebitAmount = 19m;
            await dbContext.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(data.User, alteredDraft));
        var item = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal(0m, item.QuantityOnHand);

        var receipt = await inventory.CreateMovementDraftAsync(
            data.User, Movement(data, "RECEIPT", 10m, true, 2m));
        await journals.PostBatchAsync(data.User, receipt);
        var firstIssue = await inventory.CreateMovementDraftAsync(data.User, Movement(data, "ISSUE-A", 6m, false));
        var staleIssue = await inventory.CreateMovementDraftAsync(data.User, Movement(data, "ISSUE-B", 5m, false));
        await journals.PostBatchAsync(data.User, firstIssue);

        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(data.User, staleIssue));
        item = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal(4m, item.QuantityOnHand);

        await using var finalDb = await ContextAsync();
        Assert.False(await finalDb.GeneralLedgerEntries.AnyAsync(entry => entry.JournalBatchId == alteredDraft));
        Assert.False(await finalDb.GeneralLedgerEntries.AnyAsync(entry => entry.JournalBatchId == staleIssue));
        Assert.Equal(4, await finalDb.GeneralLedgerEntries.CountAsync(entry => entry.CompanyId == data.Company));
    }

    [Fact]
    public async Task ItemUpdateReturnsTheCommittedConcurrencyToken()
    {
        var data = await SetupAsync("inventory-save-token");
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var item = Assert.Single(await inventory.GetItemsAsync(data.User));
        var token = await inventory.UpdateItemAsync(data.User, item.Id, item.RowVersion, new InventoryItemInput
        {
            Number = item.Number,
            Description = "Autosaved description",
            BaseUnitOfMeasure = item.BaseUnitOfMeasure,
            InventoryAccountId = data.InventoryAccount,
            IncreaseOffsetAccountId = item.IncreaseOffsetAccountId,
            IssueExpenseAccountId = data.IssueExpense
        });
        var saved = Assert.Single(await inventory.GetItemsAsync(data.User));
        Assert.Equal("Autosaved description", saved.Description);
        Assert.Equal(saved.RowVersion, token);
    }

    [Fact]
    public async Task ItemMappingsCannotChangeAfterMovementDraftExists()
    {
        var data = await SetupAsync("inventory-mapping-lock");
        await using var scope = factory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var item = Assert.Single(await inventory.GetItemsAsync(data.User));
        await inventory.CreateMovementDraftAsync(data.User, Movement(data, "MAPPING-LOCK", 1m, true, 1m));

        var changed = new InventoryItemInput
        {
            Number = "ITEM-1",
            Description = "Inventory item",
            BaseUnitOfMeasure = "EA",
            InventoryAccountId = data.InventoryAccount,
            IncreaseOffsetAccountId = data.AlternateOffset,
            IssueExpenseAccountId = data.IssueExpense
        };
        await Assert.ThrowsAsync<ValidationException>(() =>
            inventory.UpdateItemAsync(data.User, item.Id, item.RowVersion, changed));
    }

    private sealed record Setup(
        ClaimsPrincipal User,
        Guid Company,
        Guid Item,
        Guid Period,
        Guid InventoryAccount,
        Guid IncreaseOffset,
        Guid IssueExpense,
        Guid AlternateOffset);

    private async Task<Setup> SetupAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email,
            $"I{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            $"{prefix} company");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
        Assert.NotNull(user);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, membership.CompanyId.ToString())
        ], "test"));
        var dbContext = services.GetRequiredService<ErpDbContext>();
        var inventoryAccount = NewAccount(membership.CompanyId, "1200", "Inventory asset", AccountCategory.Asset);
        var increaseOffset = NewAccount(membership.CompanyId, "3000", "Opening inventory offset", AccountCategory.Equity);
        var alternateOffset = NewAccount(membership.CompanyId, "3100", "Alternate inventory offset", AccountCategory.Equity);
        var issueExpense = NewAccount(membership.CompanyId, "5000", "Inventory issue expense", AccountCategory.Expense);
        dbContext.GeneralLedgerAccounts.AddRange(inventoryAccount, increaseOffset, alternateOffset, issueExpense);
        await dbContext.SaveChangesAsync();
        var journals = services.GetRequiredService<IGeneralJournalService>();
        var periodId = await journals.CreatePeriodAsync(principal,
            new AccountingPeriodInput
            {
                Name = $"OCT-{Guid.NewGuid():N}"[..8],
                StartsOn = new DateOnly(2026, 10, 1),
                EndsOn = new DateOnly(2026, 10, 31)
            });
        var inventory = services.GetRequiredService<IInventoryService>();
        var itemId = await inventory.CreateItemAsync(principal, new InventoryItemInput
        {
            Number = "ITEM-1",
            Description = "Inventory item",
            BaseUnitOfMeasure = "EA",
            InventoryAccountId = inventoryAccount.Id,
            IncreaseOffsetAccountId = increaseOffset.Id,
            IssueExpenseAccountId = issueExpense.Id
        });
        return new Setup(
            principal,
            membership.CompanyId,
            itemId,
            periodId,
            inventoryAccount.Id,
            increaseOffset.Id,
            issueExpense.Id,
            alternateOffset.Id);
    }

    private static GeneralLedgerAccount NewAccount(
        Guid companyId,
        string number,
        string name,
        AccountCategory category) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        Number = number,
        Name = name,
        Category = category,
        Type = AccountType.Posting,
        AllowsDirectPosting = true
    };

    private static InventoryMovementInput Movement(
        Setup setup,
        string documentNumber,
        decimal quantity,
        bool isReceipt,
        decimal unitCost = 0m) => new()
    {
        ItemId = setup.Item,
        AccountingPeriodId = setup.Period,
        PostingDate = PostingDate,
        DocumentNumber = documentNumber,
        Description = $"Inventory movement {documentNumber}",
        Quantity = quantity,
        IsReceipt = isReceipt,
        UnitCost = unitCost
    };

    private async Task<ErpDbContext> ContextAsync() =>
        await factory.Services.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContextAsync();
}
