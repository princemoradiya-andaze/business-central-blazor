namespace BusinessCentralBlazor.Web.Components.Layout;

internal static class NavigationMenuCatalog
{
    public static IReadOnlyList<NavigationGroup> Groups { get; } =
    [
        new("finance", "Finance",
        [
            new("General journals", "/financial/journals"),
            new("Chart of accounts", "/financial/general-ledger/accounts"),
            new("Dimensions", "/financial/dimensions"),
            new("Accounting periods", "/financial/accounting-periods"),
            new("Tax setup", "/financial/tax-setup")
        ],
        ["/financial/journals", "/financial/general-ledger/accounts", "/financial/dimensions", "/financial/accounting-periods", "/financial/tax-setup"]),
        new("sales", "Sales",
        [
            new("Customers", "/financial/sales/customers"),
            new("Create sales order", "/financial/sales/create-order"),
            new("Sales orders", "/financial/sales/orders"),
            new("Ship and invoice orders", "/financial/sales/fulfillment"),
            new("Open receivables", "/financial/sales/receivables")
        ],
        ["/financial/sales"]),
        new("purchasing", "Purchasing",
        [
            new("Vendors", "/financial/purchasing#vendors-heading"),
            new("Create purchase order", "/financial/purchasing#purchase-order-heading"),
            new("Receive and invoice orders", "/financial/purchasing#receipt-invoice-heading"),
            new("Open vendor payables", "/financial/purchasing#payables-heading")
        ],
        ["/financial/purchasing"]),
        new("cash", "Cash management",
        [
            new("Bank accounts and reconciliation", "/financial/banks")
        ],
        ["/financial/banks", "/financial/bank-statements"]),
        new("inventory", "Inventory",
        [
            new("Inventory items", "/financial/inventory#items-heading"),
            new("Create stock movement", "/financial/inventory#movement-heading"),
            new("Movement history", "/financial/inventory#movements-heading")
        ],
        ["/financial/inventory"]),
        new("reports", "Reports",
        [
            new("General ledger entries", "/financial/general-ledger/entries"),
            new("Trial balance", "/financial/general-ledger/trial-balance")
        ],
        ["/financial/general-ledger/entries", "/financial/general-ledger/trial-balance"]),
        new("more", "More",
        [
            new("Home", "/"),
            new("Company settings", "/financial/company-settings"),
            new("Workspace notes", "/workspace/notes")
        ],
        ["/financial/company-settings", "/workspace"])
    ];
}

internal sealed record NavigationGroup(
    string Key,
    string Label,
    IReadOnlyList<NavigationMenuItem> Items,
    IReadOnlyList<string> ActivePrefixes);

internal sealed record NavigationMenuItem(string Label, string Href);
