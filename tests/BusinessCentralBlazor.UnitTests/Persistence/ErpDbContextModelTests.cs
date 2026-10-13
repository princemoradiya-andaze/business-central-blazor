using BusinessCentralBlazor.Domain.Entities;
using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BusinessCentralBlazor.UnitTests.Persistence;

public sealed class ErpDbContextModelTests
{
    [Fact]
    public void ModelContainsCompanyScopedPhaseZeroTables()
    {
        using var context = CreateContext();
        var tableNames = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var requiredTables = new HashSet<string>(StringComparer.Ordinal)
        {
            "Tenants",
            "Companies",
            "CompanySettings",
            "WorkspaceNotes",
            "TenantMemberships",
            "CompanyMemberships",
            "Currencies",
            "CurrencyRates",
            "AccountingPeriods",
            "GeneralLedgerAccounts",
            "TaxCodes",
            "TaxRates",
            "GeneralJournalTemplates",
            "GeneralJournalBatches",
            "GeneralJournalLines",
            "JournalLineDimensions",
            "GeneralLedgerEntries",
            "Dimensions",
            "DimensionValues",
            "Customers",
            "Vendors",
            "Items",
            "InventoryMovements",
            "PurchaseOrders",
            "PurchaseOrderLines",
            "PurchaseReceipts",
            "PurchaseInvoices",
            "NumberSeries",
            "AuditEntries",
            "AspNetUsers",
            "AspNetRoles"
        };

        Assert.True(
            requiredTables.IsSubsetOf(tableNames),
            $"Missing tables: {string.Join(", ", requiredTables.Except(tableNames))}");
    }

    [Fact]
    public void JournalLinesUseCompanyScopedAccountForeignKeysAndPostingChecks()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var line = model.FindEntityType(typeof(GeneralJournalLine))!;
        var accountForeignKey = Assert.Single(line.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "AccountId"]));
        var periodForeignKey = Assert.Single(line.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "AccountingPeriodId"]));

        Assert.Equal(
            ["CompanyId", "Id"],
            accountForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(
            ["CompanyId", "Id"],
            periodForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(line.GetCheckConstraints(), constraint => constraint.Name == "CK_GeneralJournalLines_DebitOrCredit");
        Assert.Contains(line.GetCheckConstraints(), constraint => constraint.Name == "CK_GeneralJournalLines_ForeignAmounts");
    }

    [Fact]
    public void JournalBatchesBelongToACompanyScopedTemplate()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var batch = model.FindEntityType(typeof(GeneralJournalBatch))!;
        var templateForeignKey = Assert.Single(batch.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "JournalTemplateId"]));

        Assert.Equal(
            ["CompanyId", "Id"],
            templateForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(batch.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "JournalTemplateId", "Name"]));
    }

    [Fact]
    public void JournalBatchReversalsUseACompanyScopedLinkAndPreventDuplicateReversal()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var batch = model.FindEntityType(typeof(GeneralJournalBatch))!;
        var reversalForeignKey = Assert.Single(batch.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "ReversedByBatchId"]));
        var reversalIndex = Assert.Single(batch.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "ReversedByBatchId"]));

        Assert.Equal(
            ["CompanyId", "Id"],
            reversalForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.True(reversalIndex.IsUnique);
        Assert.Equal("[ReversedByBatchId] IS NOT NULL", reversalIndex.GetFilter());
    }

    [Fact]
    public void AccountingAndGeneralLedgerModelsSeparateBusinessCentralConcepts()
    {
        using var context = CreateContext();
        var period = context.Model.FindEntityType(typeof(AccountingPeriod))!;
        var account = context.Model.FindEntityType(typeof(GeneralLedgerAccount))!;

        Assert.NotNull(period.FindProperty(nameof(AccountingPeriod.StartsOn)));
        Assert.NotNull(period.FindProperty(nameof(AccountingPeriod.IsClosed)));
        Assert.NotNull(period.FindProperty(nameof(AccountingPeriod.DateLocked)));
        Assert.Contains(period.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "StartsOn"]));
        Assert.Equal(
            typeof(AccountCategory),
            account.FindProperty(nameof(GeneralLedgerAccount.Category))!.ClrType);
        Assert.Equal(
            typeof(AccountType),
            account.FindProperty(nameof(GeneralLedgerAccount.Type))!.ClrType);
    }

    [Fact]
    public void TaxSetupAndJournalTaxReferencesAreCompanyScopedAndAuditable()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var taxCode = model.FindEntityType(typeof(TaxCode))!;
        var taxRate = model.FindEntityType(typeof(TaxRate))!;
        var line = model.FindEntityType(typeof(GeneralJournalLine))!;
        var entry = model.FindEntityType(typeof(GeneralLedgerEntry))!;

        Assert.Contains(taxCode.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "Code"]));
        foreach (var accountProperty in new[] { "LiabilityAccountId", "RecoverableAccountId" })
        {
            var mapping = Assert.Single(taxCode.GetForeignKeys(), foreignKey =>
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual(["CompanyId", accountProperty]));
            Assert.Equal(
                ["CompanyId", "Id"],
                mapping.PrincipalKey.Properties.Select(property => property.Name));
        }
        Assert.Contains(taxRate.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "TaxCodeId", "StartsOn"]));
        var codeForeignKey = Assert.Single(taxRate.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "TaxCodeId"]));
        Assert.Equal(
            ["CompanyId", "Id"],
            codeForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(taxRate.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_TaxRates_DateRange");
        Assert.Contains(taxRate.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_TaxRates_PositiveRate");

        foreach (var entity in new[] { line, entry })
        {
            var taxRateForeignKey = Assert.Single(entity.GetForeignKeys(), foreignKey =>
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual(["CompanyId", "TaxCodeId", "TaxRateId"]));
            Assert.Equal(
                ["CompanyId", "TaxCodeId", "Id"],
                taxRateForeignKey.PrincipalKey.Properties.Select(property => property.Name));
            Assert.Contains(entity.GetCheckConstraints(), constraint =>
                constraint.Name == "CK_" + entity.GetTableName() + "_TaxDetails");
            Assert.Equal(
                4,
                entity.FindProperty(nameof(GeneralJournalLine.TaxBaseAmount))!.GetScale());
        }
    }

    [Fact]
    public void FinancialAmountsUseFourDecimalPlacesAndConcurrencyTokens()
    {
        using var context = CreateContext();
        var line = context.Model.FindEntityType(typeof(GeneralJournalLine))!;
        var batch = context.Model.FindEntityType(typeof(GeneralJournalBatch))!;
        var numberSeries = context.Model.FindEntityType(typeof(NumberSeries))!;

        Assert.Equal(19, line.FindProperty(nameof(GeneralJournalLine.DebitAmount))!.GetPrecision());
        Assert.Equal(4, line.FindProperty(nameof(GeneralJournalLine.DebitAmount))!.GetScale());
        Assert.True(batch.FindProperty(nameof(GeneralJournalBatch.RowVersion))!.IsConcurrencyToken);
        Assert.True(numberSeries.FindProperty(nameof(NumberSeries.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void WorkspaceNotesHaveCompanyAndTenantScopedRelationships()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var note = model.FindEntityType(typeof(WorkspaceNote))!;
        var companyForeignKey = Assert.Single(note.GetForeignKeys(), foreignKey =>
            foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["TenantId", "CompanyId"]));

        Assert.Equal(
            ["TenantId", "Id"],
            companyForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(120, note.FindProperty(nameof(WorkspaceNote.Title))!.GetMaxLength());
        Assert.Equal(4000, note.FindProperty(nameof(WorkspaceNote.Body))!.GetMaxLength());
    }

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(
                "Server=(local);Database=ModelValidation;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ErpDbContext(options);
    }

    [Fact]
    public void BankingForeignKeysAreCompanyScopedAndDuplicateMatchesHaveFilteredUniqueIndex()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        foreach (var type in new[] { typeof(BankAccount), typeof(BankJournal), typeof(BankStatement), typeof(BankStatementLine) })
        {
            var entity = model.FindEntityType(type)!;
            foreach (var fk in entity.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType != typeof(Currency)))
                Assert.Equal("CompanyId", fk.Properties[0].Name);
        }
        var bank = model.FindEntityType(typeof(BankAccount))!;
        Assert.True(bank.FindProperty(nameof(BankAccount.Revision))!.IsConcurrencyToken);
        Assert.Contains(bank.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "GeneralLedgerAccountId"]));
        var statement = model.FindEntityType(typeof(BankStatement))!;
        Assert.True(statement.FindProperty(nameof(BankStatement.Revision))!.IsConcurrencyToken);
        var line = model.FindEntityType(typeof(BankStatementLine))!;
        var matchIndex = Assert.Single(line.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["CompanyId", "GeneralLedgerEntryId"]));
        Assert.True(matchIndex.IsUnique);
        Assert.Equal("[GeneralLedgerEntryId] IS NOT NULL", matchIndex.GetFilter());
        Assert.Equal(19, line.FindProperty(nameof(BankStatementLine.Amount))!.GetPrecision());
        Assert.Equal(4, line.FindProperty(nameof(BankStatementLine.Amount))!.GetScale());
        Assert.Contains(line.GetCheckConstraints(), constraint => constraint.Name == "CK_BankStatementLines_NonzeroAmount");
    }

    [Fact]
    public void InventoryItemsAndMovementsUseCompanyScopedReferencesAndWeightedAverageBalances()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var item = model.FindEntityType(typeof(Item))!;
        var movement = model.FindEntityType(typeof(InventoryMovement))!;

        foreach (var accountProperty in new[]
                 {
                     nameof(Item.InventoryAccountId),
                     nameof(Item.InventoryIncreaseOffsetAccountId),
                     nameof(Item.InventoryIssueExpenseAccountId)
                 })
        {
            var foreignKey = Assert.Single(item.GetForeignKeys(), candidate =>
                candidate.Properties.Select(property => property.Name)
                    .SequenceEqual(["CompanyId", accountProperty]));
            Assert.Equal(
                ["CompanyId", "Id"],
                foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        }

        Assert.True(item.FindProperty(nameof(Item.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(4, item.FindProperty(nameof(Item.QuantityOnHand))!.GetScale());
        Assert.Equal(4, item.FindProperty(nameof(Item.InventoryValue))!.GetScale());
        Assert.Contains(item.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_Items_NonNegativeInventory");
        Assert.Contains(movement.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "DocumentNumber"]));
        Assert.Contains(movement.GetIndexes(), index =>
            !index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "JournalBatchId"]));
        foreach (var foreignKey in movement.GetForeignKeys()
                     .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType != typeof(BusinessCentralBlazor.Infrastructure.Identity.ErpUser)))
        {
            Assert.Equal("CompanyId", foreignKey.Properties[0].Name);
        }
        Assert.Contains(movement.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_InventoryMovements_NonzeroQuantity");
    }

    [Fact]
    public void PurchasingDocumentsAndPostingAccountsUseCompanyScopedReferences()
    {
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var vendor = model.FindEntityType(typeof(Vendor))!;
        var settings = model.FindEntityType(typeof(CompanySettings))!;
        var order = model.FindEntityType(typeof(PurchaseOrder))!;
        var line = model.FindEntityType(typeof(PurchaseOrderLine))!;
        var receipt = model.FindEntityType(typeof(PurchaseReceipt))!;
        var invoice = model.FindEntityType(typeof(PurchaseInvoice))!;

        foreach (var (entity, property) in new[]
                 {
                     (vendor, nameof(Vendor.PayablesAccountId)),
                     (settings, nameof(CompanySettings.GoodsReceivedNotInvoicedAccountId))
                 })
        {
            var accountForeignKey = Assert.Single(entity.GetForeignKeys(), foreignKey =>
                foreignKey.Properties.Select(candidate => candidate.Name)
                    .SequenceEqual(["CompanyId", property]));
            Assert.Equal(
                ["CompanyId", "Id"],
                accountForeignKey.PrincipalKey.Properties.Select(candidate => candidate.Name));
        }

        Assert.True(order.FindProperty(nameof(PurchaseOrder.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(order.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "OrderNumber"]));
        Assert.Equal(4, line.FindProperty(nameof(PurchaseOrderLine.Quantity))!.GetScale());
        Assert.Equal(4, line.FindProperty(nameof(PurchaseOrderLine.UnitCost))!.GetScale());
        Assert.Contains(line.GetCheckConstraints(), constraint =>
            constraint.Name == "CK_PurchaseOrderLines_PositiveQuantityAndCost");
        Assert.Contains(receipt.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "PurchaseOrderId"]));
        Assert.Contains(invoice.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(["CompanyId", "InvoiceNumber"]));
        foreach (var entity in new[] { order, line, receipt, invoice })
        {
            Assert.All(entity.GetForeignKeys()
                    .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType !=
                                         typeof(BusinessCentralBlazor.Infrastructure.Identity.ErpUser)),
                foreignKey => Assert.Equal("CompanyId", foreignKey.Properties[0].Name));
        }
    }
}
