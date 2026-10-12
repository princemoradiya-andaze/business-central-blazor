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
- [ ] Merge the schema-alignment update through a PR into `dev`, then repeat migration, SQL smoke-test, build/test, and app-startup verification locally on the merged `dev` code.
- [ ] After local `dev` verification, promote Phase 0 to `main` through a release PR; verify CI and repeat the applicable local checks on `main` before closing the issue.

## Phase 1 — Foundation framework

- [ ] Implement ASP.NET Core Identity authentication and authorization.
- [ ] Enforce tenant/company membership boundaries.
- [ ] Build the application shell and reusable page/CRUD framework.

## Later phases

- [ ] Core financials: chart of accounts, general ledger, dimensions, journals, bank, and tax.
- [ ] Sales, purchasing, inventory, and document lifecycles.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

The user authorized read-only inspection of selected metadata from the local Business Central 28.4 source archive, and the schema was revised to align key journal, account, and accounting-period concepts without copying Microsoft implementation code. The new alignment migration still needs to reach `dev` and be verified locally against `BusinessCentralBlazor_Phase0`; hosted deployment is not part of the agreed workflow. Keep Phase 0 open until that local verification and the later `main` PR/CI/local check are complete.
