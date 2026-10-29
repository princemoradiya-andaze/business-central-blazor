using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class BankPageTests(AuthenticationWebApplicationFactory factory) : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task CompanyMemberCanRenderBankSetupJournalAndReconciliationForms()
    {
        var email = $"bank-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "BANKPAGE", "Bank page company");
        Guid bankId;
        Guid statementId;
        Guid journalId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
                new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())], "test"));
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            if (!await db.Currencies.AnyAsync(item => item.Code == "USD"))
                db.Currencies.Add(new Currency { Code = "USD", Name = "US Dollar", MinorUnit = 2 });
            db.CompanySettings.Add(new CompanySettings { CompanyId = company.CompanyId, BaseCurrencyCode = "USD" });
            await db.SaveChangesAsync();
            var ledger = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>();
            var account = await ledger.CreateAsync(principal,
                new GeneralLedgerAccountInput { Number = "1000", Name = "Cash", Category = AccountCategory.Asset, Type = AccountType.Posting });
            var offset = await ledger.CreateAsync(principal, new GeneralLedgerAccountInput {
                Number = "5000", Name = "Expense", Category = AccountCategory.Expense, Type = AccountType.Posting });
            var period = await scope.ServiceProvider.GetRequiredService<IGeneralJournalService>().CreatePeriodAsync(principal,
                new AccountingPeriodInput { Name = "October 2026", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31) });
            var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
            bankId = await bank.CreateAccountAsync(principal, new BankAccountInput {
                Code = "MAIN", Name = "Main operating bank", GeneralLedgerAccountId = account });
            journalId = await bank.CreateJournalAsync(principal, bankId, new BankJournalInput {
                DocumentNumber = "BANK-REC-001", Description = "Bank page receipt", Amount = 10, IsReceipt = true,
                AccountingPeriodId = period, OffsetAccountId = offset, PostingDate = new(2026, 10, 12) });
            statementId = await bank.CreateStatementAsync(principal, bankId, new BankStatementInput {
                Reference = "OCTOBER", StartsOn = new(2026, 10, 1), EndsOn = new(2026, 10, 31), ClosingBalance = 10 });
            await bank.AddLinesAsync(principal, statementId, 0, [new BankStatementLineInput {
                Reference = "UNMATCHED", TransactionDate = new(2026, 10, 12), Description = "Deposit awaiting posting", Amount = 10 }]);
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var login = await client.GetAsync("/account/login");
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = Token(await login.Content.ReadAsStringAsync()),
            ["Input.Email"] = email, ["Input.Password"] = Password, ["ReturnUrl"] = "/" }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var selector = await client.GetAsync("/account/company");
        using var selected = await client.PostAsync("/account/company", new FormUrlEncodedContent(new Dictionary<string, string> {
            ["__RequestVerificationToken"] = Token(await selector.Content.ReadAsStringAsync()),
            ["SelectedCompanyId"] = company.CompanyId.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, selected.StatusCode);
        foreach (var (route, expected) in new[] {
            ("/financial/banks", "New bank account"),
            ($"/financial/banks/{bankId}", "Bank transactions"),
            ($"/financial/banks/{bankId}?view=journals&new=receipt", "New Bank Receipt"),
            ($"/financial/banks/{bankId}?view=journals&new=payment", "New Bank Payment"),
            ($"/financial/banks/{bankId}?view=statements", "OCTOBER"),
            ($"/financial/banks/{bankId}?view=statements&new=statement", "New Manual Bank Statement"),
            ($"/financial/journals/{journalId}", "Bank page receipt"),
            ($"/financial/bank-statements/{statementId}", "Deposit awaiting posting") })
        {
            using var page = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains(expected, html);
            Assert.Contains("Bank accounts", html);
            if (route == $"/financial/journals/{journalId}")
            {
                Assert.Contains("financial-detail", html);
                Assert.Contains("Record actions", html);
                Assert.Contains("Journal lines", html);
                Assert.Contains("Balance (debit minus credit)", html);
                Assert.Contains("Post batch", html);
                Assert.Contains("Add line", html);
                Assert.Contains("Back to General journals", html);
            }
            if (route == $"/financial/bank-statements/{statementId}")
            {
                Assert.Contains("financial-detail", html);
                Assert.Contains("Record actions", html);
                Assert.Contains("id=\"statement-header\"", html);
                Assert.Contains("form=\"statement-header\"", html);
                Assert.Contains("Save header", html);
                Assert.Contains("Complete reconciliation", html);
                Assert.Contains("Statement balance difference", html);
                Assert.Contains("Unreconciled posted bank activity", html);
                Assert.Contains("table-responsive", html);
                Assert.Contains($"/financial/banks/{bankId}?view=statements", html);
            }
            if (route == "/financial/banks")
            {
                Assert.Contains("Choose view", html);
                Assert.Contains("Filters", html);
                Assert.Contains("Details", html);
                Assert.Contains("id=\"new-bank-action\"", html);
                Assert.Contains("id=\"bank-status-quick\"", html);
            }
            if (route.StartsWith($"/financial/banks/{bankId}", StringComparison.Ordinal))
            {
                Assert.Contains("id=\"bank-transactions-status\"", html);
                Assert.Contains("Bank transaction types", html);
                Assert.Contains("bank-activity-context", html);
                Assert.DoesNotContain("Bank receipts, payments and statements</h1>", html);
                Assert.Contains("record-row-selected", html);
                if (!route.Contains("view=statements", StringComparison.Ordinal))
                {
                    Assert.Contains("BANK-REC-001", html);
                    Assert.Contains($"/financial/journals/{journalId}", html);
                    Assert.Contains("no automatic posting", html);
                }
                if (route.Contains("new=", StringComparison.Ordinal))
                {
                    Assert.Contains("id=\"entity-card-heading\"", html);
                    Assert.Contains("id=\"bank-activity-create\"", html);
                    Assert.Contains(">Cancel</button>", html);
                    Assert.Contains("readonly", html);
                    Assert.DoesNotContain("Previous record", html);
                    Assert.DoesNotContain("Next record", html);
                }
                else Assert.DoesNotContain("id=\"bank-activity-create\"", html);
            }
        }
        using var foreignBank = await client.GetAsync($"/financial/banks/{Guid.NewGuid()}");
        Assert.Contains("Bank account not found in the active company.", await foreignBank.Content.ReadAsStringAsync());
        using var foreignStatement = await client.GetAsync($"/financial/bank-statements/{Guid.NewGuid()}");
        Assert.Contains("Statement not found in the active company.", await foreignStatement.Content.ReadAsStringAsync());
        using var invalidCard = await client.GetAsync($"/financial/banks/{bankId}?view=statements&new=payment");
        Assert.Contains("The requested bank transaction card is not available in this view.", await invalidCard.Content.ReadAsStringAsync());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
                new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())], "test"));
            var service = scope.ServiceProvider.GetRequiredService<IBankService>();
            var bank = (await service.GetAccountsAsync(principal)).Single();
            await service.UpdateAccountAsync(principal, bankId, bank.Revision, new() {
                Code = bank.Code, Name = bank.Name, AccountIdentifier = bank.AccountIdentifier,
                GeneralLedgerAccountId = bank.GeneralLedgerAccountId, IsBlocked = true });
        }
        using var blockedCard = await client.GetAsync($"/financial/banks/{bankId}?view=journals&new=receipt");
        var blockedHtml = await blockedCard.Content.ReadAsStringAsync();
        Assert.Contains("This bank account is blocked.", blockedHtml);
        Assert.Matches("<fieldset\\b[^>]*\\bdisabled\\b", blockedHtml);
        Assert.Contains("Reactivate account", blockedHtml);
        Assert.Contains("aria-disabled=\"true\"", blockedHtml);
        Assert.Contains("BANK-REC-001", blockedHtml);
    }

    [Fact]
    public async Task AnonymousUsersAreRedirectedForAllBankingPages()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var route in new[] { "/financial/banks", $"/financial/banks/{Guid.NewGuid()}",
            $"/financial/banks/{Guid.NewGuid()}?view=journals&new=receipt",
            $"/financial/banks/{Guid.NewGuid()}?view=journals&new=payment",
            $"/financial/banks/{Guid.NewGuid()}?view=statements",
            $"/financial/banks/{Guid.NewGuid()}?view=statements&new=statement",
            $"/financial/bank-statements/{Guid.NewGuid()}" })
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
        }
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return HttpUtility.HtmlDecode(match.Groups[1].Value);
    }
}
