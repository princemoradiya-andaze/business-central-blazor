using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

internal sealed class CompanyAuthorizationTestApiStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var segments = context.Request.Path.Value?
                    .Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
                if (segments.Length != 5 ||
                    segments[0] != "__test-api" ||
                    segments[1] != "company" ||
                    segments[4] != "name")
                {
                    await nextMiddleware();
                    return;
                }

                if (!Guid.TryParse(segments[2], out var tenantId) ||
                    !Guid.TryParse(segments[3], out var companyId))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                if (context.Request.Method != HttpMethods.Get &&
                    context.Request.Method != HttpMethods.Put)
                {
                    context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    return;
                }

                var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
                if (!authentication.Succeeded || authentication.Principal is null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                context.User = authentication.Principal;
                var scopeAuthorizer = context.RequestServices.GetRequiredService<ICompanyScopeAuthorizer>();
                var authorizedCompany = await scopeAuthorizer.ResolveAuthorizedCompanyAsync(
                    context.User,
                    tenantId,
                    companyId);
                if (authorizedCompany is null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                var dbContext = context.RequestServices.GetRequiredService<ErpDbContext>();
                var company = await dbContext.Companies.SingleOrDefaultAsync(item =>
                    item.TenantId == authorizedCompany.TenantId &&
                    item.Id == authorizedCompany.CompanyId);
                if (company is null)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (context.Request.Method == HttpMethods.Get)
                {
                    await context.Response.WriteAsJsonAsync(new CompanyNameResponse(company.Name));
                    return;
                }

                var request = await context.Request.ReadFromJsonAsync<UpdateCompanyNameRequest>();
                if (string.IsNullOrWhiteSpace(request?.Name))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                company.Name = request.Name.Trim();
                await dbContext.SaveChangesAsync();
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });

            next(app);
        };

    private sealed record CompanyNameResponse(string Name);

    private sealed record UpdateCompanyNameRequest(string Name);
}
