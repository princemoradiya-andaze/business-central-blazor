using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed class DimensionInput
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;
}

public sealed class DimensionValueInput
{
    [Required]
    [StringLength(32)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public bool IsBlocked { get; set; }
}

public sealed record DimensionListItem(Guid Id, string Code, string Name, int ValueCount);

public sealed record DimensionDetail(Guid Id, string Code, string Name);

public sealed record DimensionValueListItem(Guid Id, string Code, string Name, bool IsBlocked);

public sealed record DimensionValueDetail(Guid Id, Guid DimensionId, string Code, string Name, bool IsBlocked);

public interface IDimensionService
{
    Task<IReadOnlyList<DimensionListItem>> GetAllAsync(ClaimsPrincipal user);
    Task<DimensionDetail?> GetAsync(ClaimsPrincipal user, Guid dimensionId);
    Task<Guid> CreateAsync(ClaimsPrincipal user, DimensionInput input);
    Task<bool> UpdateAsync(ClaimsPrincipal user, Guid dimensionId, DimensionInput input);
    Task<IReadOnlyList<DimensionValueListItem>> GetValuesAsync(ClaimsPrincipal user, Guid dimensionId);
    Task<DimensionValueDetail?> GetValueAsync(ClaimsPrincipal user, Guid dimensionId, Guid valueId);
    Task<Guid> CreateValueAsync(ClaimsPrincipal user, Guid dimensionId, DimensionValueInput input);
    Task<bool> UpdateValueAsync(
        ClaimsPrincipal user,
        Guid dimensionId,
        Guid valueId,
        DimensionValueInput input);
}

public sealed class DimensionService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IDimensionService
{
    public async Task<IReadOnlyList<DimensionListItem>> GetAllAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.Dimensions
            .AsNoTracking()
            .Where(dimension => dimension.CompanyId == company.CompanyId)
            .OrderBy(dimension => dimension.Code)
            .Select(dimension => new DimensionListItem(
                dimension.Id,
                dimension.Code,
                dimension.Name,
                dbContext.DimensionValues.Count(value =>
                    value.CompanyId == company.CompanyId && value.DimensionId == dimension.Id)))
            .ToListAsync();
    }

    public async Task<DimensionDetail?> GetAsync(ClaimsPrincipal user, Guid dimensionId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.Dimensions
            .AsNoTracking()
            .Where(dimension => dimension.CompanyId == company.CompanyId && dimension.Id == dimensionId)
            .Select(dimension => new DimensionDetail(dimension.Id, dimension.Code, dimension.Name))
            .SingleOrDefaultAsync();
    }

    public async Task<Guid> CreateAsync(ClaimsPrincipal user, DimensionInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await EnsureUniqueDimensionCodeAsync(dbContext, company.CompanyId, input.Code, null);

        var dimension = new Dimension
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Code = input.Code,
            Name = input.Name
        };
        dbContext.Dimensions.Add(dimension);
        await dbContext.SaveChangesAsync();
        return dimension.Id;
    }

    public async Task<bool> UpdateAsync(ClaimsPrincipal user, Guid dimensionId, DimensionInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var dimension = await dbContext.Dimensions.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == dimensionId);
        if (dimension is null)
        {
            return false;
        }

        await EnsureUniqueDimensionCodeAsync(dbContext, company.CompanyId, input.Code, dimensionId);
        dimension.Code = input.Code;
        dimension.Name = input.Name;
        await dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<IReadOnlyList<DimensionValueListItem>> GetValuesAsync(
        ClaimsPrincipal user,
        Guid dimensionId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        if (!await dbContext.Dimensions.AnyAsync(dimension =>
                dimension.CompanyId == company.CompanyId && dimension.Id == dimensionId))
        {
            return [];
        }

        return await dbContext.DimensionValues
            .AsNoTracking()
            .Where(value => value.CompanyId == company.CompanyId && value.DimensionId == dimensionId)
            .OrderBy(value => value.Code)
            .Select(value => new DimensionValueListItem(value.Id, value.Code, value.Name, value.IsBlocked))
            .ToListAsync();
    }

    public async Task<DimensionValueDetail?> GetValueAsync(
        ClaimsPrincipal user,
        Guid dimensionId,
        Guid valueId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.DimensionValues
            .AsNoTracking()
            .Where(value =>
                value.CompanyId == company.CompanyId &&
                value.DimensionId == dimensionId &&
                value.Id == valueId)
            .Select(value => new DimensionValueDetail(
                value.Id,
                value.DimensionId,
                value.Code,
                value.Name,
                value.IsBlocked))
            .SingleOrDefaultAsync();
    }

    public async Task<Guid> CreateValueAsync(
        ClaimsPrincipal user,
        Guid dimensionId,
        DimensionValueInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await EnsureDimensionExistsAsync(dbContext, company.CompanyId, dimensionId);
        await EnsureUniqueValueCodeAsync(dbContext, company.CompanyId, dimensionId, input.Code, null);

        var value = new DimensionValue
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            DimensionId = dimensionId,
            Code = input.Code,
            Name = input.Name,
            IsBlocked = input.IsBlocked
        };
        dbContext.DimensionValues.Add(value);
        await dbContext.SaveChangesAsync();
        return value.Id;
    }

    public async Task<bool> UpdateValueAsync(
        ClaimsPrincipal user,
        Guid dimensionId,
        Guid valueId,
        DimensionValueInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var value = await dbContext.DimensionValues.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId &&
            item.DimensionId == dimensionId &&
            item.Id == valueId);
        if (value is null)
        {
            return false;
        }

        await EnsureUniqueValueCodeAsync(dbContext, company.CompanyId, dimensionId, input.Code, valueId);
        value.Code = input.Code;
        value.Name = input.Name;
        value.IsBlocked = input.IsBlocked;
        await dbContext.SaveChangesAsync();
        return true;
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required to manage dimensions.");

    private static void NormalizeAndValidate(DimensionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Code = input.Code?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        ValidateFields(input);
    }

    private static void NormalizeAndValidate(DimensionValueInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Code = input.Code?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        ValidateFields(input);
    }

    private static void ValidateFields(object input)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
    }

    private static async Task EnsureUniqueDimensionCodeAsync(
        ErpDbContext dbContext,
        Guid companyId,
        string code,
        Guid? dimensionId)
    {
        var dimensions = dbContext.Dimensions.Where(dimension =>
            dimension.CompanyId == companyId && dimension.Code == code);
        if (dimensionId is { } currentId)
        {
            dimensions = dimensions.Where(dimension => dimension.Id != currentId);
        }

        if (await dimensions.AnyAsync())
        {
            throw new ValidationException("A dimension with this code already exists in the active company.");
        }
    }

    private static async Task EnsureUniqueValueCodeAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid dimensionId,
        string code,
        Guid? valueId)
    {
        var values = dbContext.DimensionValues.Where(value =>
            value.CompanyId == companyId &&
            value.DimensionId == dimensionId &&
            value.Code == code);
        if (valueId is { } currentId)
        {
            values = values.Where(value => value.Id != currentId);
        }

        if (await values.AnyAsync())
        {
            throw new ValidationException("A value with this code already exists in this dimension.");
        }
    }

    private static async Task EnsureDimensionExistsAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid dimensionId)
    {
        if (!await dbContext.Dimensions.AnyAsync(dimension =>
                dimension.CompanyId == companyId && dimension.Id == dimensionId))
        {
            throw new ValidationException("The selected dimension is not available in the active company.");
        }
    }
}
