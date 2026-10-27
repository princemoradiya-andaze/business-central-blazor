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

    [Fact]
    public async Task VendorCanBeEditedBlockedAndReactivatedWithoutLosingPurchasingHistory()
    {
        var setup = await SetupAsync("vendor-lifecycle");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var journals = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var orderId = await CreateOrderAsync(purchasing, setup, "PO-HISTORY");
        await purchasing.UpdateVendorAsync(setup.User, setup.VendorId, new PurchaseVendorInput
        {
            Number = " VEND-EDITED ",
            Name = " Updated vendor ",
            PayablesAccountId = setup.PayablesAccountId
        });
        var edited = Assert.Single(await purchasing.GetVendorsAsync(setup.User));
        Assert.Equal("VEND-EDITED", edited.Number);
        Assert.Equal("Updated vendor", edited.Name);
        Assert.Equal(setup.PayablesAccountId, edited.PayablesAccountId);

        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        Assert.True(Assert.Single(await purchasing.GetVendorsAsync(setup.User)).IsBlocked);
        await Assert.ThrowsAsync<ValidationException>(() => CreateOrderAsync(purchasing, setup, "PO-BLOCKED"));
        var posting = new PurchaseDocumentPostingInput
        {
            AccountingPeriodId = setup.PeriodId,
            PostingDate = PostingDate
        };
        await Assert.ThrowsAsync<ValidationException>(() =>
            purchasing.CreateReceiptDraftAsync(setup.User, orderId, posting));
        Assert.Equal(orderId, Assert.Single(await purchasing.GetOrdersAsync(setup.User)).Id);

        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, false);
        var receiptId = await purchasing.CreateReceiptDraftAsync(setup.User, orderId, posting);
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, receiptId));
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, false);
        await journals.PostBatchAsync(setup.User, receiptId);

        posting.DocumentNumber = "VENDOR-HISTORY-INV";
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        await Assert.ThrowsAsync<ValidationException>(() =>
            purchasing.CreateInvoiceDraftAsync(setup.User, orderId, posting));
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, false);
        var invoiceId = await purchasing.CreateInvoiceDraftAsync(setup.User, orderId, posting);
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        await Assert.ThrowsAsync<ValidationException>(() => journals.PostBatchAsync(setup.User, invoiceId));
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, false);
        await journals.PostBatchAsync(setup.User, invoiceId);
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);

        Assert.Equal(PurchaseOrderStatus.Invoiced, Assert.Single(await purchasing.GetOrdersAsync(setup.User)).Status);
        Assert.Equal(10m, Assert.Single(await purchasing.GetOpenPayablesAsync(setup.User)).OpenAmount);
        await using var db = await ContextAsync();
        Assert.Single(await db.Vendors.Where(vendor => vendor.CompanyId == setup.CompanyId).ToListAsync());
        Assert.Single(await db.PurchaseReceipts.Where(receipt => receipt.CompanyId == setup.CompanyId).ToListAsync());
        Assert.Single(await db.PurchaseInvoices.Where(invoice => invoice.CompanyId == setup.CompanyId).ToListAsync());
        Assert.NotEmpty(await db.GeneralLedgerEntries.Where(entry => entry.CompanyId == setup.CompanyId).ToListAsync());
        var audit = await db.AuditEntries.Where(entry =>
            entry.CompanyId == setup.CompanyId && entry.EntityId == setup.VendorId.ToString()).ToListAsync();
        Assert.Single(audit, entry => entry.Action == "VendorUpdated");
        Assert.Equal(5, audit.Count(entry => entry.Action == "VendorBlocked"));
        Assert.Equal(4, audit.Count(entry => entry.Action == "VendorReactivated"));
        Assert.All(audit, entry => Assert.Equal(
            Guid.Parse(setup.User.FindFirstValue(ClaimTypes.NameIdentifier)!), entry.ActorUserId));
        Assert.Contains(audit, entry => entry.MetadataJson!.Contains("\"IsBlocked\":true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VendorReadsAndWritesAreScopedToActiveCompany()
    {
        var local = await SetupAsync("vendor-scope-local");
        var foreign = await SetupAsync("vendor-scope-foreign");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var input = new PurchaseVendorInput
        {
            Number = "CHANGED",
            Name = "Should not change",
            PayablesAccountId = local.PayablesAccountId
        };
        Assert.Equal(local.VendorId, Assert.Single(await purchasing.GetVendorsAsync(local.User)).Id);
        foreach (var id in new[] { foreign.VendorId, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<ValidationException>(() => purchasing.UpdateVendorAsync(local.User, id, input));
            await Assert.ThrowsAsync<ValidationException>(() => purchasing.SetVendorBlockedAsync(local.User, id, true));
            await Assert.ThrowsAsync<ValidationException>(() => purchasing.SetVendorBlockedAsync(local.User, id, false));
        }
        Assert.Equal("VEND-1", Assert.Single(await purchasing.GetVendorsAsync(foreign.User)).Number);
        Assert.False(Assert.Single(await purchasing.GetVendorsAsync(foreign.User)).IsBlocked);
        await using var db = await ContextAsync();
        Assert.Empty(await db.AuditEntries.Where(entry =>
            entry.CompanyId == local.CompanyId && entry.EntityId == foreign.VendorId.ToString()).ToListAsync());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("grni")]
    [InlineData("asset")]
    [InlineData("blocked")]
    [InlineData("non-posting")]
    [InlineData("no-direct-posting")]
    [InlineData("missing")]
    [InlineData("empty")]
    public async Task VendorCreateAndEditUseTheSamePayablesAccountRules(string invalidKind)
    {
        var setup = await SetupAsync($"vendor-{invalidKind}");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var invalidAccountId = Guid.NewGuid();
        await using (var db = await ContextAsync())
        {
            var account = NewAccount(setup.CompanyId, "2999", "Invalid payables", AccountCategory.Liability);
            switch (invalidKind)
            {
                case "foreign": account.CompanyId = Guid.NewGuid(); break;
                case "grni": invalidAccountId = setup.GrniAccountId; break;
                case "asset": account.Category = AccountCategory.Asset; break;
                case "blocked": account.IsBlocked = true; break;
                case "non-posting": account.Type = AccountType.Heading; break;
                case "no-direct-posting": account.AllowsDirectPosting = false; break;
                case "empty": invalidAccountId = Guid.Empty; break;
            }
            if (invalidKind is not ("grni" or "missing" or "empty"))
            {
                db.GeneralLedgerAccounts.Add(account);
                invalidAccountId = account.Id;
                await db.SaveChangesAsync();
            }
        }
        var input = new PurchaseVendorInput
        {
            Number = "VEND-INVALID",
            Name = "Invalid account",
            PayablesAccountId = invalidAccountId
        };
        await Assert.ThrowsAsync<ValidationException>(() => purchasing.CreateVendorAsync(setup.User, input));
        await Assert.ThrowsAsync<ValidationException>(() => purchasing.UpdateVendorAsync(setup.User, setup.VendorId, input));
        var vendor = Assert.Single(await purchasing.GetVendorsAsync(setup.User));
        Assert.Equal("VEND-1", vendor.Number);
        Assert.Equal(setup.PayablesAccountId, vendor.PayablesAccountId);
    }

    [Fact]
    public async Task VendorEditRejectsDuplicateAndInvalidFieldsButAllowsAnEligibleAccountChange()
    {
        var setup = await SetupAsync("vendor-edit-validation");
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        await purchasing.CreateVendorAsync(setup.User, new PurchaseVendorInput
        {
            Number = "VEND-2", Name = "Second vendor", PayablesAccountId = setup.PayablesAccountId
        });
        foreach (var (number, name) in new[]
        {
            (" VEND-2 ", "Duplicate"), (" ", "Missing number"), ("VEND-1", " "),
            (new string('N', 33), "Long number"), ("VEND-1", new string('N', 161))
        })
        {
            await Assert.ThrowsAsync<ValidationException>(() => purchasing.UpdateVendorAsync(
                setup.User, setup.VendorId, new PurchaseVendorInput
                {
                    Number = number, Name = name, PayablesAccountId = setup.PayablesAccountId
                }));
        }
        var replacement = NewAccount(setup.CompanyId, "2300", "Other payables", AccountCategory.Liability);
        await using (var db = await ContextAsync())
        {
            db.GeneralLedgerAccounts.Add(replacement);
            await db.SaveChangesAsync();
        }
        await purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true);
        await purchasing.UpdateVendorAsync(setup.User, setup.VendorId, new PurchaseVendorInput
        {
            Number = "VEND-1", Name = "Edited while blocked", PayablesAccountId = replacement.Id
        });
        var edited = (await purchasing.GetVendorsAsync(setup.User)).Single(vendor => vendor.Id == setup.VendorId);
        Assert.True(edited.IsBlocked);
        Assert.Equal(replacement.Id, edited.PayablesAccountId);
        await Assert.ThrowsAsync<ValidationException>(() => purchasing.SaveGrniAccountAsync(setup.User, replacement.Id));
    }

    [Fact]
    public async Task VendorsWithoutPayablesAccountRemainVisibleAndCanBeBlocked()
    {
        var setup = await SetupAsync("vendor-unconfigured");
        var id = Guid.NewGuid();
        await using (var db = await ContextAsync())
        {
            db.Vendors.Add(new Vendor { Id = id, CompanyId = setup.CompanyId, Number = "LEGACY", Name = "Unconfigured" });
            await db.SaveChangesAsync();
        }
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        await purchasing.SetVendorBlockedAsync(setup.User, id, true);
        var vendor = (await purchasing.GetVendorsAsync(setup.User)).Single(candidate => candidate.Id == id);
        Assert.Null(vendor.PayablesAccountId);
        Assert.Empty(vendor.PayablesAccountNumber);
        Assert.True(vendor.IsBlocked);
    }

    [Fact]
    public async Task VendorOperationsRejectRevokedMembership()
    {
        var setup = await SetupAsync("vendor-revoked");
        await factory.RemoveCompanyMembershipAsync(
            Guid.Parse(setup.User.FindFirstValue(ClaimTypes.NameIdentifier)!), setup.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var purchasing = scope.ServiceProvider.GetRequiredService<IPurchasingService>();
        var input = new PurchaseVendorInput
        {
            Number = "REVOKED", Name = "Revoked", PayablesAccountId = setup.PayablesAccountId
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.GetVendorsAsync(setup.User));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.CreateVendorAsync(setup.User, input));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.UpdateVendorAsync(setup.User, setup.VendorId, input));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => purchasing.SetVendorBlockedAsync(setup.User, setup.VendorId, false));
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
