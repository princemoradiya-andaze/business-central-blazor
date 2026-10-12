namespace BusinessCentralBlazor.Domain.Entities;

public sealed class BankAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountIdentifier { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public Guid GeneralLedgerAccountId { get; set; }
    public bool IsBlocked { get; set; }
    public int Revision { get; set; }
}

public sealed class BankJournal
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public Guid JournalBatchId { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public bool IsReceipt { get; set; }
}

public sealed class BankStatement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankAccountId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public decimal OpeningBalance { get; set; }
    public decimal ClosingBalance { get; set; }
    public bool IsCompleted { get; set; }
    public int Revision { get; set; }
}

public sealed class BankStatementLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BankStatementId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid? GeneralLedgerEntryId { get; set; }
}
