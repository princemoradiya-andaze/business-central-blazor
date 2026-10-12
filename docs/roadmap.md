# Roadmap

Only mark tasks complete when their acceptance criteria have been verified.

## Phase 0 — Repository setup and schema discovery

- [x] Inspect the clean repository, GitHub default branch, current branch, and available development tools.
- [x] Locate and inventory the requested W1/platform artifacts without modifying them.
- [x] Scaffold the .NET 10 modular solution, CI workflow, and architecture/data-model documentation.
- [x] Verify solution restore/build and the initial architecture test.
- [x] Document the issue/branch/PR lifecycle and dated project change history in `docs/project-playbook.md`.
- [x] Create `dev` from `main`; merge the initial foundation through a PR and verify local dev-branch build/tests and GitHub CI.
- [x] Require PRs and passing strict `build-and-test` checks on `main` and `dev`; disable administrator bypass, force-push, and branch deletion.
- [x] Obtain the user's explicit permission to inspect selected local AL metadata; review only relevant definitions read-only and leave all source archives unchanged.
- [x] Catalogue selected foundational metadata with artifact/version provenance and record scope and coverage gaps.
- [x] Design and document the application-owned schema, including selected Business Central-aligned concepts, relationships, posting invariants, and ER diagram.
- [x] Implement initial EF Core/Identity entities and SQL Server migration; apply to a new local development database.
- [x] Verify model mappings, SQL constraints/indexes, migration history, and dependency vulnerability status.
- [x] Run the SQL persistence/constraint smoke test; verify all test rows roll back.
- [x] Run restore/build/tests, EF migration drift, SQL smoke, dependency, app HTTP, and secret/artifact checks on the initial schema branch.
- [x] Merge the independently designed schema branch through a PR into `dev`; verify merged restore/build/tests, migration drift, and SQL smoke test locally and in CI.
- [x] Merge the schema-alignment update through a PR into `dev`, then repeat migration, SQL smoke-test, build/test, and app-startup verification locally on the merged `dev` code.
- [x] After local `dev` verification, promote Phase 0 to `main` through a release PR; verify CI and repeat the applicable local checks on `main`.

## Phase 1 — Foundation framework

- [ ] Phase tracker: [#10](https://github.com/princemoradiya-andaze/business-central-blazor/issues/10) — complete all four scoped issues below, then verify on `dev` and release to `main`.
- [ ] [#11](https://github.com/princemoradiya-andaze/business-central-blazor/issues/11) — ASP.NET Core Identity sign-in/out and administrator-managed accounts; no public self-registration.
- [x] [#13](https://github.com/princemoradiya-andaze/business-central-blazor/issues/13) — tenant/company memberships and validated active-company context (merged in PR #18; local verification passed).
- [ ] [#14](https://github.com/princemoradiya-andaze/business-central-blazor/issues/14) — authorization policies and cross-tenant/company data isolation (in progress).
- [ ] [#12](https://github.com/princemoradiya-andaze/business-central-blazor/issues/12) — authenticated application shell, navigation, and reusable CRUD foundation.

**Merge gate:** Ask the user for explicit approval before merging every PR, even when all CI checks pass.

## Later phases

- [ ] Core financials: chart of accounts, general ledger, dimensions, journals, bank, and tax.
- [ ] Sales, purchasing, inventory, and document lifecycles.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

Phase 0 is complete on `main`. Phase 1 is tracked by issue #10 and four implementation issues (#11–#14). Administrator-managed authentication and CLI provisioning are merged into `dev` through PRs #16 and #17; the user verified local administrator sign-in. Issue #13 (company membership/context) is merged in PR #18 and locally verified. Issue #14 (authorization/isolation) is in progress; issue #12 (app shell/CRUD) follows it. Ask the user before merging every PR. Hosted deployment is not part of the agreed workflow.
