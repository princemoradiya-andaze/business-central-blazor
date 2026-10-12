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

## Identity and administrator-managed accounts

The web app requires `ConnectionStrings__ErpDatabase` to use Identity-backed routes. Apply the existing migrations before bootstrapping an account. Public self-registration is intentionally disabled. The local administrator tool creates the first administrator exactly once; subsequent accounts are created by an operator using the same trusted local tool. Protect local access to the database and shell accordingly.

```powershell
$env:ConnectionStrings__ErpDatabase = 'Server=.\BCDEMO;Database=BusinessCentralBlazor_Phase0;Trusted_Connection=True;TrustServerCertificate=True'
dotnet ef database update --project src\BusinessCentralBlazor.Infrastructure --startup-project src\BusinessCentralBlazor.Web
$env:BC_ADMIN_EMAIL = Read-Host 'Administrator email'
$env:BC_ADMIN_DISPLAY_NAME = Read-Host 'Administrator display name'
$securePassword = Read-Host 'Administrator password' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
try {
    $env:BC_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    dotnet run --project tools\BusinessCentralBlazor.AdminTool -- bootstrap-admin
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    Remove-Item Env:BC_ADMIN_PASSWORD -ErrorAction SilentlyContinue
}
Remove-Item Env:BC_ADMIN_EMAIL, Env:BC_ADMIN_DISPLAY_NAME -ErrorAction SilentlyContinue
```

For each later account, set `BC_USER_EMAIL`, `BC_USER_DISPLAY_NAME`, and `BC_USER_PASSWORD` in the current process, then run `dotnet run --project tools\BusinessCentralBlazor.AdminTool -- create-user`. The tool assigns the standard `User` role and refuses to run until an administrator exists. Use a unique password of at least 12 characters with uppercase, lowercase, digit, and non-alphanumeric characters; Identity locks accounts for 15 minutes after five failed sign-in attempts. The tool never accepts passwords as command-line arguments or prints them. Environment variables are still visible to the process and its child processes, so remove them immediately after use and do not persist them in shell profiles or repository files.

## Company membership and active-company context

Authenticated users can choose only active companies for which both a tenant membership and a company membership exist. The selected company ID is stored as a claim in the protected authentication cookie, but that claim is only a selection hint: `IActiveCompanyContext.ResolveAsync` rechecks the current membership rows and company-active status against SQL Server whenever called. Missing, removed, or inactive memberships therefore resolve to no active company. Users without an eligible company see a safe no-access state; they must be assigned a membership before accessing company-owned features. Any future business operation must resolve and enforce this context server-side rather than trusting a company ID supplied by the browser. The company-context change does not alter the EF model or require a migration.

## Company authorization and data isolation

The Blazor component endpoint and company-owned pages use the `ActiveCompany` authorization policy. It requires a signed-in user whose selected company still has an active company membership and tenant membership. The Razor Page at `/account/company` remains available to signed-in users without an active selection so they can choose an eligible company.

This route policy is not a substitute for per-operation checks. Every company-owned API or service operation must call `ICompanyScopeAuthorizer.ResolveAuthorizedCompanyAsync` with the authenticated principal and the requested tenant/company IDs, then use the returned validated IDs in all database filters and writes. It returns no scope for anonymous users, absent/stale selections, revoked memberships, inactive companies, or a tenant/company pair other than the validated active selection. Never query or mutate data using only an ID supplied by the client. The authorization boundary is application-level; database row-level security is not implemented.

## Branch and review workflow

- `main` is production-ready; `dev` is the integration branch.
- Create short-lived feature/fix branches from `dev`; open pull requests into `dev`.
- Verify merged `dev` locally against SQL Server, then promote a tested release from `dev` to `main` through a pull request and verify `main` locally.
- Require the configured CI check to pass before merging; ask the user for explicit approval before merging every feature or release PR.
- Do not force-push or commit secrets. Hosted deployment is not part of the current workflow.
- Create one GitHub issue per bounded task, then create `feature/<issue-number>-<short-description>` or `fix/<issue-number>-<short-description>` from `dev`. Link the issue in its PR and keep it open until local checks, merged `dev` verification, and applicable `main` verification are complete.

The `dev` branch was created from `main`. The repository is public by the user's choice. Both `main` and `dev` require PRs and the `build-and-test` status check, with strict up-to-date checks and administrator enforcement; zero approving reviews are configured, as requested. Direct force-pushes and deletion are disabled. Do not change repository visibility without authorization.

## CI/CD

`.github/workflows/ci.yml` restores, builds, and tests the solution for `main`, `dev`, and pull requests targeting those branches. Confirm each required check has completed successfully before asking for merge approval. The agreed workflow uses local SQL Server Express and does not include hosted dev or production deployment. See [the project playbook](project-playbook.md) for phase gates and dated history.
