using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Web.Services;

public sealed record CompanyInformationInput
{
    public Guid Version { get; set; }
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [StringLength(200)] public string Address { get; set; } = "";
    [StringLength(200)] public string Address2 { get; set; } = "";
    [StringLength(100)] public string City { get; set; } = "";
    [StringLength(100)] public string County { get; set; } = "";
    [StringLength(20)] public string PostCode { get; set; } = "";
    [RegularExpression("^([A-Za-z]{2})?$", ErrorMessage = "Use a two-letter country/region code, such as GB or IN.")]
    public string CountryRegionCode { get; set; } = "";
    [StringLength(100)] public string ContactName { get; set; } = "";
    [StringLength(40)] public string PhoneNumber { get; set; } = "";
    [StringLength(40)] public string VatRegistrationNumber { get; set; } = "";
    private string? email;
    [StringLength(254), EmailAddress]
    public string? Email
    {
        get => email;
        set => email = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    [StringLength(250)] public string Website { get; set; } = "";
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    public string? LogoUrl => Logo is null ? null : $"data:{LogoContentType};base64,{Convert.ToBase64String(Logo)}";
}

public interface ICompanyInformationService
{
    Task<CompanyInformationInput> GetAsync(ClaimsPrincipal user);
    Task<CompanyInformationInput> SaveAsync(ClaimsPrincipal user, CompanyInformationInput input);
}

public sealed class CompanyInformationService(
    IDbContextFactory<ErpDbContext> factory, ICompanyScopeAuthorizer authorizer) : ICompanyInformationService
{
    public const int MaximumLogoBytes = 2 * 1024 * 1024;

    public async Task<CompanyInformationInput> GetAsync(ClaimsPrincipal user)
    {
        var company = await authorizer.ResolveActiveCompanyAsync(user) ?? throw new UnauthorizedAccessException();
        await using var db = await factory.CreateDbContextAsync();
        var profile = await db.CompanyInformation.AsNoTracking().SingleOrDefaultAsync(item => item.CompanyId == company.CompanyId);
        return new()
        {
            Name = company.Name, Version = profile?.Version ?? Guid.Empty,
            Address = profile?.Address ?? "", Address2 = profile?.Address2 ?? "", City = profile?.City ?? "",
            County = profile?.County ?? "", PostCode = profile?.PostCode ?? "", CountryRegionCode = profile?.CountryRegionCode ?? "",
            ContactName = profile?.ContactName ?? "", PhoneNumber = profile?.PhoneNumber ?? "",
            VatRegistrationNumber = profile?.VatRegistrationNumber ?? "",
            Email = string.IsNullOrWhiteSpace(profile?.Email) ? null : profile.Email,
            Website = profile?.Website ?? "", Logo = profile?.Logo, LogoContentType = profile?.LogoContentType
        };
    }

    public async Task<CompanyInformationInput> SaveAsync(ClaimsPrincipal user, CompanyInformationInput input)
    {
        var scope = await authorizer.ResolveActiveCompanyAsync(user) ?? throw new UnauthorizedAccessException();
        var value = input with
        {
            Name = input.Name?.Trim() ?? "", Address = input.Address?.Trim() ?? "", Address2 = input.Address2?.Trim() ?? "",
            City = input.City?.Trim() ?? "", County = input.County?.Trim() ?? "", PostCode = input.PostCode?.Trim() ?? "",
            CountryRegionCode = input.CountryRegionCode?.Trim().ToUpperInvariant() ?? "", ContactName = input.ContactName?.Trim() ?? "",
            PhoneNumber = input.PhoneNumber?.Trim() ?? "", VatRegistrationNumber = input.VatRegistrationNumber?.Trim() ?? "",
            Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(), Website = input.Website?.Trim() ?? "",
            Logo = input.Logo?.ToArray()
        };
        Validator.ValidateObject(value, new ValidationContext(value), true);
        if (value.CountryRegionCode.Length > 0 &&
            !System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
                .Any(culture => new System.Globalization.RegionInfo(culture.Name).TwoLetterISORegionName == value.CountryRegionCode))
            throw new ValidationException("Enter a recognized two-letter country/region code.");
        if (value.Website.Length > 0 && (!Uri.TryCreate(value.Website, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)))
            throw new ValidationException("Enter a website beginning with https:// or http://.");
        ValidateLogo(value.Logo, value.LogoContentType);
        if (value.Logo is null) value.LogoContentType = null;
        await using var db = await factory.CreateDbContextAsync();
        var company = await db.Companies.SingleAsync(item => item.Id == scope.CompanyId && item.TenantId == scope.TenantId);
        var profile = await db.CompanyInformation.SingleOrDefaultAsync(item => item.CompanyId == scope.CompanyId);
        if ((profile?.Version ?? Guid.Empty) != value.Version)
            throw new ValidationException("Company information changed in another session. Close and reopen the card before saving.");
        if (profile is null)
        {
            profile = new CompanyInformation { CompanyId = scope.CompanyId };
            db.CompanyInformation.Add(profile);
        }
        company.Name = value.Name;
        profile.Address = value.Address; profile.Address2 = value.Address2; profile.City = value.City;
        profile.County = value.County; profile.PostCode = value.PostCode; profile.CountryRegionCode = value.CountryRegionCode;
        profile.ContactName = value.ContactName; profile.PhoneNumber = value.PhoneNumber;
        profile.VatRegistrationNumber = value.VatRegistrationNumber; profile.Email = value.Email ?? "";
        profile.Website = value.Website; profile.Logo = value.Logo; profile.LogoContentType = value.LogoContentType;
        profile.Version = value.Version = Guid.NewGuid();
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(), CompanyId = scope.CompanyId,
            ActorUserId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null,
            Action = "CompanyInformationUpdated", EntityType = nameof(CompanyInformation),
            EntityId = scope.CompanyId.ToString(), OccurredAtUtc = DateTimeOffset.UtcNow
        });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ValidationException("Company information changed in another session. Close and reopen the card before saving."); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ValidationException("Company information was created in another session. Close and reopen the card before saving.");
        }
        return value;
    }

    public static void ValidateLogo(byte[]? bytes, string? contentType)
    {
        if (bytes is null) return;
        var valid = bytes.Length is > 12 and <= MaximumLogoBytes && contentType switch
        {
            "image/png" => bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            "image/webp" => bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new ValidationException("Choose a PNG, JPEG or WebP logo up to 2 MB. Its file type must match its contents.");
    }
}
