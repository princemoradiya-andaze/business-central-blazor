using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed class GeneralLedgerAccountInput
{
    [Required]
    [StringLength(32)]
    public string Number { get; set; } = string.Empty;

    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    public AccountCategory Category { get; set; }
    public AccountType Type { get; set; } = AccountType.Posting;
    public Guid? ParentAccountId { get; set; }
    public bool AllowsDirectPosting { get; set; } = true;
    public bool IsBlocked { get; set; }
}

public sealed record GeneralLedgerAccountListItem(
    Guid Id,
    string Number,
    string Name,
    AccountCategory Category,
    AccountType Type,
    Guid? ParentAccountId,
    string? ParentAccountNumber,
    bool AllowsDirectPosting,
    bool IsBlocked);

public sealed record GeneralLedgerAccountDetail(
    Guid Id,
    string Number,
    string Name,
    AccountCategory Category,
    AccountType Type,
    Guid? ParentAccountId,
    bool AllowsDirectPosting,
    bool IsBlocked);

public interface IGeneralLedgerAccountService
{
    Task<IReadOnlyList<GeneralLedgerAccountListItem>> GetAllAsync(ClaimsPrincipal user);
    Task<GeneralLedgerAccountDetail?> GetAsync(ClaimsPrincipal user, Guid accountId);
    Task<Guid> CreateAsync(ClaimsPrincipal user, GeneralLedgerAccountInput input);
    Task<bool> UpdateAsync(ClaimsPrincipal user, Guid accountId, GeneralLedgerAccountInput input);
}

public sealed class GeneralLedgerAccountService(
    IDbContextFactory<ErpDbContext> dbContextFactory,
    ICompanyScopeAuthorizer companyScopeAuthorizer) : IGeneralLedgerAccountService
{
    public async Task<IReadOnlyList<GeneralLedgerAccountListItem>> GetAllAsync(ClaimsPrincipal user)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var accounts = await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId)
            .OrderBy(account => account.Number)
            .ToListAsync();
        var accountNumbers = accounts.ToDictionary(account => account.Id, account => account.Number);

        return accounts.Select(account => new GeneralLedgerAccountListItem(
            account.Id,
            account.Number,
            account.Name,
            account.Category,
            account.Type,
            account.ParentAccountId,
            account.ParentAccountId is { } parentId && accountNumbers.TryGetValue(parentId, out var number)
                ? number
                : null,
            account.AllowsDirectPosting,
            account.IsBlocked)).ToArray();
    }

    public async Task<GeneralLedgerAccountDetail?> GetAsync(ClaimsPrincipal user, Guid accountId)
    {
        var company = await GetRequiredCompanyAsync(user);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        return await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == company.CompanyId && account.Id == accountId)
            .Select(account => new GeneralLedgerAccountDetail(
                account.Id,
                account.Number,
                account.Name,
                account.Category,
                account.Type,
                account.ParentAccountId,
                account.AllowsDirectPosting,
                account.IsBlocked))
            .SingleOrDefaultAsync();
    }

    public async Task<Guid> CreateAsync(ClaimsPrincipal user, GeneralLedgerAccountInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await ValidateNumberAsync(dbContext, company.CompanyId, input.Number, null);

        var accountId = Guid.NewGuid();
        await ValidateParentAsync(dbContext, company.CompanyId, accountId, input.ParentAccountId);
        var account = new GeneralLedgerAccount
        {
            Id = accountId,
            CompanyId = company.CompanyId,
            Number = input.Number,
            Name = input.Name,
            Category = input.Category,
            Type = input.Type,
            ParentAccountId = input.ParentAccountId,
            AllowsDirectPosting = input.AllowsDirectPosting,
            IsBlocked = input.IsBlocked
        };

        dbContext.GeneralLedgerAccounts.Add(account);
        await dbContext.SaveChangesAsync();
        return account.Id;
    }

    public async Task<bool> UpdateAsync(
        ClaimsPrincipal user,
        Guid accountId,
        GeneralLedgerAccountInput input)
    {
        var company = await GetRequiredCompanyAsync(user);
        NormalizeAndValidate(input);
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var account = await dbContext.GeneralLedgerAccounts.SingleOrDefaultAsync(item =>
            item.CompanyId == company.CompanyId && item.Id == accountId);
        if (account is null)
        {
            return false;
        }

        await ValidateNumberAsync(dbContext, company.CompanyId, input.Number, accountId);
        await ValidateParentAsync(dbContext, company.CompanyId, accountId, input.ParentAccountId);
        account.Number = input.Number;
        account.Name = input.Name;
        account.Category = input.Category;
        account.Type = input.Type;
        account.ParentAccountId = input.ParentAccountId;
        account.AllowsDirectPosting = input.AllowsDirectPosting;
        account.IsBlocked = input.IsBlocked;
        await dbContext.SaveChangesAsync();
        return true;
    }

    private async Task<CompanyMembershipOption> GetRequiredCompanyAsync(ClaimsPrincipal user) =>
        await companyScopeAuthorizer.ResolveActiveCompanyAsync(user)
        ?? throw new UnauthorizedAccessException("An active company is required for Chart of Accounts.");

    private static void NormalizeAndValidate(GeneralLedgerAccountInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Number = input.Number?.Trim() ?? string.Empty;
        input.Name = input.Name?.Trim() ?? string.Empty;
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);
        if (!Enum.IsDefined(input.Category))
        {
            throw new ValidationException("Select a valid account category.");
        }

        if (!Enum.IsDefined(input.Type))
        {
            throw new ValidationException("Select a valid account type.");
        }
    }

    private static async Task ValidateNumberAsync(
        ErpDbContext dbContext,
        Guid companyId,
        string number,
        Guid? accountId)
    {
        var accounts = dbContext.GeneralLedgerAccounts.Where(account =>
            account.CompanyId == companyId && account.Number == number);
        if (accountId is { } existingAccountId)
        {
            accounts = accounts.Where(account => account.Id != existingAccountId);
        }

        if (await accounts.AnyAsync())
        {
            throw new ValidationException("An account with this number already exists in the active company.");
        }
    }

    private static async Task ValidateParentAsync(
        ErpDbContext dbContext,
        Guid companyId,
        Guid accountId,
        Guid? parentAccountId)
    {
        if (parentAccountId is not { } parentId)
        {
            return;
        }

        if (parentId == accountId)
        {
            throw new ValidationException("An account cannot be its own parent.");
        }

        var parent = await dbContext.GeneralLedgerAccounts
            .AsNoTracking()
            .Where(account => account.CompanyId == companyId && account.Id == parentId)
            .Select(account => new { account.Id, account.ParentAccountId })
            .SingleOrDefaultAsync();
        if (parent is null)
        {
            throw new ValidationException("Select a parent account from the active company.");
        }

        var visited = new HashSet<Guid>();
        Guid? ancestorId = parent.Id;
        while (ancestorId is { } currentId)
        {
            if (!visited.Add(currentId))
            {
                throw new ValidationException("The selected parent is part of an invalid account hierarchy.");
            }

            if (currentId == accountId)
            {
                throw new ValidationException("The selected parent would create an account hierarchy cycle.");
            }

            ancestorId = await dbContext.GeneralLedgerAccounts
                .AsNoTracking()
                .Where(account => account.CompanyId == companyId && account.Id == currentId)
                .Select(account => account.ParentAccountId)
                .SingleOrDefaultAsync();
        }
    }
}
