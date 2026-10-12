# Business Central Blazor

An independently implemented, modular-monolith ERP application using Blazor Server, .NET, Entity Framework Core, and SQL Server. It is inspired by common ERP workflows; it is not Microsoft Dynamics 365 Business Central and does not include Microsoft implementation code or assets.

## Status

The repository is at the Phase 0 foundation stage. The solution and layer boundaries are scaffolded; authentication, persistence, business modules, and database migrations have not been implemented.

## Prerequisites

- .NET 10 SDK
- SQL Server for database-backed development (not yet required to build the current scaffold)

## Build and run

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore
dotnet test BusinessCentralBlazor.sln --no-build
dotnet run --project src/BusinessCentralBlazor.Web
```

## Architecture

- `BusinessCentralBlazor.Domain` holds business rules and has no dependencies on outer layers.
- `BusinessCentralBlazor.Application` contains use cases and depends on Domain.
- `BusinessCentralBlazor.Infrastructure` implements persistence and external integrations.
- `BusinessCentralBlazor.Web` hosts the Blazor Server interface.
- `tests/` contains automated test projects.

See [the architecture overview](docs/architecture/overview.md), [the schema discovery report](docs/schema/discovery-report.md), and [the roadmap](docs/roadmap.md).

## Development workflow

Use short-lived feature branches and pull requests into `dev`; promote reviewed, CI-passing releases from `dev` to `main`. The GitHub repository currently has only `main`; existing branch-protection status could not be verified. See [the development guide](docs/development-guide.md).
