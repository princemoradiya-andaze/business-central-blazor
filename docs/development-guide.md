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
- Verify merged `dev` locally against SQL Server, then promote a tested release from `dev` to `main` through a pull request and verify `main` locally.
- Require the configured CI check to pass before merging; follow repository PR protections.
- Do not force-push or commit secrets. Hosted deployment is not part of the current workflow.
- Create one GitHub issue per bounded task, then create `feature/#<issue-number>-<short-description>` or `fix/#<issue-number>-<short-description>` from `dev`. Link the issue in its PR and keep it open until local checks, merged `dev` verification, and applicable `main` verification are complete.

The `dev` branch was created from `main`. The repository is public by the user's choice. Both `main` and `dev` require PRs and the `build-and-test` status check, with strict up-to-date checks and administrator enforcement; zero approving reviews are configured, as requested. Direct force-pushes and deletion are disabled. Do not change repository visibility without authorization.

## CI/CD

`.github/workflows/ci.yml` restores, builds, and tests the solution for `main`, `dev`, and pull requests targeting those branches. CI has passed for earlier `dev` integrations; the current schema-alignment changes still need their PR checks. The agreed workflow uses local SQL Server Express and does not include hosted dev or production deployment. See [the project playbook](project-playbook.md) for phase gates and dated history.
