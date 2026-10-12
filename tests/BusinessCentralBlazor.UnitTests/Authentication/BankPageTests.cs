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
            var account = await scope.ServiceProvider.GetRequiredService<IGeneralLedgerAccountService>().CreateAsync(principal,
                new GeneralLedgerAccountInput { Number = "1000", Name = "Cash", Category = AccountCategory.Asset, Type = AccountType.Posting });
            var bank = scope.ServiceProvider.GetRequiredService<IBankService>();
            bankId = await bank.CreateAccountAsync(principal, new BankAccountInput {
                Code = "MAIN", Name = "Main operating bank", GeneralLedgerAccountId = account });
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
            ($"/financial/banks/{bankId}", "Create draft journal"),
            ($"/financial/bank-statements/{statementId}", "Deposit awaiting posting") })
        {
            using var page = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var html = await page.Content.ReadAsStringAsync();
            Assert.Contains(expected, html);
            Assert.Contains("Bank accounts", html);
        }
        using var foreignBank = await client.GetAsync($"/financial/banks/{Guid.NewGuid()}");
        Assert.Contains("Bank account not found in the active company.", await foreignBank.Content.ReadAsStringAsync());
        using var foreignStatement = await client.GetAsync($"/financial/bank-statements/{Guid.NewGuid()}");
        Assert.Contains("Statement not found in the active company.", await foreignStatement.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnonymousUsersAreRedirectedForAllBankingPages()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        foreach (var route in new[] { "/financial/banks", $"/financial/banks/{Guid.NewGuid()}", $"/financial/bank-statements/{Guid.NewGuid()}" })
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
