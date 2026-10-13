using BusinessCentralBlazor.Domain.Enums;

namespace BusinessCentralBlazor.Domain.Entities;

public sealed class SalesOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CustomerId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public SalesOrderStatus Status { get; set; }
    public Guid? ShipmentJournalBatchId { get; set; }
    public DateOnly? ShipmentPostingDate { get; set; }
    public Guid? InvoiceJournalBatchId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class SalesOrderLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid SalesAccountId { get; set; }
    public Guid? TaxCodeId { get; set; }
}

public sealed class SalesInvoice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid JournalBatchId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
}
