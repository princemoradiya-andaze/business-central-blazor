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
- [ ] Verify applicable on-premises artifact source-use terms and user entitlement before inspecting or extracting AL source.
- [ ] Catalogue selected source metadata with provenance and identify version/coverage gaps.
- [x] Design and document the initial independent application schema, relationships, posting invariants, and ER diagram.
- [x] Implement initial EF Core/Identity entities and SQL Server migration; apply to a new local development database.
- [x] Verify model mappings, SQL constraints/indexes, migration history, and dependency vulnerability status.
- [x] Run the SQL persistence/constraint smoke test; verify all test rows roll back.
- [x] Re-run final local restore/build/tests, EF migration drift check, SQL smoke test, dependency audit, app HTTP smoke test, and secret/artifact exclusion on this issue branch.
- [x] Merge the independently designed schema branch through a PR into `dev`; verify merged restore/build/tests, migration drift, and SQL smoke test locally and in CI.
- [ ] Verify Phase 0 in an available hosted dev environment.
- [ ] Promote the completed, metadata-reviewed Phase 0 release to `main` through an approved release PR and verify production/main before closing the issue.

## Phase 1 — Foundation framework

- [ ] Implement ASP.NET Core Identity authentication and authorization.
- [ ] Enforce tenant/company membership boundaries.
- [ ] Build the application shell and reusable page/CRUD framework.

## Later phases

- [ ] Core financials: chart of accounts, general ledger, dimensions, journals, bank, and tax.
- [ ] Sales, purchasing, inventory, and document lifecycles.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

Verify applicable on-premises license terms and entitlement for the locally available Business Central 28.4 artifacts before inspecting AL source. The independent schema and initial SQL Server migration are implemented and verified locally/in CI on `dev`; next unblock permitted metadata discovery and identify/provision hosted dev and production release environments. Keep the Phase 0 issue open and do not promote it to `main` until these gates are verified.
