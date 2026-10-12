using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using BusinessCentralBlazor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class AuthenticationFlowTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>, IAsyncLifetime
{
    private const string TestEmail = "auth-test@example.invalid";
    private const string TestPassword = "Valid-Test-Password-2026!";

    public async Task InitializeAsync() =>
        await factory.CreateTestUserAsync(TestEmail, TestPassword);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void IdentityDefaultsRequireStrongPasswordsAndEnableLockout()
    {
        var options = new IdentityOptions();

        ErpIdentityDefaults.Configure(options);

        Assert.Equal(12, options.Password.RequiredLength);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireNonAlphanumeric);
        Assert.True(options.User.RequireUniqueEmail);
        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
    }

    [Fact]
    public async Task AnonymousUsersAreRedirectedToSignIn()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task LoginIsPublicAndRegistrationRouteDoesNotExist()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var login = await client.GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain("Register", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        await SignInAsync(client);
        var company = await factory.AddCompanyMembershipAsync(
            TestEmail,
            $"REG-{Guid.NewGuid():N}",
            "Registration-route test company");
        await SelectCompanyAsync(client, company.CompanyId);
        var registration = await client.GetAsync("/account/register");

        Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
    }

    [Fact]
    public async Task LoginAutomaticallySelectsTheOnlyEligibleCompanyAndReturnsToRequestedLocalPage()
    {
        var email = $"single-company-login-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "SINGLE", "Single Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await PostLoginAsync(client, email, TestPassword, "/workspace/notes");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/workspace/notes", response.Headers.Location?.OriginalString);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains(
            $"Company: {company.Name} ({company.Code})",
            await home.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoginWithoutEligibleCompaniesDoesNotSelectACompany()
    {
        var email = $"no-company-login-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await PostLoginAsync(client, email, TestPassword);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/account/access-denied", home.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task LoginRequiresSelectionWhenMultipleCompaniesAreEligibleAndPreservesLocalReturnUrl()
    {
        var email = $"multiple-company-login-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "MULTI1", "First Company");
        await factory.AddCompanyMembershipAsync(email, "MULTI2", "Second Company");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var signIn = await PostLoginAsync(client, email, TestPassword, "/workspace/notes");

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/account/company?returnUrl=%2Fworkspace%2Fnotes", signIn.Headers.Location?.OriginalString);
        var companyPage = await client.GetAsync(signIn.Headers.Location);
        var html = await companyPage.Content.ReadAsStringAsync();
        var token = GetAntiforgeryToken(html);
        using var selection = await client.PostAsync(
            "/account/company",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["SelectedCompanyId"] = company.CompanyId.ToString(),
                ["ReturnUrl"] = "/workspace/notes"
            }));

        Assert.Equal(HttpStatusCode.Redirect, selection.StatusCode);
        Assert.Equal("/workspace/notes", selection.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task InvalidCredentialsUseTheSameGenericError()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var unknownAccountResponse = await SubmitLoginAsync(client, "missing@example.invalid", TestPassword);
        var invalidPasswordResponse = await SubmitLoginAsync(client, TestEmail, "Wrong-Password-2026!");
        const string genericError = "Sign-in failed. Check your credentials or contact an administrator.";

        Assert.Contains(genericError, unknownAccountResponse, StringComparison.Ordinal);
        Assert.Contains(genericError, invalidPasswordResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidCredentialsSignInAndPostSignsOut()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await SignInAsync(client);
        var company = await factory.AddCompanyMembershipAsync(
            TestEmail,
            $"AUTH-{Guid.NewGuid():N}",
            "Authentication-flow test company");
        await SelectCompanyAsync(client, company.CompanyId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Contains("Sign out", homeHtml, StringComparison.Ordinal);

        var token = GetAntiforgeryToken(homeHtml);
        using var signOut = await client.PostAsync(
            "/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, signOut.StatusCode);
        Assert.Equal("/account/login", signOut.Headers.Location?.OriginalString);

        using var anonymousHome = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, anonymousHome.StatusCode);
    }

    [Fact]
    public async Task LoginIgnoresExternalReturnUrls()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var loginPage = await client.GetAsync("/account/login?returnUrl=https%3A%2F%2Fevil.example");
        var token = GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ReturnUrl"] = "https://evil.example",
                ["Input.Email"] = TestEmail,
                ["Input.Password"] = TestPassword,
                ["Input.RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client)
    {
        return await PostLoginAsync(client, TestEmail, TestPassword, "/");
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

    private static async Task<string> SubmitLoginAsync(HttpClient client, string email, string password)
    {
        using var response = await PostLoginAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(
        HttpClient client,
        string email,
        string password,
        string returnUrl = "/")
    {
        var loginPage = await client.GetAsync("/account/login");
        var token = GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        return await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ReturnUrl"] = returnUrl,
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false"
            }));
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
