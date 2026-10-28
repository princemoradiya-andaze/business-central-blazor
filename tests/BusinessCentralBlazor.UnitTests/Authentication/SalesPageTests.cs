using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class SalesPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task SalesWorkspacesSelectRecordsAndExposeCompanyScopedReadOnlyAndProcessingCards()
    {
        var email = $"sales-workspaces-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(email, "SALESUI", "Sales UI");
        var otherCompany = await factory.AddCompanyMembershipAsync(email, "OTHERUI", "Other Sales UI");
        var customerId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var revenueId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        var shippedId = Guid.NewGuid();
        var invoicedId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var companyId = membership.CompanyId;
            db.Add(new GeneralLedgerAccount { Id = assetId, CompanyId = companyId, Number = "1300", Name = "Receivables", Category = AccountCategory.Asset, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new GeneralLedgerAccount { Id = revenueId, CompanyId = companyId, Number = "4000", Name = "Revenue", Category = AccountCategory.Income, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new Customer { Id = customerId, CompanyId = companyId, Number = "C100", Name = "Workspace customer", ReceivablesAccountId = assetId });
            db.Add(new Item { Id = itemId, CompanyId = companyId, Number = "ITEM100", Description = "Workspace inventory", Type = ItemType.Inventory, BaseUnitOfMeasure = "EA" });
            db.Add(new AccountingPeriod { Id = Guid.NewGuid(), CompanyId = companyId, Name = "October 2026", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31) });
            foreach (var (id, number, status) in new[]
            {
                (openId, "SO-100", SalesOrderStatus.Open),
                (Guid.NewGuid(), "SO-150", SalesOrderStatus.ShipmentDraft),
                (shippedId, "SO-200", SalesOrderStatus.Shipped),
                (Guid.NewGuid(), "SO-250", SalesOrderStatus.InvoiceDraft),
                (invoicedId, "SO-300", SalesOrderStatus.Invoiced)
            })
            {
                db.Add(new SalesOrder { Id = id, CompanyId = companyId, CustomerId = customerId, OrderNumber = number, Description = "Workspace order", OrderDate = new(2026, 10, 20), Status = status, ShipmentJournalBatchId = status == SalesOrderStatus.Open ? null : journalId });
                db.Add(new SalesOrderLine { Id = Guid.NewGuid(), CompanyId = companyId, SalesOrderId = id, LineNumber = 10000, ItemId = itemId, SalesAccountId = revenueId, Description = "Workspace line", Quantity = 2m, UnitPrice = 100m });
            }
            db.Add(new SalesInvoice { Id = Guid.NewGuid(), CompanyId = companyId, SalesOrderId = invoicedId, CustomerId = customerId, JournalBatchId = journalId, InvoiceNumber = "SI-100 & A", PostingDate = new(2026, 10, 20), NetAmount = 200, TaxAmount = 20, TotalAmount = 220, PostedAtUtc = DateTimeOffset.UtcNow });
            var foreignCustomerId = Guid.NewGuid();
            var foreignAssetId = Guid.NewGuid();
            db.Add(new GeneralLedgerAccount { Id = foreignAssetId, CompanyId = otherCompany.CompanyId, Number = "1300", Name = "Foreign receivables", Category = AccountCategory.Asset, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new Customer { Id = foreignCustomerId, CompanyId = otherCompany.CompanyId, Number = "FOREIGN-C", Name = "Foreign customer", ReceivablesAccountId = foreignAssetId });
            db.Add(new SalesOrder { Id = foreignId, CompanyId = otherCompany.CompanyId, CustomerId = foreignCustomerId, OrderNumber = "FOREIGN-ORDER", Description = "Other company", OrderDate = new(2026, 10, 20), Status = SalesOrderStatus.Open });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);
        var list = await client.GetStringAsync("/financial/sales/orders");
        Assert.Matches($"<tr[^>]*class=\"record-row-selected\"[^>]*>\\s*<th[^>]*>\\s*<a[^>]*href=\"/financial/sales/orders/{openId}\"", list);
        Assert.Contains("href=\"/financial/sales/orders/new\"", list);
        Assert.DoesNotContain("href=\"/financial/sales/create-order\"", list);
        Assert.DoesNotContain("FOREIGN-ORDER", list);
        Assert.DoesNotContain("class=\"page-command-bar\"", list);
        Assert.Contains("value=\"ShipmentDraft\"", list);
        Assert.Contains("value=\"InvoiceDraft\"", list);
        var all = list.IndexOf("id=\"sales-orders-status\"", StringComparison.Ordinal);
        var search = list.IndexOf("id=\"sales-orders-search\"", StringComparison.Ordinal);
        var newAction = list.IndexOf("id=\"new-sales-order-action\"", StringComparison.Ordinal);
        Assert.True(all >= 0 && search > all && newAction > search);

        var create = Dialog(await client.GetStringAsync("/financial/sales/orders/new"));
        Assert.Contains("New Sales Order Card", create);
        Assert.Contains("sales-order-number", create);
        Assert.Contains("sales-line-tax-0", create);
        Assert.Contains(">Save</button>", create);
        Assert.Contains(">Cancel</button>", create);
        Assert.DoesNotContain("Next record", create);

        var orderCard = Dialog(await client.GetStringAsync($"/financial/sales/orders/{openId}"));
        Assert.Contains("Read-only", orderCard);
        Assert.Contains("Workspace line", orderCard);
        Assert.Contains("ITEM100", orderCard);
        Assert.DoesNotContain("sales-order-number", orderCard);
        Assert.Matches("aria-label=\"Previous record\"[^>]*disabled", orderCard);
        Assert.DoesNotMatch("aria-label=\"Next record\"[^>]*disabled", orderCard);
        var lastCard = Dialog(await client.GetStringAsync($"/financial/sales/orders/{invoicedId}"));
        Assert.Matches("aria-label=\"Next record\"[^>]*disabled", lastCard);
        Assert.Contains($"/financial/journals/{journalId}", lastCard);

        var fulfillment = await client.GetStringAsync("/financial/sales/fulfillment");
        Assert.Contains($"/financial/sales/fulfillment/{openId}", fulfillment);
        Assert.Contains($"/financial/sales/fulfillment/{shippedId}", fulfillment);
        Assert.DoesNotContain("SO-300", fulfillment);
        Assert.DoesNotContain("SO-150", fulfillment);
        Assert.DoesNotContain("SO-250", fulfillment);
        var shipment = Dialog(await client.GetStringAsync($"/financial/sales/fulfillment/{openId}"));
        Assert.Contains("Create shipment journal", shipment);
        Assert.Contains("sales-posting-date", shipment);
        Assert.DoesNotContain("sales-invoice-number", shipment);
        var invoice = Dialog(await client.GetStringAsync($"/financial/sales/fulfillment/{shippedId}"));
        Assert.Contains("Create invoice journal", invoice);
        Assert.Contains("sales-invoice-number", invoice);
        Assert.Contains("Posting creates the shipment or invoice", invoice);

        var receivables = await client.GetStringAsync("/financial/sales/receivables");
        Assert.Contains("SI-100%20%26%20A", receivables);
        var postedCard = Dialog(await client.GetStringAsync("/financial/sales/receivables?invoice=SI-100%20%26%20A"));
        Assert.Contains("Posted Invoice", postedCard);
        Assert.Contains("220.00", postedCard);
        Assert.Contains("Read-only", postedCard);
        Assert.DoesNotContain("<form", postedCard);

        foreach (var route in new[] { $"/financial/sales/orders/{foreignId}", $"/financial/sales/fulfillment/{invoicedId}", "/financial/sales/receivables?invoice=UNAVAILABLE" })
        {
            var missing = await client.GetStringAsync(route);
            Assert.DoesNotContain("id=\"entity-card-heading\"", missing);
            Assert.Contains("not available", missing);
        }
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var denied = await anonymous.GetAsync($"/financial/sales/orders/{openId}");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
    }

    private static string Dialog(string html) => Regex.Match(html, "<dialog\\b.*?</dialog>", RegexOptions.Singleline).Value;

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
            ["/financial/sales/create-order"] = ("New Sales Order Card", "id=\"receivables-heading\""),
            ["/financial/sales/orders/new"] = ("New Sales Order Card", "id=\"receivables-heading\""),
            ["/financial/sales/orders"] = ("aria-label=\"Sales orders\"", "id=\"sales-order-number\""),
            ["/financial/sales/fulfillment"] = ("aria-label=\"Ship and invoice orders\"", "id=\"sales-posting-date\""),
            ["/financial/sales/receivables"] = ("aria-label=\"Open receivables\"", "id=\"sales-order-number\"")
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
            Assert.DoesNotContain("href=\"/financial/sales/create-order\"", sectionHtml, StringComparison.Ordinal);
            if (!route.Contains("/customers", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("class=\"page-command-bar\"", sectionHtml, StringComparison.Ordinal);
                Assert.Contains("record-workspace-main", sectionHtml, StringComparison.Ordinal);
            }
            if (route == "/financial/sales/customers/new")
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
