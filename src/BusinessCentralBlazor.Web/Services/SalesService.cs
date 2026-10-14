using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessCentralBlazor.Web.Services;

public sealed class SalesCustomerInput
{
    [Required, StringLength(32)]
    public string Number { get; set; } = string.Empty;

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    public Guid ReceivablesAccountId { get; set; }
}

public sealed class SalesOrderInput
{
    [Required, StringLength(32)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public DateOnly OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public List<SalesOrderLineInput> Lines { get; set; } = [];
}

public sealed class SalesOrderLineInput
{
    public Guid ItemId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999999.9999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999999.9999")]
    public decimal UnitPrice { get; set; }

    public Guid SalesAccountId { get; set; }
    public Guid? TaxCodeId { get; set; }
}

public sealed class SalesDocumentPostingInput
{
    [StringLength(32)]
    public string DocumentNumber { get; set; } = string.Empty;

    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public sealed record SalesCustomerOption(
    Guid Id,
    string Number,
    string Name,
    Guid ReceivablesAccountId,
    string ReceivablesAccountNumber,
    bool IsBlocked,
    bool IsInactive);

public sealed record SalesItemOption(Guid Id, string Number, string Description, decimal QuantityOnHand, bool IsBlocked);

public sealed record SalesAccountOption(Guid Id, string Number, string Name, AccountCategory Category, bool IsBlocked);

public sealed record SalesOrderLineDetail(
    int LineNumber,
    Guid ItemId,
    string ItemNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string SalesAccountNumber,
    Guid? TaxCodeId);

public sealed record SalesOrderDetail(
    Guid Id,
    string OrderNumber,
    string Description,
    DateOnly OrderDate,
    string CustomerNumber,
    string CustomerName,
    SalesOrderStatus Status,
    Guid? ShipmentJournalBatchId,
    Guid? InvoiceJournalBatchId,
    IReadOnlyList<SalesOrderLineDetail> Lines);

public sealed record SalesReceivableDetail(
    string InvoiceNumber,
    DateOnly PostingDate,
    string OrderNumber,
    string CustomerNumber,
    string CustomerName,
    decimal NetAmount,
    decimal TaxAmount,
    decimal OpenAmount);

public interface ISalesService
{
    Task<IReadOnlyList<SalesCustomerOption>> GetCustomersAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<SalesItemOption>> GetItemsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<SalesAccountOption>> GetSalesAccountsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<AccountingPeriodListItem>> GetOpenPeriodsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<SalesOrderDetail>> GetOrdersAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<SalesReceivableDetail>> GetOpenReceivablesAsync(ClaimsPrincipal user);
    Task CreateCustomerAsync(ClaimsPrincipal user, SalesCustomerInput input);
    Task UpdateCustomerAsync(ClaimsPrincipal user, Guid customerId, SalesCustomerInput input);
    Task SetCustomerInactiveAsync(ClaimsPrincipal user, Guid customerId, bool isInactive);
    Task<Guid> CreateOrderAsync(ClaimsPrincipal user, SalesOrderInput input);
    Task<Guid> CreateShipmentDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        SalesDocumentPostingInput input);
    Task<Guid> CreateInvoiceDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        SalesDocumentPostingInput input);
}

public interface ISalesJournalPostingService
{
    Task ApplyForPostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> lines,
        DateTimeOffset postedAtUtc);
}

public sealed class SalesService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : ISalesService, ISalesJournalPostingService
{
    private const string SalesTemplateCode = "SALES";
    private const decimal MaximumAmount = 999999999999999.9999m;

    public async Task<IReadOnlyList<SalesCustomerOption>> GetCustomersAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await (
            from customer in dbContext.Customers.AsNoTracking()
            join account in dbContext.GeneralLedgerAccounts.AsNoTracking()
                on new { customer.CompanyId, ReceivablesAccountId = customer.ReceivablesAccountId!.Value }
                equals new { account.CompanyId, ReceivablesAccountId = account.Id }
            where customer.CompanyId == companyId
            orderby customer.Number
            select new SalesCustomerOption(
                customer.Id,
                customer.Number,
                customer.Name,
                account.Id,
                account.Number,
                customer.IsBlocked,
                customer.IsInactive))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SalesItemOption>> GetItemsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.Items.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Type == ItemType.Inventory)
            .OrderBy(item => item.Number)
            .Select(item => new SalesItemOption(
                item.Id,
                item.Number,
                item.Description,
                item.QuantityOnHand,
                item.IsBlocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SalesAccountOption>> GetSalesAccountsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId &&
                              account.Type == AccountType.Posting &&
                              (account.Category == AccountCategory.Income ||
                               account.Category == AccountCategory.Asset) &&
                              account.AllowsDirectPosting)
            .OrderBy(account => account.Number)
            .Select(account => new SalesAccountOption(
                account.Id,
                account.Number,
                account.Name,
                account.Category,
                account.IsBlocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<AccountingPeriodListItem>> GetOpenPeriodsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.AccountingPeriods.AsNoTracking()
            .Where(period => period.CompanyId == companyId && !period.IsClosed && !period.DateLocked)
            .OrderBy(period => period.StartsOn)
            .Select(period => new AccountingPeriodListItem(
                period.Id,
                period.Name,
                period.StartsOn,
                period.EndsOn,
                period.IsClosed,
                period.DateLocked))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var codes = await dbContext.TaxCodes.AsNoTracking()
            .Where(code => code.CompanyId == companyId && !code.IsBlocked)
            .OrderBy(code => code.Code)
            .ToListAsync();
        var codeIds = codes.Select(code => code.Id).ToArray();
        var rates = await dbContext.TaxRates.AsNoTracking()
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

    public async Task<IReadOnlyList<SalesOrderDetail>> GetOrdersAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var orders = await dbContext.SalesOrders.AsNoTracking()
            .Where(order => order.CompanyId == companyId)
            .OrderByDescending(order => order.OrderDate)
            .ThenBy(order => order.OrderNumber)
            .ToListAsync();
        if (orders.Count == 0)
        {
            return [];
        }

        var customerIds = orders.Select(order => order.CustomerId).Distinct().ToArray();
        var customers = await dbContext.Customers.AsNoTracking()
            .Where(customer => customer.CompanyId == companyId && customerIds.Contains(customer.Id))
            .ToDictionaryAsync(customer => customer.Id);
        var orderIds = orders.Select(order => order.Id).ToArray();
        var lines = await (
            from line in dbContext.SalesOrderLines.AsNoTracking()
            join item in dbContext.Items.AsNoTracking()
                on new { line.CompanyId, line.ItemId } equals new { item.CompanyId, ItemId = item.Id }
            join account in dbContext.GeneralLedgerAccounts.AsNoTracking()
                on new { line.CompanyId, line.SalesAccountId } equals new { account.CompanyId, SalesAccountId = account.Id }
            where line.CompanyId == companyId && orderIds.Contains(line.SalesOrderId)
            orderby line.LineNumber
            select new
            {
                line.SalesOrderId,
                Detail = new SalesOrderLineDetail(
                    line.LineNumber,
                    line.ItemId,
                    item.Number,
                    line.Description,
                    line.Quantity,
                    line.UnitPrice,
                    account.Number,
                    line.TaxCodeId)
            }).ToListAsync();
        return orders.Select(order =>
        {
            var customer = customers[order.CustomerId];
            return new SalesOrderDetail(
                order.Id,
                order.OrderNumber,
                order.Description,
                order.OrderDate,
                customer.Number,
                customer.Name,
                order.Status,
                order.ShipmentJournalBatchId,
                order.InvoiceJournalBatchId,
                lines.Where(line => line.SalesOrderId == order.Id).Select(line => line.Detail).ToArray());
        }).ToArray();
    }

    public async Task<IReadOnlyList<SalesReceivableDetail>> GetOpenReceivablesAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await (
            from invoice in dbContext.SalesInvoices.AsNoTracking()
            join customer in dbContext.Customers.AsNoTracking()
                on new { invoice.CompanyId, invoice.CustomerId } equals new { customer.CompanyId, CustomerId = customer.Id }
            join order in dbContext.SalesOrders.AsNoTracking()
                on new { invoice.CompanyId, invoice.SalesOrderId } equals new { order.CompanyId, SalesOrderId = order.Id }
            where invoice.CompanyId == companyId && invoice.PostedAtUtc != null
            orderby invoice.PostingDate, invoice.InvoiceNumber
            select new SalesReceivableDetail(
                invoice.InvoiceNumber,
                invoice.PostingDate,
                order.OrderNumber,
                customer.Number,
                customer.Name,
                invoice.NetAmount,
                invoice.TaxAmount,
                invoice.TotalAmount))
            .ToListAsync();
    }

    public async Task CreateCustomerAsync(ClaimsPrincipal user, SalesCustomerInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        if (await dbContext.Customers.AnyAsync(customer =>
                customer.CompanyId == companyId && customer.Number == input.Number))
        {
            throw new ValidationException("A customer with this number already exists in the active company.");
        }

        await ValidatePostingAccountAsync(
            dbContext,
            companyId,
            input.ReceivablesAccountId,
            AccountCategory.Asset,
            "accounts receivable");
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Number = input.Number,
            Name = input.Name,
            ReceivablesAccountId = input.ReceivablesAccountId
        };
        dbContext.Customers.Add(customer);
        AddSalesAudit(
            dbContext,
            companyId,
            user,
            "SalesCustomerCreated",
            nameof(Customer),
            customer.Id,
            DateTimeOffset.UtcNow,
            new { customer.Number, customer.Name });
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateCustomerAsync(ClaimsPrincipal user, Guid customerId, SalesCustomerInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var customer = await dbContext.Customers.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == customerId);
        if (customer is null)
        {
            throw new ValidationException("The customer is not available in the active company.");
        }

        if (await dbContext.Customers.AnyAsync(candidate =>
                candidate.CompanyId == companyId &&
                candidate.Id != customerId &&
                candidate.Number == input.Number))
        {
            throw new ValidationException("A customer with this number already exists in the active company.");
        }

        await ValidatePostingAccountAsync(
            dbContext,
            companyId,
            input.ReceivablesAccountId,
            AccountCategory.Asset,
            "accounts receivable");

        customer.Number = input.Number;
        customer.Name = input.Name;
        customer.ReceivablesAccountId = input.ReceivablesAccountId;
        AddSalesAudit(
            dbContext,
            companyId,
            user,
            "SalesCustomerUpdated",
            nameof(Customer),
            customer.Id,
            DateTimeOffset.UtcNow,
            new { customer.Number, customer.Name });
        await dbContext.SaveChangesAsync();
    }

    public async Task SetCustomerInactiveAsync(ClaimsPrincipal user, Guid customerId, bool isInactive)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var customer = await dbContext.Customers.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == customerId);
        if (customer is null)
        {
            throw new ValidationException("The customer is not available in the active company.");
        }

        if (customer.IsInactive == isInactive)
        {
            return;
        }

        customer.IsInactive = isInactive;
        AddSalesAudit(
            dbContext,
            companyId,
            user,
            isInactive ? "SalesCustomerDeactivated" : "SalesCustomerReactivated",
            nameof(Customer),
            customer.Id,
            DateTimeOffset.UtcNow,
            new { customer.Number, customer.IsInactive });
        await dbContext.SaveChangesAsync();
    }

    public async Task<Guid> CreateOrderAsync(ClaimsPrincipal user, SalesOrderInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);

        if (await dbContext.SalesOrders.AnyAsync(order =>
                order.CompanyId == companyId && order.OrderNumber == input.OrderNumber))
        {
            throw new ValidationException("A sales order with this number already exists in the active company.");
        }

        var customer = await dbContext.Customers.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == input.CustomerId &&
            !candidate.IsBlocked &&
            !candidate.IsInactive);
        if (customer?.ReceivablesAccountId is not { } receivablesAccountId)
        {
            throw new ValidationException("Select an active customer with a configured receivables account.");
        }

        await ValidatePostingAccountAsync(
            dbContext,
            companyId,
            receivablesAccountId,
            AccountCategory.Asset,
            "accounts receivable");
        var itemIds = input.Lines.Select(line => line.ItemId).ToArray();
        if (itemIds.Distinct().Count() != itemIds.Length)
        {
            throw new ValidationException("An item can appear only once on a sales order.");
        }

        var order = new SalesOrder
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            CustomerId = customer.Id,
            OrderNumber = input.OrderNumber,
            Description = input.Description,
            OrderDate = input.OrderDate,
            Status = SalesOrderStatus.Open
        };
        dbContext.SalesOrders.Add(order);
        for (var index = 0; index < input.Lines.Count; index++)
        {
            var lineInput = input.Lines[index];
            var item = await dbContext.Items.SingleOrDefaultAsync(candidate =>
                candidate.CompanyId == companyId &&
                candidate.Id == lineInput.ItemId &&
                candidate.Type == ItemType.Inventory &&
                !candidate.IsBlocked);
            if (item is null)
            {
                throw new ValidationException("Select an active inventory item from the active company.");
            }

            await ValidatePostingAccountAsync(
                dbContext, companyId, lineInput.SalesAccountId, AccountCategory.Income, "sales revenue");
            await ValidateItemCostAccountsAsync(dbContext, companyId, item);
            ValidateQuantityAndUnitPrice(lineInput.Quantity, lineInput.UnitPrice);
            if (lineInput.TaxCodeId is { } taxCodeId &&
                !await dbContext.TaxCodes.AnyAsync(code =>
                    code.CompanyId == companyId && code.Id == taxCodeId && !code.IsBlocked))
            {
                throw new ValidationException("Select an unblocked tax code from the active company.");
            }

            dbContext.SalesOrderLines.Add(new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SalesOrderId = order.Id,
                LineNumber = (index + 1) * 10000,
                ItemId = item.Id,
                Description = item.Description,
                Quantity = lineInput.Quantity,
                UnitPrice = lineInput.UnitPrice,
                SalesAccountId = lineInput.SalesAccountId,
                TaxCodeId = lineInput.TaxCodeId
            });
        }

        await dbContext.SaveChangesAsync();
        dbContext.AuditEntries.Add(CreateAuditEntry(
            companyId,
            user,
            "SalesOrderCreated",
            nameof(SalesOrder),
            order.Id,
            DateTimeOffset.UtcNow,
            new { order.OrderNumber, order.CustomerId, LineCount = input.Lines.Count }));
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return order.Id;
    }

    public async Task<Guid> CreateShipmentDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        SalesDocumentPostingInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        var order = await dbContext.SalesOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == orderId);
        if (order is null || order.Status != SalesOrderStatus.Open)
        {
            throw new ValidationException("Only an open sales order in the active company can be shipped.");
        }

        var customer = await GetActiveCustomerAsync(dbContext, companyId, order.CustomerId);
        if (customer.ReceivablesAccountId is not { } receivablesAccountId)
        {
            throw new ValidationException("The customer no longer has a configured accounts receivable account.");
        }

        await ValidatePostingAccountAsync(
            dbContext, companyId, receivablesAccountId, AccountCategory.Asset, "accounts receivable");
        var period = await GetOpenPeriodAsync(dbContext, companyId, input.AccountingPeriodId, input.PostingDate);
        var lines = await GetOrderLinesAsync(dbContext, companyId, order.Id);
        var shipmentLines = new List<ShipmentLinePosting>();
        foreach (var orderLine in lines)
        {
            var item = await GetActiveInventoryItemAsync(dbContext, companyId, orderLine.ItemId);
            await ValidateItemCostAccountsAsync(dbContext, companyId, item);
            shipmentLines.Add(new ShipmentLinePosting(
                orderLine,
                item.Id,
                CalculateInventoryIssueAmount(item, orderLine.Quantity)));
        }

        var batch = await CreateSalesBatchAsync(
            dbContext,
            companyId,
            period.Id,
            $"SHIP-{order.OrderNumber}",
            $"Shipment for {order.OrderNumber}");

        var journalLineNumber = 10000;
        foreach (var shipmentLine in shipmentLines)
        {
            var item = await GetActiveInventoryItemAsync(dbContext, companyId, shipmentLine.ItemId);
            var description = $"Shipment {order.OrderNumber}: {shipmentLine.OrderLine.Description}";
            AddJournalLine(
                dbContext, companyId, batch.Id, period.Id, item.InventoryIssueExpenseAccountId!.Value,
                journalLineNumber, input, order.OrderNumber, description, shipmentLine.InventoryValue, 0);
            AddJournalLine(
                dbContext, companyId, batch.Id, period.Id, item.InventoryAccountId!.Value,
                journalLineNumber + 1000, input, order.OrderNumber, description, 0, shipmentLine.InventoryValue);
            journalLineNumber += 10000;
        }

        order.Status = SalesOrderStatus.ShipmentDraft;
        order.ShipmentJournalBatchId = batch.Id;
        order.ShipmentPostingDate = input.PostingDate;
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return batch.Id;
    }

    public async Task<Guid> CreateInvoiceDraftAsync(
        ClaimsPrincipal user,
        Guid orderId,
        SalesDocumentPostingInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        input.DocumentNumber = input.DocumentNumber.Trim();
        if (string.IsNullOrWhiteSpace(input.DocumentNumber))
        {
            throw new ValidationException("Enter an invoice number.");
        }
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        var order = await dbContext.SalesOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == orderId);
        if (order is null || order.Status != SalesOrderStatus.Shipped)
        {
            throw new ValidationException("Only a fully shipped sales order can be invoiced.");
        }

        if (await dbContext.SalesInvoices.AnyAsync(invoice =>
                invoice.CompanyId == companyId && invoice.InvoiceNumber == input.DocumentNumber))
        {
            throw new ValidationException("An invoice with this number already exists in the active company.");
        }

        var customer = await GetActiveCustomerAsync(dbContext, companyId, order.CustomerId);
        var receivablesAccountId = customer.ReceivablesAccountId
            ?? throw new ValidationException("Configure an accounts receivable posting account for this customer.");
        await ValidatePostingAccountAsync(
            dbContext, companyId, receivablesAccountId, AccountCategory.Asset, "accounts receivable");
        var period = await GetOpenPeriodAsync(dbContext, companyId, input.AccountingPeriodId, input.PostingDate);
        var lines = await GetOrderLinesAsync(dbContext, companyId, order.Id);
        var totals = new List<InvoiceLinePosting>();
        foreach (var line in lines)
        {
            await ValidatePostingAccountAsync(
                dbContext, companyId, line.SalesAccountId, AccountCategory.Income, "sales revenue");
            var net = RoundAmount(checked(line.Quantity * line.UnitPrice));
            var taxDetails = await CalculateTaxAsync(dbContext, companyId, line, input.PostingDate, net);
            totals.Add(new InvoiceLinePosting(line, net, taxDetails));
        }

        var netTotal = SumAmounts(totals.Select(line => line.NetAmount));
        var taxTotal = SumAmounts(totals.Select(line => line.Tax?.Amount ?? 0));
        var grossTotal = RoundNonNegative(checked(netTotal + taxTotal));
        var batch = await CreateSalesBatchAsync(
            dbContext,
            companyId,
            period.Id,
            $"INV-{input.DocumentNumber}",
            $"Invoice {input.DocumentNumber}");
        var invoice = new SalesInvoice
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            SalesOrderId = order.Id,
            CustomerId = customer.Id,
            JournalBatchId = batch.Id,
            InvoiceNumber = input.DocumentNumber,
            PostingDate = input.PostingDate,
            NetAmount = netTotal,
            TaxAmount = taxTotal,
            TotalAmount = grossTotal
        };
        dbContext.SalesInvoices.Add(invoice);
        var lineNumber = 10000;
        AddJournalLine(
            dbContext, companyId, batch.Id, period.Id, receivablesAccountId,
            lineNumber, input, invoice.InvoiceNumber, $"Accounts receivable {invoice.InvoiceNumber}", grossTotal, 0);
        lineNumber += 10000;
        foreach (var total in totals)
        {
            AddJournalLine(
                dbContext, companyId, batch.Id, period.Id, total.OrderLine.SalesAccountId,
                lineNumber, input, invoice.InvoiceNumber, total.OrderLine.Description, 0, total.NetAmount);
            lineNumber += 10000;
            if (total.Tax is { } tax)
            {
                var taxLine = new GeneralJournalLine
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    JournalBatchId = batch.Id,
                    AccountingPeriodId = period.Id,
                    AccountId = tax.LiabilityAccountId,
                    LineNumber = lineNumber,
                    PostingDate = input.PostingDate,
                    DocumentNumber = invoice.InvoiceNumber,
                    Description = $"Tax {tax.Code}",
                    CreditAmount = tax.Amount,
                    TaxCodeId = tax.TaxCodeId,
                    TaxRateId = tax.RateId,
                    TaxRatePercent = tax.RatePercent,
                    TaxBaseAmount = total.NetAmount,
                    TaxPostingType = TaxPostingType.Liability
                };
                dbContext.GeneralJournalLines.Add(taxLine);
                lineNumber += 10000;
            }
        }

        order.Status = SalesOrderStatus.InvoiceDraft;
        order.InvoiceJournalBatchId = batch.Id;
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return batch.Id;
    }

    public async Task ApplyForPostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> lines,
        DateTimeOffset postedAtUtc)
    {
        var shipment = await dbContext.SalesOrders.SingleOrDefaultAsync(order =>
            order.CompanyId == companyId &&
            order.ShipmentJournalBatchId == batch.Id);
        if (shipment is not null)
        {
            await ApplyShipmentPostingAsync(dbContext, user, companyId, batch, lines, shipment, postedAtUtc);
            return;
        }

        var invoice = await dbContext.SalesInvoices.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.JournalBatchId == batch.Id &&
            candidate.PostedAtUtc == null);
        if (invoice is not null)
        {
            await ApplyInvoicePostingAsync(dbContext, user, companyId, batch, lines, invoice, postedAtUtc);
        }
    }

    private async Task ApplyShipmentPostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> journalLines,
        SalesOrder order,
        DateTimeOffset postedAtUtc)
    {
        if (batch.Status != JournalBatchStatus.Open ||
            order.Status != SalesOrderStatus.ShipmentDraft ||
            order.ShipmentPostingDate is not { } postingDate ||
            batch.Description != $"Shipment for {order.OrderNumber}")
        {
            throw new ValidationException("The linked sales shipment journal is invalid.");
        }

        var customer = await GetActiveCustomerAsync(dbContext, companyId, order.CustomerId);
        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == batch.DefaultAccountingPeriodId &&
            !candidate.IsClosed &&
            !candidate.DateLocked);
        if (period is null || postingDate < period.StartsOn || postingDate > period.EndsOn)
        {
            throw new ValidationException("The shipment accounting period is closed or does not contain the posting date.");
        }

        var orderLines = await GetOrderLinesAsync(dbContext, companyId, order.Id);
        if (journalLines.Count != orderLines.Count * 2)
        {
            throw new ValidationException("The shipment journal lines do not match the sales order.");
        }

        var lineNumber = 10000;
        foreach (var orderLine in orderLines)
        {
            var item = await GetActiveInventoryItemAsync(dbContext, companyId, orderLine.ItemId);
            await ValidateItemCostAccountsAsync(dbContext, companyId, item);
            var unitCost = item.UnitCost;
            var amount = CalculateInventoryIssueAmount(item, orderLine.Quantity);
            var expenseLine = journalLines.SingleOrDefault(line => line.LineNumber == lineNumber);
            var inventoryLine = journalLines.SingleOrDefault(line => line.LineNumber == lineNumber + 1000);
            ValidateLinkedJournalLine(
                expenseLine, companyId, batch.Id, period.Id, order.OrderNumber, postingDate,
                $"Shipment {order.OrderNumber}: {orderLine.Description}",
                item.InventoryIssueExpenseAccountId!.Value, amount, 0);
            ValidateLinkedJournalLine(
                inventoryLine, companyId, batch.Id, period.Id, order.OrderNumber, postingDate,
                $"Shipment {order.OrderNumber}: {orderLine.Description}",
                item.InventoryAccountId!.Value, 0, amount);

            item.QuantityOnHand = checked(item.QuantityOnHand - orderLine.Quantity);
            item.InventoryValue = RoundNonNegative(checked(item.InventoryValue - amount));
            if (item.QuantityOnHand == 0)
            {
                item.InventoryValue = 0;
                item.UnitCost = 0;
            }

            dbContext.InventoryMovements.Add(new InventoryMovement
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ItemId = item.Id,
                AccountingPeriodId = period.Id,
                JournalBatchId = batch.Id,
                DocumentNumber = BuildShipmentMovementNumber(order.OrderNumber, orderLine.LineNumber),
                Description = $"Sales shipment {order.OrderNumber}",
                PostingDate = postingDate,
                Quantity = -orderLine.Quantity,
                UnitCost = unitCost,
                InventoryValue = amount,
                PostedAtUtc = postedAtUtc,
                PostedByUserId = GetUserId(user)
            });
            lineNumber += 10000;
        }

        order.Status = SalesOrderStatus.Shipped;
        AddSalesAudit(
            dbContext, companyId, user, "SalesShipmentPosted", nameof(SalesOrder), order.Id, postedAtUtc,
            new { order.OrderNumber, order.ShipmentJournalBatchId, order.ShipmentPostingDate });
    }

    private async Task ApplyInvoicePostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> journalLines,
        SalesInvoice invoice,
        DateTimeOffset postedAtUtc)
    {
        var order = await dbContext.SalesOrders.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == invoice.SalesOrderId &&
            candidate.Status == SalesOrderStatus.InvoiceDraft &&
            candidate.InvoiceJournalBatchId == batch.Id);
        if (order is null || batch.Status != JournalBatchStatus.Open ||
            batch.Description != $"Invoice {invoice.InvoiceNumber}")
        {
            throw new ValidationException("The linked sales invoice journal is invalid.");
        }

        var customer = await GetActiveCustomerAsync(dbContext, companyId, invoice.CustomerId);
        var receivablesAccountId = customer.ReceivablesAccountId
            ?? throw new ValidationException("The customer has no accounts receivable posting account.");
        await ValidatePostingAccountAsync(
            dbContext, companyId, receivablesAccountId, AccountCategory.Asset, "accounts receivable");
        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == batch.DefaultAccountingPeriodId &&
            !candidate.IsClosed &&
            !candidate.DateLocked);
        if (period is null || invoice.PostingDate < period.StartsOn || invoice.PostingDate > period.EndsOn)
        {
            throw new ValidationException("The invoice accounting period is closed or does not contain the posting date.");
        }

        var orderLines = await GetOrderLinesAsync(dbContext, companyId, order.Id);
        var totals = new List<InvoiceLinePosting>();
        foreach (var line in orderLines)
        {
            var net = RoundAmount(checked(line.Quantity * line.UnitPrice));
            var tax = await CalculateTaxAsync(dbContext, companyId, line, invoice.PostingDate, net);
            totals.Add(new InvoiceLinePosting(line, net, tax));
        }

        var netTotal = SumAmounts(totals.Select(line => line.NetAmount));
        var taxTotal = SumAmounts(totals.Select(line => line.Tax?.Amount ?? 0));
        if (netTotal != invoice.NetAmount ||
            taxTotal != invoice.TaxAmount ||
            RoundNonNegative(checked(netTotal + taxTotal)) != invoice.TotalAmount)
        {
            throw new ValidationException("The sales invoice totals changed after the draft was created.");
        }

        var expectedCount = 1 + totals.Count + totals.Count(line => line.Tax is not null);
        if (journalLines.Count != expectedCount)
        {
            throw new ValidationException("The invoice journal lines do not match the sales order.");
        }

        var lineNumber = 10000;
        ValidateLinkedJournalLine(
            journalLines.SingleOrDefault(line => line.LineNumber == lineNumber),
            companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
            $"Accounts receivable {invoice.InvoiceNumber}", receivablesAccountId, invoice.TotalAmount, 0);
        lineNumber += 10000;
        foreach (var total in totals)
        {
            ValidateLinkedJournalLine(
                journalLines.SingleOrDefault(line => line.LineNumber == lineNumber),
                companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
                total.OrderLine.Description, total.OrderLine.SalesAccountId, 0, total.NetAmount);
            lineNumber += 10000;
            if (total.Tax is { } tax)
            {
                var taxLine = journalLines.SingleOrDefault(line => line.LineNumber == lineNumber);
                ValidateLinkedJournalLine(
                    taxLine,
                    companyId, batch.Id, period.Id, invoice.InvoiceNumber, invoice.PostingDate,
                        $"Tax {tax.Code}", tax.LiabilityAccountId, 0, tax.Amount, allowTaxMetadata: true);
                if (taxLine?.TaxCodeId != tax.TaxCodeId ||
                    taxLine.TaxRateId != tax.RateId ||
                    taxLine.TaxRatePercent != tax.RatePercent ||
                    taxLine.TaxBaseAmount != total.NetAmount ||
                    taxLine.TaxPostingType != TaxPostingType.Liability)
                {
                    throw new ValidationException("The invoice tax journal line has changed.");
                }

                lineNumber += 10000;
            }
        }

        invoice.PostedAtUtc = postedAtUtc;
        invoice.PostedByUserId = GetUserId(user);
        order.Status = SalesOrderStatus.Invoiced;
        AddSalesAudit(
            dbContext, companyId, user, "SalesInvoicePosted", nameof(SalesInvoice), invoice.Id, postedAtUtc,
            new { invoice.InvoiceNumber, invoice.SalesOrderId, invoice.NetAmount, invoice.TaxAmount, invoice.TotalAmount });
    }

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
        if (line is null ||
            line.CompanyId != companyId ||
            line.JournalBatchId != batchId ||
            line.AccountingPeriodId != periodId ||
            line.AccountId != accountId ||
            line.DocumentNumber != documentNumber ||
            line.PostingDate != postingDate ||
            line.Description != description ||
            line.DebitAmount != debit ||
            line.CreditAmount != credit ||
            (allowTaxMetadata
                ? line.TaxCodeId is null || line.TaxPostingType != TaxPostingType.Liability
                : line.TaxCodeId is not null ||
                  line.TaxRateId is not null ||
                  line.TaxRatePercent is not null ||
                  line.TaxBaseAmount is not null ||
                  line.TaxPostingType is not null))
        {
            throw new ValidationException("The linked sales journal does not match its source document.");
        }
    }

    private static async Task<List<SalesOrderLine>> GetOrderLinesAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid orderId) =>
        await dbContext.SalesOrderLines
            .Where(line => line.CompanyId == companyId && line.SalesOrderId == orderId)
            .OrderBy(line => line.LineNumber)
            .ToListAsync();

    private static async Task<Customer> GetActiveCustomerAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid customerId) =>
        await dbContext.Customers.SingleOrDefaultAsync(customer =>
                customer.CompanyId == companyId && customer.Id == customerId && !customer.IsBlocked)
            ?? throw new ValidationException("The customer is no longer active in the selected company.");

    private static async Task<Item> GetActiveInventoryItemAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid itemId) =>
        await dbContext.Items.SingleOrDefaultAsync(item =>
                item.CompanyId == companyId &&
                item.Id == itemId &&
                item.Type == ItemType.Inventory &&
                !item.IsBlocked)
            ?? throw new ValidationException("A sales order item is no longer active in the selected company.");

    private static async Task<AccountingPeriod> GetOpenPeriodAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid periodId,
        DateOnly date) =>
        await dbContext.AccountingPeriods.SingleOrDefaultAsync(period =>
                period.CompanyId == companyId &&
                period.Id == periodId &&
                !period.IsClosed &&
                !period.DateLocked &&
                period.StartsOn <= date &&
                period.EndsOn >= date)
            ?? throw new ValidationException("Select an open, unlocked accounting period containing the posting date.");

    private static async Task ValidatePostingAccountAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid accountId,
        AccountCategory category,
        string role)
    {
        if (!await dbContext.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId &&
                account.Id == accountId &&
                account.Category == category &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked))
        {
            throw new ValidationException(
                $"Select an unblocked, directly-postable {role} account from the active company.");
        }
    }

    private static async Task ValidateItemCostAccountsAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Item item)
    {
        if (item.InventoryAccountId is not { } inventoryAccountId ||
            item.InventoryIssueExpenseAccountId is not { } expenseAccountId)
        {
            throw new ValidationException("Configure the inventory asset and issue expense accounts for every sales item.");
        }

        await ValidatePostingAccountAsync(
            dbContext, companyId, inventoryAccountId, AccountCategory.Asset, "inventory asset");
        await ValidatePostingAccountAsync(
            dbContext, companyId, expenseAccountId, AccountCategory.Expense, "inventory issue expense");
    }

    private static async Task<InvoiceTaxPosting?> CalculateTaxAsync(
        ErpDbContext dbContext,
        Guid companyId,
        SalesOrderLine line,
        DateOnly postingDate,
        decimal netAmount)
    {
        if (line.TaxCodeId is not { } taxCodeId)
        {
            return null;
        }

        var taxCode = await dbContext.TaxCodes.SingleOrDefaultAsync(code =>
            code.CompanyId == companyId && code.Id == taxCodeId && !code.IsBlocked);
        var rate = await dbContext.TaxRates.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.TaxCodeId == taxCodeId &&
            candidate.StartsOn <= postingDate &&
            (candidate.EndsOn == null || candidate.EndsOn >= postingDate));
        if (taxCode is null || rate is null)
        {
            throw new ValidationException("The selected sales tax code has no active rate for the invoice date.");
        }

        await ValidatePostingAccountAsync(
            dbContext, companyId, taxCode.LiabilityAccountId, AccountCategory.Liability, "sales tax liability");
        var amount = RoundNonNegative(checked(netAmount * rate.RatePercent / 100m));
        if (amount <= 0)
        {
            throw new ValidationException("The sales tax amount rounds to zero; increase the line value or remove its tax code.");
        }

        return new InvoiceTaxPosting(taxCode.Id, taxCode.Code, rate.Id, rate.RatePercent,
            taxCode.LiabilityAccountId, amount);
    }

    private static decimal CalculateInventoryIssueAmount(Item item, decimal quantity)
    {
        if (quantity <= 0 || item.QuantityOnHand < quantity)
        {
            throw new ValidationException($"There is not enough stock on hand for item '{item.Number}'.");
        }

        return quantity == item.QuantityOnHand
            ? item.InventoryValue
            : RoundAmount(checked(quantity * item.UnitCost));
    }

    private static async Task<GeneralJournalTemplate> GetOrCreateSalesTemplateAsync(
        ErpDbContext dbContext,
        Guid companyId)
    {
        var template = await dbContext.GeneralJournalTemplates.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Code == SalesTemplateCode);
        if (template is not null)
        {
            return template;
        }

        template = new GeneralJournalTemplate
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = SalesTemplateCode,
            Description = "Sales documents",
            Type = JournalTemplateType.Sales
        };
        dbContext.GeneralJournalTemplates.Add(template);
        await dbContext.SaveChangesAsync();
        return template;
    }

    private static async Task<GeneralJournalBatch> CreateSalesBatchAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid periodId,
        string name,
        string description)
    {
        var template = await GetOrCreateSalesTemplateAsync(dbContext, companyId);
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
        dbContext.GeneralJournalBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        return batch;
    }

    private static void AddJournalLine(
        ErpDbContext dbContext,
        Guid companyId,
        Guid batchId,
        Guid periodId,
        Guid accountId,
        int lineNumber,
        SalesDocumentPostingInput input,
        string documentNumber,
        string description,
        decimal debit,
        decimal credit) =>
        dbContext.GeneralJournalLines.Add(new GeneralJournalLine
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

    private static AuditEntry CreateAuditEntry(
        Guid companyId,
        ClaimsPrincipal user,
        string action,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        object metadata) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        ActorUserId = GetUserId(user),
        Action = action,
        EntityType = entityType,
        EntityId = entityId.ToString(),
        OccurredAtUtc = occurredAtUtc,
        MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata)
    };

    private static void AddSalesAudit(
        ErpDbContext dbContext,
        Guid companyId,
        ClaimsPrincipal user,
        string action,
        string entityType,
        Guid entityId,
        DateTimeOffset occurredAtUtc,
        object metadata) =>
        dbContext.AuditEntries.Add(
            CreateAuditEntry(companyId, user, action, entityType, entityId, occurredAtUtc, metadata));

    private static string BuildShipmentMovementNumber(string orderNumber, int lineNumber)
    {
        var suffix = $"-S{lineNumber}";
        return $"{orderNumber[..Math.Min(orderNumber.Length, 64 - suffix.Length)]}{suffix}";
    }

    private async Task<Guid> GetRequiredCompanyIdAsync(ClaimsPrincipal user) =>
        (await companyScopeAuthorizer.ResolveActiveCompanyAsync(user))?.CompanyId
        ?? throw new UnauthorizedAccessException("An active company is required to manage sales.");

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static decimal RoundAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > MaximumAmount)
        {
            throw new ValidationException("A sales amount is outside the supported accounting range.");
        }

        return rounded;
    }

    private static decimal RoundNonNegative(decimal amount)
    {
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (rounded < 0 || rounded > MaximumAmount)
        {
            throw new ValidationException("A sales amount is outside the supported accounting range.");
        }

        return rounded;
    }

    private static decimal SumAmounts(IEnumerable<decimal> amounts)
    {
        try
        {
            return RoundNonNegative(amounts.Aggregate(0m, checked((sum, amount) => sum + amount)));
        }
        catch (OverflowException exception)
        {
            throw new ValidationException("The sales document total exceeds the supported accounting range.", exception);
        }
    }

    private static void NormalizeAndValidate(SalesCustomerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Number = input.Number?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), true);
        if (input.ReceivablesAccountId == Guid.Empty)
        {
            throw new ValidationException("Select an accounts receivable posting account.");
        }
    }

    private static void NormalizeAndValidate(SalesOrderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.OrderNumber = input.OrderNumber?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), true);
        if (input.CustomerId == Guid.Empty || input.OrderDate == default ||
            input.Lines.Count is < 1 or > 50)
        {
            throw new ValidationException("Select a customer and enter between one and 50 sales order lines.");
        }

        foreach (var line in input.Lines)
        {
            if (line.ItemId == Guid.Empty || line.SalesAccountId == Guid.Empty)
            {
                throw new ValidationException("Select an inventory item and revenue account for every order line.");
            }

            ValidateQuantityAndUnitPrice(line.Quantity, line.UnitPrice);
        }
    }

    private static void NormalizeAndValidate(SalesDocumentPostingInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.DocumentNumber = input.DocumentNumber?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), true);
        if (input.AccountingPeriodId == Guid.Empty || input.PostingDate == default)
        {
            throw new ValidationException("Select an open accounting period and posting date.");
        }
    }

    private static void ValidateQuantityAndUnitPrice(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0 ||
            quantity > MaximumAmount ||
            decimal.Round(quantity, 4) != quantity ||
            unitPrice <= 0 ||
            unitPrice > MaximumAmount ||
            decimal.Round(unitPrice, 4) != unitPrice)
        {
            throw new ValidationException("Enter positive quantity and unit price values with no more than four decimal places.");
        }
    }

    private static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(ErpDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    }

    private sealed record InvoiceLinePosting(
        SalesOrderLine OrderLine,
        decimal NetAmount,
        InvoiceTaxPosting? Tax);

    private sealed record ShipmentLinePosting(
        SalesOrderLine OrderLine,
        Guid ItemId,
        decimal InventoryValue);

    private sealed record InvoiceTaxPosting(
        Guid TaxCodeId,
        string Code,
        Guid RateId,
        decimal RatePercent,
        Guid LiabilityAccountId,
        decimal Amount);
}
