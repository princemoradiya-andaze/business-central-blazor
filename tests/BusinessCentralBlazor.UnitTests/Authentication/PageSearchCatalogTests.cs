using BusinessCentralBlazor.Web.Components.Layout;

namespace BusinessCentralBlazor.UnitTests.Authentication;

public sealed class PageSearchCatalogTests
{
    [Theory]
    [InlineData("CUSTOMERS", "/financial/sales/customers")]
    [InlineData("trial balance", "/financial/general-ledger/trial-balance")]
    [InlineData("accounts chart", "/financial/general-ledger/accounts")]
    [InlineData("company information", "/?companyInformation=1")]
    [InlineData("purchasing setup", "/financial/purchasing/setup")]
    [InlineData("all pages", "/navigation")]
    public void SearchFindsActualDestinations(string query, string href)
    {
        Assert.Contains(PageSearchCatalog.Search(query), item => item.Href == href);
    }

    [Fact]
    public void ExploreGroupsAreDistinctAndContainNoDuplicateDestinations()
    {
        var pages = PageSearchCatalog.Search("", false);
        var reports = PageSearchCatalog.Search("", true);
        Assert.NotEmpty(pages);
        Assert.NotEmpty(reports);
        Assert.All(pages, item => Assert.False(item.IsReport));
        Assert.All(reports, item => Assert.True(item.IsReport));
        var all = PageSearchCatalog.Search("");
        Assert.Equal(all.Count, all.Select(item => item.Href).Distinct().Count());
        Assert.Single(all, item => item.IsCompanyInformation);
    }

    [Fact]
    public void UnknownQueryHasNoFallbackResults() => Assert.Empty(PageSearchCatalog.Search("no-such-page"));
}
