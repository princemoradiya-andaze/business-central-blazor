using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class DimensionServiceTests(AuthenticationWebApplicationFactory factory)
    : IClassFixture<AuthenticationWebApplicationFactory>
{
    private const string TestPassword = "Valid-Test-Password-2026!";

    [Fact]
    public async Task DimensionsAndValuesCanBeCreatedListedAndUpdated()
    {
        var email = $"dimensions-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "DIMENSIONS", "Dimensions Company");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();

        var dimensionId = await service.CreateAsync(principal, new DimensionInput
        {
            Code = " DEPT ",
            Name = " Department "
        });
        var valueId = await service.CreateValueAsync(principal, dimensionId, new DimensionValueInput
        {
            Code = "SALES",
            Name = "Sales"
        });

        var listedDimension = Assert.Single(await service.GetAllAsync(principal));
        Assert.Equal("DEPT", listedDimension.Code);
        Assert.Equal(1, listedDimension.ValueCount);
        Assert.Equal("Department", listedDimension.Name);
        Assert.Equal("SALES", Assert.Single(await service.GetValuesAsync(principal, dimensionId)).Code);

        Assert.True(await service.UpdateAsync(principal, dimensionId, new DimensionInput
        {
            Code = "DEPT",
            Name = "Departmental analysis"
        }));
        Assert.True(await service.UpdateValueAsync(principal, dimensionId, valueId, new DimensionValueInput
        {
            Code = "SALES",
            Name = "Sales team",
            IsBlocked = true
        }));

        Assert.Equal("Departmental analysis", (await service.GetAsync(principal, dimensionId))?.Name);
        var updatedValue = await service.GetValueAsync(principal, dimensionId, valueId);
        Assert.NotNull(updatedValue);
        Assert.Equal("Sales team", updatedValue.Name);
        Assert.True(updatedValue.IsBlocked);
    }

    [Fact]
    public async Task DimensionsAndValuesAreCompanyScopedAndCodesAreUniqueAtTheirLevel()
    {
        var firstEmail = $"dimension-owner-{Guid.NewGuid():N}@example.invalid";
        var secondEmail = $"dimension-other-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(firstEmail, TestPassword);
        await factory.CreateTestUserAsync(secondEmail, TestPassword);
        var firstCompany = await factory.AddCompanyMembershipAsync(firstEmail, "DIMONE", "Dimension One");
        var secondCompany = await factory.AddCompanyMembershipAsync(secondEmail, "DIMTWO", "Dimension Two");
        var firstPrincipal = await CreatePrincipalAsync(firstEmail, firstCompany.CompanyId);
        var secondPrincipal = await CreatePrincipalAsync(secondEmail, secondCompany.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();

        var firstDimensionId = await service.CreateAsync(firstPrincipal, new DimensionInput
        {
            Code = "DEPT",
            Name = "Department"
        });
        var anotherDimensionId = await service.CreateAsync(firstPrincipal, new DimensionInput
        {
            Code = "REGION",
            Name = "Region"
        });
        var firstValueId = await service.CreateValueAsync(firstPrincipal, firstDimensionId, new DimensionValueInput
        {
            Code = "NORTH",
            Name = "North"
        });
        var otherDimensionValueId = await service.CreateValueAsync(
            firstPrincipal,
            anotherDimensionId,
            new DimensionValueInput { Code = "NORTH", Name = "Northern region" });

        var secondDimensionId = await service.CreateAsync(secondPrincipal, new DimensionInput
        {
            Code = "DEPT",
            Name = "Other company's department"
        });
        await service.CreateValueAsync(secondPrincipal, secondDimensionId, new DimensionValueInput
        {
            Code = "NORTH",
            Name = "Other company's north"
        });

        Assert.Null(await service.GetAsync(secondPrincipal, firstDimensionId));
        Assert.Null(await service.GetValueAsync(secondPrincipal, firstDimensionId, firstValueId));
        Assert.Empty(await service.GetValuesAsync(secondPrincipal, firstDimensionId));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(firstPrincipal, new DimensionInput
        {
            Code = "DEPT",
            Name = "Duplicate department"
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateValueAsync(
            firstPrincipal,
            firstDimensionId,
            new DimensionValueInput { Code = "NORTH", Name = "Duplicate value" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateValueAsync(
            secondPrincipal,
            firstDimensionId,
            new DimensionValueInput { Code = "SOUTH", Name = "Cross-company value" }));

        Assert.Equal("North", (await service.GetValueAsync(firstPrincipal, firstDimensionId, firstValueId))?.Name);
        Assert.Equal("Northern region",
            (await service.GetValueAsync(firstPrincipal, anotherDimensionId, otherDimensionValueId))?.Name);
    }

    [Fact]
    public async Task InvalidDimensionAndValueEditsDoNotChangePersistedData()
    {
        var email = $"dimension-rules-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "DIMRULES", "Dimension Rules");
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();
        var firstDimensionId = await service.CreateAsync(principal, new DimensionInput
        {
            Code = "DEPT",
            Name = "Department"
        });
        var secondDimensionId = await service.CreateAsync(principal, new DimensionInput
        {
            Code = "REGION",
            Name = "Region"
        });
        var firstValueId = await service.CreateValueAsync(principal, firstDimensionId, new DimensionValueInput
        {
            Code = "SALES",
            Name = "Sales"
        });
        var secondValueId = await service.CreateValueAsync(principal, firstDimensionId, new DimensionValueInput
        {
            Code = "OPS",
            Name = "Operations"
        });

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(
            principal,
            firstDimensionId,
            new DimensionInput { Code = "REGION", Name = "Department" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateValueAsync(
            principal,
            firstDimensionId,
            secondValueId,
            new DimensionValueInput { Code = "SALES", Name = "Duplicate" }));
        Assert.False(await service.UpdateValueAsync(
            principal,
            secondDimensionId,
            firstValueId,
            new DimensionValueInput { Code = "OTHER", Name = "Wrong parent" }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(principal, new DimensionInput
        {
            Code = " ",
            Name = "Invalid"
        }));
        await Assert.ThrowsAsync<ValidationException>(() => service.CreateValueAsync(
            principal,
            firstDimensionId,
            new DimensionValueInput { Code = "   ", Name = "Invalid" }));

        Assert.Equal("Department", (await service.GetAsync(principal, firstDimensionId))?.Name);
        Assert.Equal("Operations",
            (await service.GetValueAsync(principal, firstDimensionId, secondValueId))?.Name);
    }

    [Fact]
    public async Task MissingCompanyIsRejectedAndOtherCompanyRowsRemainPrivate()
    {
        var email = $"dimension-no-company-{Guid.NewGuid():N}@example.invalid";
        await factory.CreateTestUserAsync(email, TestPassword);
        var company = await factory.AddCompanyMembershipAsync(email, "DIMAUTH", "Dimension Authorization");
        var userId = await GetUserIdAsync(email);
        var noCompanyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "test"));
        var principal = await CreatePrincipalAsync(email, company.CompanyId);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IDimensionService>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAllAsync(noCompanyPrincipal));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            noCompanyPrincipal,
            new DimensionInput { Code = "DEPT", Name = "Department" }));

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

        await using var dbScope = factory.Services.CreateAsyncScope();
        var dbContext = dbScope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var persistedDimension = await dbContext.Dimensions.SingleAsync(item => item.Id == dimensionId);
        var persistedValue = await dbContext.DimensionValues.SingleAsync(item => item.Id == valueId);
        Assert.Equal(company.CompanyId, persistedDimension.CompanyId);
        Assert.Equal(company.CompanyId, persistedValue.CompanyId);
    }

    private async Task<ClaimsPrincipal> CreatePrincipalAsync(string email, Guid companyId)
    {
        var userId = await GetUserIdAsync(email);
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ActiveCompanyClaim.Type, companyId.ToString())
        ], "test"));
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }
}
