using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class ReportsPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ReportsRequireAuthenticationAndRenderOnlyActiveCompanyData()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var route in new[] { "/financial/general-ledger/entries", "/financial/general-ledger/trial-balance" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
        }

        var email = $"reports-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "REPORT", "Reports Page Company");
        var other = await factory.AddCompanyMembershipAsync(email, "PRIVATE", "Private Report Company");
        var accountId = Guid.NewGuid();
        var privateAccountId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.AddRange(
                new GeneralLedgerAccount { Id = accountId, CompanyId = company.CompanyId, Number = "9510", Name = "Visible tax", Type = AccountType.Posting },
                new GeneralLedgerAccount { Id = privateAccountId, CompanyId = other.CompanyId, Number = "PRIVATE-NO", Name = "Private account", Type = AccountType.Posting },
                new GeneralLedgerEntry { Id = Guid.NewGuid(), CompanyId = company.CompanyId, AccountId = accountId, JournalBatchId = journalId, EntryNumber = 1, PostingDate = new(2026, 10, 29), DocumentNumber = "VISIBLE-DOC", Description = "Visible description", DebitAmount = 75m },
                new GeneralLedgerEntry { Id = Guid.NewGuid(), CompanyId = other.CompanyId, AccountId = privateAccountId, EntryNumber = 2, DocumentNumber = "PRIVATE-DOC", DebitAmount = 999m });
            await db.SaveChangesAsync();
        }

        var login = await client.GetStringAsync("/account/login");
        using var signedIn = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(login), ["Input.Email"] = email, ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false", ["ReturnUrl"] = "/"
        }));
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        var selector = await client.GetStringAsync("/account/company");
        using var selected = await client.PostAsync("/account/company", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(selector), ["SelectedCompanyId"] = company.CompanyId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, selected.StatusCode);

        var entries = await client.GetStringAsync("/financial/general-ledger/entries");
        Assert.Contains("record-row-selected", entries);
        Assert.Contains("VISIBLE-DOC", entries);
        Assert.Contains($"/financial/journals/{journalId}", entries);
        Assert.Contains($"/financial/general-ledger/accounts/{accountId}/activity", entries);
        Assert.Contains("Choose view", entries);
        Assert.Contains("Enter analysis mode", entries);
        Assert.DoesNotContain("PRIVATE-DOC", entries);
        Assert.DoesNotContain("PRIVATE-NO", entries);

        var trial = await client.GetStringAsync("/financial/general-ledger/trial-balance");
        Assert.Contains("id=\"trial-request-heading\"", trial);
        Assert.Contains("Handled by the browser", trial);
        Assert.Contains("Filter totals by", trial);
        Assert.Contains("Maximum rows", trial);
        Assert.Contains("Send to...", trial);
        Assert.Contains("Visible tax", trial);
        Assert.DoesNotContain("Private account", trial);
        Assert.DoesNotContain("trial-preview-heading", trial);

        var privateFilter = await client.GetStringAsync($"/financial/general-ledger/entries?accountId={privateAccountId}");
        Assert.Contains("No posted entries match", privateFilter);
        Assert.DoesNotContain("PRIVATE-DOC", privateFilter);
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return HttpUtility.HtmlDecode(match.Groups[1].Value);
    }
}
