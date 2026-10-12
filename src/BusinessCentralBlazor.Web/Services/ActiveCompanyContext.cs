using System.Security.Claims;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public static class ActiveCompanyClaim
{
    public const string Type = "erp:active_company_id";
}

public sealed record CompanyMembershipOption(
    Guid TenantId,
    Guid CompanyId,
    string Code,
    string Name,
    string RoleName);

public interface IActiveCompanyContext
{
    Task<IReadOnlyList<CompanyMembershipOption>> GetEligibleCompaniesAsync(ClaimsPrincipal user);
    Task<CompanyMembershipOption?> ResolveAsync(ClaimsPrincipal user);
}

public sealed class ActiveCompanyContext(IDbContextFactory<ErpDbContext> dbContextFactory) : IActiveCompanyContext
{
    public async Task<IReadOnlyList<CompanyMembershipOption>> GetEligibleCompaniesAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return [];
        }

        return await GetEligibleCompaniesForUserAsync(userId);
    }

    private async Task<IReadOnlyList<CompanyMembershipOption>> GetEligibleCompaniesForUserAsync(Guid userId)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        var eligibleCompanies =
            from membership in dbContext.CompanyMemberships.AsNoTracking()
            join tenantMembership in dbContext.TenantMemberships.AsNoTracking()
                on new { membership.TenantId, membership.UserId }
                equals new { tenantMembership.TenantId, tenantMembership.UserId }
            join company in dbContext.Companies.AsNoTracking()
                on new { membership.TenantId, CompanyId = membership.CompanyId }
                equals new { company.TenantId, CompanyId = company.Id }
            where membership.UserId == userId && company.IsActive
            orderby company.Name
            select new CompanyMembershipOption(
                company.TenantId,
                company.Id,
                company.Code,
                company.Name,
                membership.RoleName);

        return await eligibleCompanies.ToListAsync();
    }

    public async Task<CompanyMembershipOption?> ResolveAsync(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(user.FindFirstValue(ActiveCompanyClaim.Type), out var companyId))
        {
            return null;
        }

        var eligibleCompanies = await GetEligibleCompaniesForUserAsync(userId);
        return eligibleCompanies.SingleOrDefault(company => company.CompanyId == companyId);
    }
}
