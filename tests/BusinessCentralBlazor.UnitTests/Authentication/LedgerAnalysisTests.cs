using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BusinessCentralBlazor.Web.Services;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class LedgerAnalysisTests
{
    private static GeneralLedgerEntryListItem Entry(long number, DateOnly date, string account, decimal debit, decimal credit, string document = "TAX") =>
        new(Guid.NewGuid(), number, date, Guid.NewGuid(), account, $"Account {account}", document, "Posted entry", debit, credit);

    private static readonly GeneralLedgerEntryListItem[] Entries =
    [
        Entry(10, new(2026, 9, 30), "1000", 20000m, 0),
        Entry(11, new(2026, 9, 30), "3000", 0, 20000m),
        Entry(20, new(2026, 10, 24), "9510", 750.1234m, 0),
        Entry(21, new(2026, 10, 24), "1000", 0, 750.1234m),
        Entry(30, new(2026, 11, 2), "9510", 50m, 0),
        Entry(31, new(2026, 11, 2), "1000", 0, 50m)
    ];

    [Fact]
    public void DateHierarchyAndSignedAmountsRetainAccountingPrecision()
    {
        Assert.Equal("2026-10", LedgerAnalysis.Text(Entries[2], "month"));
        Assert.Equal("2026 Q4", LedgerAnalysis.Text(Entries[2], "quarter"));
        Assert.Equal("2026", LedgerAnalysis.Text(Entries[2], "year"));
        Assert.Equal("2026-10-24", LedgerAnalysis.Text(Entries[2], "date"));
        Assert.Equal(-750.1234m, LedgerAnalysis.Amount(Entries[3], "amount"));
        Assert.Equal(0m, Entries.Sum(entry => LedgerAnalysis.Amount(entry, "amount")));
        Assert.Equal(20800.1234m, Entries.Sum(entry => LedgerAnalysis.Amount(entry, "debit")));
    }

    [Fact]
    public void PerViewFiltersApplyBeforeGroupingWithoutChangingSource()
    {
        var view = new LedgerAnalysisView
        {
            Groups = ["account", "month"],
            Filters = [new() { Field = "account", Operator = "equals", Value = "9510" },
                new() { Field = "amount", Operator = "gte", Value = "100" }]
        };
        LedgerAnalysis.Validate(new() { Views = [view] });
        var filtered = LedgerAnalysis.Filter(Entries, view);
        var group = Assert.Single(LedgerAnalysis.Group(filtered, view.Groups));
        Assert.Equal("9510", group.Label);
        Assert.Equal(750.1234m, group.Entries.Sum(entry => LedgerAnalysis.Amount(entry, "amount")));
        Assert.Equal("2026-10", Assert.Single(group.Children).Label);
        Assert.Equal(6, Entries.Length);
    }

    [Fact]
    public void NestedGroupsAndPivotAmountsReconcileWithFullFilteredTotals()
    {
        var view = new LedgerAnalysisView { Groups = ["account"], ColumnLabels = ["month"], Values = ["debit", "credit", "amount"], Pivot = true };
        var groups = LedgerAnalysis.Group(Entries, view.Groups);
        Assert.Equal(3, groups.Count);
        Assert.Equal(6, groups.Sum(group => group.Entries.Count));
        var keys = LedgerAnalysis.PivotKeys(Entries, view);
        Assert.Equal(3, keys.Count);
        Assert.Equal(["2026-09", "2026-10", "2026-11"], keys.Select(LedgerAnalysis.KeyLabel));
        foreach (var measure in view.Values)
        {
            var total = groups.Sum(group => keys.Sum(key =>
                group.Entries.Where(entry => LedgerAnalysis.Key(entry, view.ColumnLabels) == key).Sum(entry => LedgerAnalysis.Amount(entry, measure))));
            Assert.Equal(Entries.Sum(entry => LedgerAnalysis.Amount(entry, measure)), total);
        }
    }

    [Theory]
    [InlineData("groups", "amount")]
    [InlineData("groups", "entry")]
    [InlineData("labels", "debit")]
    [InlineData("values", "account")]
    [InlineData("values", "entry")]
    public void InvalidFieldRolesAreRejected(string area, string field)
    {
        var view = new LedgerAnalysisView();
        if (area == "groups") view.Groups.Add(field);
        else if (area == "labels") view.ColumnLabels.Add(field);
        else view.Values.Add(field);
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [view] }));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    public void InvalidAmountFilterNeverProducesSuccessShapedResults(string value)
    {
        var view = new LedgerAnalysisView { Filters = [new() { Field = "amount", Operator = "gte", Value = value }] };
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [view] }));
    }

    [Fact]
    public void TextFiltersAreCaseInsensitiveAndAmountBoundsAreInclusive()
    {
        var view = new LedgerAnalysisView { Filters = [new() { Field = "document", Value = "tax" },
            new() { Field = "amount", Operator = "lte", Value = "-750.1234" }] };
        LedgerAnalysis.Validate(new() { Views = [view] });
        Assert.Equal(2, LedgerAnalysis.Filter(Entries, view).Count);
        view.Filters[0].Value = "missing";
        Assert.Empty(LedgerAnalysis.Filter(Entries, view));
    }

    [Fact]
    public void SortingUsesNumericAmountsAndEntryNumbers()
    {
        var view = new LedgerAnalysisView { SortField = "entry" };
        Assert.Equal(10, LedgerAnalysis.Sort(Entries.Reverse(), view).First().EntryNumber);
        view.SortField = "amount";
        view.SortDescending = true;
        Assert.Equal(20000m, LedgerAnalysis.Amount(LedgerAnalysis.Sort(Entries, view).First(), "amount"));
    }

    [Fact]
    public void DuplicateViewsAreDetachedAndPreferencesContainNoLedgerData()
    {
        var original = new LedgerAnalysisView { Groups = ["account"], Filters = [new() { Value = "9510" }] };
        var copy = original.Copy();
        copy.Name = "Copy"; copy.Groups.Add("month"); copy.Filters[0].Value = "1000"; copy.Columns.Remove("date");
        Assert.Single(original.Groups);
        Assert.Equal("9510", original.Filters[0].Value);
        Assert.Contains("date", original.Columns);
        var json = JsonSerializer.Serialize(new LedgerAnalysisPreferences { Views = [original, copy], SelectedView = 1 });
        var restored = JsonSerializer.Deserialize<LedgerAnalysisPreferences>(json)!;
        LedgerAnalysis.Validate(restored);
        Assert.Equal(1, restored.SelectedView);
        Assert.DoesNotContain("PostingDate", json);
        Assert.DoesNotContain("DebitAmount", json);
    }

    [Fact]
    public void CorruptAndUnsupportedPreferencesAreRejected()
    {
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Version = 2 }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { SelectedView = 5 }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [] }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [new() { Columns = ["unknown"] }] }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [new() { Columns = ["date", "date"] }] }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [new() { Columns = null! }] }));
        Assert.Throws<ValidationException>(() => LedgerAnalysis.Validate(new() { Views = [new() { Filters = [new() { Field = "account", Operator = "gte" }] }] }));
    }

    [Fact]
    public void PivotLimitRejectsOverflowWithoutTruncatingAtTheBoundary()
    {
        var entries = Enumerable.Range(1, 201).Select(index => Entry(index, new(2026, 10, 29), index.ToString(), index, 0)).ToArray();
        var view = new LedgerAnalysisView { ColumnLabels = ["account"], Values = ["amount"] };
        Assert.Equal(200, LedgerAnalysis.PivotKeys(entries.Take(200).ToArray(), view).Count);
        Assert.Throws<ValidationException>(() => LedgerAnalysis.PivotKeys(entries, view));
        Assert.Equal(20301m, entries.Sum(entry => entry.DebitAmount));
        view.Values = ["debit", "credit"];
        Assert.Throws<ValidationException>(() => LedgerAnalysis.PivotKeys(entries.Take(101).ToArray(), view));
    }
}
