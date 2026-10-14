using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class HomeDashboardPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task HomePageRequiresSignInAndShowsCompanyDashboard()
    {
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        var email = $"home-dashboard-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "HOMEDASH", "Home Dashboard Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Authentication test user", html, StringComparison.Ordinal);
        Assert.Matches("Good (morning|afternoon|evening), Authentication test user", html);
        Assert.Contains("Company overview", html, StringComparison.Ordinal);
        Assert.Contains("id=\"home-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("Actions", html, StringComparison.Ordinal);
        Assert.Contains("Sales order", html, StringComparison.Ordinal);
        Assert.Contains("Purchase order", html, StringComparison.Ordinal);
        Assert.Contains("New journal", html, StringComparison.Ordinal);
        Assert.Contains("Find entries", html, StringComparison.Ordinal);
        Assert.Contains("Trial balance", html, StringComparison.Ordinal);
        Assert.Contains("Business overview", html, StringComparison.Ordinal);
        Assert.Contains("Customers", html, StringComparison.Ordinal);
        Assert.Contains("Vendors", html, StringComparison.Ordinal);
        Assert.Contains("Open sales orders", html, StringComparison.Ordinal);
        Assert.Contains("Open purchase orders", html, StringComparison.Ordinal);
        Assert.Contains("Inventory items", html, StringComparison.Ordinal);
        Assert.Contains("Chart of accounts", html, StringComparison.Ordinal);
        Assert.Contains("Open receivables", html, StringComparison.Ordinal);
        Assert.Contains("Open vendor payables", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales/customers\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales/orders\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/general-ledger/accounts\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales/create-order\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales/customers/new\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing#purchase-order-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing#vendors-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/journals/new\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/general-ledger/entries\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/general-ledger/trial-balance\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/navigation\"", html, StringComparison.Ordinal);
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
                ["Input.Password"] = Password,
                ["Input.RememberMe"] = "false"
            }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task SelectCompanyAsync(HttpClient client, Guid companyId)
    {
        var companyPage = await client.GetAsync("/account/company");
        var token = GetAntiforgeryToken(await companyPage.Content.ReadAsStringAsync());
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
