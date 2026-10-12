using Microsoft.AspNetCore.Identity;

namespace BusinessCentralBlazor.Infrastructure.Identity;

public sealed class ErpUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ErpRole : IdentityRole<Guid>
{
}

public static class ErpIdentityDefaults
{
    public const string AdministratorRole = "Administrator";
    public const string UserRole = "User";

    public static void Configure(IdentityOptions options)
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredUniqueChars = 4;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;
    }
}
