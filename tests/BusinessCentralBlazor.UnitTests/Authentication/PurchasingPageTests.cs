using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class PurchasingPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task PurchasingPageRequiresSignInAndRendersForSelectedCompany()
    {
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/financial/purchasing");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        var email = $"purchasing-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email, $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(), "Purchasing Page Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);

        using var response = await client.GetAsync("/financial/purchasing");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Purchasing and payables", html, StringComparison.Ordinal);
        Assert.Contains("Goods received not invoiced (GRNI) account", html, StringComparison.Ordinal);
        Assert.Contains("Create purchase order", html, StringComparison.Ordinal);
        Assert.Contains("Receive and invoice orders", html, StringComparison.Ordinal);
        Assert.Contains("Open vendor payables", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/vendors\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("FormName=\"purchase-vendor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"purchase-vendor-number\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/financial/purchasing/vendors")]
    [InlineData("/financial/purchasing/vendors/new")]
    [InlineData("/financial/purchasing/vendors/8711e20c-e555-416c-a631-dd7ff7b7799a/edit")]
    public async Task VendorRoutesRequireSignIn(string route)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task VendorWorkspaceRendersSupportedViewsFormsAndOnlyActiveCompanyRecords()
    {
        var email = $"vendors-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email, $"V{Guid.NewGuid():N}"[..8].ToUpperInvariant(), "Vendors Page Company");
        var foreignMembership = await factory.AddCompanyMembershipAsync(
            email, $"V{Guid.NewGuid():N}"[..8].ToUpperInvariant(), "Foreign Vendors Company");
        var localId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.GeneralLedgerAccounts.Add(new GeneralLedgerAccount
            {
                Id = accountId, CompanyId = membership.CompanyId, Number = "2000", Name = "Payables",
                Category = AccountCategory.Liability, Type = AccountType.Posting, AllowsDirectPosting = true
            });
            db.Vendors.AddRange(
                new Vendor
                {
                    Id = localId, CompanyId = membership.CompanyId, Number = "V-LOCAL",
                    Name = "Local vendor", PayablesAccountId = accountId, IsBlocked = true
                },
                new Vendor { Id = foreignId, CompanyId = foreignMembership.CompanyId, Number = "V-FOREIGN", Name = "Foreign vendor" });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);
        using var response = await client.GetAsync("/financial/purchasing/vendors");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var expected in new[]
        {
            "aria-label=\"Vendors\"", "id=\"vendor-search\"", "id=\"vendor-status\"",
            "id=\"vendor-filter-toggle\"", "id=\"vendor-filter-pane\"", "id=\"vendor-details-toggle\"",
            "id=\"vendor-details-pane\"", "Tall tiles", ">Tiles</span>", ">List</span>",
            ">+ New</a>", "Block", "V-LOCAL", "Local vendor", "2000", "Blocked",
            "class=\"vendor-action-bar\"", "class=\"vendor-command-separator\"",
            "class=\"vendor-search-toggle\"", "class=\"vendor-search-input \"",
            "class=\"vendor-view-filter\"", "id=\"vendor-status-options\"",
            "id=\"vendor-view-toggle\"", "id=\"vendor-view-menu\"",
            "role=\"menuitemradio\"", "aria-checked=\"true\"", "aria-controls=\"vendor-search\""
        })
        {
            Assert.Contains(expected, html, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("V-FOREIGN", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</button>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"page-command-bar\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"purchase-order-heading\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("module-menu-toggle active", html, StringComparison.Ordinal);
        Assert.DoesNotContain("vendor-command-select", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"vendor-number\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"vendor-search\"[^>]*disabled[^>]*tabindex=\"-1\"[^>]*aria-hidden=\"true\"", html);
        Assert.Matches("id=\"vendor-status-options\"[^>]*hidden", html);
        Assert.Matches("id=\"vendor-view-menu\"[^>]*hidden", html);
        Assert.Matches("id=\"vendor-filter-pane\"[^>]*hidden", html);
        Assert.Matches("id=\"vendor-details-pane\"[^>]*hidden", html);

        using var newResponse = await client.GetAsync("/financial/purchasing/vendors/new");
        var newHtml = await newResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, newResponse.StatusCode);
        Assert.Contains("New Vendor Card", newHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-number\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-name\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-payables\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("<dialog", newHtml, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"entity-card-heading\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("Vendor Card", newHtml, StringComparison.Ordinal);
        Assert.Contains("Close record card", newHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-card-commands\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"vendor-card-fields\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-card-footer\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Vendors\"", newHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"page-command-bar\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"32\"", newHtml, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"160\"", newHtml, StringComparison.Ordinal);

        using var editResponse = await client.GetAsync($"/financial/purchasing/vendors/{localId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Saved", editHtml, StringComparison.Ordinal);
        Assert.Contains("value=\"V-LOCAL\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("value=\"Local vendor\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"vendor-action-bar\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("workspace-card-dialog entity-dialog", editHtml, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"entity-card-heading\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"entity-card-heading\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"vendor-card-fields\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-card-commands\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("class=\"entity-card-footer\"", editHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("type=\"submit\"", Regex.Match(editHtml, "<dialog\\b.*?</dialog>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
        Assert.Contains("Previous record", editHtml, StringComparison.Ordinal);
        Assert.Contains("Next record", editHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-number\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-name\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"vendor-payables\"", editHtml, StringComparison.Ordinal);
        Assert.Contains("Close record card", editHtml, StringComparison.Ordinal);
        Assert.Contains("Reactivate", editHtml, StringComparison.Ordinal);

        foreach (var unavailableId in new[] { foreignId, Guid.NewGuid() })
        {
            using var unavailable = await client.GetAsync($"/financial/purchasing/vendors/{unavailableId}/edit");
            var unavailableHtml = await unavailable.Content.ReadAsStringAsync();
            Assert.Contains("The vendor is not available in the active company.", unavailableHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("id=\"vendor-number\"", unavailableHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"customer-card-dialog\"", unavailableHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("V-FOREIGN", unavailableHtml, StringComparison.Ordinal);
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
