using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class EntityRecordPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Theory]
    [InlineData("/financial/sales/customers", "customer-row-selected", "sales-customer-number", "Customer Card")]
    [InlineData("/financial/purchasing/vendors", "vendor-row-selected", "vendor-number", "Vendor Card")]
    [InlineData("/financial/inventory", "record-row-selected", "item-number", "Item Card")]
    [InlineData("/financial/banks", "record-row-selected", "bank-code", "Bank Account Card")]
    [InlineData("/financial/general-ledger/accounts", "record-row-selected", "account-number", "G/L Account Card")]
    [InlineData("/financial/tax-setup", "record-row-selected", "tax-code", "Tax Code Card")]
    public async Task AllListsSelectFirstAndLinkToSharedEditableCards(string route, string selectedClass,
        string fieldId, string cardType)
    {
        var email = $"entity-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "CARDS", "Entity Card Tests");
        var firstId = Guid.NewGuid();
        var lastId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var postingId = Guid.NewGuid();
            var liabilityId = Guid.NewGuid();
            if (route.EndsWith("tax-setup", StringComparison.Ordinal))
            {
                db.Add(new GeneralLedgerAccount { Id = postingId, CompanyId = company.CompanyId, Number = "9000", Name = "Tax recoverable", Category = AccountCategory.Asset, Type = AccountType.Posting, AllowsDirectPosting = true });
                db.Add(new GeneralLedgerAccount { Id = liabilityId, CompanyId = company.CompanyId, Number = "9100", Name = "Tax payable", Category = AccountCategory.Liability, Type = AccountType.Posting, AllowsDirectPosting = true });
            }
            if (route.EndsWith("customers", StringComparison.Ordinal) || route.EndsWith("inventory", StringComparison.Ordinal))
                db.Add(new GeneralLedgerAccount { Id = postingId, CompanyId = company.CompanyId, Number = "9000", Name = "Posting account", Category = AccountCategory.Asset, Type = AccountType.Posting, AllowsDirectPosting = true });
            foreach (var (id, number, name) in new[] { (lastId, "2000", "Second record"), (firstId, "1000", "First record") })
            {
                if (route.EndsWith("customers", StringComparison.Ordinal))
                    db.Add(new Customer { Id = id, CompanyId = company.CompanyId, Number = number, Name = name, ReceivablesAccountId = postingId });
                else if (route.EndsWith("vendors", StringComparison.Ordinal))
                    db.Add(new Vendor { Id = id, CompanyId = company.CompanyId, Number = number, Name = name });
                else if (route.EndsWith("inventory", StringComparison.Ordinal))
                    db.Add(new Item { Id = id, CompanyId = company.CompanyId, Number = number, Description = name, Type = ItemType.Inventory, BaseUnitOfMeasure = "EA", InventoryAccountId = postingId, InventoryIncreaseOffsetAccountId = postingId, InventoryIssueExpenseAccountId = postingId });
                else if (route.EndsWith("banks", StringComparison.Ordinal))
                    db.Add(new BankAccount { Id = id, CompanyId = company.CompanyId, Code = number, Name = name, CurrencyCode = "USD" });
                else if (route.EndsWith("tax-setup", StringComparison.Ordinal))
                    db.Add(new TaxCode { Id = id, CompanyId = company.CompanyId, Code = number, Description = name, LiabilityAccountId = liabilityId, RecoverableAccountId = postingId });
                else
                    db.Add(new GeneralLedgerAccount { Id = id, CompanyId = company.CompanyId, Number = number, Name = name, Category = AccountCategory.Asset, Type = AccountType.Posting });
            }
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.GetStringAsync("/account/login");
        using var signedIn = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(login), ["Input.Email"] = email,
            ["Input.Password"] = Password, ["ReturnUrl"] = "/"
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var selection = await client.GetStringAsync("/account/company");
        using var selected = await client.PostAsync("/account/company", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(selection), ["SelectedCompanyId"] = company.CompanyId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, selected.StatusCode);

        var list = await client.GetStringAsync(route);
        Assert.Matches($"<tr class=\"{selectedClass}\"[^>]*>\\s*<th[^>]*>\\s*<a[^>]*href=\"{route}/{firstId}/edit\"", list);
        Assert.DoesNotContain(">Edit</button>", list);
        Assert.Contains("First record", list);
        var details = Regex.Match(list, "<aside[^>]*id=\"[^\"]*-details-pane\".*?</aside>", RegexOptions.Singleline).Value;
        Assert.Contains("First record", details);

        var edit = Dialog(await client.GetStringAsync($"{route}/{firstId}/edit"));
        Assert.Contains("workspace-card-dialog entity-dialog", edit);
        Assert.Contains(cardType, edit);
        Assert.Contains($"id=\"{fieldId}\"", edit);
        Assert.Contains("value=\"1000\"", edit);
        Assert.Contains("Saved", edit);
        Assert.DoesNotContain("type=\"submit\"", edit);
        Assert.Matches("aria-label=\"Previous record\"[^>]*disabled", edit);
        Assert.DoesNotMatch("aria-label=\"Next record\"[^>]*disabled", edit);

        var last = Dialog(await client.GetStringAsync($"{route}/{lastId}/edit"));
        Assert.Matches("aria-label=\"Next record\"[^>]*disabled", last);
        Assert.DoesNotMatch("aria-label=\"Previous record\"[^>]*disabled", last);
        var create = Dialog(await client.GetStringAsync($"{route}/new"));
        Assert.Contains("workspace-card-dialog entity-dialog", create);
        Assert.Contains($"New {cardType}", create);
        Assert.Contains("type=\"submit\"", create);
        Assert.Contains(">Cancel</button>", create);
        Assert.DoesNotContain("aria-label=\"Next record\"", create);
        Assert.DoesNotContain("aria-label=\"Previous record\"", create);
        var unavailable = await client.GetStringAsync($"{route}/{Guid.NewGuid()}/edit");
        Assert.DoesNotContain("id=\"entity-card-heading\"", unavailable);
        Assert.Contains("not available in the active company", unavailable);
    }

    private static string Token(string html) => HttpUtility.HtmlDecode(
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant).Groups[1].Value);

    private static string Dialog(string html) =>
        Regex.Match(html, "<dialog\\b.*?</dialog>", RegexOptions.Singleline | RegexOptions.CultureInvariant).Value;
}
