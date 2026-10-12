# Development guide

## Local workflow

Use the repository's .NET 10 SDK selection in `global.json`.

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore
dotnet test BusinessCentralBlazor.sln --no-build
dotnet run --project src/BusinessCentralBlazor.Web
```

SQL Server is not required for a build, but the initial schema has been migrated and verified against local SQL Server Express. For database work, restore the local EF tool, set `ConnectionStrings__ErpDatabase` in the current shell, and follow [the schema migration instructions](schema/initial-schema.md). CI checks that the EF model has no unapplied model changes after the migration. Do not add local connection strings containing credentials to tracked files.

## Branch and review workflow

- `main` is production-ready; `dev` is the integration branch.
- Create short-lived feature/fix branches from `dev`; open pull requests into `dev`.
- Promote a tested release from `dev` to `main` through a pull request.
- Require CI to pass and a human review before merging. Restrict direct pushes, protect release tags, and require production environment approval before deployment.
- Do not force-push, commit secrets, or deploy production from a feature branch.
- Create one GitHub issue per bounded task, then create `feature/#<issue-number>-<short-description>` or `fix/#<issue-number>-<short-description>` from `dev`. Link the issue in its PR and keep it open until local, dev, and applicable release verification are complete.

At the initial inspection, `dev` did not exist and GitHub reported a plan restriction when reading repository rulesets. The `dev` branch was subsequently created from `main`. The repository currently reports itself as public (the user confirmed this is intentional). Both `main` and `dev` now require PRs and the `build-and-test` status check, with strict up-to-date checks and administrator enforcement; zero approving reviews are configured, as requested. Direct force-pushes and deletion are disabled. Do not change repository visibility without authorization.

## CI/CD

`.github/workflows/ci.yml` restores, builds, and tests the solution for `main`, `dev`, and pull requests targeting those branches. CI has passed for the initial `dev` integration. The repository has no configured GitHub deployment environments, Azure CLI is not installed, and Azure resources/credentials were not verified; therefore no hosted dev or production deployment is configured or claimed. Production deployment requires environment approvals and secret-managed credentials. See [the project playbook](project-playbook.md) for phase gates and dated history.
