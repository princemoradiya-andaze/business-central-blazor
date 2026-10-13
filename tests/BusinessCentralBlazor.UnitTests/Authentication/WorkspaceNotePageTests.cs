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
        Assert.Contains("Company: Navigation Directory Company (NAVDIR)", html, StringComparison.Ordinal);
        Assert.Contains("Cash management", html, StringComparison.Ordinal);
        Assert.Contains("Reports", html, StringComparison.Ordinal);
        Assert.Contains("Workspace notes", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/sales#customers-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/purchasing#vendors-heading\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/financial/general-ledger/trial-balance\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales quotes", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Purchase invoices", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveCompanyNavigationAndNotePagesRenderForSignedInMembers()
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

        using var listResponse = await client.GetAsync("/workspace/notes");
        var listHtml = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("Workspace notes", listHtml, StringComparison.Ordinal);
        Assert.Contains("Navigation test note", listHtml, StringComparison.Ordinal);
        Assert.Contains("/workspace/notes/new", listHtml, StringComparison.Ordinal);
        Assert.Contains("Company: Notes Page Company (NOTEPAGE)", listHtml, StringComparison.Ordinal);
        Assert.Contains("company-switcher", listHtml, StringComparison.Ordinal);
        Assert.Contains("/account/company", listHtml, StringComparison.Ordinal);

        using var detailResponse = await client.GetAsync($"/workspace/notes/{noteId}");
        var detailHtml = await detailResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.Contains("A safe, synthetic note for the page test.", detailHtml, StringComparison.Ordinal);
        Assert.Contains("Delete", detailHtml, StringComparison.Ordinal);

        using var editResponse = await client.GetAsync($"/workspace/notes/{noteId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Save changes", editHtml, StringComparison.Ordinal);
        Assert.Contains("Navigation test note", editHtml, StringComparison.Ordinal);

        using var createResponse = await client.GetAsync("/workspace/notes/new");
        var createHtml = await createResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.Contains("Create workspace note", createHtml, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"120\"", createHtml, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"4000\"", createHtml, StringComparison.Ordinal);
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
        Assert.Contains("Note not found", detailsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private company note", detailsHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("This content must stay private.", detailsHtml, StringComparison.Ordinal);

        using var editResponse = await ownerClient.GetAsync($"/workspace/notes/{privateNoteId}/edit");
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        Assert.Contains("Note not found", editHtml, StringComparison.Ordinal);
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
