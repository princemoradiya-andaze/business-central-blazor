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

The Customers list opens **New customer** as a Customer Card dialog, including direct visits to `/financial/sales/customers/new`. Save creates the customer using the existing company-scoped workflow; Cancel, the back arrow, Close, or Escape dismisses the dialog without saving. Only customer number, name, and receivables account are currently supported.

The Customers workspace supports List, Tall tiles, and Tiles views, plus independently toggled filter and customer-details panes. Select a row or tile to enable Edit and Delete; Delete marks a customer inactive rather than permanently removing it, and inactive customers can be reactivated.

Use short-lived issue branches and pull requests into `dev`; promote only completed, reviewed, CI-passing releases from `dev` to `main`. See [the development guide](docs/development-guide.md).
