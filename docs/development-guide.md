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

For isolated local UI testing only, `setup-local-demo` creates one `Local Demo Tenant`, one active `LOCAL-DEMO` company, and tenant/company Administrator memberships for an existing administrator account. It is restricted to the `BusinessCentralBlazor_Phase0` database, requires that no tenants or companies exist, and prompts for explicit confirmation. It does not create users, company settings, or sample notes. Set `BC_DEMO_USER_EMAIL` to the administrator's email before running it; clear the variable afterward.

## Company membership and active-company context

Authenticated users can choose only active companies for which both a tenant membership and a company membership exist. The selected company ID is stored as a claim in the protected authentication cookie, but that claim is only a selection hint: `IActiveCompanyContext.ResolveAsync` rechecks the current membership rows and company-active status against SQL Server whenever called. Missing, removed, or inactive memberships therefore resolve to no active company. Users without an eligible company see a safe no-access state; they must be assigned a membership before accessing company-owned features. Any future business operation must resolve and enforce this context server-side rather than trusting a company ID supplied by the browser. The company-context change does not alter the EF model or require a migration.

After successful sign-in, the app reloads eligible memberships from the database. It selects the company automatically only when exactly one active company is available; users with multiple eligible companies are sent to the company selector, while users with none receive the no-access flow. A requested local return URL is preserved through company selection.

## Company authorization and data isolation

The Blazor component endpoint and company-owned pages use the `ActiveCompany` authorization policy. It requires a signed-in user whose selected company still has an active company membership and tenant membership. The Razor Page at `/account/company` remains available to signed-in users without an active selection so they can choose an eligible company.

This route policy is not a substitute for per-operation checks. Every company-owned API or service operation must resolve scope through `ICompanyScopeAuthorizer`: call `ResolveAuthorizedCompanyAsync` with the requested tenant/company IDs when a request identifies its target, or `ResolveActiveCompanyAsync` when the operation is scoped to the current selection. Use only the returned validated IDs in database filters and writes. These methods return no scope for anonymous users, absent/stale selections, revoked memberships, inactive companies, or a tenant/company pair other than the validated active selection. Never query or mutate data using only an ID supplied by the client. The authorization boundary is application-level; database row-level security is not implemented.

Blazor Server circuits can run component operations concurrently. Web services therefore create a separate `ErpDbContext` from `IDbContextFactory<ErpDbContext>` for each database operation; do not retain a context in a circuit-scoped service or share one across awaited operations.

## Shared shell and workspace notes

The authenticated Blazor shell shows the active company and links to a SQL-backed Workspace notes example. Notes are stored in the `WorkspaceNotes` table, and the service derives the tenant/company scope from the authenticated principal for every list, detail, create, edit, or delete operation. Company and tenant IDs are never accepted from the note form. A note ID from another company is treated as not found. The example is generic foundation UI, not an ERP module; no sample notes are seeded automatically.

The `AddWorkspaceNotes` EF migration has been applied to the documented local development database, `BusinessCentralBlazor_Phase0` on `.\BCDEMO`. For another SQL Server database, check the target database name before running `dotnet ef database update`. Users also need an active company membership before the Blazor workspace is available.

## General-ledger inquiry and trial balance

The authenticated financial menu links to company-scoped posted entries at `/financial/general-ledger/entries`, account activity at `/financial/general-ledger/accounts/{accountId}/activity`, and the trial balance at `/financial/general-ledger/trial-balance`. Entry search supports inclusive posting-date bounds, account selection, and a document-number substring. Account activity reports opening debit/credit balances before the start date, debit/credit totals within the selected range, and the resulting closing balance. The trial balance includes posting accounts and their net debit or credit balances through the selected date, inclusive, with reconciling totals.

These reports read immutable `GeneralLedgerEntries` only. Account and active-company scope is resolved server-side for every operation; posted entries cannot be edited or deleted through inquiry screens. This feature reuses the existing schema and requires no migration.

## Company base currency

`/financial/company-settings` configures the active company's base currency and can add entries to
the shared currency catalogue (visible to all companies). The base currency is required before
bank-account setup; changing it is blocked after the company has journal lines, bank accounts,
posted ledger activity, or currency rates.

## Bank accounts, receipts/payments, and manual reconciliation

`/financial/banks` provides company-scoped bank/cash setup; each bank uses one dedicated, unblocked,
directly-postable Asset G/L account. Configure `CompanySettings.BaseCurrencyCode` and an active matching
`Currency` first. Bank currency is fixed to that base currency (no FX); identifiers are ordinary account
references, never online-banking credentials. Bank codes and G/L mappings are company-unique. Names,
identifiers and blocked status can be edited; G/L remapping is rejected once journals, statements or
posted activity exist.

The bank detail page creates a balanced draft CashReceipts or Payments journal atomically with its
bank link. A positive receipt debits the bank and credits the selected offset; a positive payment does
the reverse. Document numbers are unique per bank. Follow the journal link to review and post through
the existing journal service, which rechecks bank status, base currency, bank G/L eligibility, document
trace and signed bank line, as well as the existing journal posting invariants. Posted activity and
full-batch reversals appear in reconciliation; no separate cash ledger or automatic adjusting entry is
created. Drafts can be edited in General journals, but must retain their linked document number and one
correctly signed bank line.

Create a non-overlapping statement period with opening and closing balances, then manually enter
lines or paste **tab-separated** text without a header: `yyyy-MM-dd`, unique line reference, description,
signed amount. Use an invariant decimal point, positive receipts and negative payments, no thousands
separators, maximum four decimals, and no embedded tabs/newlines in fields. Imports are all-or-nothing
and limited to 1000 lines / 400000 characters; duplicate line references are rejected, not silently
skipped. Header corrections, removal of unmatched lines, and unmatching are allowed before completion.

Manually select an exact signed-amount posted bank G/L entry through the statement end date. Earlier
uncleared entries are eligible; future entries, other banks/companies, drafts, amount/sign mismatches,
and already-matched entries are rejected. Matching is one-to-one, not automatic or many-to-one.
The screen shows unmatched statement lines, outstanding posted activity, statement arithmetic
(`closing - opening - lines`), and statement-versus-G/L closing difference. Complete only when every
statement line is matched and statement arithmetic is zero. Outstanding G/L timing differences may
remain visible; they do not block completion or trigger adjustment posting. Completed statements and
matches are locked. Server-side company checks, serializable SQL transactions, unique indexes and
revision concurrency tokens protect writes; on a duplicate/stale edit reload before retrying. Banking
changes are audited, including original/new entry IDs on match/unmatch.

Migration `20261012115411_AddCompanyBankingAndReconciliation` adds four banking tables; **it has not
been applied to the local database**. Full local SQL Server/browser end-to-end testing is deferred, at
the user's request, until all remaining Phase 2 PRs are merged into `dev`. After integration and approved
migration application, verify bank setup in two companies, receipt/payment posting and ledger traces,
header/line corrections, tab import validation/duplicate rejection, matching/unmatching, outstanding
items and differences, reversal activity, completion locking, and simultaneous attempts to match the
same entry from two sessions. Automated tests use an isolated in-memory store and SQL Server model/
generated migration metadata; they do not establish live SQL concurrency or migration application.
Bank feeds, credential storage, payment initiation, automatic reconciliation, check printing and FX
remain out of scope.

## Inventory items and stock movements

`/financial/inventory` configures company-scoped inventory items with an inventory Asset account,
an opening/increase offset account, and an inventory issue Expense account. Each receipt or issue
creates a linked general-journal draft; review and post it through General journals to update the
item balance and G/L in the same transaction. Receipts recalculate weighted-average cost, issues use
the current average and cannot create negative stock, and generic journal reversal is blocked for
inventory-linked batches. Correct posted stock movements with a compensating inventory movement.
The initial slice uses one company-wide balance per item and does not include warehouses or bins.

Migration `20261013035640_AddInventoryFoundation` has been applied to the local
`BusinessCentralBlazor_Phase0` database on `.\BCDEMO`; migration history, the `InventoryMovements`
table, rowversion column, checks, and company-scoped foreign keys were verified. Automated tests
cover inventory costing/posting and the authorized page; full browser-based SQL Server workflow and
concurrency verification remain outstanding.

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
