using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

namespace BusinessCentralBlazor.Web.Services;

public sealed record LedgerAnalysisField(string Id, string Label, bool IsAmount = false, bool CanGroup = true);

public sealed class LedgerAnalysisFilter
{
    public string Field { get; set; } = "account";
    public string Operator { get; set; } = "contains";
    public string Value { get; set; } = "";
}

public sealed class LedgerAnalysisView
{
    public string Name { get; set; } = "Analysis 1";
    public List<string> Columns { get; set; } = ["date", "document", "account", "name", "description", "debit", "credit", "amount"];
    public List<string> Groups { get; set; } = [];
    public List<string> Values { get; set; } = ["amount"];
    public bool Pivot { get; set; }
    public List<string> ColumnLabels { get; set; } = [];
    public List<LedgerAnalysisFilter> Filters { get; set; } = [];
    public string? SortField { get; set; }
    public bool SortDescending { get; set; }

    public LedgerAnalysisView Copy() => JsonSerializer.Deserialize<LedgerAnalysisView>(JsonSerializer.Serialize(this))!;
}

public sealed class LedgerAnalysisPreferences
{
    public int Version { get; set; } = 1;
    public int SelectedView { get; set; }
    public List<LedgerAnalysisView> Views { get; set; } = [new()];
}

public sealed record LedgerAnalysisGroup(string Key, string Label, int Depth, IReadOnlyList<GeneralLedgerEntryListItem> Entries,
    IReadOnlyList<LedgerAnalysisGroup> Children);

public static class LedgerAnalysis
{
    public static IReadOnlyList<LedgerAnalysisField> Fields { get; } =
    [
        new("date", "Posting Date"), new("year", "Posting Date Year"), new("quarter", "Posting Date Quarter"),
        new("month", "Posting Date Month"), new("entry", "Entry No.", CanGroup: false),
        new("account", "G/L Account No."), new("name", "G/L Account Name"), new("document", "Document No."),
        new("description", "Description"), new("debit", "Debit Amount", true, false),
        new("credit", "Credit Amount", true, false), new("amount", "Amount (Debit - Credit)", true, false)
    ];

    public static LedgerAnalysisField Field(string id) =>
        Fields.FirstOrDefault(field => field.Id == id) ?? throw new ValidationException("Unknown analysis field.");

    public static string Text(GeneralLedgerEntryListItem entry, string field) => field switch
    {
        "date" => entry.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        "year" => entry.PostingDate.Year.ToString(CultureInfo.InvariantCulture),
        "quarter" => $"{entry.PostingDate.Year} Q{(entry.PostingDate.Month + 2) / 3}",
        "month" => entry.PostingDate.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        "entry" => entry.EntryNumber.ToString(CultureInfo.InvariantCulture),
        "account" => entry.AccountNumber,
        "name" => entry.AccountName,
        "document" => entry.DocumentNumber,
        "description" => entry.Description,
        _ => Amount(entry, field).ToString("G", CultureInfo.InvariantCulture)
    };

    public static decimal Amount(GeneralLedgerEntryListItem entry, string field) => field switch
    {
        "debit" => entry.DebitAmount,
        "credit" => entry.CreditAmount,
        "amount" => entry.DebitAmount - entry.CreditAmount,
        _ => throw new ValidationException("Only amount fields can be added to Values.")
    };

    public static void Validate(LedgerAnalysisPreferences preferences)
    {
        if (preferences.Version != 1 || preferences.Views is null || preferences.Views.Count is < 1 or > 50 ||
            preferences.SelectedView < 0 || preferences.SelectedView >= preferences.Views.Count)
            throw new ValidationException("The saved analysis configuration is invalid or uses an unsupported version.");
        foreach (var view in preferences.Views)
        {
            if (view is null || string.IsNullOrWhiteSpace(view.Name) || view.Name.Length > 60 ||
                view.Columns is null || view.Groups is null || view.Values is null || view.ColumnLabels is null || view.Filters is null)
                throw new ValidationException("Each analysis view must have a name and a valid configuration.");
            ValidateFields(view.Columns, _ => true);
            ValidateFields(view.Groups, field => field.CanGroup);
            ValidateFields(view.ColumnLabels, field => field.CanGroup);
            ValidateFields(view.Values, field => field.IsAmount);
            if (view.SortField is not null) Field(view.SortField);
            if (view.Filters.Count > 30) throw new ValidationException("Use at most 30 filters per analysis view.");
            foreach (var filter in view.Filters)
            {
                if (filter is null || filter.Value is null || filter.Value.Length > 200)
                    throw new ValidationException("Analysis filter values must be at most 200 characters.");
                var field = Field(filter.Field);
                var allowed = field.IsAmount ? new[] { "equals", "gte", "lte" } : ["contains", "equals"];
                if (!allowed.Contains(filter.Operator))
                    throw new ValidationException("The filter comparison is not supported for this field.");
                if (field.IsAmount && filter.Value.Length > 0 &&
                    !decimal.TryParse(filter.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out _))
                    throw new ValidationException("Enter a valid amount using a decimal point, without thousands separators.");
            }
        }
    }

    private static void ValidateFields(List<string> ids, Func<LedgerAnalysisField, bool> eligible)
    {
        if (ids.Count > Fields.Count || ids.Distinct().Count() != ids.Count || ids.Any(id => !eligible(Field(id))))
            throw new ValidationException("An analysis field is duplicated or is not eligible for this area.");
    }

    public static IReadOnlyList<GeneralLedgerEntryListItem> Filter(
        IEnumerable<GeneralLedgerEntryListItem> entries, LedgerAnalysisView view)
    {
        return entries.Where(entry => view.Filters.All(filter =>
        {
            if (filter.Value.Length == 0) return true;
            if (Field(filter.Field).IsAmount)
            {
                var value = decimal.Parse(filter.Value, CultureInfo.InvariantCulture);
                var amount = Amount(entry, filter.Field);
                return filter.Operator switch { "equals" => amount == value, "gte" => amount >= value, _ => amount <= value };
            }
            var text = Text(entry, filter.Field);
            return filter.Operator == "equals" ? text.Equals(filter.Value, StringComparison.OrdinalIgnoreCase) :
                text.Contains(filter.Value, StringComparison.OrdinalIgnoreCase);
        })).ToArray();
    }

    public static IEnumerable<GeneralLedgerEntryListItem> Sort(IEnumerable<GeneralLedgerEntryListItem> entries, LedgerAnalysisView view)
    {
        if (view.SortField is not { } field) return entries;
        if (Field(field).IsAmount)
            return view.SortDescending ? entries.OrderByDescending(entry => Amount(entry, field)) : entries.OrderBy(entry => Amount(entry, field));
        if (field == "entry")
            return view.SortDescending ? entries.OrderByDescending(entry => entry.EntryNumber) : entries.OrderBy(entry => entry.EntryNumber);
        return view.SortDescending ? entries.OrderByDescending(entry => Text(entry, field), StringComparer.OrdinalIgnoreCase) :
            entries.OrderBy(entry => Text(entry, field), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<LedgerAnalysisGroup> Group(IReadOnlyList<GeneralLedgerEntryListItem> entries,
        IReadOnlyList<string> fields, int depth = 0, string path = "")
    {
        if (depth >= fields.Count) return [];
        return entries.GroupBy(entry => Text(entry, fields[depth]), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var key = path + "/" + JsonSerializer.Serialize(group.Key);
                var rows = group.ToArray();
                return new LedgerAnalysisGroup(key, group.Key, depth, rows, Group(rows, fields, depth + 1, key));
            }).ToArray();
    }

    public static string Key(GeneralLedgerEntryListItem entry, IReadOnlyList<string> fields) =>
        JsonSerializer.Serialize(fields.Select(field => Text(entry, field)));

    public static IReadOnlyList<string> PivotKeys(IReadOnlyList<GeneralLedgerEntryListItem> entries, LedgerAnalysisView view)
    {
        var keys = entries.Select(entry => Key(entry, view.ColumnLabels)).Distinct().Order(StringComparer.Ordinal).ToArray();
        if ((long)keys.Length * view.Values.Count > 200)
            throw new ValidationException("This pivot exceeds 200 value columns. Narrow the list or analysis filters, or choose fewer column labels or values. No partial totals are shown.");
        return keys;
    }

    public static string KeyLabel(string key) => string.Join(" / ", JsonSerializer.Deserialize<string[]>(key)!);
}
