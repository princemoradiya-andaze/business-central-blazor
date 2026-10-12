# Business Central Blazor — Project Playbook

> Wiki-ready project guide. Keep this page current as workflow, phase status, verification, and release history change. This page is a project record, not evidence that a deployment or release succeeded.

## Project purpose

Build an independently implemented, modular-monolith ERP using Blazor Server, .NET, Entity Framework Core, and SQL Server. It is inspired by common ERP workflows; it is not Microsoft Dynamics 365 Business Central and must not include proprietary Microsoft implementation code, assets, or data without verified permission.

## Source of truth

- GitHub issues define the scope, acceptance criteria, and completion state of individual units of work.
- Git branches and pull requests record implementation, review, and integration.
- The repository `docs/roadmap.md` tracks phase-level progress.
- This playbook records the agreed development and release process and a dated project change history.
- CI results, local test output, and environment verification are evidence; do not describe a check or deployment as successful unless it was actually run and observed.

## Issue, branch, and pull-request lifecycle

1. Create one GitHub issue per feature, fix, or bounded project task. Document scope, dependencies, risks, and testable acceptance criteria. Keep an issue open while any required verification remains incomplete.
2. Create an issue branch from the latest `dev` branch. Use `feature/<issue-number>-<short-description>` for features and `fix/<issue-number>-<short-description>` for fixes. For example: `feature/42-company-settings`. Keep the issue number in the branch name without a literal `#`, which is awkward in shell commands.
3. Implement only the issue's agreed scope. Keep changes reviewable, update related docs/roadmap, and do not commit secrets, local databases, proprietary artifacts, or unapproved source.
4. On the feature branch, run the relevant local checks before committing. At minimum for code changes, restore/build/test the solution. Run additional module, migration, security, or UI checks that the issue requires, and record actual results in the PR and issue.
5. Push the feature branch and open a PR into `dev`, linking the issue. After review and required CI checks pass, ask the user for explicit approval before merging. Never merge a PR without that approval. Do not push feature work directly to `main`.
6. Verify the merged `dev` branch locally against the agreed SQL Server development database using the issue's acceptance criteria. Keep the issue open if local verification or fixes are still outstanding.
7. When all issues and acceptance criteria for a phase are complete and the merged `dev` branch passes local verification, open a release PR from `dev` to `main`. Require successful CI and ask the user for explicit approval before merging. Do not promote an incomplete phase.
8. After the release PR is merged, verify the `main` branch locally against the agreed SQL Server database before marking phase issues complete and proceeding to the next phase. Record the PR and test evidence. Hosted deployment and production testing are not part of the current workflow.

Branch names must avoid spaces. Preserve issue numbers in branch names and PR titles/bodies so related work is traceable. Never force-push or rewrite shared history as a routine workflow.

## Verification gates

### Local, before feature PR

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore --configuration Release
dotnet test BusinessCentralBlazor.sln --no-build --configuration Release
```

Run the web app and perform the issue-specific manual/browser checks where applicable. Database-backed changes also require migration creation/application and integration tests against the local development database. Keep the connection string in the current PowerShell process and verify the target database name before applying migrations. Record unavailable tools/resources explicitly rather than substituting claims.

### CI, before integration

The repository workflow `.github/workflows/ci.yml` is configured to restore, Release-build, and test PRs targeting `dev` or `main`, and pushes to `main` or `dev`. Confirm the GitHub check actually completed successfully before relying on it. The workflow's presence alone does not establish that it has run.

### Local branch verification and release

Validate the merged `dev` build locally with SQL Server before phase promotion. After the `dev`-to-`main` release PR is merged, repeat the applicable checks against `main` locally. Do not infer a hosted deployment from a branch merge; deployment is outside the current project workflow.

## Phase progress

Update this table when phase status materially changes. Link issues, PRs, commits, CI results, and deployment evidence where available.

| Phase | Status | Exit gate |
|---|---|---|
| Phase 0 — Repository setup and schema discovery | Complete | Completed on `main`; local SQL Server migration, SQL smoke, build/tests, and app startup verified. |
| Phase 1 — Foundation framework | In progress | Complete issues #11–#14, verify on `dev`, then pass release PR/CI and local verification on `main`. Ask the user before merging every PR. |
| Phase 2 — Core financials | Not started | Financial workflows and transaction invariants pass unit, integration, and environment tests. |
| Phase 3 — Sales, purchasing, and inventory | Not started | Document lifecycles and stock/financial consistency are verified. |
| Phase 4 — Advanced capabilities and operations | Not started | Production, deployment, monitoring, recovery, and advanced capabilities meet release criteria. |

Detailed task status lives in [`roadmap.md`](roadmap.md).

## Dated project change history

Record each material update newest-first. Use the local project timezone (Asia/Kolkata, UTC+05:30); distinguish committed work from pending, merged, and deployed work.

### 2026-10-12

- **11:16 IST — Phase 1 implementation started:** issue #10 tracks four implementation issues (#11–#14). The user chose administrator-managed accounts without public self-registration and selected a one-time CLI bootstrap using credentials from environment variables. Authentication is being implemented on `feature/11-admin-auth`; the user explicitly requires approval before every PR merge.
- **10:56–10:57 IST — Schema alignment merged and locally verified on `dev`:** PR #6 merged to `dev` at `0a0ca99`. Applied `20261012051809_AlignJournalAndAccountingFoundation` to the existing Phase 0 database `BusinessCentralBlazor_Phase0` on `.\BCDEMO`; both migrations now show as applied. The updated rollback-only SQL smoke test passed all five checks. Release build passed (0 warnings/errors), tests passed (6/6), EF reported no pending model changes, and the local app returned HTTP 200 at `http://localhost:5211`. This updates only the named Phase 0 database; it is not a hosted deployment. The release PR and local verification on `main` remain.
- **10:31–10:35 IST — User local verification of the existing `dev` schema:** the user confirmed the initial migration is applied to `BusinessCentralBlazor_Phase0`, all five rollback-only SQL smoke-test checks passed, Release build and tests succeeded, and the web app home page loaded at `http://localhost:5211`. At that point, these results verified only the pre-alignment schema; the aligned schema was verified locally at 10:56–10:57 IST as recorded above.
- **10:39 IST — Selected Business Central metadata review:** the user confirmed permission to inspect the local source artifacts and selected a concept-alignment approach rather than a field-for-field copy. Read selected foundational table definitions from the Base Application source archive in memory; neither the archive nor repository source files were modified. Updated the application model with journal templates, account structure/category distinction, and period/line relationships. The alignment migration and its local verification are pending.
- **10:14–10:17 IST — Final local checks and GitHub rules:** Release build passed with 0 warnings/errors; tests passed (4/4); local Blazor endpoint returned HTTP 200; EF reported no pending model changes; all five solution projects reported no known vulnerable NuGet packages. The rollback-only SQL smoke test passed, and verification confirmed its tenant/company/currency/journal/ledger rows were not retained. Artifact examples are ignored and no proprietary files were added to Git.
- **GitHub settings:** the user confirmed repository visibility is intentionally public and chose PR + required CI protections without an approving-review count. Enforced PRs, strict required `build-and-test` checks, administrator enforcement, and disabled force-push/deletion on both `main` and `dev`. No deployment environment exists.
- **10:20 IST — Schema integration to `dev`:** PR for the independent EF Core schema was merged at `f305f0fd6a2b3f6bf6e191f43c0d6eb153ad5657`. PR CI and push CI on that exact `dev` commit passed. After fast-forwarding local `dev`, tool restore, restore, Release build (0 warnings/errors), tests (4/4), migration drift check, and the rollback-only SQL integrity test all passed again. This is local/CI verification of `dev`, not a deployment or test in a hosted dev environment.
- **Phase 0 still open:** the initial schema passed user-reported local checks, but the new alignment migration has not reached `dev` or been tested against the user's database. Hosted dev/production environments are not required for the agreed workflow. Keep issue #1 open until the aligned schema is verified on `dev`, then released through a PR and checked locally on `main`.
- **10:03–10:13 IST — Independent initial schema:** confirmed W1 manifest build `28.4.53241.0` pairs platform `28.0.53152.0`; no product source license or entitlement was verified, and Microsoft Online Product Terms do not establish rights for the on-premises artifacts. AL source contents remain uninspected. Created a separate feature branch for issue #1; added EF Core 10.0.12 SQL Server and ASP.NET Identity persistence, an initial company/accounting schema and migration, and a rollback-only SQL integrity test script.
- **Local database verification:** applied migration `20261012043920_InitialCompanyAndFinancialSchema` to newly created `BusinessCentralBlazor_Phase0` on local SQL Server Express; left existing databases untouched. The SQL smoke test persisted valid rows inside a transaction and confirmed rejection of invalid zero-sided journal lines, cross-company references, and duplicate ledger posting, then rolled all rows back. EF reported no pending model changes.
- **Dependency verification:** updated EF packages from 10.0.0 to 10.0.12 after NuGet identified a high-severity transitive `System.Security.Cryptography.Xml` 9.0.0 vulnerability; the current infrastructure dependency scan reports no known vulnerable packages.
- **Local-only workflow:** GitHub branches and CI are used for integration/release; SQL Server Express is the local development database. No hosted deployment environment is configured or required at this stage.
- **09:58–10:00 IST — Initial integration branch:** created `dev` from `main` and merged the Phase 0 foundation through its issue PR at merge commit `9ec1a4d4c6e294a45b42b00dcabfd4144dae21e5`. PR CI and the subsequent `dev` push CI both passed. On the merged local `dev` branch, restore succeeded, Release build succeeded with 0 warnings and 0 errors, and unit tests passed (1/1). This verifies the branch locally; it does not claim a deployment or test in a separate hosted development environment.
- **Workflow boundary:** `main` remains unchanged. Phase 0 remains in progress and its issue remains open; no production deployment or hosted-environment verification is expected.
- **09:53 IST — Workflow documentation:** the wiki-ready project playbook was added to the repository for publishing or copying into the GitHub Wiki. It does not itself create or publish a GitHub Wiki page.
- **09:30–09:53 IST — Phase 0 foundation:** inspected the clean repository and local tools; inventoried the locally available W1/platform archives without extracting them; scaffolded the .NET 10 modular solution, initial CI workflow, architecture/schema documentation, and PR template. Commit `c268788` (`Set up .NET ERP foundation`) was created on the phase-0 feature branch.
- **Local verification:** `dotnet restore BusinessCentralBlazor.sln` succeeded; Release build succeeded with 0 warnings and 0 errors; unit tests passed (1/1). The user also reported that the app was listening locally and shared a browser screenshot of the home page.
- **Boundaries at the initial commit:** no AL source was inspected at that time; no database or migration was created at that commit. The later discovery and schema integration results are recorded above; no hosted deployment is claimed.
- **Issue workflow:** the Phase 0 completion issue tracks the remaining work. Keep it open until its license/schema/database criteria and the required local, dev, and release checks are completed.

## Operational constraints and blockers

- The user authorized read-only inspection of selected local AL metadata. Keep the source archives, app packages, license files, and sample databases out of Git; do not copy implementation code or assets.
- `main` and `dev` require PRs and the strict `build-and-test` check; administrator enforcement is enabled, with force-push and branch deletion disabled. No approval count is required, per user choice.
- Hosted deployment and Azure resources are not part of the agreed current workflow.
- Do not move Phase 0 to `main` until the aligned schema passes local verification on `dev`, the release PR and CI pass, and local verification on `main` is complete.
