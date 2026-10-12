using BusinessCentralBlazor.Domain.Entities;
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
            "TenantMemberships",
            "CompanyMemberships",
            "Currencies",
            "CurrencyRates",
            "AccountingPeriods",
            "GeneralLedgerAccounts",
            "GeneralJournalBatches",
            "GeneralJournalLines",
            "JournalLineDimensions",
            "GeneralLedgerEntries",
            "Dimensions",
            "DimensionValues",
            "Customers",
            "Vendors",
            "Items",
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

        Assert.Equal(
            ["CompanyId", "Id"],
            accountForeignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Contains(line.GetCheckConstraints(), constraint => constraint.Name == "CK_GeneralJournalLines_DebitOrCredit");
        Assert.Contains(line.GetCheckConstraints(), constraint => constraint.Name == "CK_GeneralJournalLines_ForeignAmounts");
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

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer(
                "Server=(local);Database=ModelValidation;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new ErpDbContext(options);
    }
}
