# Business Central Blazor

An independently implemented, modular-monolith ERP application using Blazor Server, .NET, Entity Framework Core, and SQL Server. It is inspired by common ERP workflows; it is not Microsoft Dynamics 365 Business Central and does not include Microsoft implementation code or assets.

## Status

Phase 0 and Phase 1 are complete on `main`. Phase 1 delivers administrator-managed authentication, tenant/company context and authorization, the authenticated application shell, and a generic company-scoped Workspace notes example. Workspace notes is foundation UI, not an equivalent Business Central feature. Public self-registration is disabled. Hosted deployment is not part of the current workflow.

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

Customers, Vendors, Items, Bank accounts, Chart of Accounts, General journals, Dimensions, Accounting periods and Tax setup select the first visible record on opening. Clicking a row or tile selects it without deselecting it; filters preserve that selection when possible, otherwise select the first remaining record. Empty results clear selection and details. List, Tall tiles and Tiles views and the independent Filters/Details panes remain available. Click a record's number/code link (also available in tiles) to open its prefilled, directly editable card.

These modules share the same card layout and save lifecycle, using only their existing fields and company-scoped services. **New** has explicit Save/Cancel and no previous/next controls. Existing cards save valid changes on leaving a field, report Unsaved changes/Saving/Saved or an error, and retain invalid or failed drafts for retry or explicit discard. Saves use detached snapshots and run serially, including edits made during an outstanding save. Item/bank saves carry forward the concurrency token returned by that write, not a token from a later list refresh. Back, Escape, internal navigation and previous/next first flush pending edits and block on validation/save failure. Browser reload/close uses the standard unsaved-change prompt; it cannot guarantee persistence after a disconnected circuit or browser termination.

Previous/next follows the current filtered service ordering and disables boundary arrows. After renumbering, ordering is recalculated; if a saved name/status change removes the open record from the filter, next chooses the first remaining record and previous the last. Customer Delete still marks inactive with reactivation available; supported block/reactivate, activity, transactions and stock movement workflows are unchanged. Stock movements remain on their separate page.

Finance cards retain the existing workflows: journal batches link to their lines and posting/reversal actions (posted batch cards are read-only), dimensions link to their values, and existing accounting periods allow only status changes. Tax code cards manage effective-dated rates with explicit Save rate/Cancel; finish or cancel an open rate editor before navigating away, or explicitly discard and close. Used rates remain immutable and overlapping periods are rejected by the company-scoped service. Tax remains manual on journal lines and is not calculated on sales or purchase documents.

Local routes: `/financial/sales/customers`, `/financial/purchasing/vendors`, `/financial/inventory`, `/financial/banks`, `/financial/general-ledger/accounts`, `/financial/journals`, `/financial/dimensions`, `/financial/accounting-periods` and `/financial/tax-setup`. Each accepts `/new` and `/{record-guid}/edit`, including existing customer/vendor/G/L deep links. Run `dotnet run --project src\BusinessCentralBlazor.Web --urls http://localhost:5097` with your configured local database, then sign in and choose a company before testing. Use disposable records when testing autosave.

Use short-lived issue branches and pull requests into `dev`; promote only completed, reviewed, CI-passing releases from `dev` to `main`. See [the development guide](docs/development-guide.md).
