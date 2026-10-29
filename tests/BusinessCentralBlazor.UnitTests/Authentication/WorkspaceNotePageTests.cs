using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class WorkspaceNotePageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task NavigationDirectoryListsImplementedCompanyPages()
    {
        var email = $"navigation-directory-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "NAVDIR", "Navigation Directory Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var anonymousResponse = await client.GetAsync("/navigation");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var response = await client.GetAsync("/navigation");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("All pages", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=\"Company navigation\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"directory-back-link\" href=\"/\"", html, StringComparison.Ordinal);
        Assert.Contains("Cash management", html, StringComparison.Ordinal);
        Assert.Contains("Reports", html, StringComparison.Ordinal);
        Assert.Contains("directory-count", html, StringComparison.Ordinal);
        Assert.Contains("(5)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace notes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/workspace", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/company-settings\"", html, StringComparison.Ordinal);
        Assert.Contains("- General journals", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-hidden=\"true\">›", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales/customers\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing/vendors\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/general-ledger/trial-balance\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales quotes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Purchase invoices", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetiredNoteRoutesCannotRenderCrudAndPreserveStoredData()
    {
        var email = $"workspace-note-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "NOTEPAGE", "Notes Page Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);
        var principal = await CreatePrincipalAsync(email, company.CompanyId);

        Guid noteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();
            noteId = await service.CreateAsync(principal, new WorkspaceNoteInput
            {
                Title = "Navigation test note",
                Body = "A safe, synthetic note for the page test."
            });
        }

        foreach (var route in new[] { "/workspace/notes", "/workspace/notes/new", $"/workspace/notes/{noteId}",
                     $"/workspace/notes/{noteId}/edit", "/workspace/notes/unknown/path" })
        {
            using var response = await client.GetAsync(route);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Page unavailable", html);
            Assert.DoesNotContain("Navigation test note", html);
            Assert.DoesNotContain("A safe, synthetic note for the page test.", html);
            Assert.DoesNotContain("Create note", html);
            Assert.DoesNotContain("Save changes", html);
            Assert.DoesNotContain("Confirm delete", html);
            Assert.DoesNotContain("href=\"/workspace", html);
            using var post = await client.PostAsync(route, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Title"] = "Should not save", ["Input.Body"] = "Retired"
            }));
            Assert.False(post.IsSuccessStatusCode);
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var retained = await verifyScope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>().GetAsync(principal, noteId);
        Assert.NotNull(retained);
        Assert.Equal("Navigation test note", retained.Title);
    }

    [Fact]
    public async Task AnonymousAndCrossCompanyNotePageRequestsDoNotRevealPrivateContent()
    {
        var ownerEmail = $"workspace-page-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"workspace-page-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(ownerEmail, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownerCompany = await factory.AddCompanyMembershipAsync(ownerEmail, "PAGEOWNER", "Page Owner Company");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "PAGEOTHER", "Page Other Company");
        var otherPrincipal = await CreatePrincipalAsync(otherEmail, otherCompany.CompanyId);

        Guid privateNoteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IWorkspaceNoteService>();
            privateNoteId = await service.CreateAsync(otherPrincipal, new WorkspaceNoteInput
            {
                Title = "Private company note",
                Body = "This content must stay private."
            });
        }

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/workspace/notes");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(ownerClient, ownerEmail);
        await SelectCompanyAsync(ownerClient, ownerCompany.CompanyId);

        using var detailsResponse = await ownerClient.GetAsync($"/workspace/notes/{privateNoteId}");
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.Contains("Page unavailable", detailsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private company note", detailsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("This content must stay private.", detailsHtml, StringComparison.Ordinal);

        using var editResponse = await ownerClient.GetAsync($"/workspace/notes/{privateNoteId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Page unavailable", editHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private company note", editHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("This content must stay private.", editHtml, StringComparison.Ordinal);
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
