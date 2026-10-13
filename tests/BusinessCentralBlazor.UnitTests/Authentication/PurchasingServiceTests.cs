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

public sealed class PurchasingServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";
    private static readonly DateOnly PostingDate = new(2026, 10, 13);

    [Fact]
    public async Task PurchaseReceiptAndInvoicePostInventoryGrniTaxAndOpenPayableAtomically()
    {
        var setup = await SetupAsync("purchasing-post");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var journals = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();

        var vendor = await purchasing.GetVendorsAsync(setup.User);
        Assert.Equal(setup.PayablesAccountNumber, Assert.Single(vendor).PayablesAccountNumber);
        var orderId = await purchasing.CreateOrderAsync(setup.User, new PurchaseOrderInput
        {
            OrderNumber = "PO-100",
            Description = "Stock replenishment",
            VendorId = setup.VendorId,
            OrderDate = PostingDate,
            Lines =
            [
                new PurchaseOrderLineInput
                {
                    ItemId = setup.ItemId,
                    Quantity = 2m,
                    UnitCost = 5m,
                    TaxCodeId = setup.TaxCodeId
                }
            ]
        });

        var receiptBatchId = await purchasing.CreateReceiptDraftAsync(
            setup.User,
            orderId,
            new PurchaseDocumentPostingInput
            {
                AccountingPeriodId = setup.PeriodId,
                PostingDate = PostingDate
            });
        await journals.PostBatchAsync(setup.User, receiptBatchId);
        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, receiptBatchId));

        var item = Assert.Single(await inventory.GetItemsAsync(setup.User));
        Assert.Equal(2m, item.QuantityOnHand);
        Assert.Equal(5m, item.UnitCost);
        Assert.Equal(10m, item.InventoryValue);

        var invoiceBatchId = await purchasing.CreateInvoiceDraftAsync(
            setup.User,
            orderId,
            new PurchaseDocumentPostingInput
            {
                DocumentNumber = "VEND-INV-100",
                AccountingPeriodId = setup.PeriodId,
                PostingDate = PostingDate
            });
        await journals.PostBatchAsync(setup.User, invoiceBatchId);
        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, invoiceBatchId));

        var orders = await purchasing.GetOrdersAsync(setup.User);
        Assert.Equal(PurchaseOrderStatus.Invoiced, Assert.Single(orders).Status);
        var payable = Assert.Single(await purchasing.GetOpenPayablesAsync(setup.User));
        Assert.Equal("VEND-INV-100", payable.InvoiceNumber);
        Assert.Equal(10m, payable.NetAmount);
        Assert.Equal(1m, payable.TaxAmount);
        Assert.Equal(11m, payable.OpenAmount);

        await using var db = await ContextAsync();
        var movements = await db.InventoryMovements
            .Where(movement => movement.CompanyId == setup.CompanyId && movement.JournalBatchId == receiptBatchId)
            .ToListAsync();
        var movement = Assert.Single(movements);
        Assert.Equal(2m, movement.Quantity);
        Assert.Equal(5m, movement.UnitCost);
        Assert.Equal(10m, movement.InventoryValue);
        Assert.NotNull(movement.PostedAtUtc);

        var entries = await db.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == setup.CompanyId)
            .ToListAsync();
        Assert.Equal(5, entries.Count);
        Assert.Equal(entries.Sum(entry => entry.DebitAmount), entries.Sum(entry => entry.CreditAmount));
        Assert.Contains(entries, entry => entry.JournalBatchId == receiptBatchId &&
                                          entry.AccountId == setup.InventoryAccountId &&
                                          entry.DebitAmount == 10m);
        Assert.Contains(entries, entry => entry.JournalBatchId == receiptBatchId &&
                                          entry.AccountId == setup.GrniAccountId &&
                                          entry.CreditAmount == 10m);
        Assert.Contains(entries, entry => entry.JournalBatchId == invoiceBatchId &&
                                          entry.AccountId == setup.GrniAccountId &&
                                          entry.DebitAmount == 10m);
        Assert.Contains(entries, entry => entry.JournalBatchId == invoiceBatchId &&
                                          entry.AccountId == setup.RecoverableTaxAccountId &&
                                          entry.DebitAmount == 1m);
        Assert.Contains(entries, entry => entry.JournalBatchId == invoiceBatchId &&
                                          entry.AccountId == setup.PayablesAccountId &&
                                          entry.CreditAmount == 11m);

        await Assert.ThrowsAsync<ValidationException>(() => journals.ReverseBatchAsync(
            setup.User,
            receiptBatchId,
            new GeneralJournalReversalInput
            {
                Name = "PURCHASE-RECEIPT-REVERSAL",
                AccountingPeriodId = setup.PeriodId,
                PostingDate = PostingDate
            }));
        await Assert.ThrowsAsync<ValidationException>(() => journals.ReverseBatchAsync(
            setup.User,
            invoiceBatchId,
            new GeneralJournalReversalInput
            {
                Name = "PURCHASE-INVOICE-REVERSAL",
                AccountingPeriodId = setup.PeriodId,
                PostingDate = PostingDate
            }));
    }

    [Fact]
    public async Task ReceiptPostingRejectsTamperedJournalWithoutChangingStockOrLedger()
    {
        var setup = await SetupAsync("purchasing-tamper");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var inventory = scope.ServiceProvider.GetRequiredService<IInventoryService>();
        var journals = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var orderId = await CreateOrderAsync(purchasing, setup, "PO-TAMPER");
        var batchId = await purchasing.CreateReceiptDraftAsync(setup.User, orderId, new PurchaseDocumentPostingInput
        {
            AccountingPeriodId = setup.PeriodId,
            PostingDate = PostingDate
        });

        await using (var db = await ContextAsync())
        {
            var line = await db.GeneralJournalLines.SingleAsync(candidate =>
                candidate.CompanyId == setup.CompanyId &&
                candidate.JournalBatchId == batchId &&
                candidate.LineNumber == 10000);
            line.DebitAmount = 9m;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, batchId));
        Assert.Equal(0m, Assert.Single(await inventory.GetItemsAsync(setup.User)).QuantityOnHand);
        await using var verification = await ContextAsync();
        Assert.Empty(await verification.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == setup.CompanyId && entry.JournalBatchId == batchId)
            .ToListAsync());
        Assert.Empty(await verification.InventoryMovements
            .Where(movement => movement.CompanyId == setup.CompanyId)
            .ToListAsync());
    }

    [Fact]
    public async Task PurchaseOrderRejectsForeignCompanyVendor()
    {
        var local = await SetupAsync("purchasing-local");
        var foreign = await SetupAsync("purchasing-foreign");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        await Assert.ThrowsAsync<ValidationException>(() => purchasing.CreateOrderAsync(local.User, new PurchaseOrderInput
        {
            OrderNumber = "PO-FOREIGN",
            Description = "Foreign vendor must be rejected",
            VendorId = foreign.VendorId,
            OrderDate = PostingDate,
            Lines = [new PurchaseOrderLineInput { ItemId = local.ItemId, Quantity = 1m, UnitCost = 1m }]
        }));
        Assert.Empty(await purchasing.GetOrdersAsync(local.User));
    }

    private async Task<Setup> SetupAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email,
            $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
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
        var db = services.GetRequiredService<ErpDbContext>();
        if (!await db.Currencies.AnyAsync(currency => currency.Code == "USD"))
        {
            db.Currencies.Add(new Currency { Code = "USD", Name = "US Dollar", MinorUnit = 2, IsActive = true });
            await db.SaveChangesAsync();
        }

        var inventoryAccount = NewAccount(membership.CompanyId, "1400", "Inventory", AccountCategory.Asset);
        var payablesAccount = NewAccount(membership.CompanyId, "2000", "Vendor payables", AccountCategory.Liability);
        var grniAccount = NewAccount(membership.CompanyId, "2100", "Goods received not invoiced", AccountCategory.Liability);
        var taxLiabilityAccount = NewAccount(membership.CompanyId, "2200", "Tax liability", AccountCategory.Liability);
        var taxRecoverableAccount = NewAccount(membership.CompanyId, "1300", "Recoverable tax", AccountCategory.Asset);
        var offsetAccount = NewAccount(membership.CompanyId, "3100", "Inventory offset", AccountCategory.Equity);
        var issueExpenseAccount = NewAccount(membership.CompanyId, "5000", "Inventory issue expense", AccountCategory.Expense);
        db.GeneralLedgerAccounts.AddRange(
            inventoryAccount, payablesAccount, grniAccount, taxLiabilityAccount,
            taxRecoverableAccount, offsetAccount, issueExpenseAccount);
        db.CompanySettings.Add(new CompanySettings
        {
            CompanyId = membership.CompanyId,
            BaseCurrencyCode = "USD"
        });
        var taxCode = new TaxCode
        {
            Id = Guid.NewGuid(),
            CompanyId = membership.CompanyId,
            Code = "PUR-VAT",
            Description = "Purchase VAT",
            LiabilityAccountId = taxLiabilityAccount.Id,
            RecoverableAccountId = taxRecoverableAccount.Id
        };
        db.TaxCodes.Add(taxCode);
        db.TaxRates.Add(new TaxRate
        {
            Id = Guid.NewGuid(),
            CompanyId = membership.CompanyId,
            TaxCodeId = taxCode.Id,
            RatePercent = 10m,
            StartsOn = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        var journals = services.GetRequiredService<IGeneralJournalService>();
        var periodId = await journals.CreatePeriodAsync(principal, new AccountingPeriodInput
        {
            Name = "October 2026",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        var purchasing = services.GetRequiredService<IPurchasingService>();
        await purchasing.SaveGrniAccountAsync(principal, grniAccount.Id);
        await purchasing.CreateVendorAsync(principal, new PurchaseVendorInput
        {
            Number = "VEND-1",
            Name = "Test vendor",
            PayablesAccountId = payablesAccount.Id
        });
        var inventory = services.GetRequiredService<IInventoryService>();
        var itemId = await inventory.CreateItemAsync(principal, new InventoryItemInput
        {
            Number = "ITEM-1",
            Description = "Purchase test item",
            BaseUnitOfMeasure = "EA",
            InventoryAccountId = inventoryAccount.Id,
            IncreaseOffsetAccountId = offsetAccount.Id,
            IssueExpenseAccountId = issueExpenseAccount.Id
        });
        var createdVendorId = Assert.Single(await purchasing.GetVendorsAsync(principal)).Id;
        return new Setup(
            principal,
            membership.CompanyId,
            itemId,
            taxCode.Id,
            periodId,
            payablesAccount.Id,
            payablesAccount.Number,
            grniAccount.Id,
            inventoryAccount.Id,
            taxRecoverableAccount.Id,
            createdVendorId);
    }

    private static async Task<Guid> CreateOrderAsync(IPurchasingService purchasing, Setup setup, string orderNumber) =>
        await purchasing.CreateOrderAsync(setup.User, new PurchaseOrderInput
        {
            OrderNumber = orderNumber,
            Description = "Stock replenishment",
            VendorId = setup.VendorId,
            OrderDate = PostingDate,
            Lines = [new PurchaseOrderLineInput
            {
                ItemId = setup.ItemId,
                Quantity = 2m,
                UnitCost = 5m
            }]
        });

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

    private async Task<ErpDbContext> ContextAsync() =>
        await factory.Services.GetRequiredService<IDbContextFactory<ErpDbContext>>().CreateDbContextAsync();

    private sealed record Setup(
        ClaimsPrincipal User,
        Guid CompanyId,
        Guid ItemId,
        Guid TaxCodeId,
        Guid PeriodId,
        Guid PayablesAccountId,
        string PayablesAccountNumber,
        Guid GrniAccountId,
        Guid InventoryAccountId,
        Guid RecoverableTaxAccountId,
        Guid VendorId);
}
