using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class InventoryPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task InventoryPageRequiresSignInAndRendersForSelectedCompany()
    {
        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        foreach (var route in new[] { "/financial/inventory", "/financial/inventory/movements", "/financial/inventory/movements?new=movement" })
        {
            using var anonymousResponse = await anonymousClient.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
            Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);
        }

        var email = $"inventory-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(
            email,
            $"P{Guid.NewGuid():N}"[..8].ToUpperInvariant(),
            "Inventory Page Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);

        using var response = await client.GetAsync("/financial/inventory");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Items", html, StringComparison.Ordinal);
        Assert.Contains("id=\"new-item-action\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"item-status-quick\"", html, StringComparison.Ordinal);
        Assert.Contains("Choose view", html, StringComparison.Ordinal);
        Assert.Contains("Details", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"movement-heading\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"movements-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/inventory/movements\"", html, StringComparison.Ordinal);
        Assert.Contains(">Stock Movements</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Movement history</a>", html, StringComparison.Ordinal);
        Assert.Contains("Inventory", html, StringComparison.Ordinal);

        using var movementsResponse = await client.GetAsync("/financial/inventory/movements");
        var movementsHtml = await movementsResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, movementsResponse.StatusCode);
        Assert.Contains("id=\"movements-heading\"", movementsHtml, StringComparison.Ordinal);
        Assert.Contains("Stock Movements", movementsHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"stock-movements-status\"", movementsHtml, StringComparison.Ordinal);
        Assert.Contains("Choose view", movementsHtml, StringComparison.Ordinal);
        Assert.Contains("No movements match", movementsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"inventory-movement\"", movementsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"new-item-action\"", movementsHtml, StringComparison.Ordinal);
        using var card = await client.GetAsync("/financial/inventory/movements?new=movement");
        var cardHtml = await card.Content.ReadAsStringAsync();
        Assert.Contains("New Stock Movement", cardHtml, StringComparison.Ordinal);
        Assert.Contains("Create or unblock an inventory item", cardHtml, StringComparison.Ordinal);
        Assert.Contains("Create an open, unlocked accounting period", cardHtml, StringComparison.Ordinal);
        Assert.Matches("<fieldset\\b[^>]*\\bdisabled\\b", cardHtml);
    }

    [Fact]
    public async Task MovementWorkflowShowsOnlyActiveCompanyItemsAndHistory()
    {
        var email = $"inventory-movements-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "MOVEPAGE", "Movement Page Company");
        var otherCompany = await factory.AddCompanyMembershipAsync(email, "MOVEOTHER", "Other Company");
        var itemId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.AddRange(
                new Item { Id = itemId, CompanyId = company.CompanyId, Type = ItemType.Inventory, Number = "VISIBLE-ITEM", Description = "Visible movement item", BaseUnitOfMeasure = "EA", InventoryAccountId = Guid.NewGuid(), InventoryIncreaseOffsetAccountId = Guid.NewGuid(), InventoryIssueExpenseAccountId = Guid.NewGuid() },
                new Item { Id = Guid.NewGuid(), CompanyId = company.CompanyId, Type = ItemType.Inventory, Number = "BLOCKED-ITEM", Description = "Blocked movement item", IsBlocked = true, InventoryAccountId = Guid.NewGuid(), InventoryIncreaseOffsetAccountId = Guid.NewGuid(), InventoryIssueExpenseAccountId = Guid.NewGuid() },
                new Item { Id = Guid.NewGuid(), CompanyId = otherCompany.CompanyId, Type = ItemType.Inventory, Number = "PRIVATE-ITEM", Description = "Private movement item" },
                new AccountingPeriod { Id = Guid.NewGuid(), CompanyId = company.CompanyId, Name = "Open period", StartsOn = new(2026, 1, 1), EndsOn = new(2026, 12, 31) },
                new GeneralJournalBatch { Id = journalId, CompanyId = company.CompanyId },
                new InventoryMovement { Id = Guid.NewGuid(), CompanyId = company.CompanyId, ItemId = itemId, DocumentNumber = "VISIBLE-MOVEMENT", JournalBatchId = journalId },
                new InventoryMovement { Id = Guid.NewGuid(), CompanyId = otherCompany.CompanyId, ItemId = itemId, DocumentNumber = "PRIVATE-MOVEMENT" });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);
        using var response = await client.GetAsync("/financial/inventory/movements");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("id=\"movement-item\"", html, StringComparison.Ordinal);
        Assert.Contains("record-row-selected", html, StringComparison.Ordinal);
        Assert.Contains("VISIBLE-MOVEMENT", html, StringComparison.Ordinal);
        Assert.Contains($"/financial/journals/{journalId}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("BLOCKED-ITEM", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-ITEM", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-MOVEMENT", html, StringComparison.Ordinal);
        using var card = await client.GetAsync("/financial/inventory/movements?new=movement");
        var cardHtml = await card.Content.ReadAsStringAsync();
        Assert.Contains("id=\"movement-item\"", cardHtml, StringComparison.Ordinal);
        Assert.Contains("Visible movement item", cardHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"entity-card-heading\"", cardHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"inventory-movement\"", cardHtml, StringComparison.Ordinal);
        Assert.Contains("Create draft journal", cardHtml, StringComparison.Ordinal);
        Assert.Contains(">Cancel</button>", cardHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Previous record", cardHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("BLOCKED-ITEM", cardHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE-ITEM", cardHtml, StringComparison.Ordinal);
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
