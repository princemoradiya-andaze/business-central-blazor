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

- [x] Phase tracker: [#10](https://github.com/princemoradiya-andaze/business-central-blazor/issues/10) — all four scoped issues complete; Phase 1 released and verified on `main` in PR #27, with completion recorded in PR #28.
- [x] [#11](https://github.com/princemoradiya-andaze/business-central-blazor/issues/11) — ASP.NET Core Identity sign-in/out and administrator-managed accounts; no public self-registration (merged in PRs #16 and #17; local sign-in verified).
- [x] [#13](https://github.com/princemoradiya-andaze/business-central-blazor/issues/13) — tenant/company memberships and validated active-company context (merged in PR #18; local verification passed).
- [x] [#14](https://github.com/princemoradiya-andaze/business-central-blazor/issues/14) — authorization policies and cross-tenant/company data isolation (merged in PR #19; CI and local Release build/22 tests passed).
- [x] [#12](https://github.com/princemoradiya-andaze/business-central-blazor/issues/12) — authenticated application shell, navigation, and reusable CRUD foundation (merged in PR #20; local verification passed).

**Merge gate:** Ask the user for explicit approval before merging every PR, even when all CI checks pass.

## Phase 2 — Core financials

- [ ] Phase tracker: [#29](https://github.com/princemoradiya-andaze/business-central-blazor/issues/29) — deliver company-scoped chart of accounts, dimensions, journals/general ledger, bank, and tax.
- [x] [#30](https://github.com/princemoradiya-andaze/business-central-blazor/issues/30) — Chart of Accounts setup and management (merged into `dev` in PR #31; local merged-`dev` Release build passed with 0 warnings/errors, all 38 tests passed, EF reported no model changes, and the local app smoke checks passed).
- [ ] [#33](https://github.com/princemoradiya-andaze/business-central-blazor/issues/33) — manage company-scoped dimensions and values; implementation in progress.

## Later phases

- [ ] Sales, purchasing, inventory, and document lifecycles.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

Phase 0 and Phase 1 are complete on `main`. Phase 1 was released in PR #27 and documented in PR #28; the merged `main` commit passed a Release build (0 warnings/errors), all 32 tests, EF model-drift validation, and local HTTP smoke checks. Phase 2 is tracked in #29. Chart of Accounts (#30) is implemented and verified on `dev` in PR #31. Dimension setup (#33) is now in progress, reusing the existing dimension schema; no migration is expected. Journal assignments, default dimensions, and dimension sets remain later work. Ask the user before merging every PR. Hosted deployment is not part of the agreed workflow.
