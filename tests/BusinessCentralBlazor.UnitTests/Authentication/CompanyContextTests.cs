using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class CompanyContextTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task AnonymousUserCannotOpenCompanySelection()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/account/company");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task SignedInUserCanSeeAndSelectOnlyEligibleCompanies()
    {
        var email = $"multiple-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var firstCompany = await factory.AddCompanyMembershipAsync(email, "ALPHA", "Alpha Company");
        var secondCompany = await factory.AddCompanyMembershipAsync(email, "BRAVO", "Bravo Company");
        await factory.AddCompanyMembershipAsync(email, "INACTIVE", "Inactive Company", companyIsActive: false);
        using var client = CreateClient();
        await SignInAsync(client, email);

        var page = await client.GetAsync("/account/company");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(firstCompany.Name, html, StringComparison.Ordinal);
        Assert.Contains(secondCompany.Name, html, StringComparison.Ordinal);
        Assert.DoesNotContain("Inactive Company", html, StringComparison.Ordinal);

        var token = GetAntiforgeryToken(html);
        using var response = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = firstCompany.CompanyId.ToString()
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var home = await client.GetAsync("/");
        Assert.Contains("Company: Alpha Company (ALPHA)", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var serviceScope = factory.Services.CreateScope();
        var context = serviceScope.ServiceProvider.GetRequiredService<IActiveCompanyContext>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, (await GetUserIdAsync(email)).ToString()),
            new Claim(ActiveCompanyClaim.Type, secondCompany.CompanyId.ToString())
        ], "test"));
        var resolved = await context.ResolveAsync(user);

        Assert.Equal(secondCompany.CompanyId, resolved?.CompanyId);
    }

    [Fact]
    public async Task ConcurrentCompanyContextLookupsUseIndependentDbContexts()
    {
        var email = $"concurrent-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "CONCURRENT", "Concurrent Company");
        var userId = await GetUserIdAsync(email);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
        ], "test"));
        using var serviceScope = factory.Services.CreateScope();
        var context = serviceScope.ServiceProvider.GetRequiredService<IActiveCompanyContext>();

        var eligibleCompaniesTask = context.GetEligibleCompaniesAsync(user);
        var resolvedCompanyTask = context.ResolveAsync(user);
        await Task.WhenAll(eligibleCompaniesTask, resolvedCompanyTask);

        Assert.Single(await eligibleCompaniesTask);
        Assert.Equal(company.CompanyId, (await resolvedCompanyTask)?.CompanyId);
    }

    [Fact]
    public async Task UserWithoutMembershipSeesSafeNoCompanyState()
    {
        var email = $"no-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        using var client = CreateClient();
        await SignInAsync(client, email);

        var response = await client.GetAsync("/account/company");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No active company memberships are assigned", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Set active company", html, StringComparison.Ordinal);
        Assert.Contains("class=\"signin-card\"", html, StringComparison.Ordinal);
        Assert.Contains("signin.css", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserCannotSelectCompanyWithoutMembership()
    {
        var email = $"unauthorized-company-{Guid.NewGuid():N}@example.invalid";
        var otherUserEmail = $"other-company-owner-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        await factory.CreateTestUserAsync(otherUserEmail, TestPassword);
        await factory.AddCompanyMembershipAsync(email, "OWNED", "Owned Company");
        var otherUsersCompany = await factory.AddCompanyMembershipAsync(
            otherUserEmail,
            "PRIVATE",
            "Private Company");
        using var client = CreateClient();
        await SignInAsync(client, email);

        var page = await client.GetAsync("/account/company");
        var token = GetAntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var response = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = otherUsersCompany.CompanyId.ToString()
            }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("That company is not available to your account.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Current company:", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokedMembershipInvalidatesPreviouslySelectedCompany()
    {
        var email = $"revoked-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "REVOKE", "Revoked Company");
        using var client = CreateClient();
        await SignInAsync(client, email);

        var page = await client.GetAsync("/account/company");
        var token = GetAntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var selection = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = company.CompanyId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, selection.StatusCode);

        await factory.RemoveCompanyMembershipAsync(await GetUserIdAsync(email), company.CompanyId);

        var response = await client.GetAsync("/account/company");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your previous company selection is no longer valid.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Current company:", html, StringComparison.Ordinal);

        using var serviceScope = factory.Services.CreateScope();
        var context = serviceScope.ServiceProvider.GetRequiredService<IActiveCompanyContext>();
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, (await GetUserIdAsync(email)).ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())
        ], "test"));
        Assert.Null(await context.ResolveAsync(user));
    }

    [Fact]
    public async Task InactiveCompanyInvalidatesPreviouslySelectedCompany()
    {
        var email = $"inactive-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "DISABLE", "Disabled Company");
        using var client = CreateClient();
        await SignInAsync(client, email);

        var page = await client.GetAsync("/account/company");
        var token = GetAntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var selection = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = company.CompanyId.ToString()
            }));
        Assert.Equal(HttpStatusCode.Redirect, selection.StatusCode);

        await factory.SetCompanyActiveAsync(company.CompanyId, isActive: false);

        var response = await client.GetAsync("/account/company");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your previous company selection is no longer valid.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Current company:", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Disabled Company", html, StringComparison.Ordinal);
    }

    private HttpClient CreateClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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

    private async Task<Guid> GetUserIdAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
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
}
