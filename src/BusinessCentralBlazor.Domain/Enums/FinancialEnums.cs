namespace BusinessCentralBlazor.Domain.Enums;

public enum AccountingPeriodStatus
{
    Open,
    Closed
}

public enum AccountType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense
}

public enum JournalBatchStatus
{
    Open,
    Posted
}

public enum ItemType
{
    Inventory,
    Service,
    NonInventory
}
