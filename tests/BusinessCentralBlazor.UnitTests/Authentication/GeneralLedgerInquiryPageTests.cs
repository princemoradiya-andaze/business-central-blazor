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

public sealed class GeneralLedgerInquiryPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ActiveCompanyMembersCanOpenLedgerInquiryPages()
    {
        var email = $"gl-inquiry-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "GLINQUIRY", "Inquiry Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accountService = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
            accountId = await accountService.CreateAsync(principal, new GeneralLedgerAccountInput
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

        using var entriesResponse = await client.GetAsync("/financial/general-ledger/entries");
        var entriesHtml = await entriesResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, entriesResponse.StatusCode);
        Assert.Contains("General Ledger Entries", entriesHtml, StringComparison.Ordinal);
        Assert.Contains("No posted entries match these filters.", entriesHtml, StringComparison.Ordinal);

        using var activityResponse = await client.GetAsync(
            $"/financial/general-ledger/accounts/{accountId}/activity");
        var activityHtml = await activityResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activityResponse.StatusCode);
        Assert.Contains("1000 — Cash", HttpUtility.HtmlDecode(activityHtml), StringComparison.Ordinal);
        Assert.Contains("Opening debit", activityHtml, StringComparison.Ordinal);
        Assert.Contains("financial-detail", activityHtml, StringComparison.Ordinal);
        Assert.Contains("Back to General Ledger Entries", activityHtml, StringComparison.Ordinal);
        Assert.Contains("account-activity-filter", activityHtml, StringComparison.Ordinal);
        Assert.Contains("Posted entries", activityHtml, StringComparison.Ordinal);

        using var trialBalanceResponse = await client.GetAsync("/financial/general-ledger/trial-balance");
        var trialBalanceHtml = await trialBalanceResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, trialBalanceResponse.StatusCode);
        Assert.Contains("Trial Balance", trialBalanceHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"trial-request-heading\"", trialBalanceHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("trial-preview-heading", trialBalanceHtml, StringComparison.Ordinal);
        Assert.Contains("Cash", trialBalanceHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"home-heading\"", trialBalanceHtml, StringComparison.Ordinal);
        Assert.Contains("report-size-icon", trialBalanceHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("run-trial-balance", trialBalanceHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousUsersCannotOpenLedgerInquiryPages()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var route in new[]
                 {
                     "/financial/general-ledger/entries",
                     "/financial/general-ledger/trial-balance"
                 })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
        }
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
