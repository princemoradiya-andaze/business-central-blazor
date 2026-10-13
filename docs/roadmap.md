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
- [x] [#33](https://github.com/princemoradiya-andaze/business-central-blazor/issues/33) — company-scoped dimension and value management (merged into `dev` in PR #34; local merged-`dev` Release build passed with 0 warnings/errors, all 44 tests passed, EF reported no model changes, expected migration state and HTTP smoke checks verified).
- [x] [#36](https://github.com/princemoradiya-andaze/business-central-blazor/issues/36) — general journal batches and lines, accounting-period setup, transactional posting, dimensions, and full-batch reversals (merged into `dev` in PR #37; user-end-to-end-tested on the feature branch; Release tests 51/51, EF model-drift check, migration, and rollback-only SQL smoke test passed).
- [x] [#38](https://github.com/princemoradiya-andaze/business-central-blazor/issues/38) — company-scoped posted-entry search, account activity/opening and closing balances, and an as-of trial balance (merged into `dev` in PR #41; Release build passed with 0 warnings/errors, all 55 tests passed, and EF model-drift check passed; no migration required).
- [ ] [#39](https://github.com/princemoradiya-andaze/business-central-blazor/issues/39) — company-scoped tax codes, effective-dated rates, liability/recoverable G/L mappings, and explicitly calculated manual journal tax lines. Automatic sales/purchase document tax calculation is deferred to Phase 3. Implemented and automatically verified in [PR #42](https://github.com/princemoradiya-andaze/business-central-blazor/pull/42), now merged into `dev`; user local end-to-end verification after Phase 2 integration remains.
- [ ] [#40](https://github.com/princemoradiya-andaze/business-central-blazor/issues/40) — company-scoped bank account setup, journal-linked receipts/payments, manual statement entry/tab-separated import, one-to-one matching to posted bank G/L activity, and visible unmatched items/differences. Implemented in [PR #43](https://github.com/princemoradiya-andaze/business-central-blazor/pull/43), targeting `dev` and based on `dev` including PRs #41/#42. Release build passed with 0 warnings/errors, all 78 tests passed, EF model-drift check passed, and generated migration SQL was inspected. The bank migration is not applied; PR integration and full local SQL/browser end-to-end verification remain. Bank feeds, payment initiation, credentials, automatic reconciliation, check printing and FX are excluded.

## Later phases

- [ ] Phase 3 tracker: [#53](https://github.com/princemoradiya-andaze/business-central-blazor/issues/53) — sales, receivables, inventory, purchasing, and document lifecycles.
- [x] [#54](https://github.com/princemoradiya-andaze/business-central-blazor/issues/54) — company-scoped inventory items and weighted-average costing (merged to `dev` in PR #55; migration applied to the approved local database; 86 Release tests and EF model-drift check passed; user completed local workflow verification).
- [x] [#56](https://github.com/princemoradiya-andaze/business-central-blazor/issues/56) — sales orders, shipment, invoicing, and open receivables (merged to `dev` in PR #57; user completed local Phase 3 sales verification).
- [ ] [#59](https://github.com/princemoradiya-andaze/business-central-blazor/issues/59) — purchasing orders, full receipts, vendor invoices, tax, GRNI, and open payables. Implementation and migration are on `feature/59-purchasing`; Release build (0 warnings/errors), all 96 Release tests, EF model-drift check, and migration SQL review passed. After explicit approval, migration `20261013063111_AddPurchasingLifecycle` was applied to `BusinessCentralBlazor_Phase0` on `.\BCDEMO` and verified in migration history. Local SQL Server/browser workflow verification remains.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

Phase 0–2 have been completed and released. Phase 3 tracker #53 now has inventory (#54) and sales (#56) slices merged to `dev` and locally verified. Purchasing (#59) is implemented and its migration is applied to the explicitly approved `BusinessCentralBlazor_Phase0` database on `.\BCDEMO`; finish local SQL Server/browser workflow verification, update the issue, and open a PR to `dev`. Ask the user before merging any PR. Hosted deployment is not part of the agreed workflow.
