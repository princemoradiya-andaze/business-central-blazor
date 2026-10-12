using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class GeneralLedgerAccountPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ActiveCompanyMembersCanViewChartAndAccountForms()
    {
        var email = $"gl-account-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "GLPAGE", "G/L Page Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);

        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
            accountId = await service.CreateAsync(principal, new GeneralLedgerAccountInput
            {
                Number = "1000",
                Name = "Cash",
                Category = AccountCategory.Asset,
                Type = AccountType.Posting
            });
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var listResponse = await client.GetAsync("/financial/general-ledger/accounts");
        var listHtml = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("Chart of Accounts", listHtml, StringComparison.Ordinal);
        Assert.Contains("1000", listHtml, StringComparison.Ordinal);
        Assert.Contains("Cash", listHtml, StringComparison.Ordinal);
        Assert.Contains("financial/general-ledger/accounts/new", listHtml, StringComparison.Ordinal);
        Assert.Contains("Company: G/L Page Company (GLPAGE)", listHtml, StringComparison.Ordinal);

        using var newResponse = await client.GetAsync("/financial/general-ledger/accounts/new");
        var newHtml = await newResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, newResponse.StatusCode);
        Assert.Contains("New account", newHtml, StringComparison.Ordinal);
        Assert.Contains("account-number", newHtml, StringComparison.Ordinal);
        Assert.Contains("Allow direct posting", newHtml, StringComparison.Ordinal);
        Assert.Contains("Blocked", newHtml, StringComparison.Ordinal);

        using var editResponse = await client.GetAsync($"/financial/general-ledger/accounts/{accountId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Save changes", editHtml, StringComparison.Ordinal);
        Assert.Contains("value=\"1000\"", editHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousAndCrossCompanyRequestsDoNotRevealAccountDetails()
    {
        var ownerEmail = $"gl-page-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"gl-page-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(ownerEmail, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownerCompany = await factory.AddCompanyMembershipAsync(ownerEmail, "GLPAGEOWN", "G/L Page Owner");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "GLPAGEOTH", "G/L Page Other");
        var otherPrincipal = await CreatePrincipalAsync(otherEmail, otherCompany.CompanyId);

        Guid privateAccountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
            privateAccountId = await service.CreateAsync(otherPrincipal, new GeneralLedgerAccountInput
            {
                Number = "9999",
                Name = "Private account name",
                Category = AccountCategory.Asset,
                Type = AccountType.Posting
            });
        }

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/financial/general-ledger/accounts");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(ownerClient, ownerEmail);
        await SelectCompanyAsync(ownerClient, ownerCompany.CompanyId);

        using var listResponse = await ownerClient.GetAsync("/financial/general-ledger/accounts");
        var listHtml = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.DoesNotContain("Private account name", listHtml, StringComparison.Ordinal);

        using var editResponse = await ownerClient.GetAsync(
            $"/financial/general-ledger/accounts/{privateAccountId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Account not found", editHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private account name", editHtml, StringComparison.Ordinal);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
    }

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
