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
        var registration = await client.GetAsync("/account/register");

        Assert.Equal(HttpStatusCode.NotFound, registration.StatusCode);
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
        var loginPage = await client.GetAsync("/account/login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var token = GetAntiforgeryToken(loginHtml);

        return await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ReturnUrl"] = "/",
                ["Input.Email"] = TestEmail,
                ["Input.Password"] = TestPassword,
                ["Input.RememberMe"] = "false"
            }));
    }

    private static async Task<string> SubmitLoginAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetAsync("/account/login");
        var token = GetAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        using var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["ReturnUrl"] = "/",
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.RememberMe"] = "false"
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
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
