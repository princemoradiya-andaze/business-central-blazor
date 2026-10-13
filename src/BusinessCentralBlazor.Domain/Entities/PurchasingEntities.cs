using BusinessCentralBlazor.Domain.Enums;

namespace BusinessCentralBlazor.Domain.Entities;

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid VendorId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateOnly OrderDate { get; set; }
    public PurchaseOrderStatus Status { get; set; }
    public Guid? ReceiptJournalBatchId { get; set; }
    public DateOnly? ReceiptPostingDate { get; set; }
    public Guid? InvoiceJournalBatchId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int LineNumber { get; set; }
    public Guid ItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public Guid? TaxCodeId { get; set; }
}

public sealed class PurchaseReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid JournalBatchId { get; set; }
    public DateOnly PostingDate { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
}

public sealed class PurchaseInvoice
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid VendorId { get; set; }
    public Guid JournalBatchId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
}
