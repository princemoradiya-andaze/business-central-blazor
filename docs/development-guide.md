# Development guide

## Local workflow

Use the repository's .NET 10 SDK selection in `global.json`.

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore
dotnet test BusinessCentralBlazor.sln --no-build
dotnet run --project src/BusinessCentralBlazor.Web
```

SQL Server is not yet required to build this foundation. Do not add local connection strings or credentials to tracked files.

## Branch and review workflow

- `main` is production-ready; `dev` is the integration branch.
- Create short-lived feature/fix branches from `dev`; open pull requests into `dev`.
- Promote a tested release from `dev` to `main` through a pull request.
- Require CI to pass and a human review before merging. Restrict direct pushes, protect release tags, and require production environment approval before deployment.
- Do not force-push, commit secrets, or deploy production from a feature branch.
- Create one GitHub issue per bounded task, then create `feature/#<issue-number>-<short-description>` or `fix/#<issue-number>-<short-description>` from `dev`. Link the issue in its PR and keep it open until local, dev, and applicable release verification are complete.

At inspection time the GitHub repository had only `main`. The authenticated GitHub API returned HTTP 403 for branch-protection/ruleset reads and stated that a private repository needs GitHub Pro (or to be public) for this feature. Existing protection status could not be verified, and this task made no protection changes. Keep the repository private and enable rules after the plan/permissions allow it; do not make it public to work around the restriction.

## CI/CD

`.github/workflows/ci.yml` restores, builds, and tests the solution for `main`, `dev`, and pull requests targeting those branches. Deployment workflows are intentionally not present: Azure CLI is not installed, Azure resources/credentials were not verified, and production deployment requires environment approvals and secret-managed credentials. See [the project playbook](project-playbook.md) for phase gates and the dated change history.
