using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class SalesPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task SalesPageRequiresSignInAndRendersForSelectedCompany()
    {
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/financial/sales");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        var email = $"sales-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email,
            $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            "Sales Page Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);

        using var response = await client.GetAsync("/financial/sales");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Customers", html, StringComparison.Ordinal);
        Assert.Contains("class=\"customer-action-bar\"", html, StringComparison.Ordinal);
        Assert.Contains(">+ New</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</button>", html, StringComparison.Ordinal);
        Assert.Contains("Delete", html, StringComparison.Ordinal);
        Assert.Contains("Search customers", html, StringComparison.Ordinal);
        Assert.Contains("customer-status", html, StringComparison.Ordinal);
        Assert.Contains("customer-view-filter", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-filter-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-filter-pane\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-filter-search\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-view-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("Tall tiles", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-details-toggle\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-details-pane\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", html, StringComparison.Ordinal);
        Assert.Contains("aria-hidden", html, StringComparison.Ordinal);
        Assert.Contains("class=\"customer-command-separator\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("customer-list-toolbar", html, StringComparison.Ordinal);
        Assert.Contains("Inactive", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"breadcrumb\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"page-command-bar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"card", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sales-customer-number\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"sales-order-heading\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"receivables-heading\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("module-menu-toggle active", html, StringComparison.Ordinal);

        var routes = new Dictionary<string, (string expected, string excluded)>
        {
            ["/financial/sales/customers"] = ("aria-label=\"Customers\"", "id=\"sales-order-heading\""),
            ["/financial/sales/customers/new"] = ("id=\"entity-card-heading\"", "class=\"page-command-bar\""),
            ["/financial/sales/create-order"] = ("id=\"sales-order-heading\"", "id=\"receivables-heading\""),
            ["/financial/sales/orders"] = ("id=\"orders-heading\"", "id=\"new-customer-heading\""),
            ["/financial/sales/fulfillment"] = ("id=\"fulfillment-heading\"", "id=\"receivables-heading\""),
            ["/financial/sales/receivables"] = ("id=\"receivables-heading\"", "id=\"new-customer-heading\"")
        };

        foreach (var (route, content) in routes)
        {
            using var sectionResponse = await client.GetAsync(route);
            var sectionHtml = await sectionResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, sectionResponse.StatusCode);
            Assert.Contains(content.expected, sectionHtml, StringComparison.Ordinal);
            Assert.DoesNotContain(content.excluded, sectionHtml, StringComparison.Ordinal);
            if (route.StartsWith("/financial/sales/customers", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("module-menu-toggle active", sectionHtml, StringComparison.Ordinal);
            }
            if (route.EndsWith("/new", StringComparison.Ordinal))
            {
                Assert.Contains("<dialog", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("Customer Card", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("aria-label=\"Customers\"", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("id=\"sales-customer-number\"", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("id=\"sales-customer-name\"", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("id=\"sales-customer-ar\"", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("Close record card", sectionHtml, StringComparison.Ordinal);
            }
        }
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
