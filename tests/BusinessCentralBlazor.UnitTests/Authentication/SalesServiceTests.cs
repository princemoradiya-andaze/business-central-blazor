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

public sealed class SalesServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";
    private static readonly DateOnly PostingDate = new(2026, 10, 12);

    [Fact]
    public async Task SalesOrderShipsAndInvoicesThroughBalancedJournalsWithOpenReceivable()
    {
        var setup = await SetupAsync("sales-lifecycle");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var inventory = services.GetRequiredService<IInventoryService>();
        var journals = services.GetRequiredService<IGeneralJournalService>();
        var sales = services.GetRequiredService<ISalesService>();
        var taxSetup = services.GetRequiredService<ITaxSetupService>();

        var receiptBatch = await inventory.CreateMovementDraftAsync(setup.User, new InventoryMovementInput
        {
            ItemId = setup.ItemId,
            AccountingPeriodId = setup.PeriodId,
            PostingDate = PostingDate,
            DocumentNumber = "OPENING-STOCK",
            Description = "Opening inventory",
            Quantity = 10m,
            IsReceipt = true,
            UnitCost = 25m
        });
        await journals.PostBatchAsync(setup.User, receiptBatch);

        var taxCodeId = await taxSetup.CreateTaxCodeAsync(setup.User, new TaxCodeInput
        {
            Code = "SALE10",
            Description = "Sales tax 10%",
            LiabilityAccountId = setup.TaxLiabilityAccountId,
            RecoverableAccountId = setup.InventoryAccountId
        });
        await taxSetup.CreateRateAsync(setup.User, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 10m,
            StartsOn = new DateOnly(2026, 10, 1)
        });

        await sales.CreateCustomerAsync(setup.User, new SalesCustomerInput
        {
            Number = "CUSTOMER-1",
            Name = "Sales Demo Customer",
            ReceivablesAccountId = setup.ReceivablesAccountId
        });
        var customer = Assert.Single(await sales.GetCustomersAsync(setup.User));
        var orderId = await sales.CreateOrderAsync(setup.User, new SalesOrderInput
        {
            OrderNumber = "SO-1001",
            Description = "Inventory sale",
            CustomerId = customer.Id,
            OrderDate = PostingDate,
            Lines =
            [
                new SalesOrderLineInput
                {
                    ItemId = setup.ItemId,
                    Quantity = 2m,
                    UnitPrice = 100m,
                    SalesAccountId = setup.RevenueAccountId,
                    TaxCodeId = taxCodeId
                }
            ]
        });

        var shipmentBatch = await sales.CreateShipmentDraftAsync(
            setup.User,
            orderId,
            Document(setup.PeriodId, string.Empty));
        await journals.PostBatchAsync(setup.User, shipmentBatch);

        var shippedOrder = Assert.Single(await sales.GetOrdersAsync(setup.User));
        Assert.Equal(SalesOrderStatus.Shipped, shippedOrder.Status);
        var shippedItem = Assert.Single(await inventory.GetItemsAsync(setup.User));
        Assert.Equal(8m, shippedItem.QuantityOnHand);
        Assert.Equal(200m, shippedItem.InventoryValue);

        var invoiceBatch = await sales.CreateInvoiceDraftAsync(
            setup.User,
            orderId,
            Document(setup.PeriodId, "SI-1001"));
        await journals.PostBatchAsync(setup.User, invoiceBatch);

        var invoicedOrder = Assert.Single(await sales.GetOrdersAsync(setup.User));
        Assert.Equal(SalesOrderStatus.Invoiced, invoicedOrder.Status);
        var receivable = Assert.Single(await sales.GetOpenReceivablesAsync(setup.User));
        Assert.Equal("SI-1001", receivable.InvoiceNumber);
        Assert.Equal(200m, receivable.NetAmount);
        Assert.Equal(20m, receivable.TaxAmount);
        Assert.Equal(220m, receivable.OpenAmount);

        await using var dbContext = await ContextAsync();
        var invoiceEntries = await dbContext.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == setup.CompanyId && entry.JournalBatchId == invoiceBatch)
            .ToListAsync();
        Assert.Contains(invoiceEntries, entry =>
            entry.AccountId == setup.ReceivablesAccountId && entry.DebitAmount == 220m);
        Assert.Contains(invoiceEntries, entry =>
            entry.AccountId == setup.RevenueAccountId && entry.CreditAmount == 200m);
        Assert.Contains(invoiceEntries, entry =>
            entry.AccountId == setup.TaxLiabilityAccountId &&
            entry.CreditAmount == 20m &&
            entry.TaxCodeId == taxCodeId &&
            entry.TaxPostingType == TaxPostingType.Liability);
        Assert.Equal(invoiceEntries.Sum(entry => entry.DebitAmount), invoiceEntries.Sum(entry => entry.CreditAmount));

        await Assert.ThrowsAsync<ValidationException>(() => journals.ReverseBatchAsync(
            setup.User,
            shipmentBatch,
            new GeneralJournalReversalInput
            {
                Name = "REVERSE-SHIPMENT",
                AccountingPeriodId = setup.PeriodId,
                PostingDate = PostingDate
            }));
    }

    [Fact]
    public async Task InsufficientStockDoesNotCreateShipmentDraftOrChangeOrderStatus()
    {
        var setup = await SetupAsync("sales-insufficient-stock");
        await using var scope = factory.Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesService>();
        var customerNumber = $"C{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        await sales.CreateCustomerAsync(setup.User, new SalesCustomerInput
        {
            Number = customerNumber,
            Name = "Stock Test Customer",
            ReceivablesAccountId = setup.ReceivablesAccountId
        });
        var customer = Assert.Single(await sales.GetCustomersAsync(setup.User));
        var orderId = await sales.CreateOrderAsync(setup.User, new SalesOrderInput
        {
            OrderNumber = $"SO{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
            Description = "Order without stock",
            CustomerId = customer.Id,
            OrderDate = PostingDate,
            Lines =
            [
                new SalesOrderLineInput
                {
                    ItemId = setup.ItemId,
                    Quantity = 1m,
                    UnitPrice = 50m,
                    SalesAccountId = setup.RevenueAccountId
                }
            ]
        });

        await Assert.ThrowsAsync<ValidationException>(() => sales.CreateShipmentDraftAsync(
            setup.User,
            orderId,
            Document(setup.PeriodId, string.Empty)));
        var order = Assert.Single(await sales.GetOrdersAsync(setup.User));
        Assert.Equal(SalesOrderStatus.Open, order.Status);
        Assert.Null(order.ShipmentJournalBatchId);
    }

    [Fact]
    public async Task ShipmentPostingRechecksChangedStockAndLeavesJournalAndInventoryUnchanged()
    {
        var setup = await SetupAsync("sales-stale-stock");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var inventory = services.GetRequiredService<IInventoryService>();
        var journals = services.GetRequiredService<IGeneralJournalService>();
        var sales = services.GetRequiredService<ISalesService>();
        var receipt = await inventory.CreateMovementDraftAsync(setup.User, new InventoryMovementInput
        {
            ItemId = setup.ItemId,
            AccountingPeriodId = setup.PeriodId,
            PostingDate = PostingDate,
            DocumentNumber = "STALE-OPENING",
            Description = "Opening inventory",
            Quantity = 10m,
            IsReceipt = true,
            UnitCost = 25m
        });
        await journals.PostBatchAsync(setup.User, receipt);
        await sales.CreateCustomerAsync(setup.User, new SalesCustomerInput
        {
            Number = "STALE-CUSTOMER",
            Name = "Stale stock customer",
            ReceivablesAccountId = setup.ReceivablesAccountId
        });
        var customer = Assert.Single(await sales.GetCustomersAsync(setup.User));
        var orderId = await sales.CreateOrderAsync(setup.User, new SalesOrderInput
        {
            OrderNumber = "SO-STALE-1",
            Description = "Stock changes before shipment posting",
            CustomerId = customer.Id,
            OrderDate = PostingDate,
            Lines =
            [
                new SalesOrderLineInput
                {
                    ItemId = setup.ItemId,
                    Quantity = 7m,
                    UnitPrice = 50m,
                    SalesAccountId = setup.RevenueAccountId
                }
            ]
        });
        var shipment = await sales.CreateShipmentDraftAsync(setup.User, orderId, Document(setup.PeriodId, string.Empty));
        var issue = await inventory.CreateMovementDraftAsync(setup.User, new InventoryMovementInput
        {
            ItemId = setup.ItemId,
            AccountingPeriodId = setup.PeriodId,
            PostingDate = PostingDate,
            DocumentNumber = "STALE-ISSUE",
            Description = "Concurrent inventory issue",
            Quantity = 4m,
            IsReceipt = false
        });
        await journals.PostBatchAsync(setup.User, issue);

        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, shipment));

        var order = Assert.Single(await sales.GetOrdersAsync(setup.User));
        Assert.Equal(SalesOrderStatus.ShipmentDraft, order.Status);
        var item = Assert.Single(await inventory.GetItemsAsync(setup.User));
        Assert.Equal(6m, item.QuantityOnHand);
        await using var dbContext = await ContextAsync();
        Assert.False(await dbContext.GeneralLedgerEntries.AnyAsync(entry =>
            entry.CompanyId == setup.CompanyId && entry.JournalBatchId == shipment));
        Assert.False(await dbContext.InventoryMovements.AnyAsync(movement =>
            movement.CompanyId == setup.CompanyId && movement.JournalBatchId == shipment));
    }

    private sealed record Setup(
        ClaimsPrincipal User,
        Guid CompanyId,
        Guid PeriodId,
        Guid ItemId,
        Guid InventoryAccountId,
        Guid ReceivablesAccountId,
        Guid RevenueAccountId,
        Guid TaxLiabilityAccountId);

    private async Task<Setup> SetupAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email,
            $"S{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
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
        var inventoryAccount = NewAccount(membership.CompanyId, "1200", "Inventory", AccountCategory.Asset);
        var receivablesAccount = NewAccount(membership.CompanyId, "1100", "Receivables", AccountCategory.Asset);
        var offsetAccount = NewAccount(membership.CompanyId, "3000", "Opening offset", AccountCategory.Equity);
        var issueExpenseAccount = NewAccount(membership.CompanyId, "5000", "Cost of goods sold", AccountCategory.Expense);
        var revenueAccount = NewAccount(membership.CompanyId, "4000", "Sales revenue", AccountCategory.Income);
        var taxLiabilityAccount = NewAccount(membership.CompanyId, "2100", "Sales tax payable", AccountCategory.Liability);
        dbContext.GeneralLedgerAccounts.AddRange(
            inventoryAccount,
            receivablesAccount,
            offsetAccount,
            issueExpenseAccount,
            revenueAccount,
            taxLiabilityAccount);
        await dbContext.SaveChangesAsync();
        var journals = services.GetRequiredService<IGeneralJournalService>();
        var periodId = await journals.CreatePeriodAsync(principal, new AccountingPeriodInput
        {
            Name = $"OCT-{Guid.NewGuid():N}"[..8],
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        var inventory = services.GetRequiredService<IInventoryService>();
        var itemId = await inventory.CreateItemAsync(principal, new InventoryItemInput
        {
            Number = $"ITEM-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Description = "Sales inventory item",
            BaseUnitOfMeasure = "EA",
            InventoryAccountId = inventoryAccount.Id,
            IncreaseOffsetAccountId = offsetAccount.Id,
            IssueExpenseAccountId = issueExpenseAccount.Id
        });
        return new Setup(
            principal,
            membership.CompanyId,
            periodId,
            itemId,
            inventoryAccount.Id,
            receivablesAccount.Id,
            revenueAccount.Id,
            taxLiabilityAccount.Id);
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

    private static SalesDocumentPostingInput Document(Guid periodId, string invoiceNumber) => new()
    {
        DocumentNumber = invoiceNumber,
        AccountingPeriodId = periodId,
        PostingDate = PostingDate
    };

    private async Task<ErpDbContext> ContextAsync() =>
        await factory.Services.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContextAsync();
}
