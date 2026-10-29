using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using BusinessCentralBlazor.Domain.Enums;

namespace BusinessCentralBlazor.Web.Services;

public sealed record TrialBalanceRequest
{
    public DateOnly AsOfDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public Guid? AccountId { get; set; }
    [StringLength(100)] public string? AccountName { get; set; }
    public AccountCategory? Category { get; set; }
    [StringLength(64)] public string? DocumentNumber { get; set; }
    public bool IncludeZeroBalances { get; set; } = true;
    public bool ShowCategory { get; set; } = true;
    public bool CompactLayout { get; set; }
    [Range(1, 500000)] public int MaximumRows { get; set; } = 500000;
    public string FormatRegion { get; set; } = "en-GB";

    public CultureInfo Culture => CultureInfo.GetCultureInfo(FormatRegion);

    public void Validate()
    {
        Validator.ValidateObject(this, new ValidationContext(this), validateAllProperties: true);
        if (AsOfDate == default)
        {
            throw new ValidationException("Choose an as of date.");
        }

        if (FormatRegion is not ("en-GB" or "en-US" or "de-DE"))
        {
            throw new ValidationException("Choose a supported format region.");
        }

        if (Category is { } category && !Enum.IsDefined(category))
        {
            throw new ValidationException("Choose a valid account category.");
        }
    }
}

public static class TrialBalanceCsv
{
    public static string Create(string companyName, TrialBalanceReport report, TrialBalanceRequest request, string accountFilter)
    {
        request.Validate();
        var csv = new StringBuilder();
        void Row(IEnumerable<string> cells) =>
            csv.AppendLine(string.Join(",", cells.Select(value => $"\"{value.Replace("\"", "\"\"")}\"")));
        static string SafeText(string value) =>
            value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') ||
            value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@') ||
            value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n') ? "'" + value : value;

        Row([SafeText(companyName), "Trial Balance", report.AsOfDate.ToString("d", request.Culture)]);
        Row(["Account", SafeText(accountFilter), "Name contains", SafeText(request.AccountName ?? ""),
            "Category", request.Category?.ToString() ?? "All", "Document contains", SafeText(request.DocumentNumber ?? ""),
            "Zero balances", request.IncludeZeroBalances ? "Included" : "Excluded"]);
        Row(["No.", "Account Name", .. request.ShowCategory ? new[] { "Category" } : [],
            .. request.CompactLayout ? new[] { "Balance" } : ["Debit Balance", "Credit Balance"]]);
        foreach (var line in report.Lines)
        {
            Row([SafeText(line.AccountNumber), SafeText(line.AccountName),
                .. request.ShowCategory ? new[] { line.Category.ToString() } : [],
                .. request.CompactLayout
                    ? new[] { (line.DebitBalance - line.CreditBalance).ToString("N4", request.Culture) }
                    : [line.DebitBalance.ToString("N4", request.Culture), line.CreditBalance.ToString("N4", request.Culture)]]);
        }

        Row(["Total", "", .. request.ShowCategory ? new[] { "" } : [],
            .. request.CompactLayout
                ? new[] { (report.TotalDebit - report.TotalCredit).ToString("N4", request.Culture) }
                : [report.TotalDebit.ToString("N4", request.Culture), report.TotalCredit.ToString("N4", request.Culture)]]);
        return csv.ToString();
    }
}
