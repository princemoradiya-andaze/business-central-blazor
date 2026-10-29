using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class CompanySettingsServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string Password = "Valid-Test-Password-2026!";

    [Fact]
    public async Task BaseCurrencyCanBeConfiguredAndCannotBeChangedAfterBankingActivity()
    {
        var email = $"company-settings-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "SETTINGS", "Settings company");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())], "test"));
        var db = services.GetRequiredService<ErpDbContext>();
        var activeCurrencyCode = CurrencyCode();
        var alternateCurrencyCode = CurrencyCode();
        var inactiveCurrencyCode = CurrencyCode();
        db.Currencies.AddRange(
            new Currency { Code = alternateCurrencyCode, Name = "Test Currency B", MinorUnit = 2 },
            new Currency { Code = inactiveCurrencyCode, Name = "Inactive Test Currency", MinorUnit = 2, IsActive = false });
        await db.SaveChangesAsync();

        var settings = services.GetRequiredService<ICompanySettingsService>();
        var initial = await settings.GetAsync(principal);
        Assert.Null(initial.BaseCurrencyCode);
        Assert.DoesNotContain(initial.Currencies, item => item.Code == inactiveCurrencyCode);
        Assert.Equal(activeCurrencyCode, await settings.CreateCurrencyAsync(
            principal, $" {activeCurrencyCode.ToLowerInvariant()} ", "Test Currency A", 2));
        Assert.Contains((await settings.GetAsync(principal)).Currencies, item => item.Code == activeCurrencyCode);
        await Assert.ThrowsAsync<ValidationException>(() =>
            settings.CreateCurrencyAsync(principal, activeCurrencyCode, "Duplicate", 2));
        await Assert.ThrowsAsync<ValidationException>(() =>
            settings.CreateCurrencyAsync(principal, "AB", "Invalid", 2));

        await settings.SaveBaseCurrencyAsync(principal, $" {activeCurrencyCode.ToLowerInvariant()} ");
        var savedSettings = await db.CompanySettings.SingleAsync(item => item.CompanyId == company.CompanyId);
        Assert.Equal(activeCurrencyCode, savedSettings.BaseCurrencyCode);
        Assert.Equal("UTC", savedSettings.TimeZoneId);
        Assert.True(await db.AuditEntries.AnyAsync(item =>
            item.CompanyId == company.CompanyId && item.Action == "BaseCurrencyConfigured"));

        await Assert.ThrowsAsync<ValidationException>(() => settings.SaveBaseCurrencyAsync(principal, inactiveCurrencyCode));
        await Assert.ThrowsAsync<ValidationException>(() => settings.SaveBaseCurrencyAsync(principal, "AB"));

        db.BankAccounts.Add(new BankAccount
        {
            Id = Guid.NewGuid(),
            CompanyId = company.CompanyId,
            Code = "MAIN",
            Name = "Main bank",
            AccountIdentifier = "TEST",
            GeneralLedgerAccountId = Guid.NewGuid(),
            CurrencyCode = activeCurrencyCode
        });
        await db.SaveChangesAsync();

        await settings.SaveBaseCurrencyAsync(principal, activeCurrencyCode);
        await Assert.ThrowsAsync<ValidationException>(() => settings.SaveBaseCurrencyAsync(principal, alternateCurrencyCode));
        Assert.Equal(activeCurrencyCode, (await settings.GetAsync(principal)).BaseCurrencyCode);

        var noActiveCompany = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], "test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => settings.GetAsync(noActiveCompany));
    }

    [Fact]
    public async Task ActiveCompanyMemberCanOpenBaseCurrencySettingsPage()
    {
        var email = $"company-settings-page-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, Password);
        var company = await factory.AddCompanyMembershipAsync(email, "SETTINGSPAGE", "Settings page company");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.Currencies.Add(new Currency { Code = CurrencyCode(), Name = "Test Currency", MinorUnit = 2 });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var loginPage = await client.GetAsync("/account/login");
        using var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await loginPage.Content.ReadAsStringAsync()),
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["ReturnUrl"] = "/"
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var selector = await client.GetAsync("/account/company");
        using var selected = await client.PostAsync("/account/company", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(await selector.Content.ReadAsStringAsync()),
            ["SelectedCompanyId"] = company.CompanyId.ToString()
        }));
        Assert.Equal(HttpStatusCode.Redirect, selected.StatusCode);

        using var page = await client.GetAsync("/financial/company-settings");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Company Settings", html);
        Assert.Contains("Base currency", html);
        Assert.Contains("Test Currency", html);
        Assert.Contains("Save base currency", html);
        Assert.Contains("Shared Currency Catalog", html);
        Assert.Contains("financial-detail", html);
        Assert.Contains("Back to Home", html);
        Assert.Contains("Discard changes", html);
        Assert.Contains("Settings page company", html);
        Assert.DoesNotContain("href=\"/workspace", html);
        Assert.DoesNotContain("id=\"currency-card-heading\"", html);
        Assert.Contains("Company workspace settings", html);
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return HttpUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string CurrencyCode() =>
        string.Concat(RandomNumberGenerator.GetBytes(3).Select(value => (char)('A' + value % 26)));
}
