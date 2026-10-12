# Roadmap

Only mark tasks complete when their acceptance criteria have been verified.

## Phase 0 — Repository setup and schema discovery

- [x] Inspect the clean repository, GitHub default branch, current branch, and available development tools.
- [x] Locate and inventory the requested W1/platform artifacts without modifying them.
- [x] Scaffold the .NET 10 modular solution, CI workflow, and architecture/data-model documentation.
- [x] Verify solution restore/build and the initial architecture test.
- [x] Document the issue/branch/PR lifecycle and dated project change history in `docs/project-playbook.md`.
- [ ] Verify applicable artifact licensing before inspecting or extracting AL source.
- [ ] Catalogue selected source metadata with provenance and identify version/coverage gaps.
- [ ] Review and finalize the application-owned schema, posting invariants, and ER diagram.
- [ ] Implement initial EF Core entities and migrations where appropriate; create the database using a supported development environment.
- [ ] Verify restore, build, tests, migration/database creation, and secret/artifact exclusion.
- [ ] Establish the `dev` integration branch and land the tested foundation through a reviewed PR; enforce required CI/review protections when repository plan and permissions permit.

## Phase 1 — Foundation framework

- [ ] Implement ASP.NET Core Identity authentication and authorization.
- [ ] Enforce tenant/company membership boundaries.
- [ ] Build the application shell and reusable page/CRUD framework.

## Later phases

- [ ] Core financials: chart of accounts, general ledger, dimensions, journals, bank, and tax.
- [ ] Sales, purchasing, inventory, and document lifecycles.
- [ ] Advanced capabilities, production operations, and Azure deployment.

## Current next step

Verify the applicable license terms for the locally available Business Central 28.4 artifacts. Then complete the narrow source metadata catalogue and finalize the independently designed initial schema before creating EF entities or migrations. Track all remaining Phase 0 acceptance criteria in the Phase 0 GitHub issue and complete dev/release verification before closing it.
