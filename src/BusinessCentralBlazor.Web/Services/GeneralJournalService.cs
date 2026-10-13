using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BusinessCentralBlazor.Web.Services;

public sealed class AccountingPeriodInput
{
    [Required]
    [StringLength(32)]
    public string Name { get; set; } = string.Empty;

    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
}

public sealed class AccountingPeriodStatusInput
{
    public bool IsClosed { get; set; }
    public bool DateLocked { get; set; }
}

public sealed class GeneralJournalBatchInput
{
    [Required]
    [StringLength(32)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string Description { get; set; } = string.Empty;

    public Guid DefaultAccountingPeriodId { get; set; }
}

public sealed class GeneralJournalLineInput
{
    [Range(1, int.MaxValue)]
    public int LineNumber { get; set; }

    public Guid AccountingPeriodId { get; set; }
    public Guid AccountId { get; set; }
    public DateOnly PostingDate { get; set; }

    [Required]
    [StringLength(64)]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(250)]
    public string Description { get; set; } = string.Empty;

    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public Guid? TaxCodeId { get; set; }
    public decimal? TaxBaseAmount { get; set; }
    public List<JournalDimensionAssignmentInput> Dimensions { get; set; } = [];
}

public sealed class JournalDimensionAssignmentInput
{
    public Guid DimensionId { get; set; }
    public Guid DimensionValueId { get; set; }
}

public sealed class GeneralJournalReversalInput
{
    [Required]
    [StringLength(32)]
    public string Name { get; set; } = string.Empty;

    [StringLength(100)]
    public string Description { get; set; } = string.Empty;

    public Guid AccountingPeriodId { get; set; }
    public DateOnly PostingDate { get; set; }
}

public sealed record AccountingPeriodListItem(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    bool IsClosed,
    bool DateLocked);

public sealed record GeneralJournalBatchListItem(
    Guid Id,
    string Name,
    string Description,
    JournalBatchStatus Status,
    string DefaultPeriodName,
    int LineCount,
    Guid? ReversedByBatchId,
    Guid? ReversalOfBatchId);

public sealed record JournalLineDimensionDetail(
    Guid DimensionId,
    string DimensionCode,
    Guid DimensionValueId,
    string DimensionValueCode);

public sealed record GeneralJournalLineDetail(
    Guid Id,
    int LineNumber,
    Guid AccountingPeriodId,
    string AccountingPeriodName,
    Guid AccountId,
    string AccountNumber,
    string AccountName,
    DateOnly PostingDate,
    string DocumentNumber,
    string Description,
    decimal DebitAmount,
    decimal CreditAmount,
    string? TaxCode,
    decimal? TaxRatePercent,
    decimal? TaxBaseAmount,
    TaxPostingType? TaxPostingType,
    IReadOnlyList<JournalLineDimensionDetail> Dimensions);

public sealed record GeneralJournalBatchDetail(
    Guid Id,
    string Name,
    string Description,
    JournalBatchStatus Status,
    string TemplateCode,
    Guid DefaultAccountingPeriodId,
    string DefaultPeriodName,
    Guid? ReversedByBatchId,
    Guid? ReversalOfBatchId,
    IReadOnlyList<GeneralJournalLineDetail> Lines);

public sealed record GeneralJournalAccountOption(Guid Id, string Number, string Name);

public sealed record GeneralJournalTaxCodeOption(
    Guid Id,
    string Code,
    string Description,
    Guid LiabilityAccountId,
    Guid RecoverableAccountId,
    IReadOnlyList<GeneralJournalTaxRateOption> Rates);

public sealed record GeneralJournalTaxRateOption(
    decimal RatePercent,
    DateOnly StartsOn,
    DateOnly? EndsOn);

public sealed record JournalDimensionValueOption(Guid Id, string Code, string Name, bool IsBlocked);

public sealed record JournalDimensionOption(
    Guid Id,
    string Code,
    string Name,
    IReadOnlyList<JournalDimensionValueOption> Values);

public interface IGeneralJournalService
{
    Task<IReadOnlyList<AccountingPeriodListItem>> GetPeriodsAsync(ClaimsPrincipal user);
    Task<Guid> CreatePeriodAsync(ClaimsPrincipal user, AccountingPeriodInput input);
    Task<bool> UpdatePeriodStatusAsync(ClaimsPrincipal user, Guid periodId, AccountingPeriodStatusInput input);
    Task<IReadOnlyList<GeneralJournalBatchListItem>> GetBatchesAsync(ClaimsPrincipal user);
    Task<GeneralJournalBatchDetail?> GetBatchAsync(ClaimsPrincipal user, Guid batchId);
    Task<IReadOnlyList<GeneralJournalAccountOption>> GetPostingAccountsAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user);
    Task<IReadOnlyList<JournalDimensionOption>> GetDimensionsAsync(ClaimsPrincipal user);
    Task<Guid> CreateBatchAsync(ClaimsPrincipal user, GeneralJournalBatchInput input);
    Task<bool> UpdateBatchAsync(ClaimsPrincipal user, Guid batchId, GeneralJournalBatchInput input);
    Task<Guid> CreateLineAsync(ClaimsPrincipal user, Guid batchId, GeneralJournalLineInput input);
    Task<bool> UpdateLineAsync(
        ClaimsPrincipal user,
        Guid batchId,
        Guid lineId,
        GeneralJournalLineInput input);
    Task<bool> DeleteLineAsync(ClaimsPrincipal user, Guid batchId, Guid lineId);
    Task PostBatchAsync(ClaimsPrincipal user, Guid batchId);
    Task<Guid> ReverseBatchAsync(
        ClaimsPrincipal user,
        Guid batchId,
        GeneralJournalReversalInput input);
}

public sealed class GeneralJournalService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer,
    IInventoryJournalPostingService inventoryJournalPostingService) : IGeneralJournalService
{
    private const string EntryNumberSeriesCode = "GL-ENTRY";
    private const decimal MaximumAmount = 999999999999999.9999m;

    public async Task<IReadOnlyList<AccountingPeriodListItem>> GetPeriodsAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == company.CompanyId)
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

    public async Task<Guid> CreatePeriodAsync(ClaimsPrincipal user, AccountingPeriodInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        await EnsurePeriodDoesNotOverlapAsync(dbContext, company.CompanyId, input.StartsOn, input.EndsOn);

        var period = new AccountingPeriod
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Name = input.Name,
            StartsOn = input.StartsOn,
            EndsOn = input.EndsOn
        };
        dbContext.AccountingPeriods.Add(period);
        await dbContext.SaveChangesAsync();
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return period.Id;
    }

    public async Task<bool> UpdatePeriodStatusAsync(
        ClaimsPrincipal user,
        Guid periodId,
        AccountingPeriodStatusInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == periodId);
        if (period is null)
        {
            return false;
        }

        period.IsClosed = input.IsClosed;
        period.DateLocked = input.DateLocked;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<GeneralJournalBatchListItem>> GetBatchesAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var batches = await dbContext.GeneralJournalBatches
            .AsNoTracking()
            .Where(batch => batch.CompanyId == company.CompanyId)
            .OrderByDescending(batch => batch.CreatedAtUtc)
            .Select(batch => new
            {
                batch.Id,
                batch.Name,
                batch.Description,
                batch.Status,
                batch.DefaultAccountingPeriodId,
                batch.ReversedByBatchId
            })
            .ToListAsync();
        if (batches.Count == 0)
        {
            return [];
        }

        var periodIds = batches.Select(batch => batch.DefaultAccountingPeriodId).Distinct().ToArray();
        var periods = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == company.CompanyId && periodIds.Contains(period.Id))
            .ToDictionaryAsync(period => period.Id, period => period.Name);
        var lineCounts = await dbContext.GeneralJournalLines
            .AsNoTracking()
            .Where(line => line.CompanyId == company.CompanyId && batches.Select(batch => batch.Id)
                .Contains(line.JournalBatchId))
            .GroupBy(line => line.JournalBatchId)
            .Select(group => new { BatchId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.BatchId, group => group.Count);
        var reversalSources = await dbContext.GeneralJournalBatches
            .AsNoTracking()
            .Where(batch => batch.CompanyId == company.CompanyId &&
                batch.ReversedByBatchId != null &&
                batches.Select(item => item.Id).Contains(batch.ReversedByBatchId.Value))
            .ToDictionaryAsync(batch => batch.ReversedByBatchId!.Value, batch => batch.Id);

        return batches.Select(batch =>
        {
            Guid? reversalOfBatchId = reversalSources.TryGetValue(batch.Id, out var sourceBatchId)
                ? sourceBatchId
                : null;
            return new GeneralJournalBatchListItem(
                batch.Id,
                batch.Name,
                batch.Description,
                batch.Status,
                periods.GetValueOrDefault(batch.DefaultAccountingPeriodId, "Unknown period"),
                lineCounts.GetValueOrDefault(batch.Id),
                batch.ReversedByBatchId,
                reversalOfBatchId);
        }).ToArray();
    }

    public async Task<GeneralJournalBatchDetail?> GetBatchAsync(ClaimsPrincipal user, Guid batchId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var batch = await dbContext.GeneralJournalBatches
            .AsNoTracking()
            .Where(item => item.CompanyId == company.CompanyId && item.Id == batchId)
            .Select(item => new
            {
                item.Id,
                item.Name,
                item.Description,
                item.Status,
                item.JournalTemplateId,
                item.DefaultAccountingPeriodId,
                item.ReversedByBatchId
            })
            .SingleOrDefaultAsync();
        if (batch is null)
        {
            return null;
        }

        var templateCode = await dbContext.GeneralJournalTemplates
            .AsNoTracking()
            .Where(template => template.CompanyId == company.CompanyId && template.Id == batch.JournalTemplateId)
            .Select(template => template.Code)
            .SingleAsync();
        var periodName = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == company.CompanyId && period.Id == batch.DefaultAccountingPeriodId)
            .Select(period => period.Name)
            .SingleAsync();
        var reversalOfBatchId = await dbContext.GeneralJournalBatches
            .AsNoTracking()
            .Where(item => item.CompanyId == company.CompanyId && item.ReversedByBatchId == batchId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefaultAsync();

        var lines = await dbContext.GeneralJournalLines
            .AsNoTracking()
            .Where(line => line.CompanyId == company.CompanyId && line.JournalBatchId == batchId)
            .OrderBy(line => line.LineNumber)
            .ToListAsync();
        var accountIds = lines.Select(line => line.AccountId).Distinct().ToArray();
        var lineIds = lines.Select(line => line.Id).ToArray();
        var periodIds = lines.Select(line => line.AccountingPeriodId).Append(batch.DefaultAccountingPeriodId).Distinct().ToArray();
        var accounts = await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId && accountIds.Contains(account.Id))
            .ToDictionaryAsync(account => account.Id);
        var taxCodeIds = lines
            .Where(line => line.TaxCodeId != null)
            .Select(line => line.TaxCodeId!.Value)
            .Distinct()
            .ToArray();
        var taxCodes = await dbContext.TaxCodes
            .AsNoTracking()
            .Where(code => code.CompanyId == company.CompanyId && taxCodeIds.Contains(code.Id))
            .ToDictionaryAsync(code => code.Id, code => code.Code);
        var linePeriods = await dbContext.AccountingPeriods
            .AsNoTracking()
            .Where(period => period.CompanyId == company.CompanyId && periodIds.Contains(period.Id))
            .ToDictionaryAsync(period => period.Id, period => period.Name);
        var assignments = await (
            from assignment in dbContext.JournalLineDimensions.AsNoTracking()
            join dimension in dbContext.Dimensions.AsNoTracking()
                on new { assignment.CompanyId, assignment.DimensionId } equals new { dimension.CompanyId, DimensionId = dimension.Id }
            join value in dbContext.DimensionValues.AsNoTracking()
                on new { assignment.CompanyId, assignment.DimensionId, assignment.DimensionValueId }
                equals new { value.CompanyId, value.DimensionId, DimensionValueId = value.Id }
            where assignment.CompanyId == company.CompanyId && lineIds.Contains(assignment.JournalLineId)
            select new
            {
                assignment.JournalLineId,
                assignment.DimensionId,
                DimensionCode = dimension.Code,
                assignment.DimensionValueId,
                DimensionValueCode = value.Code
            })
            .ToListAsync();
        var dimensionsByLine = assignments
            .GroupBy(item => item.JournalLineId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<JournalLineDimensionDetail>)group
                    .Select(item => new JournalLineDimensionDetail(
                        item.DimensionId,
                        item.DimensionCode,
                        item.DimensionValueId,
                        item.DimensionValueCode))
                    .ToArray());
        var lineDetails = lines.Select(line =>
        {
            var account = accounts[line.AccountId];
            return new GeneralJournalLineDetail(
                line.Id,
                line.LineNumber,
                line.AccountingPeriodId,
                linePeriods.GetValueOrDefault(line.AccountingPeriodId, "Unknown period"),
                line.AccountId,
                account.Number,
                account.Name,
                line.PostingDate,
                line.DocumentNumber,
                line.Description,
                line.DebitAmount,
                line.CreditAmount,
                line.TaxCodeId is { } taxCodeId ? taxCodes.GetValueOrDefault(taxCodeId) : null,
                line.TaxRatePercent,
                line.TaxBaseAmount,
                line.TaxPostingType,
                dimensionsByLine.GetValueOrDefault(line.Id, []));
        }).ToArray();

        return new GeneralJournalBatchDetail(
            batch.Id,
            batch.Name,
            batch.Description,
            batch.Status,
            templateCode,
            batch.DefaultAccountingPeriodId,
            periodName,
            batch.ReversedByBatchId,
            reversalOfBatchId,
            lineDetails);
    }

    public async Task<IReadOnlyList<GeneralJournalAccountOption>> GetPostingAccountsAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked)
            .OrderBy(account => account.Number)
            .Select(account => new GeneralJournalAccountOption(account.Id, account.Number, account.Name))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<GeneralJournalTaxCodeOption>> GetTaxCodesAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var codes = await dbContext.TaxCodes
            .AsNoTracking()
            .Where(code => code.CompanyId == company.CompanyId && !code.IsBlocked)
            .OrderBy(code => code.Code)
            .ToListAsync();
        if (codes.Count == 0)
        {
            return [];
        }

        var codeIds = codes.Select(code => code.Id).ToArray();
        var rates = await dbContext.TaxRates
            .AsNoTracking()
            .Where(rate => rate.CompanyId == company.CompanyId && codeIds.Contains(rate.TaxCodeId))
            .OrderBy(rate => rate.StartsOn)
            .ToListAsync();
        return codes.Select(code => new GeneralJournalTaxCodeOption(
            code.Id,
            code.Code,
            code.Description,
            code.LiabilityAccountId,
            code.RecoverableAccountId,
            rates.Where(rate => rate.TaxCodeId == code.Id)
                .Select(rate => new GeneralJournalTaxRateOption(
                    rate.RatePercent,
                    rate.StartsOn,
                    rate.EndsOn))
                .ToArray()))
            .ToArray();
    }

    public async Task<IReadOnlyList<JournalDimensionOption>> GetDimensionsAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var dimensions = await dbContext.Dimensions
            .AsNoTracking()
            .Where(dimension => dimension.CompanyId == company.CompanyId)
            .OrderBy(dimension => dimension.Code)
            .Select(dimension => new { dimension.Id, dimension.Code, dimension.Name })
            .ToListAsync();
        if (dimensions.Count == 0)
        {
            return [];
        }

        var dimensionIds = dimensions.Select(dimension => dimension.Id).ToArray();
        var values = await dbContext.DimensionValues
            .AsNoTracking()
            .Where(value => value.CompanyId == company.CompanyId && dimensionIds.Contains(value.DimensionId))
            .OrderBy(value => value.Code)
            .Select(value => new
            {
                value.Id,
                value.DimensionId,
                value.Code,
                value.Name,
                value.IsBlocked
            })
            .ToListAsync();
        return dimensions.Select(dimension => new JournalDimensionOption(
            dimension.Id,
            dimension.Code,
            dimension.Name,
            values.Where(value => value.DimensionId == dimension.Id)
                .Select(value => new JournalDimensionValueOption(
                    value.Id,
                    value.Code,
                    value.Name,
                    value.IsBlocked))
                .ToArray()))
            .ToArray();
    }

    public async Task<Guid> CreateBatchAsync(ClaimsPrincipal user, GeneralJournalBatchInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var period = await GetRequiredOpenPeriodAsync(
            dbContext,
            company.CompanyId,
            input.DefaultAccountingPeriodId);
        var template = await dbContext.GeneralJournalTemplates.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Type == JournalTemplateType.General);
        if (template is null)
        {
            template = new GeneralJournalTemplate
            {
                Id = Guid.NewGuid(),
                CompanyId = company.CompanyId,
                Code = "GENERAL",
                Description = "General journal",
                Type = JournalTemplateType.General
            };
            dbContext.GeneralJournalTemplates.Add(template);
        }

        if (await dbContext.GeneralJournalBatches.AnyAsync(batch =>
                batch.CompanyId == company.CompanyId &&
                batch.JournalTemplateId == template.Id &&
                batch.Name == input.Name))
        {
            throw new ValidationException("A journal batch with this name already exists in the active company.");
        }

        var batch = new GeneralJournalBatch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            JournalTemplateId = template.Id,
            DefaultAccountingPeriodId = period.Id,
            Name = input.Name,
            Description = input.Description,
            Status = JournalBatchStatus.Open,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        dbContext.GeneralJournalBatches.Add(batch);
        await dbContext.SaveChangesAsync();
        return batch.Id;
    }

    public async Task<bool> UpdateBatchAsync(
        ClaimsPrincipal user,
        Guid batchId,
        GeneralJournalBatchInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var batch = await dbContext.GeneralJournalBatches.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == batchId);
        if (batch is null)
        {
            return false;
        }

        if (batch.Status != JournalBatchStatus.Open)
        {
            throw new ValidationException("Posted journal batches cannot be edited.");
        }

        var period = await GetRequiredOpenPeriodAsync(
            dbContext,
            company.CompanyId,
            input.DefaultAccountingPeriodId);
        if (await dbContext.GeneralJournalBatches.AnyAsync(item =>
                item.CompanyId == company.CompanyId &&
                item.JournalTemplateId == batch.JournalTemplateId &&
                item.Name == input.Name &&
                item.Id != batch.Id))
        {
            throw new ValidationException("A journal batch with this name already exists in the active company.");
        }

        batch.Name = input.Name;
        batch.Description = input.Description;
        batch.DefaultAccountingPeriodId = period.Id;
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException("Another user changed this journal batch. Reload and try again.", exception);
        }

        return true;
    }

    public async Task<Guid> CreateLineAsync(
        ClaimsPrincipal user,
        Guid batchId,
        GeneralJournalLineInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var batch = await GetRequiredOpenBatchAsync(dbContext, company.CompanyId, batchId);
        var taxDetails = await ValidateLineAsync(dbContext, company.CompanyId, batchId, null, input);
        var line = new GeneralJournalLine
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            JournalBatchId = batch.Id,
            AccountingPeriodId = input.AccountingPeriodId,
            AccountId = input.AccountId,
            LineNumber = input.LineNumber,
            PostingDate = input.PostingDate,
            DocumentNumber = input.DocumentNumber,
            Description = input.Description,
            DebitAmount = input.DebitAmount,
            CreditAmount = input.CreditAmount,
            TaxCodeId = input.TaxCodeId,
            TaxRateId = taxDetails?.RateId,
            TaxRatePercent = taxDetails?.RatePercent,
            TaxBaseAmount = input.TaxBaseAmount,
            TaxPostingType = taxDetails?.PostingType
        };
        dbContext.GeneralJournalLines.Add(line);
        AddDimensionAssignments(dbContext, company.CompanyId, line.Id, input.Dimensions);
        await dbContext.SaveChangesAsync();
        return line.Id;
    }

    public async Task<bool> UpdateLineAsync(
        ClaimsPrincipal user,
        Guid batchId,
        Guid lineId,
        GeneralJournalLineInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await GetRequiredOpenBatchAsync(dbContext, company.CompanyId, batchId);
        var line = await dbContext.GeneralJournalLines.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId &&
            item.JournalBatchId == batchId &&
            item.Id == lineId);
        if (line is null)
        {
            return false;
        }

        var taxDetails = await ValidateLineAsync(dbContext, company.CompanyId, batchId, lineId, input);
        line.AccountingPeriodId = input.AccountingPeriodId;
        line.AccountId = input.AccountId;
        line.LineNumber = input.LineNumber;
        line.PostingDate = input.PostingDate;
        line.DocumentNumber = input.DocumentNumber;
        line.Description = input.Description;
        line.DebitAmount = input.DebitAmount;
        line.CreditAmount = input.CreditAmount;
        line.TaxCodeId = input.TaxCodeId;
        line.TaxRateId = taxDetails?.RateId;
        line.TaxRatePercent = taxDetails?.RatePercent;
        line.TaxBaseAmount = input.TaxBaseAmount;
        line.TaxPostingType = taxDetails?.PostingType;
        var oldAssignments = await dbContext.JournalLineDimensions
            .Where(item => item.CompanyId == company.CompanyId && item.JournalLineId == lineId)
            .ToListAsync();
        dbContext.JournalLineDimensions.RemoveRange(oldAssignments);
        AddDimensionAssignments(dbContext, company.CompanyId, line.Id, input.Dimensions);
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteLineAsync(ClaimsPrincipal user, Guid batchId, Guid lineId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await GetRequiredOpenBatchAsync(dbContext, company.CompanyId, batchId);
        var line = await dbContext.GeneralJournalLines.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId &&
            item.JournalBatchId == batchId &&
            item.Id == lineId);
        if (line is null)
        {
            return false;
        }

        var assignments = await dbContext.JournalLineDimensions
            .Where(item => item.CompanyId == company.CompanyId && item.JournalLineId == lineId)
            .ToListAsync();
        dbContext.JournalLineDimensions.RemoveRange(assignments);
        dbContext.GeneralJournalLines.Remove(line);
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task PostBatchAsync(ClaimsPrincipal user, Guid batchId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        var batch = await dbContext.GeneralJournalBatches.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == batchId);
        if (batch is null)
        {
            throw new ValidationException("The journal batch was not found in the active company.");
        }

        if (batch.Status != JournalBatchStatus.Open)
        {
            throw new ValidationException("Only an open journal batch can be posted.");
        }

        var lines = await dbContext.GeneralJournalLines
            .Where(line => line.CompanyId == company.CompanyId && line.JournalBatchId == batchId)
            .OrderBy(line => line.LineNumber)
            .ToListAsync();
        ValidateBatchIsBalanced(lines);
        await BankService.ValidateJournalForPostingAsync(dbContext, company.CompanyId, batchId, lines);
        foreach (var line in lines)
        {
            await ValidatePersistedLineAsync(dbContext, company.CompanyId, line);
        }

        var now = DateTimeOffset.UtcNow;
        await inventoryJournalPostingService.ApplyForPostingAsync(
            dbContext,
            user,
            company.CompanyId,
            batch,
            lines,
            now);
        var numberSeries = await GetEntryNumberSeriesAsync(dbContext, company.CompanyId);
        var userId = GetUserId(user);
        foreach (var line in lines)
        {
            dbContext.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.CompanyId,
                JournalBatchId = batch.Id,
                JournalLineId = line.Id,
                AccountId = line.AccountId,
                EntryNumber = numberSeries.NextNumber++,
                PostingDate = line.PostingDate,
                DocumentNumber = line.DocumentNumber,
                Description = line.Description,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                TaxCodeId = line.TaxCodeId,
                TaxRateId = line.TaxRateId,
                TaxRatePercent = line.TaxRatePercent,
                TaxBaseAmount = line.TaxBaseAmount,
                TaxPostingType = line.TaxPostingType,
                CreatedAtUtc = now
            });
        }

        batch.Status = JournalBatchStatus.Posted;
        batch.PostedAtUtc = now;
        batch.PostedByUserId = userId;
        await SavePostingAsync(dbContext);
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }
    }

    public async Task<Guid> ReverseBatchAsync(
        ClaimsPrincipal user,
        Guid batchId,
        GeneralJournalReversalInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await using var transaction = await BeginSerializableTransactionAsync(dbContext);
        var original = await dbContext.GeneralJournalBatches.SingleOrDefaultAsync(batch =>
            batch.CompanyId == company.CompanyId && batch.Id == batchId);
        if (original is null || original.Status != JournalBatchStatus.Posted)
        {
            throw new ValidationException("Only a posted journal batch in the active company can be reversed.");
        }

        if (original.ReversedByBatchId is not null)
        {
            throw new ValidationException("This journal batch has already been reversed.");
        }

        if (await dbContext.InventoryMovements.AnyAsync(movement =>
                movement.CompanyId == company.CompanyId &&
                movement.JournalBatchId == original.Id))
        {
            throw new ValidationException(
                "Inventory movement journals cannot be reversed as general journals. Create a compensating inventory movement instead.");
        }

        var period = await GetRequiredOpenPeriodAsync(
            dbContext,
            company.CompanyId,
            input.AccountingPeriodId);
        ValidatePostingDate(period, input.PostingDate);
        if (await dbContext.GeneralJournalBatches.AnyAsync(batch =>
                batch.CompanyId == company.CompanyId &&
                batch.JournalTemplateId == original.JournalTemplateId &&
                batch.Name == input.Name))
        {
            throw new ValidationException("A journal batch with this name already exists in the active company.");
        }

        var originalLines = await dbContext.GeneralJournalLines
            .AsNoTracking()
            .Where(line => line.CompanyId == company.CompanyId && line.JournalBatchId == batchId)
            .OrderBy(line => line.LineNumber)
            .ToListAsync();
        var originalLineIds = originalLines.Select(line => line.Id).ToArray();
        var originalEntries = await dbContext.GeneralLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.CompanyId == company.CompanyId &&
                entry.JournalBatchId == batchId &&
                originalLineIds.Contains(entry.JournalLineId))
            .ToDictionaryAsync(entry => entry.JournalLineId);
        if (originalLines.Count == 0 ||
            originalEntries.Count != originalLines.Count ||
            originalLines.Any(line => !originalEntries.ContainsKey(line.Id)))
        {
            throw new ValidationException("The posted batch has incomplete ledger entries and cannot be reversed.");
        }

        var assignments = await dbContext.JournalLineDimensions
            .AsNoTracking()
            .Where(item => item.CompanyId == company.CompanyId && originalLineIds.Contains(item.JournalLineId))
            .ToListAsync();
        var numberSeries = await GetEntryNumberSeriesAsync(dbContext, company.CompanyId);
        var reversal = new GeneralJournalBatch
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            JournalTemplateId = original.JournalTemplateId,
            DefaultAccountingPeriodId = period.Id,
            Name = input.Name,
            Description = input.Description,
            Status = JournalBatchStatus.Posted,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            PostedAtUtc = DateTimeOffset.UtcNow,
            PostedByUserId = GetUserId(user)
        };
        original.ReversedByBatchId = reversal.Id;
        dbContext.GeneralJournalBatches.Add(reversal);
        var now = DateTimeOffset.UtcNow;
        foreach (var originalLine in originalLines)
        {
            var originalEntry = originalEntries[originalLine.Id];
            var reversalLineId = Guid.NewGuid();
            dbContext.GeneralJournalLines.Add(new GeneralJournalLine
            {
                Id = reversalLineId,
                CompanyId = company.CompanyId,
                JournalBatchId = reversal.Id,
                AccountingPeriodId = period.Id,
                AccountId = originalEntry.AccountId,
                LineNumber = originalLine.LineNumber,
                PostingDate = input.PostingDate,
                DocumentNumber = BuildReversalDocumentNumber(originalEntry.DocumentNumber),
                Description = BuildReversalDescription(original.Name, originalEntry.Description),
                DebitAmount = originalEntry.CreditAmount,
                CreditAmount = originalEntry.DebitAmount,
                CurrencyCode = originalEntry.CurrencyCode,
                CurrencyDebitAmount = originalEntry.CurrencyCreditAmount,
                CurrencyCreditAmount = originalEntry.CurrencyDebitAmount,
                TaxCodeId = originalEntry.TaxCodeId,
                TaxRateId = originalEntry.TaxRateId,
                TaxRatePercent = originalEntry.TaxRatePercent,
                TaxBaseAmount = originalEntry.TaxBaseAmount,
                TaxPostingType = originalEntry.TaxPostingType
            });
            foreach (var assignment in assignments.Where(item => item.JournalLineId == originalLine.Id))
            {
                dbContext.JournalLineDimensions.Add(new JournalLineDimension
                {
                    CompanyId = company.CompanyId,
                    JournalLineId = reversalLineId,
                    DimensionId = assignment.DimensionId,
                    DimensionValueId = assignment.DimensionValueId
                });
            }

            dbContext.GeneralLedgerEntries.Add(new GeneralLedgerEntry
            {
                Id = Guid.NewGuid(),
                CompanyId = company.CompanyId,
                JournalBatchId = reversal.Id,
                JournalLineId = reversalLineId,
                AccountId = originalEntry.AccountId,
                EntryNumber = numberSeries.NextNumber++,
                PostingDate = input.PostingDate,
                DocumentNumber = BuildReversalDocumentNumber(originalEntry.DocumentNumber),
                Description = BuildReversalDescription(original.Name, originalEntry.Description),
                DebitAmount = originalEntry.CreditAmount,
                CreditAmount = originalEntry.DebitAmount,
                CurrencyCode = originalEntry.CurrencyCode,
                CurrencyDebitAmount = originalEntry.CurrencyCreditAmount,
                CurrencyCreditAmount = originalEntry.CurrencyDebitAmount,
                TaxCodeId = originalEntry.TaxCodeId,
                TaxRateId = originalEntry.TaxRateId,
                TaxRatePercent = originalEntry.TaxRatePercent,
                TaxBaseAmount = originalEntry.TaxBaseAmount,
                TaxPostingType = originalEntry.TaxPostingType,
                CreatedAtUtc = now
            });
        }

        await SavePostingAsync(dbContext);
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }

        return reversal.Id;
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required to manage general journals.");

    private static void NormalizeAndValidate(AccountingPeriodInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Name = input.Name?.Trim() ?? string.Empty;
        ValidateFields(input);
        if (input.StartsOn == default || input.EndsOn == default || input.StartsOn > input.EndsOn)
        {
            throw new ValidationException("Enter a valid accounting-period date range.");
        }
    }

    private static void NormalizeAndValidate(GeneralJournalBatchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        ValidateFields(input);
    }

    private static void NormalizeAndValidate(GeneralJournalLineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.DocumentNumber = input.DocumentNumber?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        ValidateFields(input);
        ValidateAmountSides(input.DebitAmount, input.CreditAmount);
        if (input.PostingDate == default)
        {
            throw new ValidationException("Enter a posting date.");
        }

        if (input.TaxCodeId is null && input.TaxBaseAmount is not null)
        {
            throw new ValidationException("Select a tax code when entering a taxable base.");
        }

        if (input.TaxBaseAmount is { } baseAmount &&
            (baseAmount <= 0 ||
             baseAmount > MaximumAmount ||
             decimal.Round(baseAmount, 4) != baseAmount))
        {
            throw new ValidationException("Enter a positive taxable base with no more than four decimal places.");
        }
    }

    private static void NormalizeAndValidate(GeneralJournalReversalInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Name = input.Name?.Trim() ?? string.Empty;
        input.Description = input.Description?.Trim() ?? string.Empty;
        ValidateFields(input);
        if (input.PostingDate == default)
        {
            throw new ValidationException("Enter a reversal posting date.");
        }
    }

    private static void ValidateFields(object input) =>
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

    private static void ValidateAmountSides(decimal debitAmount, decimal creditAmount)
    {
        var validDebit = debitAmount > 0 && creditAmount == 0;
        var validCredit = creditAmount > 0 && debitAmount == 0;
        if ((!validDebit && !validCredit) ||
            debitAmount > MaximumAmount ||
            creditAmount > MaximumAmount ||
            decimal.Round(debitAmount, 4) != debitAmount ||
            decimal.Round(creditAmount, 4) != creditAmount)
        {
            throw new ValidationException(
                "Enter a positive debit or credit amount (not both) with no more than four decimal places.");
        }
    }

    private static async Task EnsurePeriodDoesNotOverlapAsync(
        ErpDbContext dbContext,
        Guid companyId,
        DateOnly startsOn,
        DateOnly endsOn)
    {
        var periods = dbContext.AccountingPeriods.Where(period =>
            period.CompanyId == companyId &&
            period.StartsOn <= endsOn &&
            period.EndsOn >= startsOn);
        if (await periods.AnyAsync())
        {
            throw new ValidationException("Accounting-period dates cannot overlap another period in this company.");
        }
    }

    private static async Task<AccountingPeriod> GetRequiredOpenPeriodAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid periodId)
    {
        var period = await dbContext.AccountingPeriods.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == periodId);
        if (period is null)
        {
            throw new ValidationException("Select an accounting period from the active company.");
        }

        if (period.IsClosed || period.DateLocked)
        {
            throw new ValidationException("The selected accounting period is closed or date-locked.");
        }

        return period;
    }

    private static void ValidatePostingDate(AccountingPeriod period, DateOnly postingDate)
    {
        if (postingDate < period.StartsOn || postingDate > period.EndsOn)
        {
            throw new ValidationException("The posting date must fall within the selected accounting period.");
        }
    }

    private static async Task<GeneralJournalBatch> GetRequiredOpenBatchAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid batchId)
    {
        var batch = await dbContext.GeneralJournalBatches.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Id == batchId);
        if (batch is null)
        {
            throw new ValidationException("The journal batch was not found in the active company.");
        }

        if (batch.Status != JournalBatchStatus.Open)
        {
            throw new ValidationException("Posted journal batches cannot be edited.");
        }

        return batch;
    }

    private static async Task<TaxLineDetails?> ValidateLineAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid batchId,
        Guid? lineId,
        GeneralJournalLineInput input)
    {
        if (await dbContext.GeneralJournalLines.AnyAsync(line =>
                line.CompanyId == companyId &&
                line.JournalBatchId == batchId &&
                line.LineNumber == input.LineNumber &&
                (lineId == null || line.Id != lineId)))
        {
            throw new ValidationException("A journal line with this line number already exists in the batch.");
        }

        var period = await GetRequiredOpenPeriodAsync(dbContext, companyId, input.AccountingPeriodId);
        ValidatePostingDate(period, input.PostingDate);
        if (!await dbContext.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId &&
                account.Id == input.AccountId &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked))
        {
            throw new ValidationException(
                "Select an unblocked posting account that allows direct posting from the active company.");
        }

        var taxDetails = await ValidateTaxLineAsync(dbContext, companyId, input);
        await ValidateDimensionsAsync(dbContext, companyId, input.Dimensions);
        return taxDetails;
    }

    private static async Task ValidatePersistedLineAsync(
        ErpDbContext dbContext,
        Guid companyId,
        GeneralJournalLine line)
    {
        ValidateAmountSides(line.DebitAmount, line.CreditAmount);
        if (line.CurrencyCode is not null ||
            line.CurrencyDebitAmount is not null ||
            line.CurrencyCreditAmount is not null)
        {
            throw new ValidationException("Foreign-currency journal lines are not supported in this workflow.");
        }

        var period = await GetRequiredOpenPeriodAsync(dbContext, companyId, line.AccountingPeriodId);
        ValidatePostingDate(period, line.PostingDate);
        if (!await dbContext.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId &&
                account.Id == line.AccountId &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked))
        {
            throw new ValidationException("A journal line references an account that is no longer eligible for posting.");
        }

        if (line.TaxCodeId is null)
        {
            if (line.TaxRateId is not null ||
                line.TaxRatePercent is not null ||
                line.TaxBaseAmount is not null ||
                line.TaxPostingType is not null)
            {
                throw new ValidationException("A journal line has incomplete tax details.");
            }
        }
        else
        {
            if (line.TaxRateId is null ||
                line.TaxRatePercent is null ||
                line.TaxBaseAmount is null ||
                line.TaxPostingType is null)
            {
                throw new ValidationException("A journal line has incomplete tax details.");
            }

            var input = new GeneralJournalLineInput
            {
                AccountingPeriodId = line.AccountingPeriodId,
                AccountId = line.AccountId,
                PostingDate = line.PostingDate,
                DebitAmount = line.DebitAmount,
                CreditAmount = line.CreditAmount,
                TaxCodeId = line.TaxCodeId,
                TaxBaseAmount = line.TaxBaseAmount
            };
            var taxDetails = await ValidateTaxLineAsync(dbContext, companyId, input)
                ?? throw new ValidationException("A journal line's tax details are incomplete.");
            if (taxDetails.RateId != line.TaxRateId ||
                taxDetails.RatePercent != line.TaxRatePercent ||
                taxDetails.PostingType != line.TaxPostingType)
            {
                throw new ValidationException("A journal line's tax rate or treatment has changed; review the line before posting.");
            }
        }

        var dimensions = await dbContext.JournalLineDimensions
            .Where(item => item.CompanyId == companyId && item.JournalLineId == line.Id)
            .Select(item => new JournalDimensionAssignmentInput
            {
                DimensionId = item.DimensionId,
                DimensionValueId = item.DimensionValueId
            })
            .ToListAsync();
        await ValidateDimensionsAsync(dbContext, companyId, dimensions);
    }

    private static async Task<TaxLineDetails?> ValidateTaxLineAsync(
        ErpDbContext dbContext,
        Guid companyId,
        GeneralJournalLineInput input)
    {
        if (input.TaxCodeId is not { } taxCodeId)
        {
            if (input.TaxBaseAmount is not null)
            {
                throw new ValidationException("Select a tax code when entering a taxable base.");
            }

            return null;
        }

        if (input.TaxBaseAmount is not { } taxBaseAmount)
        {
            throw new ValidationException("Enter the taxable base for a tax journal line.");
        }
        if (taxBaseAmount <= 0 ||
            taxBaseAmount > MaximumAmount ||
            decimal.Round(taxBaseAmount, 4) != taxBaseAmount)
        {
            throw new ValidationException("Enter a positive taxable base with no more than four decimal places.");
        }

        var taxCode = await dbContext.TaxCodes.SingleOrDefaultAsync(code =>
            code.CompanyId == companyId && code.Id == taxCodeId && !code.IsBlocked);
        if (taxCode is null)
        {
            throw new ValidationException("Select an unblocked tax code from the active company.");
        }

        var rate = await dbContext.TaxRates.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId &&
            item.TaxCodeId == taxCodeId &&
            item.StartsOn <= input.PostingDate &&
            (item.EndsOn == null || item.EndsOn >= input.PostingDate));
        if (rate is null)
        {
            throw new ValidationException("No tax rate for this code is effective on the journal posting date.");
        }

        var postingType = input.DebitAmount > 0
            ? TaxPostingType.Recoverable
            : TaxPostingType.Liability;
        var mappedAccountId = postingType == TaxPostingType.Recoverable
            ? taxCode.RecoverableAccountId
            : taxCode.LiabilityAccountId;
        var expectedCategory = postingType == TaxPostingType.Recoverable
            ? AccountCategory.Asset
            : AccountCategory.Liability;
        if (input.AccountId != mappedAccountId ||
            !await dbContext.GeneralLedgerAccounts.AnyAsync(account =>
                account.CompanyId == companyId &&
                account.Id == mappedAccountId &&
                account.Category == expectedCategory &&
                account.Type == AccountType.Posting &&
                account.AllowsDirectPosting &&
                !account.IsBlocked))
        {
            throw new ValidationException(
                $"A {postingType.ToString().ToLowerInvariant()} tax line must use its unblocked configured posting account.");
        }

        decimal expectedTaxAmount;
        try
        {
            expectedTaxAmount = decimal.Round(
                checked(taxBaseAmount * rate.RatePercent / 100m),
                4,
                MidpointRounding.AwayFromZero);
        }
        catch (OverflowException exception)
        {
            throw new ValidationException("The calculated tax amount exceeds the supported accounting range.", exception);
        }

        var enteredTaxAmount = input.DebitAmount > 0 ? input.DebitAmount : input.CreditAmount;
        if (expectedTaxAmount <= 0 || enteredTaxAmount != expectedTaxAmount)
        {
            throw new ValidationException(
                $"The tax line amount must equal the taxable base multiplied by the effective rate ({expectedTaxAmount:0.0000}).");
        }

        return new TaxLineDetails(rate.Id, rate.RatePercent, postingType);
    }

    private static async Task ValidateDimensionsAsync(
        ErpDbContext dbContext,
        Guid companyId,
        IReadOnlyCollection<JournalDimensionAssignmentInput> dimensions)
    {
        if (dimensions.Select(item => item.DimensionId).Distinct().Count() != dimensions.Count)
        {
            throw new ValidationException("A journal line can have only one value for each dimension.");
        }

        if (dimensions.Any(item => item.DimensionId == Guid.Empty || item.DimensionValueId == Guid.Empty))
        {
            throw new ValidationException("Select a valid dimension and value for every assignment.");
        }

        foreach (var assignment in dimensions)
        {
            if (!await (
                    from dimension in dbContext.Dimensions
                    join value in dbContext.DimensionValues
                        on new { dimension.CompanyId, DimensionId = dimension.Id }
                        equals new { value.CompanyId, value.DimensionId }
                    where dimension.CompanyId == companyId &&
                        dimension.Id == assignment.DimensionId &&
                        value.Id == assignment.DimensionValueId &&
                        !value.IsBlocked
                    select dimension.Id)
                .AnyAsync())
            {
                throw new ValidationException(
                    "Each dimension value must be active, unblocked, and belong to its dimension in the active company.");
            }
        }
    }

    private static void AddDimensionAssignments(
        ErpDbContext dbContext,
        Guid companyId,
        Guid lineId,
        IEnumerable<JournalDimensionAssignmentInput> dimensions)
    {
        foreach (var assignment in dimensions)
        {
            dbContext.JournalLineDimensions.Add(new JournalLineDimension
            {
                CompanyId = companyId,
                JournalLineId = lineId,
                DimensionId = assignment.DimensionId,
                DimensionValueId = assignment.DimensionValueId
            });
        }
    }

    private sealed record TaxLineDetails(Guid RateId, decimal RatePercent, TaxPostingType PostingType);

    private static void ValidateBatchIsBalanced(IReadOnlyCollection<GeneralJournalLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new ValidationException("Add at least one journal line before posting.");
        }

        try
        {
            var totalDebit = 0m;
            var totalCredit = 0m;
            foreach (var line in lines)
            {
                totalDebit = checked(totalDebit + line.DebitAmount);
                totalCredit = checked(totalCredit + line.CreditAmount);
            }

            if (totalDebit != totalCredit)
            {
                throw new ValidationException("The journal batch must balance: total debit must equal total credit.");
            }
        }
        catch (OverflowException exception)
        {
            throw new ValidationException("The journal batch totals exceed the supported accounting amount range.", exception);
        }
    }

    private static async Task<NumberSeries> GetEntryNumberSeriesAsync(ErpDbContext dbContext, Guid companyId)
    {
        var series = await dbContext.NumberSeries.SingleOrDefaultAsync(item =>
            item.CompanyId == companyId && item.Code == EntryNumberSeriesCode);
        if (series is not null)
        {
            return series;
        }

        var lastEntryNumber = await dbContext.GeneralLedgerEntries
            .Where(entry => entry.CompanyId == companyId)
            .Select(entry => (long?)entry.EntryNumber)
            .MaxAsync() ?? 0;
        series = new NumberSeries
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Code = EntryNumberSeriesCode,
            Prefix = "GLE",
            NextNumber = checked(lastEntryNumber + 1),
            MinimumDigits = 1
        };
        dbContext.NumberSeries.Add(series);
        return series;
    }

    private static async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(ErpDbContext dbContext)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
    }

    private static async Task SavePostingAsync(ErpDbContext dbContext)
    {
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ValidationException(
                "Another journal operation changed this batch or the entry-number sequence. Reload and retry.",
                exception);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw new ValidationException(
                "Another journal operation posted or reversed this data concurrently. Reload and retry.",
                exception);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }

    private static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static string BuildReversalDocumentNumber(string documentNumber) =>
        $"REV-{documentNumber}"[..Math.Min(64, documentNumber.Length + 4)];

    private static string BuildReversalDescription(string originalBatchName, string description)
    {
        var reversalDescription = $"Reversal of {originalBatchName}: {description}";
        return reversalDescription[..Math.Min(250, reversalDescription.Length)];
    }
}
