using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed class InventoryItemInput
{
    [Required, StringLength(32)]
    public string Number { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string BaseUnitOfMeasure { get; set; } = string.Empty;

    public Guid InventoryAccountId { get; set; }
    public Guid IncreaseOffsetAccountId { get; set; }
    public Guid IssueExpenseAccountId { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class InventoryMovementInput
{
    public Guid ItemId { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Required, StringLength(64)]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required, StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public bool IsReceipt { get; set; } = true;
    public decimal UnitCost { get; set; }
}

public sealed record InventoryAccountOption(
    Guid Id,
    string Number,
    string Name,
    AccountCategory Category,
    AccountType Type,
    bool AllowsDirectPosting,
    bool IsBlocked);

public sealed record InventoryItemListItem(
    Guid Id,
    string Number,
    string Description,
    string BaseUnitOfMeasure,
    decimal QuantityOnHand,
    decimal UnitCost,
    decimal InventoryValue,
    Guid InventoryAccountId,
    Guid IncreaseOffsetAccountId,
    Guid IssueExpenseAccountId,
    bool IsBlocked,
    byte[] RowVersion);

public sealed record InventoryMovementListItem(
    Guid Id,
    Guid ItemId,
    string ItemNumber,
    DateOnly PostingDate,
    string DocumentNumber,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal InventoryValue,
    Guid JournalBatchId,
    bool IsPosted);

public interface IInventoryService
{
    Task<IReadOnlyList<InventoryAccountOption>> GetAccountsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<AccountingPeriodListItem>> GetOpenPeriodsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<InventoryItemListItem>> GetItemsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<InventoryMovementListItem>> GetMovementsAsync(ClaimsPrincipal user);
    Task<Guid> CreateItemAsync(ClaimsPrincipal user, InventoryItemInput input);
    Task UpdateItemAsync(ClaimsPrincipal user, Guid itemId, byte[] rowVersion, InventoryItemInput input);
    Task<Guid> CreateMovementDraftAsync(ClaimsPrincipal user, InventoryMovementInput input);
}

public interface IInventoryJournalPostingService
{
    Task ApplyForPostingAsync(
        ErpDbContext dbContext,
        ClaimsPrincipal user,
        Guid companyId,
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> lines,
        DateTimeOffset postedAtUtc);
}

public sealed class InventoryService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IInventoryService, IInventoryJournalPostingService
{
    private const decimal MaximumAmount = 999999999999999.9999m;
    private const string InventoryJournalTemplateCode = "INVENTORY";

    public async Task<IReadOnlyList<InventoryAccountOption>> GetAccountsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts.AsNoTracking()
            .Where(account => account.CompanyId == companyId)
            .OrderBy(account => account.Number)
            .Select(account => new InventoryAccountOption(
                account.Id,
                account.Number,
                account.Name,
                account.Category,
                account.Type,
                account.AllowsDirectPosting,
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

    public async Task<IReadOnlyList<InventoryItemListItem>> GetItemsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.Items.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.Type == ItemType.Inventory)
            .OrderBy(item => item.Number)
            .Select(item => new InventoryItemListItem(
                item.Id,
                item.Number,
                item.Description,
                item.BaseUnitOfMeasure,
                item.QuantityOnHand,
                item.UnitCost,
                item.InventoryValue,
                item.InventoryAccountId!.Value,
                item.InventoryIncreaseOffsetAccountId!.Value,
                item.InventoryIssueExpenseAccountId!.Value,
                item.IsBlocked,
                item.RowVersion))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<InventoryMovementListItem>> GetMovementsAsync(ClaimsPrincipal user)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await (
            from movement in dbContext.InventoryMovements.AsNoTracking()
            join item in dbContext.Items.AsNoTracking()
                on new { movement.CompanyId, movement.ItemId } equals new { item.CompanyId, ItemId = item.Id }
            join batch in dbContext.GeneralJournalBatches.AsNoTracking()
                on new { movement.CompanyId, movement.JournalBatchId } equals new { batch.CompanyId, JournalBatchId = batch.Id }
            where movement.CompanyId == companyId
            orderby movement.PostingDate descending, movement.DocumentNumber
            select new InventoryMovementListItem(
                movement.Id,
                movement.ItemId,
                item.Number,
                movement.PostingDate,
                movement.DocumentNumber,
                movement.Description,
                movement.Quantity,
                movement.UnitCost,
                movement.InventoryValue,
                movement.JournalBatchId,
                batch.Status == JournalBatchStatus.Posted))
            .ToListAsync();
    }

    public async Task<Guid> CreateItemAsync(ClaimsPrincipal user, InventoryItemInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await ValidateUniqueItemNumberAsync(dbContext, companyId, input.Number, null);
        await ValidateItemAccountsAsync(dbContext, companyId, input);
        var item = new Item
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Type = ItemType.Inventory,
            QuantityOnHand = 0,
            InventoryValue = 0,
            UnitCost = 0
        };
        Apply(item, input);
        dbContext.Items.Add(item);
        await dbContext.SaveChangesAsync();
        return item.Id;
    }

    public async Task UpdateItemAsync(
        ClaimsPrincipal user,
        Guid itemId,
        byte[] rowVersion,
        InventoryItemInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        ArgumentNullException.ThrowIfNull(rowVersion);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var item = await dbContext.Items.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == itemId && candidate.Type == ItemType.Inventory);
        if (item is null)
        {
            throw new ValidationException("The inventory item was not found in the active company.");
        }

        if (!item.RowVersion.SequenceEqual(rowVersion))
        {
            throw new ValidationException("The inventory item changed. Reload it before saving again.");
        }

        await ValidateUniqueItemNumberAsync(dbContext, companyId, input.Number, itemId);
        await ValidateItemAccountsAsync(dbContext, companyId, input);
        if (await dbContext.InventoryMovements.AnyAsync(movement =>
                movement.CompanyId == companyId && movement.ItemId == itemId) &&
            (item.InventoryAccountId != input.InventoryAccountId ||
             item.InventoryIncreaseOffsetAccountId != input.IncreaseOffsetAccountId ||
             item.InventoryIssueExpenseAccountId != input.IssueExpenseAccountId))
        {
            throw new ValidationException("Inventory account mappings cannot change after a movement draft exists.");
        }

        Apply(item, input);
        await SaveChangesAsync(dbContext);
    }

    public async Task<Guid> CreateMovementDraftAsync(ClaimsPrincipal user, InventoryMovementInput input)
    {
        var companyId = await GetRequiredCompanyIdAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);

        var item = await dbContext.Items.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == input.ItemId && candidate.Type == ItemType.Inventory);
        if (item is null || item.IsBlocked)
        {
            throw new ValidationException("Select an active, unblocked inventory item in the active company.");
        }

        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == input.AccountingPeriodId &&
            !candidate.IsClosed &&
            !candidate.DateLocked);
        if (period is null || input.PostingDate < period.StartsOn || input.PostingDate > period.EndsOn)
        {
            throw new ValidationException("Select an open, unlocked accounting period containing the posting date.");
        }

        if (await dbContext.InventoryMovements.AnyAsync(movement =>
                movement.CompanyId == companyId && movement.DocumentNumber == input.DocumentNumber))
        {
            throw new ValidationException("An inventory movement with this document number already exists in the active company.");
        }

        await ValidateMappedAccountsAsync(dbContext, companyId, item);
        var amount = CalculateMovementAmount(item, input);
        var template = await GetOrCreateTemplateAsync(dbContext, companyId);
        var batch = new GeneralJournalBatch
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            JournalTemplateId = template.Id,
            DefaultAccountingPeriodId = period.Id,
            Name = $"INV-{Guid.NewGuid():N}"[..28],
            Description = $"Inventory movement {input.DocumentNumber}",
            Status = JournalBatchStatus.Open,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var inventoryAccountId = item.InventoryAccountId!.Value;
        var offsetAccountId = input.IsReceipt
            ? item.InventoryIncreaseOffsetAccountId!.Value
            : item.InventoryIssueExpenseAccountId!.Value;
        var inventoryLine = NewJournalLine(
            companyId,
            batch.Id,
            period.Id,
            input,
            10000,
            inventoryAccountId,
            input.IsReceipt ? amount : 0,
            input.IsReceipt ? 0 : amount);
        var offsetLine = NewJournalLine(
            companyId,
            batch.Id,
            period.Id,
            input,
            20000,
            offsetAccountId,
            input.IsReceipt ? 0 : amount,
            input.IsReceipt ? amount : 0);
        var movement = new InventoryMovement
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ItemId = item.Id,
            AccountingPeriodId = period.Id,
            JournalBatchId = batch.Id,
            DocumentNumber = input.DocumentNumber,
            Description = input.Description,
            PostingDate = input.PostingDate,
            Quantity = input.IsReceipt ? input.Quantity : -input.Quantity,
            UnitCost = input.IsReceipt ? input.UnitCost : item.UnitCost,
            InventoryValue = amount
        };

        dbContext.GeneralJournalBatches.Add(batch);
        dbContext.GeneralJournalLines.AddRange(inventoryLine, offsetLine);
        dbContext.InventoryMovements.Add(movement);
        await SaveChangesAsync(dbContext);
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
        var movement = await dbContext.InventoryMovements.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.JournalBatchId == batch.Id);
        if (movement is null)
        {
            return;
        }

        if (movement.PostedAtUtc is not null)
        {
            throw new ValidationException("This inventory movement has already been posted.");
        }

        var item = await dbContext.Items.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == movement.ItemId &&
            candidate.Type == ItemType.Inventory &&
            !candidate.IsBlocked);
        if (item is null)
        {
            throw new ValidationException("The inventory item is no longer active and unblocked.");
        }

        await ValidateMappedAccountsAsync(dbContext, companyId, item);
        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId &&
            candidate.Id == movement.AccountingPeriodId &&
            !candidate.IsClosed &&
            !candidate.DateLocked);
        if (period is null ||
            movement.PostingDate < period.StartsOn ||
            movement.PostingDate > period.EndsOn ||
            batch.DefaultAccountingPeriodId != period.Id)
        {
            throw new ValidationException("The inventory movement period is closed, date-locked, or no longer valid.");
        }

        var isReceipt = movement.Quantity > 0;
        if (!isReceipt && movement.UnitCost != item.UnitCost)
        {
            throw new ValidationException(
                "The weighted-average cost changed after this issue draft was created. Create a new movement draft.");
        }

        var input = new InventoryMovementInput
        {
            ItemId = movement.ItemId,
            AccountingPeriodId = movement.AccountingPeriodId,
            PostingDate = movement.PostingDate,
            DocumentNumber = movement.DocumentNumber,
            Description = movement.Description,
            Quantity = Math.Abs(movement.Quantity),
            IsReceipt = isReceipt,
            UnitCost = movement.UnitCost
        };
        var amount = CalculateMovementAmount(item, input);
        if (amount != movement.InventoryValue)
        {
            throw new ValidationException(
                "The inventory quantity or weighted-average cost changed after this draft was created. Create a new movement draft.");
        }

        ValidateMovementJournal(batch, lines, movement, item, amount, isReceipt);
        if (isReceipt)
        {
            var newQuantity = checked(item.QuantityOnHand + movement.Quantity);
            var newValue = RoundAmount(checked(item.InventoryValue + amount));
            item.QuantityOnHand = newQuantity;
            item.InventoryValue = newValue;
            item.UnitCost = RoundAmount(newValue / newQuantity);
        }
        else
        {
            if (item.QuantityOnHand < Math.Abs(movement.Quantity))
            {
                throw new ValidationException("There is not enough stock on hand to post this issue.");
            }

            item.QuantityOnHand = checked(item.QuantityOnHand + movement.Quantity);
            item.InventoryValue = RoundAmount(item.InventoryValue - amount);
            if (item.QuantityOnHand == 0)
            {
                item.InventoryValue = 0;
                item.UnitCost = 0;
            }
        }

        movement.InventoryValue = amount;
        movement.PostedAtUtc = postedAtUtc;
        movement.PostedByUserId = GetUserId(user);
        dbContext.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ActorUserId = GetUserId(user),
            Action = "InventoryMovementPosted",
            EntityType = nameof(InventoryMovement),
            EntityId = movement.Id.ToString(),
            OccurredAtUtc = postedAtUtc,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                movement.DocumentNumber,
                movement.ItemId,
                movement.Quantity,
                movement.UnitCost,
                movement.InventoryValue,
                movement.JournalBatchId
            })
        });
    }

    private static GeneralJournalLine NewJournalLine(
        Guid companyId,
        Guid batchId,
        Guid periodId,
        InventoryMovementInput input,
        int lineNumber,
        Guid accountId,
        decimal debit,
        decimal credit) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = companyId,
        JournalBatchId = batchId,
        AccountingPeriodId = periodId,
        AccountId = accountId,
        LineNumber = lineNumber,
        PostingDate = input.PostingDate,
        DocumentNumber = input.DocumentNumber,
        Description = input.Description,
        DebitAmount = debit,
        CreditAmount = credit
    };

    private static decimal CalculateMovementAmount(Item item, InventoryMovementInput input)
    {
        if (input.IsReceipt)
        {
            return RoundAmount(checked(input.Quantity * input.UnitCost));
        }

        if (item.QuantityOnHand < input.Quantity)
        {
            throw new ValidationException("There is not enough stock on hand to create this issue draft.");
        }

        return input.Quantity == item.QuantityOnHand
            ? item.InventoryValue
            : RoundAmount(checked(input.Quantity * item.UnitCost));
    }

    private static void ValidateMovementJournal(
        GeneralJournalBatch batch,
        IReadOnlyList<GeneralJournalLine> lines,
        InventoryMovement movement,
        Item item,
        decimal amount,
        bool isReceipt)
    {
        if (batch.Status != JournalBatchStatus.Open ||
            batch.Description != $"Inventory movement {movement.DocumentNumber}" ||
            lines.Count != 2)
        {
            throw new ValidationException("The linked inventory journal draft is invalid.");
        }

        var inventoryAccountId = item.InventoryAccountId!.Value;
        var offsetAccountId = isReceipt
            ? item.InventoryIncreaseOffsetAccountId!.Value
            : item.InventoryIssueExpenseAccountId!.Value;
        var inventoryLine = lines.SingleOrDefault(line =>
            line.AccountId == inventoryAccountId && line.LineNumber == 10000);
        var offsetLine = lines.SingleOrDefault(line =>
            line.AccountId == offsetAccountId && line.LineNumber == 20000);
        if (inventoryLine is null ||
            offsetLine is null ||
            lines.Any(line =>
                line.DocumentNumber != movement.DocumentNumber ||
                line.PostingDate != movement.PostingDate ||
                line.Description != movement.Description ||
                line.AccountingPeriodId != movement.AccountingPeriodId ||
                line.TaxCodeId is not null ||
                line.DebitAmount < 0 ||
                line.CreditAmount < 0) ||
            inventoryLine.DebitAmount != (isReceipt ? amount : 0) ||
            inventoryLine.CreditAmount != (isReceipt ? 0 : amount) ||
            offsetLine.DebitAmount != (isReceipt ? 0 : amount) ||
            offsetLine.CreditAmount != (isReceipt ? amount : 0))
        {
            throw new ValidationException("The linked inventory journal lines do not match the inventory movement.");
        }
    }

    private static async Task<GeneralJournalTemplate> GetOrCreateTemplateAsync(ErpDbContext dbContext, Guid companyId)
    {
        var template = await dbContext.GeneralJournalTemplates.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Code == InventoryJournalTemplateCode);
        if (template is not null)
        {
            return template;
        }

        template = new GeneralJournalTemplate
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = InventoryJournalTemplateCode,
            Description = "Inventory movements",
            Type = JournalTemplateType.General
        };
        dbContext.GeneralJournalTemplates.Add(template);
        await dbContext.SaveChangesAsync();
        return template;
    }

    private static async Task ValidateUniqueItemNumberAsync(
        ErpDbContext dbContext,
        Guid companyId,
        string number,
        Guid? itemId)
    {
        if (await dbContext.Items.AnyAsync(item =>
                item.CompanyId == companyId &&
                item.Number == number &&
                (itemId == null || item.Id != itemId)))
        {
            throw new ValidationException("An item with this number already exists in the active company.");
        }
    }

    private static async Task ValidateItemAccountsAsync(
        ErpDbContext dbContext,
        Guid companyId,
        InventoryItemInput input)
    {
        if (input.InventoryAccountId == input.IncreaseOffsetAccountId ||
            input.InventoryAccountId == input.IssueExpenseAccountId ||
            input.IncreaseOffsetAccountId == input.IssueExpenseAccountId)
        {
            throw new ValidationException("Choose three distinct inventory posting accounts.");
        }

        await ValidateEligibleAccountAsync(
            dbContext, companyId, input.InventoryAccountId, AccountCategory.Asset, "inventory asset");
        await ValidateEligibleAccountAsync(
            dbContext, companyId, input.IncreaseOffsetAccountId, null, "inventory increase offset");
        await ValidateEligibleAccountAsync(
            dbContext, companyId, input.IssueExpenseAccountId, AccountCategory.Expense, "inventory issue expense");
    }

    private static async Task ValidateMappedAccountsAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Item item)
    {
        if (item.InventoryAccountId is not { } inventoryAccountId ||
            item.InventoryIncreaseOffsetAccountId is not { } increaseOffsetAccountId ||
            item.InventoryIssueExpenseAccountId is not { } issueExpenseAccountId)
        {
            throw new ValidationException("Configure all three G/L account mappings for this inventory item.");
        }

        await ValidateEligibleAccountAsync(
            dbContext, companyId, inventoryAccountId, AccountCategory.Asset, "inventory asset");
        await ValidateEligibleAccountAsync(
            dbContext, companyId, increaseOffsetAccountId, null, "inventory increase offset");
        await ValidateEligibleAccountAsync(
            dbContext, companyId, issueExpenseAccountId, AccountCategory.Expense, "inventory issue expense");
    }

    private static async Task ValidateEligibleAccountAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid accountId,
        AccountCategory? category,
        string description)
    {
        var account = await dbContext.GeneralLedgerAccounts.SingleOrDefaultAsync(candidate =>
            candidate.CompanyId == companyId && candidate.Id == accountId);
        if (account is null ||
            account.Type != AccountType.Posting ||
            !account.AllowsDirectPosting ||
            account.IsBlocked ||
            (category is not null && account.Category != category))
        {
            throw new ValidationException(
                $"Select an unblocked, directly-postable {description} G/L account from the active company.");
        }
    }

    private static void NormalizeAndValidate(InventoryItemInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Number = input.Number?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        input.BaseUnitOfMeasure = input.BaseUnitOfMeasure?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (input.InventoryAccountId == Guid.Empty ||
            input.IncreaseOffsetAccountId == Guid.Empty ||
            input.IssueExpenseAccountId == Guid.Empty)
        {
            throw new ValidationException("Select all three G/L account mappings for the inventory item.");
        }
    }

    private static void NormalizeAndValidate(InventoryMovementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.DocumentNumber = input.DocumentNumber?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (input.ItemId == Guid.Empty || input.AccountingPeriodId == Guid.Empty)
        {
            throw new ValidationException("Select an inventory item and accounting period.");
        }

        if (input.Quantity <= 0 ||
            input.Quantity > MaximumAmount ||
            decimal.Round(input.Quantity, 4) != input.Quantity)
        {
            throw new ValidationException("Enter a positive quantity with no more than four decimal places.");
        }

        if (input.IsReceipt &&
            (input.UnitCost <= 0 ||
             input.UnitCost > MaximumAmount ||
             decimal.Round(input.UnitCost, 4) != input.UnitCost))
        {
            throw new ValidationException("Enter a positive receipt unit cost with no more than four decimal places.");
        }
    }

    private static decimal RoundAmount(decimal amount)
    {
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        if (rounded <= 0 || rounded > MaximumAmount)
        {
            throw new ValidationException("The inventory movement value is outside the supported amount range.");
        }

        return rounded;
    }

    private static void Apply(Item item, InventoryItemInput input)
    {
        item.Number = input.Number;
        item.Description = input.Description;
        item.BaseUnitOfMeasure = input.BaseUnitOfMeasure;
        item.InventoryAccountId = input.InventoryAccountId;
        item.InventoryIncreaseOffsetAccountId = input.IncreaseOffsetAccountId;
        item.InventoryIssueExpenseAccountId = input.IssueExpenseAccountId;
        item.IsBlocked = input.IsBlocked;
    }

    private async Task<Guid> GetRequiredCompanyIdAsync(ClaimsPrincipal user) =>
        (await companyScopeAuthorizer.ResolveActiveCompanyAsync(user))?.CompanyId
        ?? throw new UnauthorizedAccessException("An active company is required for inventory.");

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    private static async Task SaveChangesAsync(ErpDbContext dbContext)
    {
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException("Inventory data changed concurrently. Reload it and retry.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Microsoft.Data.SqlClient.SqlException
        {
            Number: 2601 or 2627
        })
        {
            throw new ValidationException(
                "The inventory reference already exists or a concurrent posting was completed. Reload before retrying.",
                exception);
        }
    }

    private static async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginSerializableTransactionAsync(
        ErpDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    }
}
