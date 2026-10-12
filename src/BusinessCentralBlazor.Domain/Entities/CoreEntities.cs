using BusinessCentralBlazor.Domain.Enums;

namespace BusinessCentralBlazor.Domain.Entities;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class Company
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CompanySettings
{
    public Guid CompanyId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
    public int FirstDayOfWeek { get; set; }
}

public sealed class WorkspaceNote
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class TenantMembership
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class CompanyMembership
{
    public Guid TenantId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class Currency
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinorUnit { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CurrencyRate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BaseCurrencyCode { get; set; } = string.Empty;
    public string QuoteCurrencyCode { get; set; } = string.Empty;
    public DateOnly EffectiveDate { get; set; }
    public decimal Rate { get; set; }
}

public sealed class AccountingPeriod
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
    public bool IsClosed { get; set; }
    public bool DateLocked { get; set; }
}

public sealed class GeneralLedgerAccount
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountCategory Category { get; set; }
    public AccountType Type { get; set; }
    public Guid? ParentAccountId { get; set; }
    public bool AllowsDirectPosting { get; set; } = true;
    public bool IsBlocked { get; set; }
}

public sealed class Dimension
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class DimensionValue
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DimensionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
}

public sealed class GeneralJournalBatch
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JournalTemplateId { get; set; }
    public Guid DefaultAccountingPeriodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JournalBatchStatus Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PostedAtUtc { get; set; }
    public Guid? PostedByUserId { get; set; }
    public Guid? ReversedByBatchId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class GeneralJournalTemplate
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JournalTemplateType Type { get; set; }
}

public sealed class GeneralJournalLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JournalBatchId { get; set; }
    public Guid AccountingPeriodId { get; set; }
    public Guid AccountId { get; set; }
    public int LineNumber { get; set; }
    public DateOnly PostingDate { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? CurrencyDebitAmount { get; set; }
    public decimal? CurrencyCreditAmount { get; set; }
}

public sealed class JournalLineDimension
{
    public Guid CompanyId { get; set; }
    public Guid JournalLineId { get; set; }
    public Guid DimensionId { get; set; }
    public Guid DimensionValueId { get; set; }
}

public sealed class GeneralLedgerEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JournalBatchId { get; set; }
    public Guid JournalLineId { get; set; }
    public Guid AccountId { get; set; }
    public long EntryNumber { get; set; }
    public DateOnly PostingDate { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? CurrencyCode { get; set; }
    public decimal? CurrencyDebitAmount { get; set; }
    public decimal? CurrencyCreditAmount { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
