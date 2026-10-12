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

public sealed class GeneralJournalPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ActiveCompanyMemberCanManagePeriodsAndViewJournalBatchPages()
    {
        var email = $"journal-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "JRNLPAGE", "Journal Page Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var account = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Number = "1000",
            Name = "Cash",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        var liabilityAccount = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Number = "2100",
            Name = "Tax liability",
            Category = AccountCategory.Liability,
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        var recoverableAccount = new GeneralLedgerAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Number = "1200",
            Name = "Tax recoverable",
            Category = AccountCategory.Asset,
            Type = AccountType.Posting,
            AllowsDirectPosting = true
        };
        dbContext.GeneralLedgerAccounts.AddRange(account, liabilityAccount, recoverableAccount);
        await dbContext.SaveChangesAsync();
        var taxSetupService = scope.ServiceProvider.GetRequiredService<ITaxSetupService>();
        var taxCodeId = await taxSetupService.CreateTaxCodeAsync(principal, new TaxCodeInput
        {
            Code = "VAT",
            Description = "Value added tax",
            LiabilityAccountId = liabilityAccount.Id,
            RecoverableAccountId = recoverableAccount.Id
        });
        await taxSetupService.CreateRateAsync(principal, new TaxRateInput
        {
            TaxCodeId = taxCodeId,
            RatePercent = 8.25m,
            StartsOn = new DateOnly(2026, 1, 1)
        });
        var journalService = scope.ServiceProvider.GetRequiredService<IGeneralJournalService>();
        var periodId = await journalService.CreatePeriodAsync(principal, new AccountingPeriodInput
        {
            Name = "2026-10",
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        var batchId = await journalService.CreateBatchAsync(principal, new GeneralJournalBatchInput
        {
            Name = "PAGE-TEST",
            Description = "Page coverage",
            DefaultAccountingPeriodId = periodId
        });
        var lineId = await journalService.CreateLineAsync(principal, batchId, new GeneralJournalLineInput
        {
            LineNumber = 10000,
            AccountingPeriodId = periodId,
            AccountId = account.Id,
            PostingDate = new DateOnly(2026, 10, 12),
            DocumentNumber = "PAGE-1",
            Description = "Page test debit",
            DebitAmount = 10m
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var periodsResponse = await client.GetAsync("/financial/accounting-periods");
        var periodsHtml = await periodsResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, periodsResponse.StatusCode);
        Assert.Contains("2026-10", periodsHtml, StringComparison.Ordinal);
        Assert.Contains("Create period", periodsHtml, StringComparison.Ordinal);

        using var listResponse = await client.GetAsync("/financial/journals");
        var listHtml = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("PAGE-TEST", listHtml, StringComparison.Ordinal);
        Assert.Contains("New journal batch", listHtml, StringComparison.Ordinal);

        using var batchResponse = await client.GetAsync($"/financial/journals/{batchId}");
        var batchHtml = await batchResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, batchResponse.StatusCode);
        Assert.Contains("Page test debit", batchHtml, StringComparison.Ordinal);
        Assert.Contains("Add line", batchHtml, StringComparison.Ordinal);
        Assert.Contains("Edit batch", batchHtml, StringComparison.Ordinal);
        Assert.Contains("/financial/journals/", batchHtml, StringComparison.Ordinal);

        using var editLineResponse = await client.GetAsync(
            $"/financial/journals/{batchId}/lines/{lineId}/edit");
        var editLineHtml = await editLineResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editLineResponse.StatusCode);
        Assert.Contains("Edit journal line", editLineHtml, StringComparison.Ordinal);
        Assert.Contains("Page test debit", editLineHtml, StringComparison.Ordinal);
        Assert.Contains("Save line", editLineHtml, StringComparison.Ordinal);

        using var newLineResponse = await client.GetAsync($"/financial/journals/{batchId}/lines/new");
        var newLineHtml = await newLineResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, newLineResponse.StatusCode);
        Assert.Contains("Tax code (manual tax line)", newLineHtml, StringComparison.Ordinal);
        Assert.Contains("VAT — Value added tax", newLineHtml, StringComparison.Ordinal);

        using var taxSetupResponse = await client.GetAsync("/financial/tax-setup");
        var taxSetupHtml = await taxSetupResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, taxSetupResponse.StatusCode);
        Assert.Contains("Tax setup", taxSetupHtml, StringComparison.Ordinal);
        Assert.Contains("VAT — Value added tax", taxSetupHtml, StringComparison.Ordinal);
        Assert.Contains("8.25", taxSetupHtml, StringComparison.Ordinal);

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/financial/journals");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);
        using var anonymousTaxResponse = await anonymousClient.GetAsync("/financial/tax-setup");
        Assert.Equal(HttpStatusCode.Redirect, anonymousTaxResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousTaxResponse.Headers.Location?.AbsolutePath);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
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
                ["Input.Password"] = TestPassword,
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
