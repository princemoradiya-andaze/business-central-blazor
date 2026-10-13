namespace BusinessCentralBlazor.Domain.Enums;

public enum AccountCategory
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}

public enum AccountType
{
    Posting,
    Heading,
    Total,
    BeginTotal,
    EndTotal
}

public enum TaxPostingType
{
    Liability,
    Recoverable
}

public enum JournalBatchStatus
{
    Open,
    Posted
}

public enum JournalTemplateType
{
    General,
    Sales,
    Purchases,
    CashReceipts,
    Payments,
    Assets
}

public enum ItemType
{
    Inventory,
    Service,
    NonInventory
}

public enum SalesOrderStatus
{
    Open,
    ShipmentDraft,
    Shipped,
    InvoiceDraft,
    Invoiced
}

public enum PurchaseOrderStatus
{
    Open,
    ReceiptDraft,
    Received,
    InvoiceDraft,
    Invoiced
}
