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
2. Create an issue branch from the latest `dev` branch. Use `feature/#<issue-number>-<short-description>` for features and `fix/#<issue-number>-<short-description>` for fixes. For example: `feature/#42-company-settings`.
3. Implement only the issue's agreed scope. Keep changes reviewable, update related docs/roadmap, and do not commit secrets, local databases, proprietary artifacts, or unapproved source.
4. On the feature branch, run the relevant local checks before committing. At minimum for code changes, restore/build/test the solution. Run additional module, migration, security, or UI checks that the issue requires, and record actual results in the PR and issue.
5. Push the feature branch and open a PR into `dev`, linking the issue. Merge only after review and required CI checks pass. Do not push feature work directly to `main`.
6. Verify the merged result in the development environment using the issue's acceptance criteria. Keep the issue open if environment verification or fixes are still outstanding.
7. When all issues and acceptance criteria for a phase are complete and the phase is verified in development, open a release PR from `dev` to `main`. Require successful CI and applicable human/release approval. Do not promote an incomplete phase.
8. Verify the promoted release in production (or the agreed main-release environment) before marking the phase issues complete and proceeding to the next phase. Record the version/tag, PR, test evidence, and deployment result.

Branch names must avoid spaces. Preserve issue numbers in branch names and PR titles/bodies so related work is traceable. Never force-push or rewrite shared history as a routine workflow.

## Verification gates

### Local, before feature PR

```powershell
dotnet restore BusinessCentralBlazor.sln
dotnet build BusinessCentralBlazor.sln --no-restore --configuration Release
dotnet test BusinessCentralBlazor.sln --no-build --configuration Release
```

Run the web app and perform the issue-specific manual/browser checks where applicable. Database-backed changes also require migration creation/application and integration tests against the supported development database. Record unavailable tools/resources explicitly rather than substituting claims.

### CI, before integration

The repository workflow `.github/workflows/ci.yml` is configured to restore, Release-build, and test PRs targeting `dev` or `main`, and pushes to `main` or `dev`. Confirm the GitHub check actually completed successfully before relying on it. The workflow's presence alone does not establish that it has run.

### Development and release

Validate the merged `dev` build in the development environment before phase promotion. Deploy `main` to production only through the approved release process; verify production after deployment. Use separate environments, least-privilege credentials, and GitHub secret/environment controls when deployment is configured.

## Phase progress

Update this table when phase status materially changes. Link issues, PRs, commits, CI results, and deployment evidence where available.

| Phase | Status | Exit gate |
|---|---|---|
| Phase 0 — Repository setup and schema discovery | In progress | License-safe metadata discovery, reviewed application schema, database migrations/creation as appropriate, successful local and CI checks, then dev and release verification. |
| Phase 1 — Foundation framework | Not started | Identity, company boundaries, application shell, and reusable page/CRUD foundation meet their issue acceptance criteria. |
| Phase 2 — Core financials | Not started | Financial workflows and transaction invariants pass unit, integration, and environment tests. |
| Phase 3 — Sales, purchasing, and inventory | Not started | Document lifecycles and stock/financial consistency are verified. |
| Phase 4 — Advanced capabilities and operations | Not started | Production, deployment, monitoring, recovery, and advanced capabilities meet release criteria. |

Detailed task status lives in [`roadmap.md`](roadmap.md).

## Dated project change history

Record each material update newest-first. Use the local project timezone (Asia/Kolkata, UTC+05:30); distinguish committed work from pending, merged, and deployed work.

### 2026-10-12

- **09:58–10:00 IST — Initial integration branch:** created `dev` from `main` and merged the Phase 0 foundation through its issue PR at merge commit `9ec1a4d4c6e294a45b42b00dcabfd4144dae21e5`. PR CI and the subsequent `dev` push CI both passed. On the merged local `dev` branch, restore succeeded, Release build succeeded with 0 warnings and 0 errors, and unit tests passed (1/1). This verifies the branch locally; it does not claim a deployment or test in a separate hosted development environment.
- **Workflow boundary:** `main` remains unchanged. Phase 0 remains in progress and its issue remains open; no production verification, migration, or deployment has occurred.
- **09:53 IST — Workflow documentation:** the wiki-ready project playbook was added to the repository for publishing or copying into the GitHub Wiki. It does not itself create or publish a GitHub Wiki page.
- **09:30–09:53 IST — Phase 0 foundation:** inspected the clean repository and local tools; inventoried the locally available W1/platform archives without extracting them; scaffolded the .NET 10 modular solution, initial CI workflow, architecture/schema documentation, and PR template. Commit `c268788` (`Set up .NET ERP foundation`) was created on the phase-0 feature branch.
- **Local verification:** `dotnet restore BusinessCentralBlazor.sln` succeeded; Release build succeeded with 0 warnings and 0 errors; unit tests passed (1/1). The user also reported that the app was listening locally and shared a browser screenshot of the home page.
- **Boundaries at the initial commit:** no AL source was inspected; no database or migration was created. The CI, merge, and dev branch verification results are recorded above; no development-environment deployment, production deployment, or main merge is claimed.
- **Issue workflow:** the Phase 0 completion issue tracks the remaining work. Keep it open until its license/schema/database criteria and the required local, dev, and release checks are completed.

## Operational constraints and blockers

- Verify applicable Business Central license terms and entitlement before inspecting/extracting AL source. Keep proprietary source, app packages, license files, and sample databases out of Git.
- GitHub reported that repository branch-protection/ruleset reads are restricted by the current plan for this private repository. Protection status is not verified; retain human review and CI as workflow requirements, and do not make the repository public to bypass the limitation.
- Azure CLI/resources have not been verified, and no deployment pipeline is configured yet.
- Do not move Phase 0 to `main` until Phase 0 acceptance criteria pass and the development/release verification gates are met.
