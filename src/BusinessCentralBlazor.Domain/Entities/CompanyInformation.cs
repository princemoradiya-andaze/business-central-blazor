namespace BusinessCentralBlazor.Domain.Entities;

public sealed class CompanyInformation
{
    public Guid CompanyId { get; set; }
    public string Address { get; set; } = "";
    public string Address2 { get; set; } = "";
    public string City { get; set; } = "";
    public string County { get; set; } = "";
    public string PostCode { get; set; } = "";
    public string CountryRegionCode { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string VatRegistrationNumber { get; set; } = "";
    public string Email { get; set; } = "";
    public string Website { get; set; } = "";
    public byte[]? Logo { get; set; }
    public string? LogoContentType { get; set; }
    public Guid Version { get; set; }
}
