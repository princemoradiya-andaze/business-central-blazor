using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessCentralBlazor.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options)
    : IdentityDbContext<ErpUser, ErpRole, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();
    public DbSet<WorkspaceNote> WorkspaceNotes => Set<WorkspaceNote>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<Currency> Currencies => Set<Currency>();
    public DbSet<CurrencyRate> CurrencyRates => Set<CurrencyRate>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<GeneralLedgerAccount> GeneralLedgerAccounts => Set<GeneralLedgerAccount>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<Dimension> Dimensions => Set<Dimension>();
    public DbSet<DimensionValue> DimensionValues => Set<DimensionValue>();
    public DbSet<GeneralJournalTemplate> GeneralJournalTemplates => Set<GeneralJournalTemplate>();
    public DbSet<GeneralJournalBatch> GeneralJournalBatches => Set<GeneralJournalBatch>();
    public DbSet<GeneralJournalLine> GeneralJournalLines => Set<GeneralJournalLine>();
    public DbSet<JournalLineDimension> JournalLineDimensions => Set<JournalLineDimension>();
    public DbSet<GeneralLedgerEntry> GeneralLedgerEntries => Set<GeneralLedgerEntry>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankJournal> BankJournals => Set<BankJournal>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureIdentity(modelBuilder);
        ConfigureTenancy(modelBuilder);
        ConfigureAccounting(modelBuilder);
        ConfigureOperations(modelBuilder);
        ConfigureSales(modelBuilder);
        ConfigurePurchasing(modelBuilder);
        ConfigureBanking(modelBuilder);
    }

    private static void ConfigureSales(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesOrder>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(order => new { order.CompanyId, order.OrderNumber }).IsUnique();
            entity.HasIndex(order => new { order.CompanyId, order.ShipmentJournalBatchId }).IsUnique()
                .HasFilter("[ShipmentJournalBatchId] IS NOT NULL");
            entity.HasIndex(order => new { order.CompanyId, order.InvoiceJournalBatchId }).IsUnique()
                .HasFilter("[InvoiceJournalBatchId] IS NOT NULL");
            entity.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
            entity.Property(order => order.Description).HasMaxLength(250).IsRequired();
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(order => order.RowVersion).IsRowVersion();
            entity.HasOne<Customer>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.ShipmentJournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.InvoiceJournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SalesOrderLine>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(line => new { line.CompanyId, line.SalesOrderId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.CompanyId, line.SalesOrderId, line.ItemId }).IsUnique();
            entity.Property(line => line.Description).HasMaxLength(250).IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(19, 4);
            entity.Property(line => line.UnitPrice).HasPrecision(19, 4);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_SalesOrderLines_PositiveQuantityAndPrice",
                "[Quantity] > 0 AND [UnitPrice] > 0"));
            entity.HasOne<SalesOrder>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.SalesOrderId })
                .HasPrincipalKey(order => new { order.CompanyId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.ItemId })
                .HasPrincipalKey(item => new { item.CompanyId, item.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.SalesAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxCode>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TaxCodeId })
                .HasPrincipalKey(code => new { code.CompanyId, code.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SalesInvoice>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.InvoiceNumber }).IsUnique();
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.JournalBatchId }).IsUnique();
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.SalesOrderId }).IsUnique();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(32).IsRequired();
            entity.Property(invoice => invoice.NetAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.TaxAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.TotalAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.PostedAtUtc).HasColumnType("datetimeoffset");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_SalesInvoices_Amounts",
                "[NetAmount] > 0 AND [TaxAmount] >= 0 AND [TotalAmount] = [NetAmount] + [TaxAmount]"));
            entity.HasOne<SalesOrder>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.SalesOrderId })
                .HasPrincipalKey(order => new { order.CompanyId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Customer>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.CustomerId })
                .HasPrincipalKey(customer => new { customer.CompanyId, customer.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany()
                .HasForeignKey(invoice => invoice.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigurePurchasing(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PurchaseOrder>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(order => new { order.CompanyId, order.OrderNumber }).IsUnique();
            entity.HasIndex(order => new { order.CompanyId, order.ReceiptJournalBatchId }).IsUnique()
                .HasFilter("[ReceiptJournalBatchId] IS NOT NULL");
            entity.HasIndex(order => new { order.CompanyId, order.InvoiceJournalBatchId }).IsUnique()
                .HasFilter("[InvoiceJournalBatchId] IS NOT NULL");
            entity.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
            entity.Property(order => order.Description).HasMaxLength(250).IsRequired();
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(order => order.RowVersion).IsRowVersion();
            entity.HasOne<Vendor>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.VendorId })
                .HasPrincipalKey(vendor => new { vendor.CompanyId, vendor.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.ReceiptJournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(order => new { order.CompanyId, order.InvoiceJournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseOrderLine>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(line => new { line.CompanyId, line.PurchaseOrderId, line.LineNumber }).IsUnique();
            entity.HasIndex(line => new { line.CompanyId, line.PurchaseOrderId, line.ItemId }).IsUnique();
            entity.Property(line => line.Description).HasMaxLength(250).IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(19, 4);
            entity.Property(line => line.UnitCost).HasPrecision(19, 4);
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_PurchaseOrderLines_PositiveQuantityAndCost",
                "[Quantity] > 0 AND [UnitCost] > 0"));
            entity.HasOne<PurchaseOrder>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.PurchaseOrderId })
                .HasPrincipalKey(order => new { order.CompanyId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Item>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.ItemId })
                .HasPrincipalKey(item => new { item.CompanyId, item.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxCode>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TaxCodeId })
                .HasPrincipalKey(code => new { code.CompanyId, code.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseReceipt>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(receipt => new { receipt.CompanyId, receipt.PurchaseOrderId }).IsUnique();
            entity.HasIndex(receipt => new { receipt.CompanyId, receipt.JournalBatchId }).IsUnique();
            entity.Property(receipt => receipt.PostedAtUtc).HasColumnType("datetimeoffset");
            entity.HasOne<PurchaseOrder>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.PurchaseOrderId })
                .HasPrincipalKey(order => new { order.CompanyId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(receipt => new { receipt.CompanyId, receipt.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany()
                .HasForeignKey(receipt => receipt.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PurchaseInvoice>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.InvoiceNumber }).IsUnique();
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.JournalBatchId }).IsUnique();
            entity.HasIndex(invoice => new { invoice.CompanyId, invoice.PurchaseOrderId }).IsUnique();
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(32).IsRequired();
            entity.Property(invoice => invoice.NetAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.TaxAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.TotalAmount).HasPrecision(19, 4);
            entity.Property(invoice => invoice.PostedAtUtc).HasColumnType("datetimeoffset");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_PurchaseInvoices_Amounts",
                "[NetAmount] > 0 AND [TaxAmount] >= 0 AND [TotalAmount] = [NetAmount] + [TaxAmount]"));
            entity.HasOne<PurchaseOrder>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.PurchaseOrderId })
                .HasPrincipalKey(order => new { order.CompanyId, order.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vendor>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.VendorId })
                .HasPrincipalKey(vendor => new { vendor.CompanyId, vendor.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(invoice => new { invoice.CompanyId, invoice.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany()
                .HasForeignKey(invoice => invoice.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureBanking(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BankAccount>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(bank => new { bank.CompanyId, bank.Code }).IsUnique();
            entity.HasIndex(bank => new { bank.CompanyId, bank.GeneralLedgerAccountId }).IsUnique();
            entity.Property(bank => bank.Code).HasMaxLength(32).IsRequired();
            entity.Property(bank => bank.Name).HasMaxLength(160).IsRequired();
            entity.Property(bank => bank.AccountIdentifier).HasMaxLength(100).IsRequired();
            entity.Property(bank => bank.CurrencyCode).HasColumnType("char(3)").IsRequired();
            entity.Property(bank => bank.Revision).IsConcurrencyToken();
            entity.HasOne<Currency>().WithMany().HasForeignKey(bank => bank.CurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(bank => new { bank.CompanyId, bank.GeneralLedgerAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BankJournal>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(journal => new { journal.CompanyId, journal.BankAccountId, journal.DocumentNumber }).IsUnique();
            entity.HasIndex(journal => new { journal.CompanyId, journal.JournalBatchId }).IsUnique();
            entity.Property(journal => journal.DocumentNumber).HasMaxLength(64).IsRequired();
            entity.HasOne<BankAccount>().WithMany()
                .HasForeignKey(journal => new { journal.CompanyId, journal.BankAccountId })
                .HasPrincipalKey(bank => new { bank.CompanyId, bank.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(journal => new { journal.CompanyId, journal.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BankStatement>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(statement => new { statement.CompanyId, statement.BankAccountId, statement.Reference }).IsUnique();
            entity.Property(statement => statement.Reference).HasMaxLength(64).IsRequired();
            entity.Property(statement => statement.OpeningBalance).HasPrecision(19, 4);
            entity.Property(statement => statement.ClosingBalance).HasPrecision(19, 4);
            entity.Property(statement => statement.Revision).IsConcurrencyToken();
            entity.ToTable(table => table.HasCheckConstraint("CK_BankStatements_DateRange", "[StartsOn] <= [EndsOn]"));
            entity.HasOne<BankAccount>().WithMany()
                .HasForeignKey(statement => new { statement.CompanyId, statement.BankAccountId })
                .HasPrincipalKey(bank => new { bank.CompanyId, bank.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<BankStatementLine>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(line => new { line.CompanyId, line.BankStatementId, line.Reference }).IsUnique();
            entity.HasIndex(line => new { line.CompanyId, line.GeneralLedgerEntryId }).IsUnique()
                .HasFilter("[GeneralLedgerEntryId] IS NOT NULL");
            entity.Property(line => line.Reference).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(250).IsRequired();
            entity.Property(line => line.Amount).HasPrecision(19, 4);
            entity.ToTable(table => table.HasCheckConstraint("CK_BankStatementLines_NonzeroAmount", "[Amount] <> 0"));
            entity.HasOne<BankStatement>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.BankStatementId })
                .HasPrincipalKey(statement => new { statement.CompanyId, statement.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerEntry>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.GeneralLedgerEntryId })
                .HasPrincipalKey(entry => new { entry.CompanyId, entry.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(160);
        });
    }

    private static void ConfigureTenancy(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(tenant => tenant.Id);
            entity.Property(tenant => tenant.Name).HasMaxLength(160).IsRequired();
            entity.Property(tenant => tenant.CreatedAtUtc).HasColumnType("datetimeoffset");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.HasAlternateKey(company => new { company.TenantId, company.Id });
            entity.HasIndex(company => new { company.TenantId, company.Code }).IsUnique();
            entity.Property(company => company.Code).HasMaxLength(32).IsRequired();
            entity.Property(company => company.Name).HasMaxLength(160).IsRequired();
            entity.Property(company => company.CreatedAtUtc).HasColumnType("datetimeoffset");
            entity.HasOne<Tenant>().WithMany().HasForeignKey(company => company.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanySettings>(entity =>
        {
            entity.HasKey(settings => settings.CompanyId);
            entity.Property(settings => settings.BaseCurrencyCode).HasColumnType("char(3)").IsRequired();
            entity.Property(settings => settings.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasIndex(settings => new { settings.CompanyId, settings.GoodsReceivedNotInvoicedAccountId });
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_CompanySettings_FirstDayOfWeek",
                "[FirstDayOfWeek] BETWEEN 0 AND 6"));
            entity.HasOne<Company>().WithOne().HasForeignKey<CompanySettings>(settings => settings.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(settings => settings.BaseCurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(settings => new { settings.CompanyId, settings.GoodsReceivedNotInvoicedAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.TenantId, membership.UserId });
            entity.Property(membership => membership.RoleName).HasMaxLength(64).IsRequired();
            entity.Property(membership => membership.CreatedAtUtc).HasColumnType("datetimeoffset");
            entity.HasOne<Tenant>().WithMany().HasForeignKey(membership => membership.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany().HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyMembership>(entity =>
        {
            entity.HasKey(membership => new { membership.TenantId, membership.CompanyId, membership.UserId });
            entity.Property(membership => membership.RoleName).HasMaxLength(64).IsRequired();
            entity.Property(membership => membership.CreatedAtUtc).HasColumnType("datetimeoffset");
            entity.HasOne<Company>().WithMany()
                .HasForeignKey(membership => new { membership.TenantId, membership.CompanyId })
                .HasPrincipalKey(company => new { company.TenantId, company.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TenantMembership>().WithMany()
                .HasForeignKey(membership => new { membership.TenantId, membership.UserId })
                .HasPrincipalKey(membership => new { membership.TenantId, membership.UserId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Currency>(entity =>
        {
            entity.HasKey(currency => currency.Code);
            entity.Property(currency => currency.Code).HasColumnType("char(3)");
            entity.Property(currency => currency.Name).HasMaxLength(80).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_Currencies_MinorUnit",
                "[MinorUnit] BETWEEN 0 AND 4"));
        });
    }

    private static void ConfigureAccounting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CurrencyRate>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(rate => new
            {
                rate.CompanyId,
                rate.BaseCurrencyCode,
                rate.QuoteCurrencyCode,
                rate.EffectiveDate
            }).IsUnique();
            entity.Property(rate => rate.BaseCurrencyCode).HasColumnType("char(3)");
            entity.Property(rate => rate.QuoteCurrencyCode).HasColumnType("char(3)");
            entity.Property(rate => rate.Rate).HasPrecision(19, 8);
            entity.ToTable(table => table.HasCheckConstraint("CK_CurrencyRates_PositiveRate", "[Rate] > 0"));
            entity.HasOne<Currency>().WithMany().HasForeignKey(rate => rate.BaseCurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(rate => rate.QuoteCurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccountingPeriod>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(period => new { period.CompanyId, period.StartsOn }).IsUnique();
            entity.Property(period => period.Name).HasMaxLength(32).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_AccountingPeriods_DateRange",
                "[StartsOn] <= [EndsOn]"));
        });

        modelBuilder.Entity<GeneralLedgerAccount>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(account => new { account.CompanyId, account.Number }).IsUnique();
            entity.Property(account => account.Number).HasMaxLength(32).IsRequired();
            entity.Property(account => account.Name).HasMaxLength(160).IsRequired();
            entity.Property(account => account.Category).HasConversion<string>().HasMaxLength(16);
            entity.Property(account => account.Type).HasConversion<string>().HasMaxLength(16);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(account => new { account.CompanyId, account.ParentAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Dimension>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(dimension => new { dimension.CompanyId, dimension.Code }).IsUnique();
            entity.Property(dimension => dimension.Code).HasMaxLength(32).IsRequired();
            entity.Property(dimension => dimension.Name).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<TaxCode>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(code => new { code.CompanyId, code.Code }).IsUnique();
            entity.Property(code => code.Code).HasMaxLength(32).IsRequired();
            entity.Property(code => code.Description).HasMaxLength(120).IsRequired();
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(code => new { code.CompanyId, code.LiabilityAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(code => new { code.CompanyId, code.RecoverableAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaxRate>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasAlternateKey(rate => new { rate.CompanyId, rate.TaxCodeId, rate.Id });
            entity.HasIndex(rate => new { rate.CompanyId, rate.TaxCodeId, rate.StartsOn }).IsUnique();
            entity.Property(rate => rate.RatePercent).HasPrecision(9, 4);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_TaxRates_PositiveRate", "[RatePercent] > 0");
                table.HasCheckConstraint(
                    "CK_TaxRates_DateRange",
                    "[EndsOn] IS NULL OR [StartsOn] <= [EndsOn]");
            });
            entity.HasOne<TaxCode>().WithMany()
                .HasForeignKey(rate => new { rate.CompanyId, rate.TaxCodeId })
                .HasPrincipalKey(code => new { code.CompanyId, code.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DimensionValue>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasAlternateKey(value => new { value.CompanyId, value.DimensionId, value.Id });
            entity.HasIndex(value => new { value.CompanyId, value.DimensionId, value.Code }).IsUnique();
            entity.Property(value => value.Code).HasMaxLength(32).IsRequired();
            entity.Property(value => value.Name).HasMaxLength(120).IsRequired();
            entity.HasOne<Dimension>().WithMany()
                .HasForeignKey(value => new { value.CompanyId, value.DimensionId })
                .HasPrincipalKey(dimension => new { dimension.CompanyId, dimension.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GeneralJournalTemplate>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(template => new { template.CompanyId, template.Code }).IsUnique();
            entity.Property(template => template.Code).HasMaxLength(10).IsRequired();
            entity.Property(template => template.Description).HasMaxLength(100).IsRequired();
            entity.Property(template => template.Type).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<GeneralJournalBatch>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(batch => new { batch.CompanyId, batch.JournalTemplateId, batch.Name }).IsUnique();
            entity.Property(batch => batch.Name).HasMaxLength(32).IsRequired();
            entity.Property(batch => batch.Description).HasMaxLength(100).IsRequired();
            entity.Property(batch => batch.Status).HasConversion<string>().HasMaxLength(16);
            entity.Property(batch => batch.CreatedAtUtc).HasColumnType("datetimeoffset");
            entity.Property(batch => batch.PostedAtUtc).HasColumnType("datetimeoffset");
            entity.Property(batch => batch.RowVersion).IsRowVersion();
            entity.HasIndex(batch => new { batch.CompanyId, batch.ReversedByBatchId })
                .IsUnique()
                .HasFilter("[ReversedByBatchId] IS NOT NULL");
            entity.HasOne<AccountingPeriod>().WithMany()
                .HasForeignKey(batch => new { batch.CompanyId, batch.DefaultAccountingPeriodId })
                .HasPrincipalKey(period => new { period.CompanyId, period.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(batch => new { batch.CompanyId, batch.ReversedByBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalTemplate>().WithMany()
                .HasForeignKey(batch => new { batch.CompanyId, batch.JournalTemplateId })
                .HasPrincipalKey(template => new { template.CompanyId, template.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany().HasForeignKey(batch => batch.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GeneralJournalLine>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(line => new { line.CompanyId, line.JournalBatchId, line.LineNumber }).IsUnique();
            entity.Property(line => line.DocumentNumber).HasMaxLength(64).IsRequired();
            entity.Property(line => line.Description).HasMaxLength(250).IsRequired();
            ConfigureAmounts(entity);
            ConfigureTaxFields(entity);
            entity.Property(line => line.CurrencyCode).HasColumnType("char(3)");
            entity.Property(line => line.CurrencyDebitAmount).HasPrecision(19, 4);
            entity.Property(line => line.CurrencyCreditAmount).HasPrecision(19, 4);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_GeneralJournalLines_LineNumber", "[LineNumber] > 0");
                table.HasCheckConstraint(
                    "CK_GeneralJournalLines_DebitOrCredit",
                    "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)");
                table.HasCheckConstraint(
                    "CK_GeneralJournalLines_ForeignAmounts",
                    "([CurrencyCode] IS NULL AND [CurrencyDebitAmount] IS NULL AND [CurrencyCreditAmount] IS NULL) OR " +
                    "([CurrencyCode] IS NOT NULL AND " +
                    "(([CurrencyDebitAmount] > 0 AND ([CurrencyCreditAmount] = 0 OR [CurrencyCreditAmount] IS NULL)) OR " +
                    "([CurrencyCreditAmount] > 0 AND ([CurrencyDebitAmount] = 0 OR [CurrencyDebitAmount] IS NULL))))");
            });
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AccountingPeriod>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.AccountingPeriodId })
                .HasPrincipalKey(period => new { period.CompanyId, period.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxRate>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.TaxCodeId, line.TaxRateId })
                .HasPrincipalKey(rate => new { rate.CompanyId, rate.TaxCodeId, rate.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(line => new { line.CompanyId, line.AccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(line => line.CurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLineDimension>(entity =>
        {
            entity.HasKey(assignment => new
            {
                assignment.CompanyId,
                assignment.JournalLineId,
                assignment.DimensionId
            });
            entity.HasOne<GeneralJournalLine>().WithMany()
                .HasForeignKey(assignment => new { assignment.CompanyId, assignment.JournalLineId })
                .HasPrincipalKey(line => new { line.CompanyId, line.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Dimension>().WithMany()
                .HasForeignKey(assignment => new { assignment.CompanyId, assignment.DimensionId })
                .HasPrincipalKey(dimension => new { dimension.CompanyId, dimension.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DimensionValue>().WithMany()
                .HasForeignKey(assignment => new
                {
                    assignment.CompanyId,
                    assignment.DimensionId,
                    assignment.DimensionValueId
                })
                .HasPrincipalKey(value => new { value.CompanyId, value.DimensionId, value.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GeneralLedgerEntry>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(entry => new { entry.CompanyId, entry.EntryNumber }).IsUnique();
            entity.HasIndex(entry => new { entry.CompanyId, entry.JournalLineId }).IsUnique();
            entity.Property(entry => entry.DocumentNumber).HasMaxLength(64).IsRequired();
            entity.Property(entry => entry.Description).HasMaxLength(250).IsRequired();
            entity.Property(entry => entry.CreatedAtUtc).HasColumnType("datetimeoffset");
            ConfigureAmounts(entity);
            ConfigureTaxFields(entity);
            entity.Property(entry => entry.CurrencyCode).HasColumnType("char(3)");
            entity.Property(entry => entry.CurrencyDebitAmount).HasPrecision(19, 4);
            entity.Property(entry => entry.CurrencyCreditAmount).HasPrecision(19, 4);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_GeneralLedgerEntries_DebitOrCredit",
                    "([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0)");
                table.HasCheckConstraint(
                    "CK_GeneralLedgerEntries_ForeignAmounts",
                    "([CurrencyCode] IS NULL AND [CurrencyDebitAmount] IS NULL AND [CurrencyCreditAmount] IS NULL) OR " +
                    "([CurrencyCode] IS NOT NULL AND " +
                    "(([CurrencyDebitAmount] > 0 AND ([CurrencyCreditAmount] = 0 OR [CurrencyCreditAmount] IS NULL)) OR " +
                    "([CurrencyCreditAmount] > 0 AND ([CurrencyDebitAmount] = 0 OR [CurrencyDebitAmount] IS NULL))))");
            });
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalLine>().WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.JournalLineId })
                .HasPrincipalKey(line => new { line.CompanyId, line.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.AccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TaxRate>().WithMany()
                .HasForeignKey(entry => new { entry.CompanyId, entry.TaxCodeId, entry.TaxRateId })
                .HasPrincipalKey(rate => new { rate.CompanyId, rate.TaxCodeId, rate.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Currency>().WithMany().HasForeignKey(entry => entry.CurrencyCode)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOperations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkspaceNote>(entity =>
        {
            entity.HasKey(note => note.Id);
            entity.HasIndex(note => new { note.TenantId, note.CompanyId, note.UpdatedAtUtc });
            entity.Property(note => note.Title).HasMaxLength(120).IsRequired();
            entity.Property(note => note.Body).HasMaxLength(4000).IsRequired();
            entity.Property(note => note.CreatedAtUtc).HasColumnType("datetimeoffset");
            entity.Property(note => note.UpdatedAtUtc).HasColumnType("datetimeoffset");
            entity.HasOne<Company>().WithMany()
                .HasForeignKey(note => new { note.TenantId, note.CompanyId })
                .HasPrincipalKey(company => new { company.TenantId, company.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        ConfigureParty<Customer>(modelBuilder, "Customers");
        ConfigureParty<Vendor>(modelBuilder, "Vendors");
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(customer => new { customer.CompanyId, customer.ReceivablesAccountId });
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(customer => new { customer.CompanyId, customer.ReceivablesAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.HasIndex(vendor => new { vendor.CompanyId, vendor.PayablesAccountId });
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(vendor => new { vendor.CompanyId, vendor.PayablesAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Item>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(item => new { item.CompanyId, item.Number }).IsUnique();
            entity.Property(item => item.Number).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(250).IsRequired();
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.BaseUnitOfMeasure).HasMaxLength(20).IsRequired();
            entity.Property(item => item.UnitCost).HasPrecision(19, 4);
            entity.Property(item => item.QuantityOnHand).HasPrecision(19, 4);
            entity.Property(item => item.InventoryValue).HasPrecision(19, 4);
            entity.Property(item => item.RowVersion).IsRowVersion();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Items_NonNegativeUnitCost", "[UnitCost] >= 0");
                table.HasCheckConstraint("CK_Items_NonNegativeInventory", "[QuantityOnHand] >= 0 AND [InventoryValue] >= 0");
            });
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(item => new { item.CompanyId, item.InventoryAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(item => new { item.CompanyId, item.InventoryIncreaseOffsetAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralLedgerAccount>().WithMany()
                .HasForeignKey(item => new { item.CompanyId, item.InventoryIssueExpenseAccountId })
                .HasPrincipalKey(account => new { account.CompanyId, account.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryMovement>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(movement => new { movement.CompanyId, movement.DocumentNumber }).IsUnique();
            entity.HasIndex(movement => new { movement.CompanyId, movement.JournalBatchId });
            entity.Property(movement => movement.DocumentNumber).HasMaxLength(64).IsRequired();
            entity.Property(movement => movement.Description).HasMaxLength(250).IsRequired();
            entity.Property(movement => movement.Quantity).HasPrecision(19, 4);
            entity.Property(movement => movement.UnitCost).HasPrecision(19, 4);
            entity.Property(movement => movement.InventoryValue).HasPrecision(19, 4);
            entity.Property(movement => movement.PostedAtUtc).HasColumnType("datetimeoffset");
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_InventoryMovements_NonzeroQuantity", "[Quantity] <> 0");
                table.HasCheckConstraint("CK_InventoryMovements_NonNegativeCost", "[UnitCost] >= 0 AND [InventoryValue] > 0");
            });
            entity.HasOne<Item>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.ItemId })
                .HasPrincipalKey(item => new { item.CompanyId, item.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AccountingPeriod>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.AccountingPeriodId })
                .HasPrincipalKey(period => new { period.CompanyId, period.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<GeneralJournalBatch>().WithMany()
                .HasForeignKey(movement => new { movement.CompanyId, movement.JournalBatchId })
                .HasPrincipalKey(batch => new { batch.CompanyId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ErpUser>().WithMany()
                .HasForeignKey(movement => movement.PostedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NumberSeries>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex(series => new { series.CompanyId, series.Code }).IsUnique();
            entity.Property(series => series.Code).HasMaxLength(32).IsRequired();
            entity.Property(series => series.Prefix).HasMaxLength(32).IsRequired();
            entity.Property(series => series.RowVersion).IsRowVersion();
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_NumberSeries_NextNumber", "[NextNumber] > 0");
                table.HasCheckConstraint("CK_NumberSeries_MinimumDigits", "[MinimumDigits] BETWEEN 1 AND 18");
            });
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.Property(entry => entry.Action).HasMaxLength(80).IsRequired();
            entity.Property(entry => entry.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.EntityId).HasMaxLength(80).IsRequired();
            entity.Property(entry => entry.OccurredAtUtc).HasColumnType("datetimeoffset");
            entity.Property(entry => entry.MetadataJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(entry => new { entry.CompanyId, entry.OccurredAtUtc });
            entity.HasOne<ErpUser>().WithMany().HasForeignKey(entry => entry.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureParty<TEntity>(ModelBuilder modelBuilder, string tableName)
        where TEntity : class
    {
        modelBuilder.Entity<TEntity>(entity =>
        {
            ConfigureCompanyEntity(entity);
            entity.HasIndex("CompanyId", "Number").IsUnique();
            entity.ToTable(tableName);
        });

        var party = modelBuilder.Entity<TEntity>();
        party.Property<string>("Number").HasMaxLength(32).IsRequired();
        party.Property<string>("Name").HasMaxLength(160).IsRequired();
        party.Property<string?>("CurrencyCode").HasColumnType("char(3)");
        party.HasOne<Currency>().WithMany().HasForeignKey("CurrencyCode").OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCompanyEntity<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.HasKey("Id");
        entity.HasAlternateKey("CompanyId", "Id");
        entity.HasOne<Company>().WithMany()
            .HasForeignKey("CompanyId")
            .HasPrincipalKey(company => company.Id)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAmounts<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<decimal>("DebitAmount").HasPrecision(19, 4);
        entity.Property<decimal>("CreditAmount").HasPrecision(19, 4);
        entity.ToTable(table =>
        {
            table.HasCheckConstraint("CK_" + entity.Metadata.GetTableName() + "_NonNegativeAmounts",
                "[DebitAmount] >= 0 AND [CreditAmount] >= 0");
        });
    }

    private static void ConfigureTaxFields<TEntity>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<Guid?>("TaxCodeId");
        entity.Property<Guid?>("TaxRateId");
        entity.Property<decimal?>("TaxRatePercent").HasPrecision(9, 4);
        entity.Property<decimal?>("TaxBaseAmount").HasPrecision(19, 4);
        entity.Property<TaxPostingType?>("TaxPostingType").HasConversion<string>().HasMaxLength(16);
        entity.ToTable(table => table.HasCheckConstraint(
            "CK_" + entity.Metadata.GetTableName() + "_TaxDetails",
            "([TaxCodeId] IS NULL AND [TaxRateId] IS NULL AND [TaxRatePercent] IS NULL AND " +
            "[TaxBaseAmount] IS NULL AND [TaxPostingType] IS NULL) OR " +
            "([TaxCodeId] IS NOT NULL AND [TaxRateId] IS NOT NULL AND [TaxRatePercent] > 0 AND " +
            "[TaxBaseAmount] > 0 AND [TaxPostingType] IN ('Liability', 'Recoverable'))"));
    }
}
