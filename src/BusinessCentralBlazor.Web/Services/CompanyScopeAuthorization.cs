using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace BusinessCentralBlazor.Web.Services;

public static class CompanyAuthorizationPolicies
{
    public const string ActiveCompany = "ActiveCompany";
}

public sealed class ActiveCompanyRequirement : IAuthorizationRequirement;

public sealed class ActiveCompanyAuthorizationHandler(IActiveCompanyContext activeCompanyContext)
    : AuthorizationHandler<ActiveCompanyRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveCompanyRequirement requirement)
    {
        if (await activeCompanyContext.ResolveAsync(context.User) is not null)
        {
            context.Succeed(requirement);
        }
    }
}

public interface ICompanyScopeAuthorizer
{
    Task<CompanyMembershipOption?> ResolveActiveCompanyAsync(ClaimsPrincipal user);

    Task<CompanyMembershipOption?> ResolveAuthorizedCompanyAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid companyId);
}

public sealed class CompanyScopeAuthorizer(IActiveCompanyContext activeCompanyContext)
    : ICompanyScopeAuthorizer
{
    public Task<CompanyMembershipOption?> ResolveActiveCompanyAsync(ClaimsPrincipal user) =>
        activeCompanyContext.ResolveAsync(user);

    public async Task<CompanyMembershipOption?> ResolveAuthorizedCompanyAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        Guid companyId)
    {
        var activeCompany = await activeCompanyContext.ResolveAsync(user);
        return activeCompany is not null &&
            activeCompany.TenantId == tenantId &&
            activeCompany.CompanyId == companyId
                ? activeCompany
                : null;
    }
}
