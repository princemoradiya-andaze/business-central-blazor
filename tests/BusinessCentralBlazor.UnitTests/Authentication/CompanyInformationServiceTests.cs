using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class CompanyInformationServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    [Fact]
    public async Task ProfileIsPersistentCompanyScopedAuditedAndConcurrencyProtected()
    {
        var email = $"profile-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, "Valid-Test-Password-2026!");
        var first = await factory.AddCompanyMembershipAsync(email, "PROFILE1", "First profile company");
        var second = await factory.AddCompanyMembershipAsync(email, "PROFILE2", "Second profile company");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
        ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, id.ToString())], "test"));
        var service = services.GetRequiredService<ICompanyInformationService>();
        var principal = Principal(first.CompanyId);
        var original = await service.GetAsync(principal);
        Assert.Equal("First profile company", original.Name);
        Assert.Equal(Guid.Empty, original.Version);
        var saved = await service.SaveAsync(principal, original with
        {
            Name = " Updated company ", Address = " 24 Test Street ", Address2 = " Floor 2 ", City = " London ",
            County = " Test county ", PostCode = " TEST ", CountryRegionCode = "gb",
            ContactName = " Test Contact ", PhoneNumber = " +44 000 ", VatRegistrationNumber = " TESTVAT ",
            Email = " test@example.invalid ", Website = " https://example.invalid ",
            Logo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="),
            LogoContentType = "image/png"
        });
        Assert.Equal("Updated company", saved.Name);
        Assert.Equal("GB", saved.CountryRegionCode);
        Assert.NotEqual(Guid.Empty, saved.Version);
        await using var readScope = factory.Services.CreateAsyncScope();
        var reread = await readScope.ServiceProvider.GetRequiredService<ICompanyInformationService>().GetAsync(principal);
        Assert.Equal(saved, reread with { Logo = saved.Logo });
        Assert.Equal(saved.Logo, reread.Logo);
        Assert.Equal("Second profile company", (await service.GetAsync(Principal(second.CompanyId))).Name);
        Assert.Null((await service.GetAsync(Principal(second.CompanyId))).Logo);
        await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(principal, original));
        var db = services.GetRequiredService<ErpDbContext>();
        Assert.Equal("Updated company", (await db.Companies.SingleAsync(c => c.Id == first.CompanyId)).Name);
        var audit = await db.AuditEntries.SingleAsync(e => e.CompanyId == first.CompanyId && e.Action == "CompanyInformationUpdated");
        Assert.Equal(user!.Id, audit.ActorUserId);
        Assert.Null(audit.MetadataJson);
        var removedLogo = await service.SaveAsync(principal, saved with { Logo = null });
        Assert.Null(removedLogo.Logo);
        Assert.Null(removedLogo.LogoContentType);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(Principal(Guid.NewGuid())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(Principal(Guid.NewGuid()), saved));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(new ClaimsPrincipal()));
        await factory.RemoveCompanyMembershipAsync(user.Id, first.CompanyId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync(principal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(principal, removedLogo));
    }

    [Fact]
    public async Task InvalidProfileValuesAreRejectedWithoutWrites()
    {
        var email = $"profile-validation-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, "Valid-Test-Password-2026!");
        var company = await factory.AddCompanyMembershipAsync(email, "PROFILEVAL", "Unchanged company");
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var user = await services.GetRequiredService<UserManager<ErpUser>>().FindByEmailAsync(email);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user!.Id.ToString()),
            new Claim(ActiveCompanyClaim.Type, company.CompanyId.ToString())], "test"));
        var service = services.GetRequiredService<ICompanyInformationService>();
        var original = await service.GetAsync(principal);
        foreach (var invalid in new[]
        {
            original with { Name = " " }, original with { Name = new string('a', 161) },
            original with { Address = new string('a', 201) }, original with { CountryRegionCode = "ZZ" },
            original with { Email = "invalid" }, original with { Website = "javascript:alert(1)" },
            original with { Logo = new byte[50], LogoContentType = "image/png" },
            original with { Logo = new byte[CompanyInformationService.MaximumLogoBytes + 1], LogoContentType = "image/jpeg" },
            original with { Logo = "<svg onload='alert(1)'>"u8.ToArray(), LogoContentType = "image/svg+xml" }
        })
            await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(principal, invalid));
        var db = services.GetRequiredService<ErpDbContext>();
        Assert.False(await db.CompanyInformation.AnyAsync(item => item.CompanyId == company.CompanyId));
        Assert.Equal(original, await service.GetAsync(principal));
        await service.SaveAsync(principal, original);
        var emptyProfile = await service.GetAsync(principal);
        emptyProfile.Email = " ";
        Assert.Null(emptyProfile.Email);
        Validator.ValidateObject(emptyProfile, new ValidationContext(emptyProfile), true);
        await service.SaveAsync(principal, emptyProfile);
    }
}
