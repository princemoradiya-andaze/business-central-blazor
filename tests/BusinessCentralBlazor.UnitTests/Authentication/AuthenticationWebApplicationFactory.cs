using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class AuthenticationWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"auth-tests-{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ErpDatabase"] = "Server=localhost;Database=IdentityTests;Trusted_Connection=True"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<ErpDbContext>>();
            services.RemoveAll<DbContextOptions<ErpDbContext>>();
            services.RemoveAll<ErpDbContext>();
            services.AddDbContext<ErpDbContext>(options => options.UseInMemoryDatabase(databaseName));
        });
    }

    public async Task CreateTestUserAsync(string email, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ErpRole>>();
        if (!await roleManager.RoleExistsAsync(ErpIdentityDefaults.UserRole))
        {
            var roleResult = await roleManager.CreateAsync(new ErpRole
            {
                Name = ErpIdentityDefaults.UserRole
            });
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create the test user role: {string.Join(", ", roleResult.Errors.Select(error => error.Code))}");
            }
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ErpUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Authentication test user"
            };
            var userResult = await userManager.CreateAsync(user, password);
            if (!userResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create the test account: {string.Join(", ", userResult.Errors.Select(error => error.Code))}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, ErpIdentityDefaults.UserRole))
        {
            var assignmentResult = await userManager.AddToRoleAsync(user, ErpIdentityDefaults.UserRole);
            if (!assignmentResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not assign the test account role: {string.Join(", ", assignmentResult.Errors.Select(error => error.Code))}");
            }
        }
    }

    public async Task<CompanyMembershipOption> AddCompanyMembershipAsync(
        string email,
        string companyCode,
        string companyName,
        bool companyIsActive = true)
    {
        await using var scope = Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<ErpDbContext>();
        var userManager = services.GetRequiredService<UserManager<ErpUser>>();
        var user = await userManager.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"The test account '{email}' does not exist.");
        var tenantId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        dbContext.AddRange(
            new Tenant
            {
                Id = tenantId,
                Name = $"Tenant {companyCode}",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new Company
            {
                Id = companyId,
                TenantId = tenantId,
                Code = companyCode,
                Name = companyName,
                IsActive = companyIsActive,
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new TenantMembership
            {
                TenantId = tenantId,
                UserId = user.Id,
                RoleName = "TenantUser",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new CompanyMembership
            {
                TenantId = tenantId,
                CompanyId = companyId,
                UserId = user.Id,
                RoleName = "CompanyUser",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        await dbContext.SaveChangesAsync();

        return new CompanyMembershipOption(tenantId, companyId, companyCode, companyName, "CompanyUser");
    }

    public async Task RemoveCompanyMembershipAsync(Guid userId, Guid companyId)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var membership = await dbContext.CompanyMemberships.SingleAsync(
            item => item.UserId == userId && item.CompanyId == companyId);
        dbContext.CompanyMemberships.Remove(membership);
        await dbContext.SaveChangesAsync();
    }

    public async Task SetCompanyActiveAsync(Guid companyId, bool isActive)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var company = await dbContext.Companies.SingleAsync(item => item.Id == companyId);
        company.IsActive = isActive;
        await dbContext.SaveChangesAsync();
    }
}
