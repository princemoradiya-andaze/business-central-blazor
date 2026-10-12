# Business Central Blazor

An independently implemented, modular-monolith ERP application using Blazor Server, .NET, Entity Framework Core, and SQL Server. It is inspired by common ERP workflows; it is not Microsoft Dynamics 365 Business Central and does not include Microsoft implementation code or assets.

## Status

The repository is at the Phase 0 foundation stage. The .NET solution, initial EF Core/Identity schema, SQL Server migration, and local database verification are in place. Login/authorization wiring, posting services, business modules, and deployment pipelines have not been implemented.

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

For the local SQL Server migration, restore the repository-local EF tool and follow the [schema migration instructions](docs/schema/initial-schema.md). Never commit local connection strings containing credentials.

## Architecture

- `BusinessCentralBlazor.Domain` holds business rules and has no dependencies on outer layers.
- `BusinessCentralBlazor.Application` contains use cases and depends on Domain.
- `BusinessCentralBlazor.Infrastructure` implements persistence and external integrations.
- `BusinessCentralBlazor.Web` hosts the Blazor Server interface.
- `tests/` contains automated test projects.

See [the architecture overview](docs/architecture/overview.md), [the schema discovery report](docs/schema/discovery-report.md), [the initial application schema](docs/schema/initial-schema.md), and [the roadmap](docs/roadmap.md).

## Development workflow

Use short-lived issue branches and pull requests into `dev`; promote only completed, reviewed, CI-passing releases from `dev` to `main`. See [the development guide](docs/development-guide.md).
