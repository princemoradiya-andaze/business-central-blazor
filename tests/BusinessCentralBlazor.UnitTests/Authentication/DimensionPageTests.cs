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

public sealed class DimensionPageTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task ActiveCompanyMemberCanViewDimensionAndValuePages()
    {
        var email = $"dimension-pages-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "DIMPAGE", "Dimension Page Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();
        var dimensionId = await service.CreateAsync(principal, new DimensionInput
        {
            Code = "DEPT",
            Name = "Department"
        });
        var valueId = await service.CreateValueAsync(principal, dimensionId, new DimensionValueInput
        {
            Code = "SALES",
            Name = "Sales"
        });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email);
        await SelectCompanyAsync(client, company.CompanyId);

        using var listResponse = await client.GetAsync("/financial/dimensions");
        var listHtml = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains("Dimensions", listHtml, StringComparison.Ordinal);
        Assert.Contains("DEPT", listHtml, StringComparison.Ordinal);
        Assert.Contains("Department", listHtml, StringComparison.Ordinal);
        Assert.Contains("/financial/dimensions/new", listHtml, StringComparison.Ordinal);
        var allPosition = listHtml.IndexOf("id=\"dimension-status-quick\"", StringComparison.Ordinal);
        var newPosition = listHtml.IndexOf("id=\"new-dimension-action\"", StringComparison.Ordinal);
        Assert.True(allPosition >= 0 && newPosition > allPosition, "The New action must follow All in the toolbar.");
        Assert.Contains("record-row-selected", listHtml, StringComparison.Ordinal);

        using var newDimensionResponse = await client.GetAsync("/financial/dimensions/new");
        var newDimensionHtml = await newDimensionResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, newDimensionResponse.StatusCode);
        Assert.Contains("New Dimension Card", newDimensionHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"dimension-code\"", newDimensionHtml, StringComparison.Ordinal);

        using var editDimensionResponse = await client.GetAsync($"/financial/dimensions/{dimensionId}/edit");
        var editDimensionHtml = await editDimensionResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editDimensionResponse.StatusCode);
        Assert.Contains("DEPT — Department", editDimensionHtml, StringComparison.Ordinal);

        using var dimensionResponse = await client.GetAsync($"/financial/dimensions/{dimensionId}");
        var dimensionHtml = await dimensionResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dimensionResponse.StatusCode);
        Assert.Contains("Sales", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains("/financial/dimensions/", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains("New value", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"dimension-value-status\"", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains("id=\"new-dimension-value-action\"", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains($"id=\"dimension-value-record-{valueId}\"", dimensionHtml, StringComparison.Ordinal);
        Assert.Contains("Choose view", dimensionHtml, StringComparison.Ordinal);
        Assert.DoesNotContain(">Edit</a>", dimensionHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Edit dimension", dimensionHtml, StringComparison.Ordinal);

        using var newValueResponse = await client.GetAsync(
            $"/financial/dimensions/{dimensionId}/values/new");
        var newValueHtml = await newValueResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, newValueResponse.StatusCode);
        Assert.Contains("New Dimension Value Card", newValueHtml, StringComparison.Ordinal);
        Assert.Contains("dimension-value-code", newValueHtml, StringComparison.Ordinal);

        using var editValueResponse = await client.GetAsync(
            $"/financial/dimensions/{dimensionId}/values/{valueId}/edit");
        var editValueHtml = await editValueResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, editValueResponse.StatusCode);
        Assert.Contains("Dimension Value Card", editValueHtml, StringComparison.Ordinal);
        Assert.Contains("SALES — Sales", HttpUtility.HtmlDecode(editValueHtml), StringComparison.Ordinal);
        Assert.Contains("value=\"SALES\"", editValueHtml, StringComparison.Ordinal);
        Assert.Contains("Blocked", editValueHtml, StringComparison.Ordinal);

        using var missingValueResponse = await client.GetAsync(
            $"/financial/dimensions/{dimensionId}/values/{Guid.NewGuid()}/edit");
        var missingValueHtml = await missingValueResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, missingValueResponse.StatusCode);
        Assert.Contains("Dimension value not found", missingValueHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"dimension-value-code\"", missingValueHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnonymousAndCrossCompanyDimensionRequestsDoNotRevealPrivateData()
    {
        var ownerEmail = $"dimension-page-owner-{Guid.NewGuid():N}@example.invalid";
        var otherEmail = $"dimension-page-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(ownerEmail, TestPassword);
        await factory.CreateTestUserAsync(otherEmail, TestPassword);
        var ownerCompany = await factory.AddCompanyMembershipAsync(ownerEmail, "DIMPO", "Dimension Owner");
        var otherCompany = await factory.AddCompanyMembershipAsync(otherEmail, "DIMPT", "Dimension Other");
        var otherPrincipal = await CreatePrincipalAsync(otherEmail, otherCompany.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();
        var privateDimensionId = await service.CreateAsync(otherPrincipal, new DimensionInput
        {
            Code = "PRIVATE",
            Name = "Private dimension"
        });
        var privateValueId = await service.CreateValueAsync(
            otherPrincipal,
            privateDimensionId,
            new DimensionValueInput { Code = "SECRET", Name = "Private value" });

        using var anonymousClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var anonymousResponse = await anonymousClient.GetAsync("/financial/dimensions");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.StartsWith("/account/login", anonymousResponse.Headers.Location?.AbsolutePath);

        using var ownerClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await SignInAsync(ownerClient, ownerEmail);
        await SelectCompanyAsync(ownerClient, ownerCompany.CompanyId);
        using var dimensionResponse = await ownerClient.GetAsync($"/financial/dimensions/{privateDimensionId}");
        var dimensionHtml = await dimensionResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, dimensionResponse.StatusCode);
        Assert.Contains("Dimension not found", dimensionHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private dimension", dimensionHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private value", dimensionHtml, StringComparison.Ordinal);

        using var valueResponse = await ownerClient.GetAsync(
            $"/financial/dimensions/{privateDimensionId}/values/{privateValueId}/edit");
        var valueHtml = await valueResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, valueResponse.StatusCode);
        Assert.Contains("Dimension value not found", valueHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Private value", valueHtml, StringComparison.Ordinal);
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
