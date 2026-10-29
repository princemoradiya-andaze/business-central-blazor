using BusinessCentralBlazor.Domain.Enums;
using BusinessCentralBlazor.Web.Services;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class TrialBalanceCsvTests
{
    [Fact]
    public void CsvPreservesPrecisionTotalsMetadataAndEscapesText()
    {
        var report = new TrialBalanceReport(new(2026, 10, 29),
        [
            new(Guid.NewGuid(), "=1000", "Cash, \"operating\"", AccountCategory.Asset, 1234.5678m, 0m),
            new(Guid.NewGuid(), "3000", "+Equity", AccountCategory.Equity, 0m, 1234.5678m)
        ], 1234.5678m, 1234.5678m);
        var csv = TrialBalanceCsv.Create("@Company", report, new() { AsOfDate = report.AsOfDate }, "All");
        Assert.Contains("\"'@Company\"", csv);
        Assert.Contains("\"'=1000\"", csv);
        Assert.Contains("\"Cash, \"\"operating\"\"\"", csv);
        Assert.Contains("\"'+Equity\"", csv);
        Assert.Contains("\"Total\",\"\",\"\",\"1,234.5678\",\"1,234.5678\"", csv);
        Assert.Contains("\"Account\",\"All\"", csv);
        Assert.Contains("\"Zero balances\",\"Included\"", csv);
        var compact = TrialBalanceCsv.Create("Company", report,
            new() { AsOfDate = report.AsOfDate, FormatRegion = "de-DE", ShowCategory = false, CompactLayout = true }, "All");
        Assert.Contains("\"No.\",\"Account Name\",\"Balance\"", compact);
        Assert.DoesNotContain("\"Debit Balance\"", compact);
        Assert.Contains("\"1.234,5678\"", compact);
        Assert.Contains("\"-1.234,5678\"", compact);
        Assert.Contains("\"Total\",\"\",\"0,0000\"", compact);
    }
}
