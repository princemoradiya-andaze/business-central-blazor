using BusinessCentralBlazor.Domain.Enums;

namespace BusinessCentralBlazor.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CurrencyCode { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class Vendor
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CurrencyCode { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class Item
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public string BaseUnitOfMeasure { get; set; } = string.Empty;
    public decimal UnitCost { get; set; }
    public bool IsBlocked { get; set; }
}

public sealed class NumberSeries
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long NextNumber { get; set; } = 1;
    public int MinimumDigits { get; set; } = 1;
    public byte[] RowVersion { get; set; } = [];
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? MetadataJson { get; set; }
}
