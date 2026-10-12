using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Identity;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

if (args.Length != 1 || args[0] is not ("bootstrap-admin" or "create-user" or "setup-local-demo"))
{
    Console.Error.WriteLine("Usage: BusinessCentralBlazor.AdminTool <bootstrap-admin|create-user|setup-local-demo>");
    return 2;
}

var bootstrapAdmin = args[0] == "bootstrap-admin";
var setupLocalDemo = args[0] == "setup-local-demo";
var environmentVariableNames = bootstrapAdmin
    ? new[] { "BC_ADMIN_EMAIL", "BC_ADMIN_PASSWORD", "BC_ADMIN_DISPLAY_NAME" }
    : setupLocalDemo
        ? new[] { "BC_DEMO_USER_EMAIL" }
        : new[] { "BC_USER_EMAIL", "BC_USER_PASSWORD", "BC_USER_DISPLAY_NAME" };
var values = environmentVariableNames.ToDictionary(
    name => name,
    name => Environment.GetEnvironmentVariable(name),
    StringComparer.Ordinal);
var missingVariables = values.Where(pair => string.IsNullOrWhiteSpace(pair.Value))
    .Select(pair => pair.Key)
    .ToArray();
if (missingVariables.Length > 0)
{
    Console.Error.WriteLine($"Set the required environment variable(s): {string.Join(", ", missingVariables)}.");
    return 2;
}

var builder = Host.CreateApplicationBuilder();
var connectionString = builder.Configuration.GetConnectionString("ErpDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__ErpDatabase before running the administrator tool.");
    return 2;
}

builder.Services.AddDbContext<ErpDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDataProtection();
builder.Services.AddIdentityCore<ErpUser>(ErpIdentityDefaults.Configure)
    .AddRoles<ErpRole>()
    .AddEntityFrameworkStores<ErpDbContext>()
    .AddDefaultTokenProviders();

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var dbContext = services.GetRequiredService<ErpDbContext>();
if (!await dbContext.Database.CanConnectAsync())
{
    Console.Error.WriteLine("Cannot connect to the configured database. Apply migrations and verify the connection first.");
    return 1;
}

var userManager = services.GetRequiredService<UserManager<ErpUser>>();
var roleManager = services.GetRequiredService<RoleManager<ErpRole>>();
if (setupLocalDemo)
{
    var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
    if (!string.Equals(databaseName, "BusinessCentralBlazor_Phase0", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Local demo setup is restricted to the BusinessCentralBlazor_Phase0 database.");
        return 1;
    }

    Console.WriteLine($"This will add one demo tenant and company to database '{databaseName}'.");
    Console.Write("Type CREATE LOCAL DEMO to continue: ");
    if (!string.Equals(Console.ReadLine(), "CREATE LOCAL DEMO", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Confirmation did not match; no changes were made.");
        return 1;
    }

    if (!await roleManager.RoleExistsAsync(ErpIdentityDefaults.AdministratorRole))
    {
        Console.Error.WriteLine("The Administrator role does not exist.");
        return 1;
    }

    var demoEmail = values["BC_DEMO_USER_EMAIL"]!.Trim();
    var demoUser = await userManager.FindByEmailAsync(demoEmail);
    if (demoUser is null ||
        !await userManager.IsInRoleAsync(demoUser, ErpIdentityDefaults.AdministratorRole))
    {
        Console.Error.WriteLine("The specified account must exist and have the Administrator role.");
        return 1;
    }

    if (await dbContext.Tenants.AnyAsync() || await dbContext.Companies.AnyAsync())
    {
        Console.Error.WriteLine("Local demo setup requires a database with no tenants or companies; no changes were made.");
        return 1;
    }

    var now = DateTimeOffset.UtcNow;
    var tenant = new Tenant
    {
        Id = Guid.NewGuid(),
        Name = "Local Demo Tenant",
        CreatedAtUtc = now
    };
    var company = new Company
    {
        Id = Guid.NewGuid(),
        TenantId = tenant.Id,
        Code = "LOCAL-DEMO",
        Name = "Local Demo Company",
        IsActive = true,
        CreatedAtUtc = now
    };

    await using var transaction = await dbContext.Database.BeginTransactionAsync();
    dbContext.Tenants.Add(tenant);
    dbContext.Companies.Add(company);
    dbContext.TenantMemberships.Add(new TenantMembership
    {
        TenantId = tenant.Id,
        UserId = demoUser.Id,
        RoleName = ErpIdentityDefaults.AdministratorRole,
        CreatedAtUtc = now
    });
    dbContext.CompanyMemberships.Add(new CompanyMembership
    {
        TenantId = tenant.Id,
        CompanyId = company.Id,
        UserId = demoUser.Id,
        RoleName = ErpIdentityDefaults.AdministratorRole,
        CreatedAtUtc = now
    });
    await dbContext.SaveChangesAsync();
    await transaction.CommitAsync();

    Console.WriteLine($"Created Local Demo Tenant and active company LOCAL-DEMO; assigned {demoEmail} as Administrator.");
    return 0;
}

var assignmentRole = bootstrapAdmin ? ErpIdentityDefaults.AdministratorRole : ErpIdentityDefaults.UserRole;
if (bootstrapAdmin)
{
    if (await roleManager.RoleExistsAsync(ErpIdentityDefaults.AdministratorRole) &&
        (await userManager.GetUsersInRoleAsync(ErpIdentityDefaults.AdministratorRole)).Count > 0)
    {
        Console.Error.WriteLine("An administrator already exists; the one-time bootstrap was refused.");
        return 1;
    }
}
else if (!await roleManager.RoleExistsAsync(ErpIdentityDefaults.AdministratorRole) ||
         (await userManager.GetUsersInRoleAsync(ErpIdentityDefaults.AdministratorRole)).Count == 0)
{
    Console.Error.WriteLine("Create an administrator with bootstrap-admin before creating user accounts.");
    return 1;
}

if (!await roleManager.RoleExistsAsync(assignmentRole))
{
    var roleResult = await roleManager.CreateAsync(new ErpRole { Name = assignmentRole });
    if (!roleResult.Succeeded)
    {
        PrintIdentityErrors("Could not create the required role", roleResult);
        return 1;
    }
}

var email = values[environmentVariableNames[0]]!.Trim();
var displayName = values[environmentVariableNames[2]]!.Trim();
if (await userManager.FindByEmailAsync(email) is not null)
{
    Console.Error.WriteLine("An account with that email already exists.");
    return 1;
}

var user = new ErpUser
{
    UserName = email,
    Email = email,
    DisplayName = displayName,
    EmailConfirmed = true
};
var createResult = await userManager.CreateAsync(user, values[environmentVariableNames[1]]!);
if (!createResult.Succeeded)
{
    PrintIdentityErrors("Could not create the account", createResult);
    return 1;
}

var assignmentResult = await userManager.AddToRoleAsync(user, assignmentRole);
if (!assignmentResult.Succeeded)
{
    PrintIdentityErrors("Could not assign the account role", assignmentResult);
    var deleteResult = await userManager.DeleteAsync(user);
    if (!deleteResult.Succeeded)
    {
        PrintIdentityErrors("Could not remove the unassigned account; manual review is required", deleteResult);
    }

    return 1;
}

Console.WriteLine($"Created the {assignmentRole} account for {email}.");
return 0;

static void PrintIdentityErrors(string message, IdentityResult result)
{
    Console.Error.WriteLine(message + ":");
    foreach (var error in result.Errors)
    {
        Console.Error.WriteLine($"- {error.Code}: {error.Description}");
    }
}
