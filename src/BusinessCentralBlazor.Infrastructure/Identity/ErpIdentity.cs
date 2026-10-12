using Microsoft.AspNetCore.Identity;

namespace BusinessCentralBlazor.Infrastructure.Identity;

public sealed class ErpUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ErpRole : IdentityRole<Guid>
{
}
