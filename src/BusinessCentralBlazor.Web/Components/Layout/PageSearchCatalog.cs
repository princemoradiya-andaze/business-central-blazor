namespace BusinessCentralBlazor.Web.Components.Layout;

public static class PageSearchCatalog
{
    public static IReadOnlyList<PageSearchItem> Search(string query, bool? reports = null)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return NavigationMenuCatalog.Groups
            .SelectMany(group => group.Items.Select(item =>
                new PageSearchItem(item.Label, item.Href, group.Label, group.Key == "reports")))
            .Append(new PageSearchItem("Company Information", "/?companyInformation=1", "More", false, true))
            .Append(new PageSearchItem("Purchasing setup", "/financial/purchasing/setup", "Purchasing", false))
            .Append(new PageSearchItem("All pages", "/navigation", "More", false))
            .Where(item => (reports is null || item.IsReport == reports) &&
                words.All(word => $"{item.Label} {item.Group}".Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => !item.Label.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record PageSearchItem(string Label, string Href, string Group, bool IsReport, bool IsCompanyInformation = false);
