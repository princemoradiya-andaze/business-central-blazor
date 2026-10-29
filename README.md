# Business Central Blazor

An independently implemented, modular-monolith ERP application using Blazor Server, .NET, Entity Framework Core, and SQL Server. It is inspired by common ERP workflows; it is not Microsoft Dynamics 365 Business Central and does not include Microsoft implementation code or assets.

## Status

Phase 0 and Phase 1 are complete on `main`. Phase 1 delivers administrator-managed authentication, tenant/company context and authorization, the authenticated application shell, and a generic company-scoped Workspace notes example. Workspace notes was foundation UI, not an equivalent Business Central feature; it is now retired from product navigation and its former pages display an unavailable notice without CRUD. Existing notes, schema, example components, and company-scoped service code are retained. Public self-registration is disabled. Hosted deployment is not part of the current workflow.

## Prerequisites

- .NET 10 SDK
- SQL Server 2019 or a supported later release for database-backed development

## Build and run

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore
dotnet test BusinessCentralBlazor.sln --no-build
dotnet run --project src/BusinessCentralBlazor.Web
```

Set `ConnectionStrings__ErpDatabase` in the current PowerShell session before running the app or EF tools. For the local SQL Server migration, restore the repository-local EF tool and follow the [schema migration instructions](docs/schema/initial-schema.md). Never commit connection strings containing credentials.

## Architecture

- `BusinessCentralBlazor.Domain` holds business rules and has no dependencies on outer layers.
- `BusinessCentralBlazor.Application` contains use cases and depends on Domain.
- `BusinessCentralBlazor.Infrastructure` implements persistence and external integrations.
- `BusinessCentralBlazor.Web` hosts the Blazor Server interface.
- `tests/` contains automated test projects.

See [the architecture overview](docs/architecture/overview.md), [the schema discovery report](docs/schema/discovery-report.md), [the initial application schema](docs/schema/initial-schema.md), and [the roadmap](docs/roadmap.md).

## Development workflow

**All pages** (`/navigation`) shows a pale-cyan directory with Home shortcuts and all existing
menu destinations. Each group starts expanded; click its heading/chevron, or focus it and press
Enter or Space, to collapse or expand its links independently. Compact 16px directory text sits
directly below the left-aligned Business Manager heading, in three independent columns on desktop
and one column on narrow screens. The back control sits in the left gutter on desktop and above
the heading on narrow screens, leaving the title and group headings on the same alignment.

The global **Settings > Company Information** card edits the active company's core profile
and logo, independently of base-currency settings. **Environment: Local** opens a searchable
Available Companies panel above the current page or card without losing its draft. The header
remains usable above cards, background scrolling is locked, and real loading states cover the
entire screen, including the header. Sign-in uses email/Next followed by password/Sign in;
authentication remains the existing local ASP.NET Core Identity flow, not Microsoft SSO.
Apply the additive `AddCompanyInformation` migration before using the profile card
(see the development guide).

**More** contains Home (unchanged) and **Company Settings**, which uses the shared financial detail
layout. Its **New Currency** card uses the shared card dialog with Save/Cancel, General fields,
and SVG expand/restore. Shared-catalog creation and active-company base-currency saving remain
separate operations, with validation, unsaved-change protection, and existing financial-activity
restrictions preserved. No migration or note-data deletion is needed.

Reports provides a compact **General Ledger Entries** workspace with applied filters, search,
selection, list/tile views, Details, and links to account activity and posted journals.
The analysis icon opens a read-only workspace with multiple named views, configurable columns,
nested groups/subtotals, calendar date hierarchies, pivots, per-view filters, and selection statistics.
View configurations are retained in this browser per user and company; ledger data is never saved
to browser storage. Leave analysis mode to return to the unchanged normal workspace.
Account activity uses the matching financial detail layout with date filters, balances, and posted entries.
**Trial Balance** opens a report request dialog over Home with SVG expand/restore controls and separate account/totals filters, layouts,
optional category/zero balances, and Advanced row-limit/region settings. Preview, CSV, and browser
print/Save as PDF share the same report snapshot. Maximum rows is a safety limit, not a page size;
oversized reports are rejected rather than truncating financial totals. PDF uses the browser printer.
See the development guide for supported filters and output limitations.

Inventory uses one **Stock Movements** menu link for movement history, with the shared list/tile
workspace and an explicit **+ New** stock movement card. Draft creation opens its linked journal;
stock changes only on explicit posting. Items and the existing inventory services are unchanged.

Cash Management keeps the Bank Accounts list and account cards unchanged. **Block/Reactivate** opens
a bank-specific confirmation card; **Transactions** opens a compact receipts/payments workspace with
a Manual statements tab. **+ New receipt**, **+ New payment**, and **+ New statement** use explicit
creation cards with validation and unsaved-input protection. Journals still require separate review
and posting in General journals. Linked journal batches and manual statement reconciliation use matching
Business Central-style detail pages with compact commands, General fields, and responsive line grids.
Posting, reversal, matching, completion, and company-scoped banking services are unchanged.

Customers, Vendors, Items, Bank accounts, Chart of Accounts, General journals, Dimensions, Accounting periods and Tax setup select the first visible record on opening. Clicking a row or tile selects it without deselecting it; filters preserve that selection when possible, otherwise select the first remaining record. Empty results clear selection and details. List, Tall tiles and Tiles views and the independent Filters/Details panes remain available. Click a record's number/code link (also available in tiles) to open its prefilled, directly editable card.

These modules share the same card layout and save lifecycle, using only their existing fields and company-scoped services. **New** has explicit Save/Cancel and no previous/next controls. Existing cards save valid changes on leaving a field, report Unsaved changes/Saving/Saved or an error, and retain invalid or failed drafts for retry or explicit discard. Saves use detached snapshots and run serially, including edits made during an outstanding save. Item/bank saves carry forward the concurrency token returned by that write, not a token from a later list refresh. Back, Escape, internal navigation and previous/next first flush pending edits and block on validation/save failure. Browser reload/close uses the standard unsaved-change prompt; it cannot guarantee persistence after a disconnected circuit or browser termination.

Previous/next follows the current filtered service ordering and disables boundary arrows. After renumbering, ordering is recalculated; if a saved name/status change removes the open record from the filter, next chooses the first remaining record and previous the last. Customer Delete still marks inactive with reactivation available; supported block/reactivate, activity, transactions and stock movement workflows are unchanged. Stock movements remain on their separate page.

Finance cards retain the existing workflows: journal batches link to their lines and posting/reversal actions (posted batch cards are read-only), dimensions link to their values, and existing accounting periods allow only status changes. Tax code cards manage effective-dated rates with explicit Save rate/Cancel; finish or cancel an open rate editor before navigating away, or explicitly discard and close. Used rates remain immutable and overlapping periods are rejected by the company-scoped service. Tax remains manual on journal lines and is not calculated on sales or purchase documents.

Local routes: `/financial/sales/customers`, `/financial/purchasing/vendors`, `/financial/inventory`, `/financial/banks`, `/financial/general-ledger/accounts`, `/financial/journals`, `/financial/dimensions`, `/financial/accounting-periods` and `/financial/tax-setup`. Each accepts `/new` and `/{record-guid}/edit`, including existing customer/vendor/G/L deep links. Run `dotnet run --project src\BusinessCentralBlazor.Web --urls http://localhost:5097` with your configured local database, then sign in and choose a company before testing. Use disposable records when testing autosave.

Use short-lived issue branches and pull requests into `dev`; promote only completed, reviewed, CI-passing releases from `dev` to `main`. See [the development guide](docs/development-guide.md).

Sales Orders (`/financial/sales/orders`), Ship and Invoice Orders (`/financial/sales/fulfillment`), and Open Receivables (`/financial/sales/receivables`) use the same compact workspace with search, status filters, first-record selection, views, and details panes. **+ New** on Sales Orders opens `/financial/sales/orders/new` with explicit Save/Cancel and editable header/line fields. The former `/financial/sales/create-order` URL remains a compatible entry to this card but is removed from navigation; the Home shortcut uses the new URL. Customers is unchanged.

Existing order cards (`/financial/sales/orders/{order-guid}`) are read-only and preserve shipment/invoice journal links. Processing cards (`/financial/sales/fulfillment/{order-guid}`) accept posting setup for eligible Open/Shipped orders; creating a journal opens its General journals page for review and posting, not automatic shipment/invoicing. Modified posting setup must be explicitly discarded before leaving. Open receivable cards (`/financial/sales/receivables?invoice={invoice-number}`) show posted invoice amounts read-only. Lists and cards resolve records through the existing active-company services; unavailable or ineligible deep links show an explicit error.

Purchasing follows the same workspace/card pattern at `/financial/purchasing/orders`, `/financial/purchasing/fulfillment`, and `/financial/purchasing/payables`. **+ New** opens `/financial/purchasing/orders/new`; existing order cards (`/orders/{order-guid}`) and posted payable cards (`/payables?invoice={invoice-number}`, with an escaped invoice number) are read-only. Processing (`/fulfillment/{order-guid}`) creates receipt/invoice journal drafts for explicit review and posting in General journals. **Purchasing setup** keeps existing GRNI configuration accessible at `/financial/purchasing/setup`. Vendors and purchasing service rules remain unchanged. The former combined page is an orders alias with compatibility for its old workflow anchors; new menu and Home links use dedicated routes.
