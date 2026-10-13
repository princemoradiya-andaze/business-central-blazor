using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessCentralBlazor.Web.Services;

public sealed class PurchaseVendorInput
{
    [Required, StringLength(32)]
    public string Number { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    public Guid PayablesAccountId { get; set; }
}

public sealed class PurchaseOrderInput
{
    [Required, StringLength(32)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public Guid VendorId { get; set; }
    public DateOnly OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public List<PurchaseOrderLineInput> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLineInput
{
    public Guid ItemId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999999.9999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999999.9999")]
    public decimal UnitCost { get; set; }

    public Guid? TaxCodeId { get; set; }
}

public sealed class PurchaseDocumentPostingInput
{
    [StringLength(32)]
    public string DocumentNumber { get; set; } = string.Empty;

    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public sealed record PurchaseVendorOption(Guid Id, string Number, string Name, string PayablesAccountNumber, bool IsBlocked);

public sealed record PurchaseItemOption(
    Guid Id,
    string Number,
    string Description,
    decimal QuantityOnHand,
    Guid? InventoryAccountId,
    bool IsBlocked);

public sealed record PurchaseAccountOption(
    Guid Id,
    string Number,
    string Name,
    AccountCategory Category,
    AccountType Type,
    bool AllowsDirectPosting,
    bool IsBlocked);

public sealed record PurchaseOrderLineDetail(
    int LineNumber,
    Guid ItemId,
    string ItemNumber,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    Guid? TaxCodeId);

public sealed record PurchaseOrderDetail(
    Guid Id,
    string OrderNumber,
    string Description,
    DateOnly OrderDate,
    string VendorNumber,
    string VendorName,
    PurchaseOrderStatus Status,
    Guid? ReceiptJournalBatchId,
    Guid? InvoiceJournalBatchId,
    IReadOnlyList<PurchaseOrderLineDetail> Lines);

public sealed record PurchasePayableDetail(
    string InvoiceNumber,
    DateOnly PostingDate,
    string OrderNumber,
    string VendorNumber,
    string VendorName,
    decimal NetAmount,
    decimal TaxAmount,
    decimal OpenAmount);

public sealed record PurchasingSetupDetail(
    Guid? GoodsReceivedNotInvoicedAccountId,
    IReadOnlyList<PurchaseAccountOption> Accounts);

public interface IPurchasingService
{
    Task<PurchasingSetupDetail> GetSetupAsync(ClaimsPrincipal user);
    Task SaveGrniAccountAsync(ClaimsPrincipal user, Guid accountId);
    Task<IReadOnlyList<PurchaseVendorOption>> GetVendorsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<PurchaseItemOption>> GetItemsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<PurchaseAccountOption>> GetAccountsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<AccountingPeriodListItem>> GetOpenPeriodsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<PurchaseOrderDetail>> GetOrdersAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<PurchasePayableDetail>> GetOpenPayablesAsync(ClaimsPrincipal user);
    Task CreateVendorAsync(ClaimsPrincipal user, PurchaseVendorInput input);
    Task<Guid> CreateOrderAsync(ClaimsPrincipal user, PurchaseOrderInput input);
    Task<Guid> CreateReceiptDraftAsync(ClaimsPrincipal user, Guid orderId, PurchaseDocumentPostingInput input);
    Task<Guid> CreateInvoiceDraftAsync(ClaimsPrincipal user, Guid orderId, PurchaseDocumentPostingInput input);
}

public interface IPurchasingJournalPostingService
{
    Task ApplyForPostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> lines,
        DateTimeOffset postedAtUtc);
}

public sealed class PurchasingService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IPurchasingService, IPurchasingJournalPostingService
{
    private const string PurchaseTemplateCode = "PURCHASES";
    private const decimal MaximumAmount = 999999999999999.9999m;

    public async Task<PurchasingSetupDetail> GetSetupAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var selected = await db.CompanySettings.AsNoTracking()
            .Where(settings => settings.CompanyId == companyId)
            .Select(settings => settings.GoodsReceivedNotInvoicedAccountId)
            .SingleOrDefaultAsync();
        var accounts = await GetAccountsAsync(db, companyId);
        return new PurchasingSetupDetail(selected, accounts);
    }

    public async Task SaveGrniAccountAsync(ClaimsPrincipal user, Guid accountId)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        if (accountId == Guid.Empty)
        {
            throw new ValidationException("Select a GRNI liability account.");
        }

        await using var db = await dbContextFactory.CreateDbContextAsync();
        await ValidatePostingAccountAsync(db, companyId, accountId, AccountCategory.Liability, "GRNI liability");
        if (await db.Vendors.AnyAsync(vendor =>
                vendor.CompanyId == companyId && vendor.PayablesAccountId == accountId))
        {
            throw new ValidationException("The GRNI account must be separate from vendor payables accounts.");
        }
        if (await db.PurchaseOrders.AnyAsync(order => order.CompanyId == companyId))
        {
            var configured = await db.CompanySettings
                .Where(settings => settings.CompanyId == companyId)
                .Select(settings => settings.GoodsReceivedNotInvoicedAccountId)
                .SingleOrDefaultAsync();
            if (configured != accountId)
            {
                throw new ValidationException(
                    "The GRNI account cannot be changed after purchase orders have been created.");
            }
        }

        var settings = await db.CompanySettings.SingleOrDefaultAsync(item => item.CompanyId == companyId)
            ?? throw new ValidationException("Configure the company's base currency before purchasing setup.");
        settings.GoodsReceivedNotInvoicedAccountId = accountId;
        await db.AuditEntries.AddAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ActorUserId = GetUserId(user),
            Action = "GoodsReceivedNotInvoicedAccountConfigured",
            EntityType = nameof(CompanySettings),
            EntityId = companyId.ToString(),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new { AccountId = accountId })
        });
        await SaveChangesAsync(db);
    }

    public async Task<IReadOnlyList<PurchaseVendorOption>> GetVendorsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await (
            from vendor in db.Vendors.AsNoTracking()
            join account in db.GeneralLedgerAccounts.AsNoTracking()
                on new { vendor.CompanyId, PayablesAccountId = vendor.PayablesAccountId!.Value }
                equals new { account.CompanyId, PayablesAccountId = account.Id }
            where vendor.CompanyId == companyId
            orderby vendor.Number
            select new PurchaseVendorOption(
                vendor.Id, vendor.Number, vendor.Name, account.Number, vendor.IsBlocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<PurchaseItemOption>> GetItemsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.Items.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Type == ItemType.Inventory)
            .OrderBy(item => item.Number)
            .Select(item => new PurchaseItemOption(
                item.Id, item.Number, item.Description, item.QuantityOnHand, item.InventoryAccountId, item.IsBlocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<PurchaseAccountOption>> GetAccountsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await GetAccountsAsync(db, companyId);
    }

    public async Task<IReadOnlyList<AccountingPeriodListItem>> GetOpenPeriodsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.AccountingPeriods.AsNoTracking()
            .Where(period => period.CompanyId == companyId && !period.IsClosed && !period.DateLocked)
            .OrderBy(period => period.StartsOn)
            .Select(period => new AccountingPeriodListItem(
                period.Id, period.Name, period.StartsOn, period.EndsOn, period.IsClosed, period.DateLocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var codes = await db.TaxCodes.AsNoTracking()
            .Where(code => code.CompanyId == companyId && !code.IsBlocked)
            .OrderBy(code => code.Code)
            .ToListAsync();
        var codeIds = codes.Select(code => code.Id).ToArray();
        var rates = await db.TaxRates.AsNoTracking()
            .Where(rate => rate.CompanyId == companyId && codeIds.Contains(rate.TaxCodeId))
            .OrderBy(rate => rate.StartsOn)
            .ToListAsync();
        return codes.Select(code => new GeneralJournalTaxCodeOption(
            code.Id,
            code.Code,
            code.Description,
            code.LiabilityAccountId,
            code.RecoverableAccountId,
            rates.Where(rate => rate.TaxCodeId == code.Id)
                .Select(rate => new GeneralJournalTaxRateOption(rate.RatePercent, rate.StartsOn, rate.EndsOn))
                .ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<PurchaseOrderDetail>> GetOrdersAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var orders = await (
            from order in db.PurchaseOrders.AsNoTracking()
            join vendor in db.Vendors.AsNoTracking()
                on new { order.CompanyId, order.VendorId } equals new { vendor.CompanyId, VendorId = vendor.Id }
            where order.CompanyId == companyId
            orderby order.OrderDate descending, order.OrderNumber
            select new { order, vendor.Number, vendor.Name })
            .ToListAsync();
        var result = new List<PurchaseOrderDetail>(orders.Count);
        foreach (var row in orders)
        {
            var lines = await (
                from line in db.PurchaseOrderLines.AsNoTracking()
                join item in db.Items.AsNoTracking()
                    on new { line.CompanyId, line.ItemId } equals new { item.CompanyId, ItemId = item.Id }
                where line.CompanyId == companyId && line.PurchaseOrderId == row.order.Id
                orderby line.LineNumber
                select new PurchaseOrderLineDetail(
                    line.LineNumber, item.Id, item.Number, line.Description,
                    line.Quantity, line.UnitCost, line.TaxCodeId))
                .ToListAsync();
            result.Add(new PurchaseOrderDetail(
                row.order.Id, row.order.OrderNumber, row.order.Description, row.order.OrderDate,
                row.Number, row.Name, row.order.Status, row.order.ReceiptJournalBatchId,
                row.order.InvoiceJournalBatchId, lines));
        }

        return result;
    }

    public async Task<IReadOnlyList<PurchasePayableDetail>> GetOpenPayablesAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await (
            from invoice in db.PurchaseInvoices.AsNoTracking()
            join order in db.PurchaseOrders.AsNoTracking()
                on new { invoice.CompanyId, invoice.PurchaseOrderId }
                equals new { order.CompanyId, PurchaseOrderId = order.Id }
            join vendor in db.Vendors.AsNoTracking()
                on new { invoice.CompanyId, invoice.VendorId }
                equals new { vendor.CompanyId, VendorId = vendor.Id }
            where invoice.CompanyId == companyId && invoice.PostedAtUtc != null
            orderby invoice.PostingDate, invoice.InvoiceNumber
            select new PurchasePayableDetail(
                invoice.InvoiceNumber, invoice.PostingDate, order.OrderNumber,
                vendor.Number, vendor.Name, invoice.NetAmount, invoice.TaxAmount, invoice.TotalAmount))
            .ToListAsync();
    }

    public async Task CreateVendorAsync(ClaimsPrincipal user, PurchaseVendorInput input)
    {
        NormalizeAndValidate(input);
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await ValidatePostingAccountAsync(db, companyId, input.PayablesAccountId, AccountCategory.Liability, "vendor payables");
        var configuredGrniAccountId = await RequireConfiguredGrniAccountAsync(db, companyId);
        if (configuredGrniAccountId == input.PayablesAccountId)
        {
            throw new ValidationException("A vendor payables account must be different from the company's GRNI account.");
        }
        if (await db.Vendors.AnyAsync(vendor => vendor.CompanyId == companyId && vendor.Number == input.Number))
        {
            throw new ValidationException("A vendor with this number already exists in the active company.");
        }

        db.Vendors.Add(new Vendor
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = input.Number,
            Name = input.Name,
            PayablesAccountId = input.PayablesAccountId
        });
        db.AuditEntries.Add(CreateAuditEntry(
            companyId, user, "VendorCreated", nameof(Vendor), input.Number, DateTimeOffset.UtcNow,
            new { input.Number, input.PayablesAccountId }));
        await SaveChangesAsync(db);
    }

    public async Task<Guid> CreateOrderAsync(ClaimsPrincipal user, PurchaseOrderInput input)
    {
        NormalizeAndValidate(input);
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(db);
        var vendor = await GetActiveVendorAsync(db, companyId, input.VendorId);
        if (vendor.PayablesAccountId is not { } payableAccountId)
        {
            throw new ValidationException("Configure a payables account for this vendor.");
        }
        await ValidatePostingAccountAsync(db, companyId, payableAccountId, AccountCategory.Liability, "vendor payables");
        await RequireGrniAccountAsync(db, companyId);
        if (await db.PurchaseOrders.AnyAsync(order => order.CompanyId == companyId && order.OrderNumber == input.OrderNumber))
        {
            throw new ValidationException("A purchase order with this number already exists in the active company.");
        }

        var order = new PurchaseOrder
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            VendorId = vendor.Id,
            OrderNumber = input.OrderNumber,
            Description = input.Description,
            OrderDate = input.OrderDate,
            Status = PurchaseOrderStatus.Open
        };
        db.PurchaseOrders.Add(order);
        var lineNumber = 10000;
        foreach (var line in input.Lines)
        {
            var item = await GetActiveItemAsync(db, companyId, line.ItemId);
            await ValidateItemAccountsAsync(db, companyId, item);
            if (line.TaxCodeId is { } taxCodeId &&
                !await db.TaxCodes.AnyAsync(code => code.CompanyId == companyId && code.Id == taxCodeId && !code.IsBlocked))
            {
                throw new ValidationException("Select an active purchase tax code from the active company.");
            }

            db.PurchaseOrderLines.Add(new PurchaseOrderLine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PurchaseOrderId = order.Id,
                LineNumber = lineNumber,
                ItemId = item.Id,
                Description = item.Description,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                TaxCodeId = line.TaxCodeId
            });
            lineNumber += 10000;
        }
        db.AuditEntries.Add(CreateAuditEntry(
            companyId, user, "PurchaseOrderCreated", nameof(PurchaseOrder), order.Id.ToString(),
            DateTimeOffset.UtcNow, new { order.OrderNumber, order.VendorId, LineCount = input.Lines.Count }));
        await SaveChangesAsync(db);
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return order.Id;
    }

    public async Task<Guid> CreateReceiptDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        PurchaseDocumentPostingInput input)
    {
        NormalizeAndValidate(input, requireDocumentNumber: false);
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(db);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == orderId);
        if (order is null || order.Status != PurchaseOrderStatus.Open || order.ReceiptJournalBatchId is not null)
        {
            throw new ValidationException("Only an open purchase order can be received.");
        }

        _ = await GetActiveVendorAsync(db, companyId, order.VendorId);
        var grniAccountId = await RequireGrniAccountAsync(db, companyId);
        await ValidatePostingAccountAsync(db, companyId, grniAccountId, AccountCategory.Liability, "GRNI liability");
        var period = await GetOpenPeriodAsync(db, companyId, input.AccountingPeriodId, input.PostingDate);
        var lines = await GetOrderLinesAsync(db, companyId, order.Id);
        var batch = await CreatePurchaseBatchAsync(
            db, companyId, period.Id, $"RCV-{order.OrderNumber}", $"Purchase receipt for {order.OrderNumber}");
        var receipt = new PurchaseReceipt
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            PurchaseOrderId = order.Id,
            JournalBatchId = batch.Id,
            PostingDate = input.PostingDate
        };
        db.PurchaseReceipts.Add(receipt);
        order.ReceiptJournalBatchId = batch.Id;
        order.ReceiptPostingDate = input.PostingDate;
        order.Status = PurchaseOrderStatus.ReceiptDraft;
        var lineNumber = 10000;
        foreach (var line in lines)
        {
            var item = await GetActiveItemAsync(db, companyId, line.ItemId);
            await ValidateItemAccountsAsync(db, companyId, item);
            var amount = RoundAmount(checked(line.Quantity * line.UnitCost));
            AddJournalLine(db, companyId, batch.Id, period.Id, input, lineNumber,
                item.InventoryAccountId!.Value, order.OrderNumber,
                $"Purchase receipt {order.OrderNumber}: {line.Description}", amount, 0);
            AddJournalLine(db, companyId, batch.Id, period.Id, input, lineNumber + 1000,
                grniAccountId, order.OrderNumber,
                $"Purchase receipt {order.OrderNumber}: {line.Description}", 0, amount);
            lineNumber += 10000;
        }

        await SaveChangesAsync(db);
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return batch.Id;
    }

    public async Task<Guid> CreateInvoiceDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        PurchaseDocumentPostingInput input)
    {
        NormalizeAndValidate(input, requireDocumentNumber: true);
        input.DocumentNumber = input.DocumentNumber.Trim();
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(db);
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == orderId);
        if (order is null || order.Status != PurchaseOrderStatus.Received || order.InvoiceJournalBatchId is not null)
        {
            throw new ValidationException("Only a received purchase order can be invoiced.");
        }
        if (await db.PurchaseInvoices.AnyAsync(invoice =>
                invoice.CompanyId == companyId && invoice.InvoiceNumber == input.DocumentNumber))
        {
            throw new ValidationException("A vendor invoice with this number already exists in the active company.");
        }

        var vendor = await GetActiveVendorAsync(db, companyId, order.VendorId);
        var payableAccountId = vendor.PayablesAccountId
            ?? throw new ValidationException("Configure a payables account for this vendor.");
        await ValidatePostingAccountAsync(db, companyId, payableAccountId, AccountCategory.Liability, "vendor payables");
        var grniAccountId = await RequireGrniAccountAsync(db, companyId);
        await ValidatePostingAccountAsync(db, companyId, grniAccountId, AccountCategory.Liability, "GRNI liability");
        var period = await GetOpenPeriodAsync(db, companyId, input.AccountingPeriodId, input.PostingDate);
        var lines = await GetOrderLinesAsync(db, companyId, order.Id);
        var totals = await GetInvoiceLineTotalsAsync(db, companyId, lines, input.PostingDate);
        var net = SumAmounts(totals.Select(total => total.NetAmount));
        var tax = SumAmounts(totals.Select(total => total.Tax?.Amount ?? 0));
        var gross = RoundNonNegative(checked(net + tax));
        var batch = await CreatePurchaseBatchAsync(
            db, companyId, period.Id, $"PINV-{input.DocumentNumber}", $"Purchase invoice {input.DocumentNumber}");
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            PurchaseOrderId = order.Id,
            VendorId = vendor.Id,
            JournalBatchId = batch.Id,
            InvoiceNumber = input.DocumentNumber,
            PostingDate = input.PostingDate,
            NetAmount = net,
            TaxAmount = tax,
            TotalAmount = gross
        };
        db.PurchaseInvoices.Add(invoice);
        order.InvoiceJournalBatchId = batch.Id;
        order.Status = PurchaseOrderStatus.InvoiceDraft;
        var lineNumber = 10000;
        AddJournalLine(db, companyId, batch.Id, period.Id, input, lineNumber, grniAccountId,
            invoice.InvoiceNumber, $"Clear GRNI {invoice.InvoiceNumber}", net, 0);
        lineNumber += 10000;
        foreach (var total in totals.Where(total => total.Tax is not null))
        {
            var taxLine = total.Tax!;
            db.GeneralJournalLines.Add(new GeneralJournalLine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                JournalBatchId = batch.Id,
                AccountingPeriodId = period.Id,
                AccountId = taxLine.RecoverableAccountId,
                LineNumber = lineNumber,
                PostingDate = input.PostingDate,
                DocumentNumber = invoice.InvoiceNumber,
                Description = $"Recoverable tax {taxLine.Code}",
                DebitAmount = taxLine.Amount,
                TaxCodeId = taxLine.TaxCodeId,
                TaxRateId = taxLine.RateId,
                TaxRatePercent = taxLine.RatePercent,
                TaxBaseAmount = total.NetAmount,
                TaxPostingType = TaxPostingType.Recoverable
            });
            lineNumber += 10000;
        }
        AddJournalLine(db, companyId, batch.Id, period.Id, input, lineNumber, payableAccountId,
            invoice.InvoiceNumber, $"Accounts payable {invoice.InvoiceNumber}", 0, gross);
        await SaveChangesAsync(db);
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return batch.Id;
    }

    public async Task ApplyForPostingAsync(
        ErpDbContext db,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> journalLines,
        DateTimeOffset postedAtUtc)
    {
        var receipt = await db.PurchaseReceipts.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.JournalBatchId == batch.Id);
        if (receipt is not null)
        {
            await ApplyReceiptPostingAsync(db, user, companyId, batch, journalLines, receipt, postedAtUtc);
            return;
        }

        var invoice = await db.PurchaseInvoices.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.JournalBatchId == batch.Id);
        if (invoice is not null)
        {
            await ApplyInvoicePostingAsync(db, user, companyId, batch, journalLines, invoice, postedAtUtc);
        }
    }

    private async Task ApplyReceiptPostingAsync(
        ErpDbContext db,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> journalLines,
        PurchaseReceipt receipt,
        DateTimeOffset postedAtUtc)
    {
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == receipt.PurchaseOrderId &&
            candidate.Status == PurchaseOrderStatus.ReceiptDraft &&
            candidate.ReceiptJournalBatchId == batch.Id);
        if (order is null || receipt.PostedAtUtc is not null ||
            batch.Status != JournalBatchStatus.Open ||
            batch.Description != $"Purchase receipt for {order.OrderNumber}" ||
            order.ReceiptPostingDate != receipt.PostingDate)
        {
            throw new ValidationException("The linked purchase receipt journal is invalid or already posted.");
        }

        _ = await GetActiveVendorAsync(db, companyId, order.VendorId);
        var grniAccountId = await RequireGrniAccountAsync(db, companyId);
        await ValidatePostingAccountAsync(db, companyId, grniAccountId, AccountCategory.Liability, "GRNI liability");
        var period = await ValidateLinkedPeriodAsync(db, companyId, batch, receipt.PostingDate);
        var orderLines = await GetOrderLinesAsync(db, companyId, order.Id);
        if (journalLines.Count != orderLines.Count * 2)
        {
            throw new ValidationException("The purchase receipt journal does not match the purchase order.");
        }

        var lineNumber = 10000;
        foreach (var line in orderLines)
        {
            var item = await GetActiveItemAsync(db, companyId, line.ItemId);
            await ValidateItemAccountsAsync(db, companyId, item);
            var amount = RoundAmount(checked(line.Quantity * line.UnitCost));
            ValidateLinkedJournalLine(
                journalLines.SingleOrDefault(candidate => candidate.LineNumber == lineNumber),
                companyId, batch.Id, period.Id, order.OrderNumber, receipt.PostingDate,
                $"Purchase receipt {order.OrderNumber}: {line.Description}",
                item.InventoryAccountId!.Value, amount, 0);
            ValidateLinkedJournalLine(
                journalLines.SingleOrDefault(candidate => candidate.LineNumber == lineNumber + 1000),
                companyId, batch.Id, period.Id, order.OrderNumber, receipt.PostingDate,
                $"Purchase receipt {order.OrderNumber}: {line.Description}",
                grniAccountId, 0, amount);

            var newQuantity = checked(item.QuantityOnHand + line.Quantity);
            var newValue = RoundAmount(checked(item.InventoryValue + amount));
            item.QuantityOnHand = newQuantity;
            item.InventoryValue = newValue;
            item.UnitCost = RoundAmount(newValue / newQuantity);
            db.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ItemId = item.Id,
                AccountingPeriodId = period.Id,
                JournalBatchId = batch.Id,
                DocumentNumber = BuildReceiptMovementNumber(order.OrderNumber, line.LineNumber),
                Description = $"Purchase receipt {order.OrderNumber}",
                PostingDate = receipt.PostingDate,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                InventoryValue = amount,
                PostedAtUtc = postedAtUtc,
                PostedByUserId = GetUserId(user)
            });
            lineNumber += 10000;
        }

        receipt.PostedAtUtc = postedAtUtc;
        receipt.PostedByUserId = GetUserId(user);
        order.Status = PurchaseOrderStatus.Received;
        AddAudit(db, companyId, user, "PurchaseReceiptPosted", nameof(PurchaseReceipt), receipt.Id, postedAtUtc,
            new { order.OrderNumber, receipt.JournalBatchId, receipt.PostingDate });
    }

    private async Task ApplyInvoicePostingAsync(
        ErpDbContext db,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> journalLines,
        PurchaseInvoice invoice,
        DateTimeOffset postedAtUtc)
    {
        var order = await db.PurchaseOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == invoice.PurchaseOrderId &&
            candidate.Status == PurchaseOrderStatus.InvoiceDraft &&
            candidate.InvoiceJournalBatchId == batch.Id);
        if (order is null || invoice.PostedAtUtc is not null ||
            batch.Status != JournalBatchStatus.Open ||
            batch.Description != $"Purchase invoice {invoice.InvoiceNumber}")
        {
            throw new ValidationException("The linked purchase invoice journal is invalid or already posted.");
        }

        var vendor = await GetActiveVendorAsync(db, companyId, invoice.VendorId);
        var payableAccountId = vendor.PayablesAccountId
            ?? throw new ValidationException("The vendor has no configured payables account.");
        await ValidatePostingAccountAsync(db, companyId, payableAccountId, AccountCategory.Liability, "vendor payables");
        var grniAccountId = await RequireGrniAccountAsync(db, companyId);
        await ValidatePostingAccountAsync(db, companyId, grniAccountId, AccountCategory.Liability, "GRNI liability");
        var period = await ValidateLinkedPeriodAsync(db, companyId, batch, invoice.PostingDate);
        var orderLines = await GetOrderLinesAsync(db, companyId, order.Id);
        var totals = await GetInvoiceLineTotalsAsync(db, companyId, orderLines, invoice.PostingDate);
        var net = SumAmounts(totals.Select(total => total.NetAmount));
        var taxAmount = SumAmounts(totals.Select(total => total.Tax?.Amount ?? 0));
        var gross = RoundNonNegative(checked(net + taxAmount));
        if (invoice.NetAmount != net || invoice.TaxAmount != taxAmount || invoice.TotalAmount != gross)
        {
            throw new ValidationException("Purchase invoice totals changed after the draft was created.");
        }

        var taxableLines = totals.Where(total => total.Tax is not null).ToList();
        if (journalLines.Count != 2 + taxableLines.Count)
        {
            throw new ValidationException("The purchase invoice journal does not match the purchase order.");
        }

        var lineNumber = 10000;
        ValidateLinkedJournalLine(
            journalLines.SingleOrDefault(candidate => candidate.LineNumber == lineNumber),
            companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
            $"Clear GRNI {invoice.InvoiceNumber}", grniAccountId, net, 0);
        lineNumber += 10000;
        foreach (var total in taxableLines)
        {
            var tax = total.Tax!;
            var line = journalLines.SingleOrDefault(candidate => candidate.LineNumber == lineNumber);
            ValidateLinkedJournalLine(
                line, companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
                $"Recoverable tax {tax.Code}", tax.RecoverableAccountId, tax.Amount, 0,
                allowTaxMetadata: true);
            if (line?.TaxCodeId != tax.TaxCodeId ||
                line.TaxRateId != tax.RateId ||
                line.TaxRatePercent != tax.RatePercent ||
                line.TaxBaseAmount != total.NetAmount ||
                line.TaxPostingType != TaxPostingType.Recoverable)
            {
                throw new ValidationException("The purchase invoice tax journal line has changed.");
            }
            lineNumber += 10000;
        }

        ValidateLinkedJournalLine(
            journalLines.SingleOrDefault(candidate => candidate.LineNumber == lineNumber),
            companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
            $"Accounts payable {invoice.InvoiceNumber}", payableAccountId, 0, gross);

        invoice.PostedAtUtc = postedAtUtc;
        invoice.PostedByUserId = GetUserId(user);
        order.Status = PurchaseOrderStatus.Invoiced;
        AddAudit(db, companyId, user, "PurchaseInvoicePosted", nameof(PurchaseInvoice), invoice.Id, postedAtUtc,
            new { invoice.InvoiceNumber, order.OrderNumber, invoice.TotalAmount, invoice.JournalBatchId });
    }

    private static async Task<IReadOnlyList<PurchaseAccountOption>> GetAccountsAsync(ErpDbContext db, Guid companyId) =>
        await db.GeneralLedgerAccounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .OrderBy(account => account.Number)
            .Select(account => new PurchaseAccountOption(
                account.Id, account.Number, account.Name, account.Category, account.Type,
                account.AllowsDirectPosting, account.IsBlocked))
            .ToListAsync();

    private async Task<Guid> GetRequiredCompanyIdAsync(ClaimsPrincipal user) =>
        (await companyScopeAuthorizer.ResolveActiveCompanyAsync(user))?.CompanyId
        ?? throw new UnauthorizedAccessException("An active company is required for purchasing.");

    private static async Task<Vendor> GetActiveVendorAsync(ErpDbContext db, Guid companyId, Guid vendorId) =>
        await db.Vendors.SingleOrDefaultAsync(vendor =>
                vendor.CompanyId == companyId && vendor.Id == vendorId && !vendor.IsBlocked)
            ?? throw new ValidationException("The vendor is not active and unblocked in the selected company.");

    private static async Task<Item> GetActiveItemAsync(ErpDbContext db, Guid companyId, Guid itemId) =>
        await db.Items.SingleOrDefaultAsync(item =>
                item.CompanyId == companyId && item.Id == itemId &&
                item.Type == ItemType.Inventory && !item.IsBlocked)
            ?? throw new ValidationException("The item is not active and unblocked in the selected company.");

    private static async Task ValidateItemAccountsAsync(ErpDbContext db, Guid companyId, Item item)
    {
        if (item.InventoryAccountId is not { } inventoryAccountId)
        {
            throw new ValidationException($"Configure an inventory asset account for item '{item.Number}'.");
        }
        await ValidatePostingAccountAsync(db, companyId, inventoryAccountId, AccountCategory.Asset, "inventory asset");
    }

    private static async Task<Guid> RequireGrniAccountAsync(ErpDbContext db, Guid companyId) =>
        await RequireConfiguredGrniAccountAsync(db, companyId)
        ?? throw new ValidationException("Configure a GRNI liability account in company settings before purchasing.");

    private static async Task<Guid?> RequireConfiguredGrniAccountAsync(ErpDbContext db, Guid companyId) =>
        await db.CompanySettings
            .Where(settings => settings.CompanyId == companyId)
            .Select(settings => settings.GoodsReceivedNotInvoicedAccountId)
            .SingleOrDefaultAsync();

    private static async Task ValidatePostingAccountAsync(
        ErpDbContext db,
        Guid companyId,
        Guid accountId,
        AccountCategory category,
        string role)
    {
        if (!await db.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId && account.Id == accountId &&
                account.Category == category && account.Type == AccountType.Posting &&
                account.AllowsDirectPosting && !account.IsBlocked))
        {
            throw new ValidationException($"Select an unblocked, directly-postable {role} account from the active company.");
        }
    }

    private static async Task<AccountingPeriod> GetOpenPeriodAsync(
        ErpDbContext db,
        Guid companyId,
        Guid periodId,
        DateOnly postingDate) =>
        await db.AccountingPeriods.SingleOrDefaultAsync(period =>
                period.CompanyId == companyId && period.Id == periodId &&
                !period.IsClosed && !period.DateLocked &&
                period.StartsOn <= postingDate && period.EndsOn >= postingDate)
            ?? throw new ValidationException("Select an open, unlocked accounting period containing the posting date.");

    private static async Task<AccountingPeriod> ValidateLinkedPeriodAsync(
        ErpDbContext db,
        Guid companyId,
        GeneralJournalBatch batch,
        DateOnly postingDate) =>
        await db.AccountingPeriods.SingleOrDefaultAsync(period =>
                period.CompanyId == companyId && period.Id == batch.DefaultAccountingPeriodId &&
                !period.IsClosed && !period.DateLocked &&
                period.StartsOn <= postingDate && period.EndsOn >= postingDate)
            ?? throw new ValidationException("The purchase journal period is closed, date-locked, or invalid.");

    private static async Task<List<PurchaseOrderLine>> GetOrderLinesAsync(
        ErpDbContext db,
        Guid companyId,
        Guid orderId)
    {
        var lines = await db.PurchaseOrderLines
            .Where(line => line.CompanyId == companyId && line.PurchaseOrderId == orderId)
            .OrderBy(line => line.LineNumber)
            .ToListAsync();
        return lines.Count > 0
            ? lines
            : throw new ValidationException("The purchase order has no lines.");
    }

    private static async Task<List<InvoiceLineTotal>> GetInvoiceLineTotalsAsync(
        ErpDbContext db,
        Guid companyId,
        IReadOnlyList<PurchaseOrderLine> lines,
        DateOnly postingDate)
    {
        var result = new List<InvoiceLineTotal>(lines.Count);
        foreach (var line in lines)
        {
            var net = RoundAmount(checked(line.Quantity * line.UnitCost));
            PurchaseTaxPosting? tax = null;
            if (line.TaxCodeId is { } taxCodeId)
            {
                var code = await db.TaxCodes.SingleOrDefaultAsync(candidate =>
                    candidate.CompanyId == companyId && candidate.Id == taxCodeId && !candidate.IsBlocked);
                var rate = await db.TaxRates.SingleOrDefaultAsync(candidate =>
                    candidate.CompanyId == companyId && candidate.TaxCodeId == taxCodeId &&
                    candidate.StartsOn <= postingDate && (candidate.EndsOn == null || candidate.EndsOn >= postingDate));
                if (code is null || rate is null)
                {
                    throw new ValidationException("The selected purchase tax code has no active rate for the invoice date.");
                }
                await ValidatePostingAccountAsync(
                    db, companyId, code.RecoverableAccountId, AccountCategory.Asset, "recoverable tax");
                var amount = RoundNonNegative(checked(net * rate.RatePercent / 100m));
                if (amount <= 0)
                {
                    throw new ValidationException("The purchase tax amount rounds to zero; increase the line value or remove its tax code.");
                }
                tax = new PurchaseTaxPosting(code.Id, code.Code, rate.Id, rate.RatePercent, code.RecoverableAccountId, amount);
            }
            result.Add(new InvoiceLineTotal(line, net, tax));
        }

        return result;
    }

    private static async Task<GeneralJournalBatch> CreatePurchaseBatchAsync(
        ErpDbContext db,
        Guid companyId,
        Guid periodId,
        string name,
        string description)
    {
        var template = await db.GeneralJournalTemplates.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Code == PurchaseTemplateCode);
        if (template is null)
        {
            template = new GeneralJournalTemplate
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Code = PurchaseTemplateCode,
                Description = "Purchase documents",
                Type = JournalTemplateType.Purchases
            };
            db.GeneralJournalTemplates.Add(template);
        }
        var batch = new GeneralJournalBatch
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            JournalTemplateId = template.Id,
            DefaultAccountingPeriodId = periodId,
            Name = $"{name[..Math.Min(name.Length, 20)]}-{Guid.NewGuid():N}"[..32],
            Description = description,
            Status = JournalBatchStatus.Open,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.GeneralJournalBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch;
    }

    private static void AddJournalLine(
        ErpDbContext db,
        Guid companyId,
        Guid batchId,
        Guid periodId,
        PurchaseDocumentPostingInput input,
        int lineNumber,
        Guid accountId,
        string documentNumber,
        string description,
        decimal debit,
        decimal credit) =>
        db.GeneralJournalLines.Add(new GeneralJournalLine
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            JournalBatchId = batchId,
            AccountingPeriodId = periodId,
            AccountId = accountId,
            LineNumber = lineNumber,
            PostingDate = input.PostingDate,
            DocumentNumber = documentNumber,
            Description = description,
            DebitAmount = debit,
            CreditAmount = credit
        });

    private static void ValidateLinkedJournalLine(
        GeneralJournalLine? line,
        Guid companyId,
        Guid batchId,
        Guid periodId,
        string documentNumber,
        DateOnly postingDate,
        string description,
        Guid accountId,
        decimal debit,
        decimal credit,
        bool allowTaxMetadata = false)
    {
        if (line is null || line.CompanyId != companyId || line.JournalBatchId != batchId ||
            line.AccountingPeriodId != periodId || line.DocumentNumber != documentNumber ||
            line.PostingDate != postingDate || line.Description != description ||
            line.AccountId != accountId || line.DebitAmount != debit || line.CreditAmount != credit ||
            line.CurrencyCode is not null || line.CurrencyDebitAmount is not null ||
            line.CurrencyCreditAmount is not null ||
            (!allowTaxMetadata && (line.TaxCodeId is not null || line.TaxRateId is not null ||
                                   line.TaxRatePercent is not null || line.TaxBaseAmount is not null ||
                                   line.TaxPostingType is not null)))
        {
            throw new ValidationException("The linked purchase journal lines have changed.");
        }
    }

    private static string BuildReceiptMovementNumber(string orderNumber, int lineNumber) =>
        $"PUR-{orderNumber}-{lineNumber}";

    private static decimal RoundAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > MaximumAmount)
        {
            throw new ValidationException("The purchase amount is outside the supported accounting range.");
        }
        return rounded;
    }

    private static decimal RoundNonNegative(decimal amount)
    {
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (rounded < 0 || rounded > MaximumAmount)
        {
            throw new ValidationException("The purchase amount is outside the supported accounting range.");
        }
        return rounded;
    }

    private static decimal SumAmounts(IEnumerable<decimal> amounts)
    {
        var total = 0m;
        foreach (var amount in amounts)
        {
            total = RoundNonNegative(checked(total + amount));
        }
        return total;
    }

    private static void NormalizeAndValidate(PurchaseVendorInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Number = input.Number?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (input.PayablesAccountId == Guid.Empty)
        {
            throw new ValidationException("Select a vendor payables account.");
        }
    }

    private static void NormalizeAndValidate(PurchaseOrderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.OrderNumber = input.OrderNumber?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (input.VendorId == Guid.Empty || input.Lines.Count == 0 ||
            input.Lines.Any(line => line.ItemId == Guid.Empty ||
                line.Quantity <= 0 || line.UnitCost <= 0 ||
                line.Quantity > MaximumAmount || line.UnitCost > MaximumAmount ||
                decimal.Round(line.Quantity, 4) != line.Quantity ||
                decimal.Round(line.UnitCost, 4) != line.UnitCost) ||
            input.Lines.Select(line => line.ItemId).Distinct().Count() != input.Lines.Count)
        {
            throw new ValidationException("Select a vendor and distinct inventory items with positive quantities and costs to four decimal places.");
        }
    }

    private static void NormalizeAndValidate(PurchaseDocumentPostingInput input, bool requireDocumentNumber)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.DocumentNumber = input.DocumentNumber?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if ((requireDocumentNumber && string.IsNullOrWhiteSpace(input.DocumentNumber)) ||
            input.AccountingPeriodId == Guid.Empty)
        {
            throw new ValidationException(
                requireDocumentNumber
                    ? "Enter a vendor invoice number and select an accounting period."
                    : "Select an accounting period.");
        }
    }

    private static AuditEntry CreateAuditEntry(
        Guid companyId,
        ClaimsPrincipal user,
        string action,
        string entityType,
        string entityId,
        DateTimeOffset occurredAtUtc,
        object metadata) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ActorUserId = GetUserId(user),
        Action = action,
        EntityType = entityType,
        EntityId = entityId,
        OccurredAtUtc = occurredAtUtc,
        MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata)
    };

    private static void AddAudit(
        ErpDbContext db,
        Guid companyId,
        ClaimsPrincipal user,
        string action,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        object metadata) =>
        db.AuditEntries.Add(CreateAuditEntry(
            companyId, user, action, entityType, entityId.ToString(), occurredAtUtc, metadata));

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static async Task SaveChangesAsync(ErpDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException("Purchasing data changed concurrently. Reload it and retry.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException
        {
            Number: 2601 or 2627
        })
        {
            throw new ValidationException(
                "The purchase reference already exists or a concurrent posting completed. Reload before retrying.",
                exception);
        }
    }

    private static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(ErpDbContext db)
    {
        if (!db.Database.IsRelational())
        {
            return null;
        }
        return await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    }

    private sealed record PurchaseTaxPosting(
        Guid TaxCodeId,
        string Code,
        Guid RateId,
        decimal RatePercent,
        Guid RecoverableAccountId,
        decimal Amount);

    private sealed record InvoiceLineTotal(PurchaseOrderLine Line, decimal NetAmount, PurchaseTaxPosting? Tax);
}
