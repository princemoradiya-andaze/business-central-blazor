using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class CompanyAuthorizationTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task SelectedCompanyCanBeReadAndUpdatedThroughDirectApiCalls()
    {
        var email = $"company-api-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "ALLOW", "Allowed Company");
        using var client = CreateClient();
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var read = await client.GetAsync(CompanyNamePath(company.TenantId, company.CompanyId));
        var readResult = await read.Content.ReadFromJsonAsync<CompanyNameResponse>();
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Allowed Company", readResult?.Name);

        using var write = await client.PutAsJsonAsync(
            CompanyNamePath(company.TenantId, company.CompanyId),
            new UpdateCompanyNameRequest("Updated Company"));
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var updatedCompany = await dbContext.Companies.SingleAsync(item => item.Id == company.CompanyId);
        Assert.Equal("Updated Company", updatedCompany.Name);

        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
    }

    [Fact]
    public async Task DirectReadAndWriteCallsCannotAccessAnotherCompanyOrTenant()
    {
        var email = $"company-api-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"company-api-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownCompany = await factory.AddCompanyMembershipAsync(email, "OWN", "Own Company");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "OTHER", "Private Company");
        using var client = CreateClient();
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, ownCompany.CompanyId);

        using var crossCompanyRead = await client.GetAsync(
            CompanyNamePath(otherCompany.TenantId, otherCompany.CompanyId));
        using var crossCompanyWrite = await client.PutAsJsonAsync(
            CompanyNamePath(otherCompany.TenantId, otherCompany.CompanyId),
            new UpdateCompanyNameRequest("Tampered Company"));
        using var crossTenantRead = await client.GetAsync(
            CompanyNamePath(otherCompany.TenantId, ownCompany.CompanyId));
        using var crossTenantWrite = await client.PutAsJsonAsync(
            CompanyNamePath(otherCompany.TenantId, ownCompany.CompanyId),
            new UpdateCompanyNameRequest("Wrong Tenant"));

        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantWrite.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        Assert.Equal(
            "Own Company",
            (await dbContext.Companies.SingleAsync(item => item.Id == ownCompany.CompanyId)).Name);
        Assert.Equal(
            "Private Company",
            (await dbContext.Companies.SingleAsync(item => item.Id == otherCompany.CompanyId)).Name);
    }

    [Fact]
    public async Task ActiveCompanyPolicyDeniesMissingAndRevokedContextButKeepsCompanySelectionAvailable()
    {
        var email = $"company-policy-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        using var client = CreateClient();
        await SignInAsync(client, email);

        using var homeWithoutMembership = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, homeWithoutMembership.StatusCode);
        Assert.Equal("/account/access-denied", homeWithoutMembership.Headers.Location?.AbsolutePath);
        var accessDeniedLocation = Assert.IsType<Uri>(homeWithoutMembership.Headers.Location);
        using var accessDenied = await client.GetAsync(accessDeniedLocation);
        Assert.Equal(HttpStatusCode.OK, accessDenied.StatusCode);
        var accessDeniedHtml = await accessDenied.Content.ReadAsStringAsync();
        Assert.Contains("/account/company", accessDeniedHtml, StringComparison.Ordinal);
        Assert.Contains("account-card", accessDeniedHtml, StringComparison.Ordinal);
        Assert.Contains("account.css", accessDeniedHtml, StringComparison.Ordinal);
        using var companySelection = await client.GetAsync("/account/company");
        Assert.Equal(HttpStatusCode.OK, companySelection.StatusCode);

        var company = await factory.AddCompanyMembershipAsync(email, "REVOKE", "Revoked Company");
        await SelectCompanyAsync(client, company.CompanyId);
        using var authorizedHome = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, authorizedHome.StatusCode);
        var authorizedHomeHtml = await authorizedHome.Content.ReadAsStringAsync();
        Assert.Contains("Manage your company's accounts, dimensions, journals, ledger activity, tax setup, and banking",
            authorizedHomeHtml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Business modules are not available yet", authorizedHomeHtml, StringComparison.Ordinal);

        await factory.RemoveCompanyMembershipAsync(await GetUserIdAsync(email), company.CompanyId);

        using var homeAfterRevocation = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, homeAfterRevocation.StatusCode);
        Assert.Equal("/account/access-denied", homeAfterRevocation.Headers.Location?.AbsolutePath);
        using var staleCompanyRead = await client.GetAsync(
            CompanyNamePath(company.TenantId, company.CompanyId));
        Assert.Equal(HttpStatusCode.Forbidden, staleCompanyRead.StatusCode);
    }

    [Fact]
    public async Task CompanyScopeAuthorizerRequiresAnAuthenticatedUserAndExactSelectedTenantAndCompany()
    {
        var email = $"company-authorizer-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "SCOPE", "Scoped Company");
        var userId = await GetUserIdAsync(email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
        ], "test"));

        await using var scope = factory.Services.CreateAsyncScope();
        var authorizer = scope.ServiceProvider.GetRequiredService<ICompanyScopeAuthorizer>();

        var authorized = await authorizer.ResolveAuthorizedCompanyAsync(
            principal,
            company.TenantId,
            company.CompanyId);
        var wrongCompany = await authorizer.ResolveAuthorizedCompanyAsync(
            principal,
            company.TenantId,
            Guid.NewGuid());
        var wrongTenant = await authorizer.ResolveAuthorizedCompanyAsync(
            principal,
            Guid.NewGuid(),
            company.CompanyId);
        var anonymous = await authorizer.ResolveAuthorizedCompanyAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            company.TenantId,
            company.CompanyId);

        Assert.Equal(company.CompanyId, authorized?.CompanyId);
        Assert.Null(wrongCompany);
        Assert.Null(wrongTenant);
        Assert.Null(anonymous);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string CompanyNamePath(Guid tenantId, Guid companyId) =>
        $"/__test-api/company/{tenantId}/{companyId}/name";

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var login = await client.GetAsync("/account/login");
        var token = GetAntiforgeryToken(await login.Content.ReadAsStringAsync());
        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ReturnUrl"] = "/",
                ["Input.Email"] = email,
                ["Input.Password"] = TestPassword,
                ["Input.RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task SelectCompanyAsync(HttpClient client, Guid companyId)
    {
        var page = await client.GetAsync("/account/company");
        var token = GetAntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var response = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = companyId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    private static string GetAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Expected an antiforgery token in the rendered form.");
        return HttpUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record CompanyNameResponse(string Name);

    private sealed record UpdateCompanyNameRequest(string Name);
}
