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
        Assert.Contains("Purchase orders", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/orders/new\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/fulfillment\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/payables\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/setup?from=orders\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/vendors\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("FormName=\"purchase-vendor\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"purchase-vendor-number\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"purchasing-grni\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"purchase-order-number\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"page-command-bar\"", html, StringComparison.Ordinal);
        Assert.Contains("record-workspace-main", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/financial/purchasing/orders")]
    [InlineData("/financial/purchasing/orders/new")]
    [InlineData("/financial/purchasing/orders/8711e20c-e555-416c-a631-dd7ff7b7799a")]
    [InlineData("/financial/purchasing/fulfillment")]
    [InlineData("/financial/purchasing/fulfillment/8711e20c-e555-416c-a631-dd7ff7b7799a")]
    [InlineData("/financial/purchasing/payables?invoice=INV-1")]
    [InlineData("/financial/purchasing/setup")]
    public async Task PurchasingWorkflowRoutesRequireSignIn(string route)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task PurchasingWorkspacesSelectRecordsAndExposeCompanyScopedCardsAndSetup()
    {
        var email = $"purchasing-workspace-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var membership = await factory.AddCompanyMembershipAsync(email, "PURCHUI", "Purchasing UI");
        var foreign = await factory.AddCompanyMembershipAsync(email, "FOREIGNP", "Foreign Purchasing UI");
        var vendorId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var grniId = Guid.NewGuid();
        var openId = Guid.NewGuid();
        var receivedId = Guid.NewGuid();
        var invoicedId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.Add(new GeneralLedgerAccount { Id = accountId, CompanyId = membership.CompanyId, Number = "2100", Name = "Payables", Category = AccountCategory.Liability, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new GeneralLedgerAccount { Id = grniId, CompanyId = membership.CompanyId, Number = "2200", Name = "GRNI", Category = AccountCategory.Liability, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new Vendor { Id = vendorId, CompanyId = membership.CompanyId, Number = "V100", Name = "Workspace vendor", PayablesAccountId = accountId });
            db.Add(new Item { Id = itemId, CompanyId = membership.CompanyId, Number = "ITEM100", Description = "Workspace item", BaseUnitOfMeasure = "EA", Type = ItemType.Inventory });
            db.Add(new AccountingPeriod { Id = Guid.NewGuid(), CompanyId = membership.CompanyId, Name = "October 2026", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31) });
            foreach (var (id, number, status) in new[]
            {
                (openId, "PO-100", PurchaseOrderStatus.Open),
                (Guid.NewGuid(), "PO-150", PurchaseOrderStatus.ReceiptDraft),
                (receivedId, "PO-200", PurchaseOrderStatus.Received),
                (Guid.NewGuid(), "PO-250", PurchaseOrderStatus.InvoiceDraft),
                (invoicedId, "PO-300", PurchaseOrderStatus.Invoiced)
            })
            {
                db.Add(new PurchaseOrder { Id = id, CompanyId = membership.CompanyId, VendorId = vendorId, OrderNumber = number, Description = "Workspace order", OrderDate = new(2026, 10, 20), Status = status, ReceiptJournalBatchId = status == PurchaseOrderStatus.Open ? null : journalId });
                db.Add(new PurchaseOrderLine { Id = Guid.NewGuid(), CompanyId = membership.CompanyId, PurchaseOrderId = id, LineNumber = 10000, ItemId = itemId, Description = "Workspace line", Quantity = 2m, UnitCost = 25m });
            }
            db.Add(new PurchaseInvoice { Id = Guid.NewGuid(), CompanyId = membership.CompanyId, PurchaseOrderId = invoicedId, VendorId = vendorId, JournalBatchId = journalId, InvoiceNumber = "INV-100 & A", PostingDate = new(2026, 10, 20), NetAmount = 50, TaxAmount = 5, TotalAmount = 55, PostedAtUtc = DateTimeOffset.UtcNow });
            var foreignVendor = Guid.NewGuid();
            var foreignAccount = Guid.NewGuid();
            db.Add(new GeneralLedgerAccount { Id = foreignAccount, CompanyId = foreign.CompanyId, Number = "2100", Name = "Foreign payables", Category = AccountCategory.Liability, Type = AccountType.Posting, AllowsDirectPosting = true });
            db.Add(new Vendor { Id = foreignVendor, CompanyId = foreign.CompanyId, Number = "V-FOREIGN", Name = "Foreign vendor", PayablesAccountId = foreignAccount });
            db.Add(new PurchaseOrder { Id = foreignId, CompanyId = foreign.CompanyId, VendorId = foreignVendor, OrderNumber = "FOREIGN-ORDER", Description = "Foreign order", OrderDate = new(2026, 10, 20), Status = PurchaseOrderStatus.Open });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, membership.CompanyId);
        var list = await client.GetStringAsync("/financial/purchasing/orders");
        Assert.Matches($"<tr[^>]*class=\"record-row-selected\"[^>]*>\\s*<th[^>]*>\\s*<a[^>]*href=\"/financial/purchasing/orders/{openId}\"", list);
        Assert.DoesNotContain("FOREIGN-ORDER", list);
        Assert.DoesNotContain("class=\"page-command-bar\"", list);
        Assert.DoesNotContain("href=\"/financial/purchasing#purchase-order-heading\"", list);
        Assert.Contains("value=\"ReceiptDraft\"", list);
        Assert.Contains("value=\"InvoiceDraft\"", list);
        Assert.Contains("href=\"/financial/purchasing/orders/new\"", list);
        var all = list.IndexOf("id=\"purchase-orders-status\"", StringComparison.Ordinal);
        var search = list.IndexOf("id=\"purchase-orders-search\"", StringComparison.Ordinal);
        var newAction = list.IndexOf("id=\"new-purchase-order-action\"", StringComparison.Ordinal);
        Assert.True(all >= 0 && search > all && newAction > search);

        var create = Dialog(await client.GetStringAsync("/financial/purchasing/orders/new"));
        Assert.Contains("New Purchase Order Card", create);
        Assert.Contains("purchase-order-vendor", create);
        Assert.Contains("purchase-line-cost-0", create);
        Assert.Contains("purchase-line-tax-0", create);
        Assert.Contains(">Save</button>", create);
        Assert.Contains(">Cancel</button>", create);
        Assert.DoesNotContain("Next record", create);
        Assert.Contains("Select and save a GRNI account", create);
        var orderCard = Dialog(await client.GetStringAsync($"/financial/purchasing/orders/{openId}"));
        Assert.Contains("Read-only", orderCard);
        Assert.Contains("Workspace line", orderCard);
        Assert.Contains("ITEM100", orderCard);
        Assert.DoesNotContain("<form", orderCard);
        Assert.Matches("aria-label=\"Previous record\"[^>]*disabled", orderCard);
        Assert.DoesNotMatch("aria-label=\"Next record\"[^>]*disabled", orderCard);
        var lastCard = Dialog(await client.GetStringAsync($"/financial/purchasing/orders/{invoicedId}"));
        Assert.Matches("aria-label=\"Next record\"[^>]*disabled", lastCard);
        Assert.Contains($"/financial/journals/{journalId}", lastCard);

        var processing = await client.GetStringAsync("/financial/purchasing/fulfillment");
        Assert.Contains($"/financial/purchasing/fulfillment/{openId}", processing);
        Assert.Contains($"/financial/purchasing/fulfillment/{receivedId}", processing);
        foreach (var excluded in new[] { "PO-150", "PO-250", "PO-300", "FOREIGN-ORDER" }) Assert.DoesNotContain(excluded, processing);
        var receipt = Dialog(await client.GetStringAsync($"/financial/purchasing/fulfillment/{openId}"));
        Assert.Contains("Create receipt journal", receipt);
        Assert.DoesNotContain("vendor-invoice-number", receipt);
        var invoice = Dialog(await client.GetStringAsync($"/financial/purchasing/fulfillment/{receivedId}"));
        Assert.Contains("Create invoice journal", invoice);
        Assert.Contains("vendor-invoice-number", invoice);
        Assert.Contains("Posting creates the receipt or invoice", invoice);
        var payables = await client.GetStringAsync("/financial/purchasing/payables");
        Assert.Contains("INV-100%20%26%20A", payables);
        var postedCard = Dialog(await client.GetStringAsync("/financial/purchasing/payables?invoice=INV-100%20%26%20A"));
        Assert.Contains("Posted Vendor Invoice", postedCard);
        Assert.Contains("55.00", postedCard);
        Assert.Contains("Read-only", postedCard);
        Assert.DoesNotContain("<form", postedCard);
        var setupCard = Dialog(await client.GetStringAsync("/financial/purchasing/setup?from=fulfillment"));
        Assert.Contains("Save GRNI account", setupCard);
        Assert.Contains("purchasing-grni", setupCard);
        Assert.Contains("2200 - GRNI", setupCard);
        Assert.DoesNotContain("Previous record", setupCard);
        foreach (var route in new[] { $"/financial/purchasing/orders/{foreignId}", $"/financial/purchasing/fulfillment/{invoicedId}", "/financial/purchasing/payables?invoice=MISSING" })
        {
            var missing = await client.GetStringAsync(route);
            Assert.Contains("not available", missing);
            Assert.DoesNotContain("id=\"entity-card-heading\"", missing);
        }
    }

    private static string Dialog(string html) => Regex.Match(html, "<dialog\\b.*?</dialog>", RegexOptions.Singleline).Value;

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
            "class=\"workspace-new-icon\" aria-hidden=\"true\"", "</span>New</a>", "Block", "V-LOCAL", "Local vendor", "2000", "Blocked",
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
