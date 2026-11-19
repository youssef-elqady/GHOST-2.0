# GHOST STATE

Last updated: 2026-11-18
Current phase: AUTH-02 — isolated SQL Server integration-test harness

> Delivery plan and phase exit criteria: [DELIVERY-ROADMAP.md](./DELIVERY-ROADMAP.md). Keep this file as the live execution log; update it after every meaningful milestone.

## Product and architecture decisions
- Primary UI: **Next.js** for both Staff and Admin.
- v0 is the UI design/build workflow owned by the user. Preserve the existing v0 visual direction; integrate screens into the existing `frontend/` app rather than creating a second production frontend.
- Design references:
  - Staff: https://github.com/youssef-elqady/ghost-staff
  - Admin: https://github.com/youssef-elqady/Admin-Dashboard
- Main repository: https://github.com/youssef-elqady/GHOST-2.0
- Backend remains .NET 10 Web API + SQL Server + EF Core. Financial calculations, permissions, stock, and session state remain server-authoritative.
- The existing `feature/frontend-v0-foundation` work is a reference/foundation to inspect and preserve; do not overwrite it blindly.
- No WPF client is planned for v1.

## Done
- SRS + ERD and business decisions BD-01 to BD-14 are documented.
- Skill and master prompt are committed.
- Existing GHOST-2.0 backend and Next.js frontend have been identified and reviewed at a high level.
- User synchronized the local `feature/products-inventory-billing` branch to origin at commit `98b83bb` on 2026-11-12; the reported status was clean and tracking origin.

## In progress
- Sprint 01: compare current backend/frontend code and inventory branch against SRS/ERD; produce KEEP / MODIFY / ADD / DEFER decisions.
- Reconcile documentation with the actual Next.js Staff + Admin direction.
- Review the separate inventory foundation branch before any merge.

## Next steps
1. Finish the code-to-SRS diff, including auth, cash ledger, sessions, inventory, migrations, concurrency, and tests.
2. Confirm the exact v0 design inputs from the Staff and Admin reference repositories and preserve their visual language.
3. Define the API contracts needed by each frontend workflow; replace mock data gradually with backend-owned data.
4. Complete Sprint 01 review and get approval before structural changes.
5. Sprint 02: authentication, roles, business day, shifts, handover, cash transactions, and staff data scoping.
6. Continue with sessions/devices, then inventory/POS, reporting, audit/backup, and production readiness.

## Non-negotiable invariants
- Backend is authoritative for money, time-based billing, permissions, stock balance, and session state.
- No money calculations or authorization decisions in the browser.
- Financial and stock operations must be transactional, idempotent where appropriate, auditable, and concurrency-safe.
- No destructive Git operations or branch merges without review.
- Preserve Arabic RTL, GHOST branding, and the user's v0 design direction.

## Decisions still open
- Discount rounding policy.
- Withdrawal alert threshold.
- Rejected stock receipt handling choice (cash reversal vs staff shortage), per approved auditable flow.
- Printer model/driver details and camera count.
- Remaining implementation choices discovered during Sprint 01.

## Sprint 01 review findings (2026-11-12)

### Confirmed aligned with SRS/ERD
- One open business day is enforced by a filtered unique index on BusinessDays.IsClosed = 0.
- One globally open shift is enforced by a filtered unique index on Shifts.Status = Open (enum value 1), matching the single cash drawer / one-open-shift rule. The domain-level check alone is per staff member, so the database index is the actual global guarantee.
- At most one unended SessionSegment per Device and per Session is enforced by filtered unique indexes. This also prevents two simultaneous active segments from occupying the same device, including paused sessions because their segment remains unended.
- Device and Session have SQL Server RowVersion concurrency tokens.
- Session billing stores the applied hourly rate per segment and computes total amount from all segments using one calculation timestamp; this supports preserving historical rates across device transfers.

### Gaps / required changes before production
- Authentication and authorization are not configured in Program.cs; StaffUserId and ShiftId are currently accepted from request payloads. Derive the acting user from authenticated claims and enforce role/resource ownership server-side.
- Completing a session currently updates session/device state and returns an amount, but does not create a Sale/Payment/CashTransaction or an idempotent financial record. Do not treat the amount response as collected revenue.
- Shift.ExpectedCash is currently mutable through UpdateExpectedCash; the target design must derive it from opening cash plus immutable CashTransactions (including handover, sales, expenses, withdrawals and reversals) rather than accepting arbitrary updates.
- SRS idempotency requirements for session completion/cancellation and payments are not represented in the current Session model/API. Add idempotency keys and transactional persistence before real cash handling.
- Current SessionsController exposes start/pause/resume/transfer/complete, but no cancel/void workflow. Implement the 300-second staff cancellation window and Admin-only post-window void according to BD-03.
- SessionStatus currently has Active/Completed/Cancelled; pause is represented by an active SessionPause rather than a SessionStatus.Paused value. This is viable only if all queries, constraints, API snapshots and reports consistently treat Active + active pause as Paused; document and test that mapping.
- Session completion currently accepts an EndShiftId from the request and checks only that the shift exists and is open. It must also validate the authenticated actor, business-day/shift rules, and any permitted cross-shift handover policy.
- Validate that the EF Core migrations and model snapshot actually contain every filtered unique index and RowVersion configuration; do not rely on fluent configuration alone without a generated/applied migration and SQL Server integration tests.

### Review status
- Devices, shifts, business days, session lifecycle and relevant EF configurations: reviewed at source level.
- Cash ledger, authentication, inventory/billing branch reconciliation, migrations/model snapshot, and full test suite: still pending.
- No production readiness claim until SQL Server integration tests cover concurrent session starts/transfers, duplicate completion/payment requests, cash reconciliation, and restart recovery.

## Sprint 01 persistence, tests, and inventory branch review (2026-11-12)

### Migration and model snapshot verification
- The active branch contains one migration only: `20261007095934_InitialPersistence`, plus its designer and `GHOSTDbContextModelSnapshot`.
- The initial migration and snapshot both include the filtered unique indexes for one open BusinessDay, one globally open Shift, one unended SessionSegment per Session and Device, and one active SessionPause per segment.
- The migration creates SQL Server rowversion columns for BusinessDays, Devices, Shifts, and Sessions; the snapshot marks the corresponding properties as concurrency tokens.
- The migration and snapshot currently describe only the foundation schema (Devices, DeviceRates, BusinessDays, Shifts, Sessions, SessionSegments, SessionPauses). They do not create Products or StockMovements.

### Automated test coverage review
- Existing tests are predominantly domain/application unit tests using fake repositories and fake UnitOfWork implementations.
- `GHOST.Tests.csproj` references the Application project only; it does not reference Infrastructure and contains no SQL Server/EF Core integration-test setup.
- No test currently proves that the SQL Server filtered indexes are actually applied, that competing database transactions cannot start/transfer the same device, or that the UnitOfWork translates real SQL Server concurrency/unique-key errors as intended.
- Billing calculator tests cover proportional partial-hour charging, captured rates across segments, sum-before-rounding, zero segments, and invalid inputs. These are useful unit tests but do not validate persisted end-to-end billing/payment.
- The current CreateDevice fake repository implementation includes the required GetByIdAsync method; any earlier report of that method missing must be rechecked against the exact local commit and test output rather than repeated as a current confirmed defect.

### Inventory branch reconciliation
- `feature/inventory-billing-foundation` is not a fast-forward continuation of `feature/products-inventory-billing`. GitHub compare reports the branches diverged: inventory branch is 10 commits ahead and 6 behind, with merge base `808dbb42948f25ef71bc407002dce2c54f11a3a8`.
- The inventory branch adds Product and StockMovement entities/configurations, InventoryService, IInventoryService, ProductsController, and DbSet registrations.
- Its Products and StockMovements model configurations include useful check constraints, SKU uniqueness, decimal precision, and Product RowVersion. ReceiveStockAsync uses a database transaction to persist the Product balance update and matching StockMovement together.
- Critical blocker: the inventory branch still shares the exact same initial migration and model snapshot as the foundation branch; no migration adds Products or StockMovements. The model has therefore moved ahead of the database schema.
- Inventory receipt currently records stock and cost but does not create a StockReceipt workflow or a corresponding cash purchase/CashTransaction ledger entry. Do not treat stock receipt as a fully reconciled cash purchase.
- Product selling-price edits update SellingPrice directly; no ProductPriceHistory entity/table was found in this review.
- Inventory controller currently has no authentication/role enforcement. InventoryService writes through DbContext directly rather than the shared IUnitOfWork abstraction; the receive operation is transactional, but conflict/error handling and idempotency need a unified policy.
- Do not merge the inventory branch until its commits are reconciled with the active branch, its model snapshot and migrations are regenerated from the combined model, and its business gaps are resolved.

### Updated review status
- Foundation EF configuration, initial migration, model snapshot, UnitOfWork, and current unit-test structure: reviewed at source level.
- SQL Server migration application, schema-vs-model validation, real concurrency tests, auth, cash ledger/payment integration, and full test execution: still pending.
- The inventory branch remains a review-only source branch; no merge approved.

## Sprint 01 business-rule mismatch found (2026-11-12)

### Critical: session billing rounding contradicts approved BD-04
- SRS BD-04 requires `GamingAmount = Ceiling(RawCost)`: round the total gaming charge upward to the next whole EGP exactly once after summing all session segments.
- `SessionBillingCalculator.CalculateTotalAmount` currently rounds to 2 decimal places with `MidpointRounding.AwayFromZero`, not to a whole EGP.
- `SessionBillingCalculatorTests.RoundsOnlyAfterSummingAllSegments` explicitly expects `0.01m` for two one-second segments at 18 EGP/hour; under approved BD-04 the expected final charge is `1.00m`.
- This is a confirmed SRS/code/test mismatch and affects every session invoice. Treat BD-04 as authoritative unless the product owner explicitly changes it. Correct the calculator and tests together before POS/payment integration; do not silently change the SRS.
- Ensure the live session amount shown to Staff uses exactly the same server-side rounding rule as the final invoice.

### Cancellation rule still incomplete
- SRS BD-03 requires a configurable 300-second cancellation window, reason selection, zero gaming charge, event/audit records, and Admin-only void after the window.
- The Session domain currently exposes `CancelSession(endShiftId, reason)` without enforcing elapsed time or role; the inspected lifecycle handler/controller path does not yet expose the complete cancellation/void workflow.
- `IsWithinGracePeriod` currently measures from `CreatedAt`; it is not a substitute for a configurable policy based on the authoritative session start time and authenticated role.
- Enforce the policy in an application service using server time and authenticated claims, then persist the session state and audit/event records atomically.

## Chat handoff / recovery protocol (2026-11-12)

This section is the canonical resume point if the conversation ends or a new ChatGPT chat is opened.

### How to resume
1. Open the main repository: https://github.com/youssef-elqady/GHOST-2.0
2. Read `docs/STATE.md` on branch `feature/products-inventory-billing` completely before proposing or changing code.
3. Read `docs/GHOST — SRS + ERD v0.2.md` for approved requirements/business rules and `ai/GHOST-master-prompt.md` for project-wide engineering constraints.
4. Read the relevant source files and tests from the live branch before making claims. Treat this STATE file as a handoff summary, not a substitute for source verification.
5. Continue from the "Immediate next action" below. Do not ask the user to repeat project decisions already recorded here.

### Current canonical repository position
- Main working branch: `feature/products-inventory-billing`.
- Latest documented commit on this branch at handoff: `301a07605656b3be2dd04d9bd39ec3469cc54b00` (billing rounding and cancellation mismatch review).
- User reported local `git status -sb` output: `## feature/products-inventory-billing...origin/feature/products-inventory-billing`, with no modified/untracked files and no ahead/behind count at the time reported.
- The user's tracking ref may be stale until fetch. Before local work, run `git fetch origin`, then `git status -sb`; if behind, use `git pull --ff-only origin feature/products-inventory-billing`. Never reset, force-push, or discard user changes.

### Current product/engineering direction
- Build a real commercial system for GHOST PlayStation, not a demo.
- Next.js is the primary client for both Staff and Admin; the user owns v0 UI design. Preserve the existing frontend and visual direction; do not create a duplicate frontend or revert to WPF.
- .NET 10 Web API + SQL Server + EF Core backend is authoritative for all money, permissions, inventory, and session state.
- Prioritize correctness, recoverability, security, auditability, idempotency, and concurrency over speed of adding screens.
- Work in small reviewed changes. Build and test after each meaningful change. No unreviewed branch merges or destructive Git actions.
- Communicate in practical Egyptian Arabic, one next user action at a time. Explain verified facts vs assumptions; never claim tests ran unless tool output proves they ran.

### Verified findings to retain
- The foundation migration/snapshot contain the core business-day, shift, session-segment, active-pause filtered indexes and SQL Server RowVersion columns, but no Products/StockMovements tables.
- The inventory branch is divergent (10 commits ahead / 6 behind at the last compare) and is not approved for merge. Its model has Product/StockMovement additions without a matching migration; stock receipt is not yet tied to the unified cash ledger.
- Current unit tests are mostly fake-based; SQL Server integration/concurrency tests are absent.
- Confirmed SRS/code mismatch: BD-04 requires ceiling to the next whole EGP once after summing all session segments. Current SessionBillingCalculator rounds to 2 decimal places; its test expects 0.01 EGP where SRS expects 1.00 EGP. Fix implementation and tests together before POS/payment.
- Cancellation/void policy BD-03 (configurable 300-second staff window, reasons, zero gaming charge, event/audit, Admin-only void after window) is not fully enforced.
- Authentication/authorization is not configured; request-supplied staff/shift identifiers must not be trusted as actor identity.
- Completing a session currently does not create a Sale/Payment/CashTransaction; returned amount is not collected revenue.
- ExpectedCash must be derived from immutable cash ledger transactions, not arbitrary updates.

### Immediate next action
After the user synchronizes to latest branch tip, continue Sprint 01 by reviewing API controllers, request DTOs, DI registrations, and existing tests around session/shift operations; then propose a small, testable implementation order. Do not start inventory merge or payment implementation yet. First fix the BD-04 billing rule with focused unit tests, after confirming the latest local source and test baseline.

### Working agreement
The assistant is expected to act as a careful senior engineering reviewer/implementer: inspect repository state and dependencies, identify exception and data-integrity risks, make minimal changes, run available build/test checks, report exact commands/results, and maintain this STATE handoff after meaningful decisions or commits. The assistant cannot assume local test execution merely from GitHub source inspection; ask the user to run local commands when local runtime/database access is required.

## Sprint 01 API, DTO, DI, and session/shift test review (2026-11-12)

### API and dependency-injection findings
- Reviewed `Program.cs`, `SessionsController`, and `BusinessDaysController` on branch tip `98b83bba42ca41fa58dd17d2d3120be1f0238deb`.
- `Program.cs` registers repositories and handlers, but has no Authentication/Authorization services or middleware. No endpoint role/policy enforcement is present in the inspected controllers.
- `SessionsController.StartSessionRequest` accepts `ShiftId` from the caller; `BusinessDaysController.OpenShiftRequest` accepts `StaffUserId`; session completion accepts `EndShiftId`. These are caller-controlled identifiers and must not be trusted as the acting identity or current shift.
- Retryable session operations expose no Idempotency-Key contract. Start, completion, cancel, and payment flows need durable idempotency before production.
- Controllers currently map most handler failures to HTTP 400; conflict, authorization, not-found, and validation/rule failures are not consistently mapped to the SRS API status/error-code contract.
- No shifts-specific controller or authenticated current-shift endpoint was found in the inspected API tree; shift open/close currently sit under `BusinessDaysController`.

### Session and shift application/domain findings
- `SessionLifecycleHandler.CompleteAsync` updates session/device state and returns a billing snapshot, but it does not create a Sale, Payment, or CashTransaction. The returned amount is not collected revenue.
- Pause/resume/transfer/completion do not write the SRS-required SessionEvent/AuditLog records in the inspected implementation.
- Completion accepts a request-supplied EndShiftId and checks only that the shift exists and is open; actor ownership, current-shift scope, business-day relationship, and permitted handover policy are not enforced.
- Session completion currently requires `SessionStatus.Active`; a paused session cannot be completed directly without resuming first. Confirm intended operational behavior against FR-22 before changing it.
- `Shift.UpdateExpectedCash` permits direct mutation of ExpectedCash. The target must derive it from immutable CashTransactions; current shift close trusts the stored ExpectedCash.
- `OpenShiftHandler` and `CloseShiftHandler` are fake-tested, but do not implement handover or ledger-based cash reconciliation.
- `SessionRepository.GetActiveByDeviceAsync` filters `SessionStatus.Active` and unended segments. This is consistent with the current domain representation where pause is an active pause on a segment, not a separate SessionStatus value; keep API/query mapping consistent.

### Billing correction gate
- Confirmed again that `Session.GetTotalAmount` delegates to `SessionBillingCalculator`, and `SessionLifecycleHandler.CreateSnapshot` uses that same method for the displayed/completed amount. Correcting the calculator therefore fixes the shared server-side amount path.
- The current calculator rounds to 2 decimal places, and the existing two-segment test expects `0.01m`; approved BD-04 and the master prompt require `Ceiling` to whole EGP once after summing all segments.
- Do not implement a separate live-display formula. Preserve the single shared calculator and update its tests with the mandatory boundary cases from the master prompt.

### Test baseline and verification limits
- `GHOST.Tests.csproj` currently references only `GHOST.Application`; there is no Infrastructure/SQL Server integration-test setup.
- Existing session/shift handler tests use fake repositories and fake UnitOfWork implementations. They do not prove SQL Server transaction, filtered-index, concurrency, or API authorization behavior.
- No GitHub Actions workflow run was associated with commit `98b83bba42ca41fa58dd17d2d3120be1f0238deb` at the time of review.
- User executed `dotnet test .\backend\tests\GHOST.Tests\GHOST.Tests.csproj --configuration Release` on commit `98b83bb`: 40 tests total, 39 passed, 1 failed; build emitted one CS8602 nullable warning in `CloseShiftHandlerTests.cs:24`.
- The failing test was `CreateDeviceHandlerTests.HandleAsync_RejectsWhenNoRatesAreProvided`. Source inspection confirmed `CreateDeviceHandler` had no empty/null rates guard, so it created and persisted a device with no hourly rates. This is a genuine pre-existing baseline defect, not a BD-04 regression.

### Baseline defect correction (pending local verification)
- Updated `CreateDeviceHandler.HandleAsync` to reject null or empty `Rates` before enumerating or creating a Device, returning the explicit error "At least one hourly rate must be provided." This prevents a device with no billable play mode from being persisted.
- Updated `CloseShiftHandlerTests` to assert the successful `ClosedShiftSnapshot` with `Assert.IsType` before reading its properties, removing the nullable dereference warning without suppressing nullable analysis.
- Code commits:
  - `c20fbc084be28bd13b702dd045a7fbfd6ea94500` — `fix: reject devices without configured hourly rates`
  - `2ed2ba3ecae2c5150aea272fd3894bc436fd6ed5` — `test: assert successful shift snapshot without nullable warning`
- These commits were made on `feature/products-inventory-billing`. The user has not yet pulled them or rerun tests. Do not describe the baseline as fixed until local test output confirms it.
- The test that rejects empty rates already existed; its assertions now align with the new handler guard. A separate null-rates test has not been added.

### Immediate next action
1. User should run `git fetch origin`, `git pull --ff-only origin feature/products-inventory-billing`, and `git status -sb`.
2. Then run `dotnet test .\backend\tests\GHOST.Tests\GHOST.Tests.csproj --configuration Release` and provide the complete output.
3. If the baseline correction passes with 40/40 and no warnings, proceed to the isolated BD-04 calculator + focused boundary tests; otherwise diagnose the exact output before further changes.
4. Do not merge inventory or implement payment until the relevant billing, auth, cash-ledger, idempotency, and integration-test gates are designed and verified.

### Working agreement
The assistant is the engineering lead/reviewer and should choose the next safe task, inspect current code before editing, make small testable commits, and keep this file current. The user executes local PowerShell commands and returns exact output. Never claim local tests/builds/database migrations passed unless the user/tool output proves it.

## Test baseline confirmed and BD-04 implementation update (2026-11-12)

### Baseline verification
- User synchronized local `feature/products-inventory-billing` to commit `2bdf598`.
- User ran `dotnet test .\\backend\\tests\\GHOST.Tests\\GHOST.Tests.csproj --configuration Release` on that commit.
- Result: **40 total, 40 passed, 0 failed, 0 skipped**; build succeeded. No compiler warnings were reported in the supplied output.
- Local Git status was clean and HEAD matched origin at `2bdf598` before the next changes.

### BD-04 code change now committed; local verification pending
- Updated `SessionBillingCalculator.CalculateTotalAmount` to sum all unrounded segment costs, then return `decimal.Ceiling(rawTotal)` exactly once for the whole session.
- The same shared calculator is used by both live session amount snapshots and completed-session amount snapshots; no separate client-side billing formula was introduced.
- Updated calculator tests to cover:
  - Existing proportional billing and captured rates.
  - Fractional final totals ceiling to the next whole EGP.
  - Multiple segments summed before ceiling (preventing per-segment overcharging).
  - Tiny positive totals becoming 1 EGP.
  - Exact whole-EGP totals remaining unchanged.
  - Empty segments returning zero and invalid duration/rate rejection.
- Commits:
  - `751886c8dfaf6101fd8812c37b31407565e20d18` — `fix: apply BD-04 ceiling to total session charge`
  - `34fe476b934d649f9e4037c582a0d16942d2cf50` — `test: cover BD-04 session billing ceiling boundaries`
- These commits are on `feature/products-inventory-billing`. User has not yet pulled or run tests against them. Do not mark BD-04 verified until local test output confirms it.
- Implementation retains decimal arithmetic over TimeSpan ticks and uses no floating-point arithmetic. If the product later enforces whole-second-only timestamps at the domain boundary, revisit the calculator input contract alongside that change; do not silently truncate sub-second durations here.

### Immediate next action
1. User should run `git pull --ff-only origin feature/products-inventory-billing`.
2. Confirm HEAD is `34fe476` and working tree is clean.
3. Run `dotnet test .\\backend\\tests\\GHOST.Tests\\GHOST.Tests.csproj --configuration Release`.
4. If all tests pass, review the resulting shared billing path and proceed to the next high-risk gap (authentication/actor and shift scoping) before payment/POS or inventory integration.

## BD-04 local verification and authentication architecture review (2026-11-12)

### BD-04 verification confirmed
- User pulled through commit `d44c258` and ran `dotnet test .\\backend\\tests\\GHOST.Tests\\GHOST.Tests.csproj --configuration Release`.
- Result: **43 total, 43 passed, 0 failed, 0 skipped**; build succeeded. This confirms the BD-04 calculator and its focused unit tests on the user's local checkout.
- No SQL Server integration test or end-to-end invoice/payment test was run; BD-04 is verified at the shared domain-calculator/unit-test level only.

### Authentication and actor identity source review
- Re-inspected the live branch at `d44c258`.
- `Program.cs` currently registers no authentication scheme, authorization services, or authentication/authorization middleware. The API controllers have no `[Authorize]` policies.
- `ICurrentUser` already exists in Application (`UserId`, `IsAuthenticated`, `IsAdmin`, `IsStaff`), but no implementation/registration was found in the inspected API composition root.
- `SessionsController.StartSessionRequest` accepts caller-supplied `ShiftId`; `BusinessDaysController.OpenShiftRequest` accepts caller-supplied `StaffUserId`; completion accepts caller-supplied `EndShiftId`. These are not trustworthy actor identity/current-shift sources.
- `StartSessionHandler` validates that the supplied shift exists and is open, but does not verify that it belongs to the authenticated staff member.
- `OpenShiftHandler` uses the supplied StaffUserId directly. This is an actor-spoofing risk until identity is derived from a verified principal.
- API project currently has no ASP.NET Core Identity/JWT package reference visible in its project file, and no User entity/store was found in the current repository tree review. Do not add `[Authorize]` alone or accept arbitrary JWT claims without a defined, persisted user identity and credential lifecycle.
- The correct next unit of work is an authentication design slice: define the persisted Staff/Admin user model and credential lifecycle, role claims, token validation/signing configuration and secret storage, then implement a claims-backed `ICurrentUser` and tests. Only after that should endpoint policies and removal of request-supplied actor IDs be wired through controllers and handlers.
- Before implementation, inspect the SRS user/staff model and existing frontend login assumptions; do not invent password reset, initial admin provisioning, or token lifetime policies silently.

### Immediate next action
1. Implement and review the smallest authentication foundation as a separate, testable slice; first establish the user/role/credential model and configuration contract from the SRS.
2. Add tests for unauthenticated access, invalid/expired tokens, Staff vs Admin role restrictions, and actor identity sourced only from claims.
3. Then migrate shift/session commands to use `ICurrentUser` and server-resolved current shift; remove StaffUserId/ShiftId/EndShiftId from public request bodies wherever they represent the caller's identity or active shift.
4. Keep payment/POS and inventory merge blocked until authentication, cash-ledger ownership, idempotency, and persistence integration gates are addressed.


## Authentication and password lifecycle decisions approved (2026-11-12)

- User approved one-time secure server-side bootstrap for the first Admin; no default password and no public bootstrap endpoint.
- Admin creates, disables, and reactivates Staff accounts.
- Admin can reset a Staff password using a cryptographically random temporary password shown once; the Staff account must change it at next login. Existing passwords are never viewable.
- Admin changes their own password using current password + new password. If the only Admin forgets it, recovery is a separate server-side recovery command, not a public endpoint.
- Password changes/resets revoke refresh sessions according to the AUTH policy; all sensitive account actions are audited without recording passwords or raw tokens.
- Approved detailed policy and AUTH-01..AUTH-07 acceptance criteria: `docs/AUTHENTICATION-DECISIONS.md`.
- Current branch tip before this documentation update: `ba0461fc6533eda4c63cb174e6bd28f84ccc2ec1`.
- Next implementation gate: inspect Identity EF Core table mapping and migration strategy against the existing Guid-based `Shift.StaffUserId` and current SQL Server schema; then implement AUTH-01 without changing existing business behavior.


## AUTH-01 Identity persistence foundation drafted (2026-11-12)

### Changes committed on `feature/products-inventory-billing`

- Added `GHOST.Infrastructure.Identity.ApplicationUser`, based on `IdentityUser<Guid>`, with `FullName`, `IsActive`, `MustChangePassword`, `CreatedAt`, `UpdatedAt`, and `CreatedByAdminId`.
- Added `Microsoft.AspNetCore.Identity.EntityFrameworkCore` version `10.0.12` to Infrastructure, matching the existing EF Core 10.0.12 packages.
- Changed `GHOSTDbContext` to derive from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- Mapped Identity tables to the SRS names: `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `RoleClaims`, and `UserTokens`.
- Added required `Shift.StaffUserId -> Users.Id` relationship with `DeleteBehavior.Restrict`, preserving historical shift attribution.

### Important verification gate (not yet passed)

- No build, tests, migration generation, migration review, or database application has been run for these changes. Do not claim AUTH-01 complete yet.
- Existing `StaffUserId` values in any local/deployed database must be checked before applying the migration. The new FK will correctly reject orphaned shift user IDs; do not disable the FK to make a migration pass. Backfill/map legacy shift actors to valid user accounts under an explicit migration plan.
- Generate the migration locally using EF tooling, inspect both Up/Down and the updated model snapshot, and verify that no existing business tables/indexes/rowversions are dropped or altered unexpectedly.
- Do not apply the migration to a live/valuable database before a verified backup and restore test.
- Next local verification: restore/build the API and test project, then generate and inspect the Identity migration. After the migration is reviewed, implement Identity DI configuration and role/bootstrap provisioning as AUTH-02/03.

### Commits

- `d15ce731` — add persisted Identity user model.
- `e775dd98` — add Identity EF Core package.
- `d13dc98a` — integrate Identity into GHOSTDbContext and map SRS table names.
- `78bfc326` — add the required Shift-to-User relationship.
- `0520520c` — explicitly initialize Guid identity IDs and security/concurrency stamps.


## AUTH-01 local database and migration inspection update (2026-11-14)

### Local environment and database inspection (user-reported)
- Local working branch: `feature/products-inventory-billing`; user reported `git status -sb` clean and tracking `origin/feature/products-inventory-billing` before this documentation update.
- Local API connection string resolves to SQL Server `localhost`, database `GHOST2`, using Windows/Trusted Connection. User explicitly confirmed this is a Development/test database, not production.
- Read-only inspection of `GHOST2` found:
  - `__EFMigrationsHistory`: one applied migration, `20261007095934_InitialPersistence`, EF ProductVersion `10.0.12`.
  - `BusinessDays`: 1 row, `IsClosed = 0`, `OpenedAt = 2026-10-19 15:07:06 +00:00`, `ClosedAt = NULL`. It has no associated Shift. Do not close/delete it automatically; it may be a test fixture but that has not been confirmed.
  - `Devices`: 1 row (`PS4 TEST VIP 1`); `DeviceRates`: 2 rows (60.00 and 80.00).
  - `Shifts`, `Sessions`, `SessionPauses`, and `SessionSegments`: 0 rows.
  - Identity table `dbo.Users` does not exist yet.
  - Existing FK list includes DeviceRates -> Devices, Sessions -> Shifts (start/end shift), SessionSegments -> Devices/Sessions, SessionPauses -> SessionSegments, and Shifts -> BusinessDays.
- These are read-only inspection results only. No database schema/data changes were made.

### Migration source/history verification
- Initial inspection using a PowerShell `Get-ChildItem` filter showed only the model snapshot; this was a misleading result, not proof that migration source files were missing.
- Git history shows commit `6dde87a` added all three migration source files.
- User then verified both `HEAD` and `6dde87a` contain:
  - `20261007095934_InitialPersistence.cs`
  - `20261007095934_InitialPersistence.Designer.cs`
  - `GHOSTDbContextModelSnapshot.cs`
- `dotnet ef migrations list --project .\\backend\\src\\GHOST.Infrastructure\\GHOST.Infrastructure.csproj --startup-project .\\backend\\src\\GHOST.API\\GHOST.API.csproj --context GHOSTDbContext` succeeded, connected to `GHOST2`, and reported only `20261007095934_InitialPersistence` as applied.
- `dotnet ef migrations has-pending-model-changes` with the same project/startup/context succeeded in building but reported: `Changes have been made to the model since the last migration. Add a new migration.`
- This pending model change is expected to include the new Identity model/table mappings and required `Shift.StaffUserId -> Users.Id` relationship, but the exact diff has NOT yet been inspected. Do not assume the pending changes are limited to those items until source/snapshot comparison is reviewed.
- EF commands emitted NU1902 and NU1903 warnings for transitive `System.Security.Cryptography.Pkcs 9.0.11` in API and Infrastructure. Vulnerability remediation is a separate pending task; identify the dependency path and patched compatible version before changing packages.
- No new migration has been generated, reviewed, or applied. No `Update-Database` has been run.

### Current AUTH-01 safety assessment
- Because `Shifts` contains zero rows, there are currently no existing Shift.StaffUserId values in this Development database requiring legacy-user backfill. This does NOT prove other schema changes are safe or that the database contains no useful test data.
- Preserve the existing BusinessDay, Device, and DeviceRates records.
- Before applying a migration, inspect the full initial migration, designer, current model snapshot, `GHOSTDbContext`, `ApplicationUser`, and `ShiftConfiguration`; identify every generated operation and confirm existing business tables, indexes, constraints, and rowversion columns are not unexpectedly dropped/rebuilt.
- Take a database backup before applying the reviewed migration, even in Development.

### Immediate next action
1. Review the current `20261007095934_InitialPersistence.cs`, its designer, and `GHOSTDbContextModelSnapshot.cs` alongside current Identity model/configuration source. Determine exactly which model changes are pending and whether any existing schema objects would be altered.
2. Only after review, create a dedicated AUTH-01 Identity migration locally, inspect its complete Up/Down operations and snapshot diff, then build/test.
3. Back up `GHOST2`; apply only after explicit migration review, then verify Identity tables, FK, indexes, and preservation of existing BusinessDay/Device/DeviceRates rows.
4. Do not begin AUTH-02 (Identity service/JWT/DI/bootstrap) until AUTH-01 persistence is reviewed and verified.
5. Separately trace `System.Security.Cryptography.Pkcs 9.0.11` using dependency-tree tooling; do not add an arbitrary direct package reference or upgrade without compatibility/advisory verification.

### Chat handoff instruction
- The user explicitly requires `docs/STATE.md` to be updated after every meaningful project response/decision/verification so a new chat can resume without asking them to repeat context.
- After each future STATE update committed to GitHub, tell the user the documentation commit was made and that their local checkout may need `git pull --ff-only` before the next local command.


## AUTH-01 migration source review completed (2026-11-14)

### Source-level comparison
- Reviewed on `feature/products-inventory-billing`:
  - `20261007095934_InitialPersistence.cs`
  - `20261007095934_InitialPersistence.Designer.cs`
  - `GHOSTDbContextModelSnapshot.cs`
  - `GHOSTDbContext.cs`
  - `Identity/ApplicationUser.cs`
  - `Persistence/Configurations/ShiftConfiguration.cs`
  - `GHOST.Infrastructure.csproj`
- Confirmed the current initial migration and its designer/snapshot contain only the original business entities: BusinessDays, Devices, DeviceRates, Shifts, Sessions, SessionSegments, SessionPauses. They contain no ASP.NET Core Identity entities/tables and no Shift -> ApplicationUser relationship.
- Confirmed current GHOSTDbContext now derives from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, maps the seven Identity tables to `Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `RoleClaims`, and `UserTokens`, and configures ApplicationUser profile fields.
- Confirmed current ShiftConfiguration adds a required FK from `Shifts.StaffUserId` to `Users.Id` with `DeleteBehavior.Restrict`. The initial migration currently has only the StaffUserId index and no FK to Users.
- Therefore the pending model changes are explained by the Identity schema + Shift-to-User FK, consistent with the current AUTH-01 source changes. The exact generated migration still must be inspected; do not assume EF generates only the expected operations until its output is reviewed.
- No existing business columns or indexes were observed being intentionally changed in the current source diff. Existing initial migration includes the filtered unique indexes and rowversion columns; they must remain intact in the generated migration and after application.
- `ApplicationUser.CreatedByAdminId` is currently a nullable scalar property and has no configured FK; do not add one in this migration unless a separately approved relationship is designed.
- The existing initial migration's `Down()` drops all original business tables; never edit/re-run that initial migration against the existing database. Add a new incremental migration only.

### Database state remains as user-reported in prior entry
- Development DB `GHOST2` has one open BusinessDay, one Device, two DeviceRates, and zero Shifts/Sessions/SessionSegments/SessionPauses. Preserve these rows.
- The open BusinessDay is not to be automatically closed or deleted.

### Next action authorized
- It is safe to generate (but NOT apply) a new incremental migration locally for AUTH-01, because the existing database already has the initial migration applied and the new FK targets an empty Shifts table in the confirmed Development database.
- User should run:
  `dotnet ef migrations add AddIdentityPersistence --project .\\backend\\src\\GHOST.Infrastructure\\GHOST.Infrastructure.csproj --startup-project .\\backend\\src\\GHOST.API\\GHOST.API.csproj --context GHOSTDbContext --output-dir Persistence/Migrations`
- This command changes local source files only; it does not update the SQL Server database. After generation, inspect the new migration's complete Up/Down and designer/snapshot diff. Reject any unexpected DropTable/DropColumn, altered business index/rowversion, or data-destructive operation.
- Do NOT run `dotnet ef database update` / `Update-Database` until the generated migration is reviewed, a backup is taken, and the user explicitly confirms application to Development DB.
- The user requires STATE to be updated after every meaningful project decision/result. This review and next action are recorded here.


## AUTH-01 migration generated locally (2026-11-14)

- User successfully ran `dotnet ef migrations add AddIdentityPersistence` against GHOSTDbContext. Build succeeded and EF generated:
  - `20261114053406_AddIdentityPersistence.cs`
  - `20261114053406_AddIdentityPersistence.Designer.cs`
  - updated `GHOSTDbContextModelSnapshot.cs`
- User's `git status -sb` confirms these are the only local changes: two new migration files and the modified snapshot. Branch is `feature/products-inventory-billing` tracking origin; no other modifications were shown.
- This command generated source files only. No database update was run; Development DB `GHOST2` has not been changed by this step.
- Build emitted NU1902 and NU1903 warnings for transitive `System.Security.Cryptography.Pkcs` version 9.0.11, corresponding to GHSA-2fmv-rv2c-59x7 (moderate) and GHSA-846f-f764-76mx (high). These remain unresolved and must be investigated separately; do not ignore or suppress them.
- Migration contents have NOT yet been reviewed. Do not apply it or commit it until its full Up/Down and snapshot changes are inspected for unexpected destructive operations, especially drops/renames, existing business columns/indexes, rowversion, and the required Shifts -> Users FK.
- Immediate next action: inspect the complete generated migration file `20261114053406_AddIdentityPersistence.cs` (the Up and Down methods) and send its output. Then review designer/snapshot diff before any database operation.


## AUTH-01 migration review — awaiting file contents (2026-11-14)
- The next requested inspection command was shared, but no command output or migration source was provided in the latest message.
- The generated migration `20261114053406_AddIdentityPersistence.cs` remains unreviewed; do not commit it or run `dotnet ef database update` yet.
- Immediate next action: run the PowerShell command below and paste its output so the migration Up/Down operations can be reviewed:
  ```powershell
  Get-Content .\backend\src\GHOST.Infrastructure\Persistence\Migrations\20261114053406_AddIdentityPersistence.cs
  ```
- Latest STATE baseline before this note: commit `4a8b2555b2483cc86fff061987c60022d9d892a3`.


## AUTH-01 migration Up/Down reviewed (2026-11-14)
- User provided the complete source for `20261114053406_AddIdentityPersistence.cs`.
- `Up()` creates the seven expected ASP.NET Core Identity tables: Roles, Users, RoleClaims, UserClaims, UserLogins, UserRoles, UserTokens; includes expected PKs, FKs, cascade rules and standard Identity indexes.
- Adds the expected required relationship `Shifts.StaffUserId -> Users.Id` with Restrict delete. It does not add/drop/alter business columns or business indexes.
- `Down()` removes that FK and the seven Identity tables only. No business table is dropped.
- Review finding: migration source shows no destructive operation against existing business data. The current Development DB has zero Shift rows, so the new required StaffUserId FK should not block solely because of existing shift rows; nevertheless, no migration has been applied.
- This is a review of the main migration file only. Still need inspect the generated Designer and the model snapshot diff to verify metadata/model alignment before considering database update.
- Immediate next action: user pulls latest STATE and shares `git diff -- backend/src/GHOST.Infrastructure/Persistence/Migrations/GHOSTDbContextModelSnapshot.cs` plus the generated Designer file.
- Do not run `dotnet ef database update` or commit migration files until the remaining review and a DB backup/explicit approval.


## AUTH-01 model snapshot reviewed; Designer remains local (2026-11-14)
- User supplied the complete diff for `GHOSTDbContextModelSnapshot.cs`.
- Snapshot adds ApplicationUser with expected custom fields and Identity fields/indexes, the six standard Identity entity types/tables, and their expected relationships.
- Snapshot adds the required `Shifts.StaffUserId -> ApplicationUser` relationship with Restrict delete, matching the reviewed migration Up operation.
- No changes to existing business entity property definitions or business indexes are visible in the supplied diff.
- Attempted to fetch `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.Designer.cs` from GitHub; it is not present on the remote branch (404), consistent with the previously reported untracked local generated file. Do not assume remote access to uncommitted local files.
- Remaining review: inspect the local generated Designer file; compare its target model metadata with the snapshot and main migration. No migration has been committed or applied.
- Immediate next action: user runs `Get-Content .\backend\src\GHOST.Infrastructure\Persistence\Migrations\20261114053406_AddIdentityPersistence.Designer.cs` locally and pastes the output.



## AUTH-01 generated Designer reviewed; next verification gate (2026-11-14)

### Designer / target model review
- User supplied the complete local file `20261114053406_AddIdentityPersistence.Designer.cs`; it is generated locally and is not yet present on the GitHub branch.
- Reviewed its `BuildTargetModel` against the supplied migration Up/Down and the supplied `GHOSTDbContextModelSnapshot.cs` diff.
- The Designer contains the unchanged existing business entities and their existing indexes/rowversion metadata, plus the expected `ApplicationUser` and six standard Identity entity types.
- Identity PKs, unique normalized username/role indexes, email index, claim/login/role/token relationships, and cascade behavior align with the migration and snapshot.
- `Shift.StaffUserId -> ApplicationUser.Id` is required and uses Restrict delete in both target model and migration, matching the source configuration.
- No unexpected business entity/property/index changes were visible in the supplied Designer or Snapshot diff.
- Combined source review of the three locally generated files (main migration, Designer, snapshot diff) is now complete at source level. The migration creates the seven expected Identity tables and adds only the Shift-to-Users FK; Down removes only that FK and those Identity tables.
- This is not proof that the database has been updated or that the generated SQL has been applied. No `database update`, backup, migration commit, or SQL Server schema change has been reported.

### Repository and documentation handoff check
- Confirmed GitHub branch `feature/products-inventory-billing` exists and currently points to `909c5cbe8652ba47e5f8633c3c1c4434329969ff` (`docs: record AUTH-01 snapshot review`), parent `e5b42e7b2a77d845a3cd45906616e1ec58dc8e71`.
- The latest commit changes only `docs/STATE.md`; combined commit status currently reports no status entries. Branch protection is not enabled.
- Read the current branch versions of this STATE file, `docs/GHOST — SRS + ERD v0.2.md`, and `ai/GHOST-master-prompt.md`. SRS/master invariants retained: backend-authoritative money/time/permissions/stock; BD-03 300-second cancel policy; BD-04 ceiling once on total session charge; BD-13 whole-pound product sale prices; BD-14 categorized staff drawer withdrawals; transactions, append-only ledgers, idempotency, concurrency, audit, recovery, real SQL Server integration tests, and preserve the existing Next.js frontend.
- AUTH policy already approved in this STATE remains authoritative: one-time secure server-side first-Admin bootstrap, Admin-managed Staff accounts, temporary random password reset shown once + mandatory change, Admin self-change, server-side recovery command for the only Admin, refresh-session revocation, and audit without passwords/raw tokens.
- Known gaps remain: API authentication/authorization not yet wired; request-supplied StaffUserId/ShiftId/EndShiftId must not be trusted; no cash ledger/payment on session completion; no SQL Server integration/concurrency test project; inventory branch remains divergent and unapproved; Pkcs 9.0.11 vulnerability warnings remain to investigate.
- The generated migration files and Snapshot change are still local/uncommitted per the last user-reported status; do not assume they exist on GitHub.

### Immediate next action
1. On the local checkout, run `dotnet test .\\backend\\tests\\GHOST.Tests\\GHOST.Tests.csproj --configuration Release` and share the complete output. The Identity context/migration generation succeeded in a build, but the full test suite has not yet been re-run after AUTH-01 source changes.
2. Also share `git status -sb` so we can confirm only the two new migration files and Snapshot are modified/untracked; do not stage or commit yet.
3. If tests pass, generate and inspect the SQL migration script (without applying it), then take a verified backup of Development DB `GHOST2`; only after SQL script review and explicit user approval may the migration be applied.
4. Do not begin AUTH-02, merge inventory, or implement payment/POS until AUTH-01 persistence is safely verified and the related auth/cash/idempotency gates are addressed.



## AUTH-01 unit test gate passed; generated migration remains local (2026-11-17)

### Local verification reported by user
- User pulled the documentation commit `c489910a528893baf2a57deaee9d1922fee61c17` successfully.
- User ran `dotnet test .\\backend\\tests\\GHOST.Tests\\GHOST.Tests.csproj --configuration Release`.
- Result: **43 total, 43 passed, 0 failed, 0 skipped**; all reported project builds succeeded on .NET 10.0.12.
- This confirms the current Application/Domain unit-test project passes after AUTH-01 model/migration generation. It does not prove SQL Server persistence, migration application, authentication flows, or database FK behavior; the test project does not provide SQL Server integration coverage.
- User's `git status -sb` confirms branch `feature/products-inventory-billing` is synchronized with origin and exactly these local changes remain:
  - Modified `backend/src/GHOST.Infrastructure/Persistence/Migrations/GHOSTDbContextModelSnapshot.cs`
  - Untracked `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.Designer.cs`
  - Untracked `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.cs`
- No other local changes were reported. Migration files are still not committed/pushed. No database update has been run.

### Immediate next action
1. Generate a SQL script for only `20261007095934_InitialPersistence` -> `20261114053406_AddIdentityPersistence`, using the existing API startup project, Infrastructure project, and `GHOSTDbContext`. Save it under a local ignored/temp artifacts directory (create the directory if needed). This is script generation only; do not execute the script or run `database update`.
2. Review the complete generated SQL for only the seven Identity table creates, the Shift-to-Users FK, expected indexes/constraints, and absence of business table drops/alters or data-destructive operations. If script output is long, inspect it in sections or attach the file.
3. Before any application to Development database `GHOST2`, take a SQL Server backup to a verified writable path and verify the backup file and restore viability. Confirm the target is still the Development database and ask for explicit approval before applying the migration.
4. Do not stage/commit migration files until SQL script review is complete. Keep AUTH-02 and inventory/payment work blocked until AUTH-01 persistence and authentication integration gates are properly implemented and tested.


## AUTH-01 SQL script generated; pending review (2026-11-17)

- User generated `artifacts/AUTH-01.sql` from `20261007095934_InitialPersistence` to `20261114053406_AddIdentityPersistence`.
- File verification: `E:\\GHOST\\GHOST-2.0\\artifacts\\AUTH-01.sql`, length 3,881 bytes.
- EF tooling build succeeded. NU1902/NU1903 warnings remain for vulnerable `System.Security.Cryptography.Pkcs 9.0.11`; track and remediate separately.
- SQL script contents have not yet been reviewed. No SQL script execution, database update, migration commit, or schema/data change is authorized or reported.
- Next: pull latest docs, inspect the complete generated SQL script, then plan verified backup/restore before requesting explicit approval for any database update.


## AUTH-01 generated SQL reviewed (2026-11-17)

- User pulled docs through commit `baa31ea` and supplied the complete generated `artifacts/AUTH-01.sql` (3,881 bytes).
- Reviewed complete SQL: creates the seven expected ASP.NET Identity tables (`Roles`, `Users`, `RoleClaims`, `UserClaims`, `UserLogins`, `UserRoles`, `UserTokens`), their expected PKs/FKs/indexes, adds `FK_Shifts_Users_StaffUserId` with `ON DELETE NO ACTION`, inserts the new migration history row, and wraps changes in a transaction.
- No DROP statements, existing business-table column changes, or unexpected data operations found in the supplied script.
- Previously read-only inspected Development DB `GHOST2`: `Shifts` had zero rows, so no existing shift rows were observed that could violate the new StaffUserId FK. This does not replace a backup.
- No SQL has been executed; no database update or migration commit has occurred.
- Next gate: take a SQL Server backup of `GHOST2`, verify the backup (preferably restore to a separate test database), then request explicit user approval before applying AUTH-01. Do not run `dotnet ef database update` before those gates.


## AUTH-01 backup preparation: SQL Server environment identified (2026-11-17)

- User pulled reviewed-state commit `0a636cc` successfully.
- Local SQL Server default instance service `MSSQLSERVER` is Running.
- `sqlcmd.exe` is installed at:
  - `C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\180\\Tools\\Binn\\SQLCMD.EXE`
  - `C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\170\\Tools\\Binn\\SQLCMD.EXE`
- No backup has been created and no database/schema/data has been changed.
- Before choosing a backup destination, inspect the SQL Server service account and SQL Server's configured default backup directory using read-only commands. Then create a backup in a directory writable by the SQL Server service and verify it (preferably restore to a separate test DB).
- User explicitly requires every meaningful project response to append a detailed, resumable progress entry to `docs/STATE.md`, including completed work, evidence, files/commits, outstanding gates, and exact next commands.
- Current next gate remains: verified backup of Development DB `GHOST2`; do not apply AUTH-01 until backup verification and explicit approval.


## AUTH-01 backup prep: sqlcmd certificate trust diagnostic (2026-11-17)

- User confirmed SQL Server service identity: `NT Service\\MSSQLSERVER`; service state Running.
- First sqlcmd metadata query using ODBC Driver 18 failed before connecting because the local SQL Server certificate chain is not trusted. This is a client TLS trust configuration issue; it does not indicate a database failure.
- Next read-only diagnostic: rerun the same local trusted-Windows-authentication query with sqlcmd `-C` (TrustServerCertificate) to accept the local server certificate for this diagnostic connection. Do not change server-wide encryption/certificate settings.
- No backup created; no database/schema/data changes; AUTH-01 not applied.


## AUTH-01 backup destination identified (2026-11-17)

- User pulled commit `e127ca6` successfully.
- Read-only SQLCMD connection succeeded using Windows authentication and `-C` (trust local server certificate for this client connection).
- SQL Server is the default instance (`SERVERPROPERTY('InstanceName') = NULL`); server name returned as `El-Qady`.
- SQL Server default backup directory returned by `SERVERPROPERTY('InstanceDefaultBackupPath')`:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup`
- SQL Server service identity remains `NT Service\\MSSQLSERVER` (service Running).
- Next: verify the backup directory exists and inspect its ACL for the SQL Server service identity before issuing a BACKUP DATABASE command. Prefer a unique timestamped .bak name; do not assume the service can write until ACL/path checks succeed.
- No backup created; no database/schema/data changed; AUTH-01 remains unapplied.


## AUTH-01 backup folder ACL check blocked by current shell permissions (2026-11-17)

- User pulled `b518607` successfully.
- `Test-Path` confirms SQL Server default backup directory exists:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup`.
- User's non-elevated PowerShell received `Access is denied` from `icacls` on that directory; this does not establish whether the SQL Server service account can write there.
- Next: inspect the directory ACL from an elevated PowerShell (Run as Administrator) using read-only `icacls`. Do not grant broad permissions or change ACLs yet. If the service account lacks write access, prefer a dedicated backup folder with narrowly scoped Modify permission for `NT SERVICE\\MSSQLSERVER`, after confirming an appropriate volume/path.
- No backup created; no database/schema/data changes; AUTH-01 not applied.


## AUTH-01 backup directory permissions confirmed (2026-11-17)

- User inspected the SQL Server backup directory from an elevated PowerShell.
- Directory ACL confirms `NT SERVICE\\MSSQLSERVER` has inherited `(OI)(CI)(F)` Full Control on:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup`.
- SYSTEM and local Administrators also have Full Control. No ACL change is needed for SQL Server to write a backup there.
- Next: check that the planned unique backup filename does not already exist, then create a full SQL Server backup of Development DB `GHOST2` using SQLCMD with Windows authentication and `-C`. Afterward run `RESTORE VERIFYONLY`; ideally restore to a separate test database before any production-targeting decision.
- No backup has been created yet; no schema/data changes; AUTH-01 not applied.


## AUTH-01 backup filename check + handoff re-read (2026-11-17)

### Latest user-reported local verification
- User successfully pulled the previous documentation commit `de339a1` on `feature/products-inventory-billing`; local fast-forward completed.
- User checked the planned backup path:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak`
- PowerShell `Test-Path` returned `False`: no file currently exists at that exact path, so the planned filename is available and no existing backup will be overwritten.
- This is only a filename existence check. No backup has been created and no database/schema/data changes have occurred.

### Handoff documents re-read from the current branch
- Re-read `docs/STATE.md` (543 lines), `docs/GHOST — SRS + ERD v0.2.md` (398 lines), and `ai/GHOST-master-prompt.md` (739 lines) from `feature/products-inventory-billing`.
- Reconfirmed controlling invariants: backend is authoritative for money/time/permissions/stock; cash and stock changes are transactional, idempotent, auditable, concurrency-safe and append-only; preserve Next.js frontend; no destructive DB/Git action without review; real SQL Server integration and restore tests are required.
- Reconfirmed business rules BD-03 (300-second cancellation window), BD-04 (ceiling once on total session amount), BD-13 (whole-pound product sale price), and BD-14 (categorized staff drawer withdrawals without pre-approval).
- Reconfirmed AUTH-01 migration and generated SQL were reviewed and found to create the seven Identity tables plus the Shift-to-Users FK, with no unexpected business-table drops/alterations.
- Current AUTH-01 gate remains backup + backup verification/restore before any migration application. Do not run `dotnet ef database update` or execute `artifacts/AUTH-01.sql` yet.
- Existing Development DB data to preserve: one open BusinessDay, one Device, two DeviceRates; zero Shifts/Sessions/SessionSegments/SessionPauses. Do not close/delete the open BusinessDay automatically.

### Immediate next action — create a full COPY_ONLY backup (not a migration)
- Confirmed SQL Server default instance `localhost`, service `MSSQLSERVER` running as `NT SERVICE\\MSSQLSERVER`; the service account has Full Control on the default Backup directory.
- Use Windows authentication and client-side `-C` only for this local SQLCMD connection because ODBC Driver 18 otherwise rejects the local untrusted certificate.
- Run the explicit BACKUP DATABASE command below with `sqlcmd -b`; it targets only `GHOST2`, uses the confirmed unused filename, `COPY_ONLY`, and `CHECKSUM`. This creates a backup file but does not change the database schema or business data:
  ```powershell
  & "C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\180\\Tools\\Binn\\SQLCMD.EXE" -S localhost -E -C -d master -b -Q "IF DB_ID(N'GHOST2') IS NULL THROW 50000, 'GHOST2 database not found', 1; SELECT name, state_desc FROM sys.databases WHERE name = N'GHOST2'; BACKUP DATABASE [GHOST2] TO DISK = N'C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;"
  ```
- After the command, verify the exact file exists and has non-zero size using PowerShell `Get-Item`; then run SQL Server `RESTORE VERIFYONLY ... WITH CHECKSUM`. A VERIFYONLY success is not the same as a full restore test; ideally restore to a separate scratch database and compare preserved row counts before migration approval.
- No backup, migration application, migration commit, or schema/data change has occurred as of this entry.


## AUTH-01 COPY_ONLY backup created (2026-11-17)

### User-reported execution result
- User pulled STATE commit `854144d` successfully before running the backup.
- Executed SQLCMD against local default SQL Server instance using Windows authentication and `-C` for the local certificate:
  `BACKUP DATABASE [GHOST2] ... WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10`.
- Pre-check confirmed `GHOST2` exists and is `ONLINE`.
- SQL Server reported 10% through 100%, processed 712 database data pages and 2 log pages, and completed 714 pages successfully in 0.047 seconds.
- SQLCMD exited back to PowerShell without a reported SQL error. This is evidence that SQL Server completed the backup command successfully.
- Backup target:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak`
- Backup is COPY_ONLY and CHECKSUM-enabled. It is a backup-only operation; no migration was applied and no database schema/business data was intentionally changed.

### Remaining verification before any migration
- File existence and non-zero size have not yet been confirmed from PowerShell.
- SQL Server `RESTORE VERIFYONLY ... WITH CHECKSUM` has not yet been run.
- A separate scratch-database restore test has not yet been performed. VERIFYONLY alone is not a full restore test.
- Do not run `dotnet ef database update` or execute the generated SQL manually until backup verification and explicit user approval.
- Immediate next action: inspect exact backup file metadata with `Get-Item`, then run `RESTORE VERIFYONLY` against the exact backup path.


## AUTH-01 backup file confirmed; VERIFYONLY command path typo (2026-11-17)

### User-reported evidence
- PowerShell `Get-Item` confirmed the exact backup file exists at:
  `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak`
- Reported file length: **5,951,488 bytes** (non-zero). The pasted table output did not visibly include a LastWriteTime value, so no timestamp is recorded here.
- The user's first `RESTORE VERIFYONLY` attempt did not execute: PowerShell reported the sqlcmd executable path was not recognized because the command omitted the `Binn` directory. This is a command-path typo, not a SQL Server backup verification failure.

### Immediate next action
- Retry the read-only verification with the previously confirmed executable path including `Binn\\SQLCMD.EXE`:
  ```powershell
  & "C:\\Program Files\\Microsoft SQL Server\\Client SDK\\ODBC\\180\\Tools\\Binn\\SQLCMD.EXE" -S localhost -E -C -d master -b -Q "RESTORE VERIFYONLY FROM DISK = N'C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak' WITH CHECKSUM;"
  ```
- Record the exact result. A successful VERIFYONLY is not a full restore test; next inspect FILELISTONLY and plan a separately named scratch-database restore, without overwriting GHOST2 and only after explicit user approval.
- No migration has been applied; no schema/business-data changes have been made.


## AUTH-01 backup RESTORE VERIFYONLY passed (2026-11-17)

### User-reported execution result
- User ran SQL Server `RESTORE VERIFYONLY` against the confirmed COPY_ONLY backup using the correct SQLCMD executable path under `Tools\\Binn\\SQLCMD.EXE`, Windows authentication, and client-side `-C`.
- Exact SQL Server output: `The backup set on file 1 is valid.`
- The command included `WITH CHECKSUM` and returned to PowerShell without a reported error.
- Combined with the earlier confirmed non-zero backup file length of 5,951,488 bytes and successful COPY_ONLY backup completion, this establishes that SQL Server can verify the backup set and its checksums.
- This is not a full restore test and does not yet prove that the database can be restored and opened successfully.

### Next gate — plan isolated restore test
- Before any AUTH-01 migration application, inspect backup logical file names with `RESTORE FILELISTONLY`, inspect SQL Server data/log paths and available disk space, then prepare a restore plan to a new scratch database (e.g. `GHOST2_AUTH01_RestoreTest`) using distinct physical MDF/LDF paths.
- Do not overwrite, rename, detach, or otherwise modify the original `GHOST2` database or its files.
- The scratch restore creates a new database and files; obtain explicit user approval before executing it. After approval, verify expected preserved row counts and run `DBCC CHECKDB` on the scratch database.
- Only after successful isolated restore verification should the user be asked for explicit approval to apply AUTH-01 to `GHOST2`. Do not run `dotnet ef database update` or execute `artifacts/AUTH-01.sql` yet.
- No migration has been applied or committed; no original database schema/business data has been changed.


## AUTH-01 isolated restore preflight: FILELISTONLY and disk space (2026-11-17)

### User-reported results
- User pulled documentation commit `9f01a45` successfully; local branch fast-forwarded from `1cdd71d` to `9f01a45`.
- `RESTORE FILELISTONLY` completed successfully for backup `GHOST2_AUTH01_PreMigration_20261117.bak` and returned exactly two logical files:
  - Data logical name: `GHOST2`; original path: `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2.mdf`; Type `D`; reported size 8,388,608 bytes.
  - Log logical name: `GHOST2_log`; original path: `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_log.ldf`; Type `L`; reported size 8,388,608 bytes.
- PowerShell `Get-PSDrive C` reported 11,330,404,352 bytes free (about 10.55 GiB) and 109,622,833,152 bytes used.
- The backup's logical names are now known. Do not restore to the original physical filenames or overwrite the existing `GHOST2` database.

### Next read-only preflight
- Query SQL Server's configured default data and log directories and check whether scratch database name `GHOST2_AUTH01_RestoreTest` already exists.
- Check whether the proposed distinct physical paths exist before using them:
  - `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest.mdf`
  - `C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest_log.ldf`
- Proposed restore will use `MOVE N'GHOST2' TO ...GHOST2_AUTH01_RestoreTest.mdf` and `MOVE N'GHOST2_log' TO ...GHOST2_AUTH01_RestoreTest_log.ldf`, creating a separate scratch database only. Obtain explicit user approval before executing the restore because it creates a database and physical files.
- After approved restore, validate preserved row counts and run `DBCC CHECKDB` on the scratch database. Do not run migration against original `GHOST2` until this gate and separate explicit migration approval are complete.
- No restore has been run; no migration applied; original database remains untouched.


## AUTH-01 scratch restore target preflight passed (2026-11-17)

### User-reported execution results
- User pulled documentation commit 6ef40c4 successfully; local branch fast-forwarded from 9f01a45 to 6ef40c4.
- SQL Server query for database GHOST2_AUTH01_RestoreTest returned (0 rows affected): no database with that name currently exists.
- SQL Server instance default data and log paths both resolve to:
  C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\
- PowerShell Test-Path returned False for both proposed distinct target files:
  - C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest.mdf
  - C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest_log.ldf
- Previously reported free space on C: is 11,330,404,352 bytes (~10.55 GiB).
- The pasted output includes an earlier Get-Item access-denied attempt and an earlier malformed sqlcmd path, but those were superseded by the successful file metadata check and successful RESTORE VERIFYONLY recorded above. They do not negate the later SQL Server verification.

### Assessment and next gate
- The proposed scratch database name and both target filenames are currently unused according to the reported checks. The SQL Server default data/log directory is known.
- A restore command can now be prepared with explicit MOVE clauses from logical names GHOST2 and GHOST2_log to the two distinct scratch filenames above. It must not use REPLACE, and must not target original GHOST2 or its MDF/LDF files.
- Do not execute the restore until the user explicitly approves creating the separate scratch database and files. This is a write operation, although it leaves the original GHOST2 database untouched.
- After approved restore, query row counts for BusinessDays, Devices, DeviceRates, Shifts, Sessions, SessionSegments, and SessionPauses, compare against the known pre-migration baseline (1, 1, 2, 0, 0, 0, 0 respectively), and run DBCC CHECKDB on the scratch database.
- No restore has yet been executed; AUTH-01 migration remains unapplied and uncommitted; original GHOST2 remains untouched.


## AUTH-01 scratch restore explicitly approved; execution pending (2026-11-17)

- User explicitly approved creating the isolated scratch database GHOST2_AUTH01_RestoreTest from the verified COPY_ONLY backup.
- Approval is limited to this separate restore test. It is not approval to modify/restore over GHOST2, apply AUTH-01, or execute any migration.
- Exact backup source: C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\Backup\\GHOST2_AUTH01_PreMigration_20261117.bak
- Restore target database: GHOST2_AUTH01_RestoreTest
- Logical-to-physical mappings: GHOST2 -> C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest.mdf; GHOST2_log -> C:\\Program Files\\Microsoft SQL Server\\MSSQL17.MSSQLSERVER\\MSSQL\\DATA\\GHOST2_AUTH01_RestoreTest_log.ldf.
- Preflight found no existing database with the target name and neither target file existed. C: had 11,330,404,352 bytes free. SQL Server default data/log directory is the target directory.
- Execution is still pending user-side SQLCMD command. The command must check DB_ID is NULL and use explicit MOVE clauses, CHECKSUM and STATS; no REPLACE clause.
- After execution, capture exact SQLCMD output. If successful, validate table row counts against baseline (BusinessDays 1, Devices 1, DeviceRates 2, Shifts 0, Sessions 0, SessionSegments 0, SessionPauses 0) and run DBCC CHECKDB on the scratch database.
- Original GHOST2 remains untouched. AUTH-01 migration has not been applied or committed.


## AUTH-01 isolated scratch database restore succeeded (2026-11-17)

### User-reported execution result
- User pulled documentation commit e5dfea4 successfully; local branch fast-forwarded from 6ef40c4 to e5dfea4.
- User executed the explicitly approved restore command with a guard that aborts if GHOST2_AUTH01_RestoreTest already exists, explicit MOVE clauses to new MDF/LDF names, CHECKSUM, and STATS. No REPLACE option was used.
- SQL Server reported 10% through 100%, processed 712 data pages and 2 log pages, and completed:
  `RESTORE DATABASE successfully processed 714 pages in 0.024 seconds (232.259 MB/sec).`
- Scratch database created: GHOST2_AUTH01_RestoreTest.
- New physical files: GHOST2_AUTH01_RestoreTest.mdf and GHOST2_AUTH01_RestoreTest_log.ldf under the SQL Server DATA directory.
- This is a successful isolated restore operation. Integrity check and row-count comparison are still pending.
- Original GHOST2 was not the restore target; no AUTH-01 migration was applied or committed.

### Immediate next action — read-only verification of scratch restore
- Query scratch database state and row counts for BusinessDays, Devices, DeviceRates, Shifts, Sessions, SessionSegments, SessionPauses; compare with pre-backup baseline 1, 1, 2, 0, 0, 0, 0.
- Run DBCC CHECKDB (GHOST2_AUTH01_RestoreTest) WITH NO_INFOMSGS and capture its complete output. These are read-only checks.
- Do not run any migration against original GHOST2 yet. After successful integrity/count checks, obtain a separate explicit approval before applying AUTH-01.


## AUTH-01 scratch verification query syntax typo (2026-11-17)

- User pulled commit 550210f successfully after the isolated restore step.
- User reran the guarded restore command and SQL Server again reported a successful restore of 714 pages to GHOST2_AUTH01_RestoreTest. The guard did not abort, so at the time of that execution SQL Server did not find a database with that name; the restore succeeded and scratch DB is expected to exist now.
- The subsequent read-only row-count + DBCC command failed at parse time with: `Msg 156 ... Incorrect syntax near the keyword 'RowCount'.` The first SELECT used `AS RowCount`, which SQL Server parses as a reserved keyword. Therefore no row counts or DBCC CHECKDB result were produced by that command; do not infer integrity failure or success from it.
- Next: rerun the same read-only scratch database verification, changing the alias to `TotalRows` (or bracketed `[RowCount]`), then capture the output including DBCC CHECKDB. No migration applied; original GHOST2 remains untouched.


## AUTH-01 scratch restore row-count comparison passed; CHECKDB output needs explicit confirmation (2026-11-17)

### User-reported verification
- User pulled documentation commit cd4b862 successfully.
- The corrected read-only SQL query ran against GHOST2_AUTH01_RestoreTest and returned these row counts:
  - BusinessDays: 1
  - Devices: 1
  - DeviceRates: 2
  - Shifts: 0
  - Sessions: 0
  - SessionSegments: 0
  - SessionPauses: 0
- These exactly match the known pre-backup baseline, so the scratch restore preserved the expected records/counts for the inspected business tables.
- The command also invoked `DBCC CHECKDB (N'GHOST2_AUTH01_RestoreTest') WITH NO_INFOMSGS`, but the pasted output contains no DBCC summary. Because NO_INFOMSGS suppresses informational output, the absence of a summary alone is not sufficient evidence to record a confirmed CHECKDB pass. Re-run CHECKDB without NO_INFOMSGS (or with an output-capturing wrapper) and capture the explicit summary/errors.
- No AUTH-01 migration has been applied or committed. Original GHOST2 remains untouched. Scratch database GHOST2_AUTH01_RestoreTest exists and should be retained until migration verification/rollback planning is complete; do not drop it without explicit approval.

### Overall AUTH-01 progress and remaining gates
- Completed: migration source/designer/snapshot review; generated SQL script review; unit tests (43/43 pass); COPY_ONLY CHECKSUM backup; backup file metadata check; RESTORE VERIFYONLY success; isolated scratch restore success; row-count comparison success.
- Pending: explicit DBCC CHECKDB summary; then a separate explicit user approval to apply AUTH-01 to original GHOST2; apply only the reviewed migration; verify migration history/schema and ensure existing business data remains intact; implement/test the remaining Identity runtime (DI, bootstrap, login/JWT, password lifecycle, authorization and audit) as separate work.
- Important scope distinction: applying AUTH-01 only creates Identity persistence tables and the StaffUserId FK. It does not by itself deliver a working authentication system or finish the broader AUTH feature.


## AUTH-01 isolated restore integrity check passed (2026-11-17)

### User-reported SQL Server verification
- User pulled documentation commit `c56678a` successfully; local fast-forward completed from `cd4b862` to `c56678a`.
- Executed read-only:
  `DBCC CHECKDB (N'GHOST2_AUTH01_RestoreTest') WITH ALL_ERRORMSGS;`
- SQL Server explicit summary:
  `CHECKDB found 0 allocation errors and 0 consistency errors in database 'GHOST2_AUTH01_RestoreTest'.`
- DBCC execution completed without reported errors. The output also enumerated the expected restored business tables and system objects.
- Combined evidence now passes the isolated backup/restore gate:
  - COPY_ONLY + CHECKSUM backup of original `GHOST2` completed (714 pages).
  - Backup file exists and is 5,951,488 bytes.
  - `RESTORE VERIFYONLY ... WITH CHECKSUM` reported: `The backup set on file 1 is valid.`
  - Restore to separate `GHOST2_AUTH01_RestoreTest` completed successfully (714 pages), using distinct MDF/LDF paths and no REPLACE.
  - Business table row counts match the pre-backup baseline exactly: BusinessDays 1, Devices 1, DeviceRates 2, Shifts 0, Sessions 0, SessionSegments 0, SessionPauses 0.
  - `DBCC CHECKDB` reports zero allocation and consistency errors.
- This evidence concerns the restored scratch copy. It does not mean AUTH-01 has been applied to the original database.
- Original `GHOST2` remains the target development database and has not been restored over, migrated, or intentionally modified by these backup/restore checks. Scratch database and its MDF/LDF files must be retained; do not drop them without explicit approval.

### Current AUTH-01 gate and next action
- Backup and isolated restore verification: **PASSED**.
- Migration source/designer/snapshot and generated `artifacts/AUTH-01.sql` review: previously completed at source level; intended changes are seven ASP.NET Identity tables plus FK `Shifts.StaffUserId -> Users.Id`; no business-table drops or unrelated data operations were found.
- Unit-test evidence: 43/43 passed in the previously reported local run. This is unit-test coverage only, not SQL Server integration or authentication-flow coverage.
- No migration has been applied or committed. Do not execute `dotnet ef database update` or `artifacts/AUTH-01.sql` until the user gives a separate, explicit approval for applying AUTH-01 to the original `GHOST2` database.
- Immediate next action: present the exact migration scope and request that separate approval. If approved, first re-check the live target database identity/state and current backup presence, then apply only the reviewed migration in a controlled step and verify `__EFMigrationsHistory`, the seven Identity tables, the FK, and the unchanged business-table row counts. Stop on any unexpected schema/data difference.
- After persistence is applied, continue AUTH implementation separately: Identity DI/configuration, secure initial Admin bootstrap, login/token lifecycle, staff account lifecycle, server-side role/resource authorization, trusted actor/shift resolution, audit events, and SQL Server integration/concurrency/idempotency tests. AUTH-01 persistence alone is not a completed authentication system.

### Repository handoff verification
- Current working branch checked: `feature/products-inventory-billing`; GitHub branch search returned that branch.
- Latest branch commit confirmed from the user's successful pull and GitHub commit inspection: `c56678a518ac563d507027175c00627705abbf6e` (`docs: record scratch restore row counts and remaining checks`).
- GitHub combined status returned no status entries, and the commit workflow-run lookup returned no workflow runs. This is **no CI evidence**, not a CI pass.


## AUTH-01 application approval received; runtime preflight pending (2026-11-17)

### User approval
- User explicitly approved applying migration `AddIdentityPersistence` to the original `GHOST2` database.
- This approval is for AUTH-01 persistence migration only. It does not authorize unrelated schema changes, destructive cleanup, or dropping the scratch restore database.
- Do not execute the migration until the immediate read-only live-target/backup preflight is completed and reviewed. User must provide the resulting SQLCMD output; the assistant has no direct SQL Server execution session.

### Current repository implementation audit (branch: feature/products-inventory-billing)
- Reviewed SRS v0.2 and current frontend/backend files on this branch.
- Frontend exists as a Next.js foundation:
  - `frontend/components/staff-dashboard.tsx`: Staff dashboard shell and navigation exist. It fetches active devices from `GET /api/devices`. Session-start modal is present but its Start button is explicitly disabled pending authenticated staff identity and active-shift resolution. Products and invoices views explicitly state they are not connected; Shift view says it needs current-shift/cash read endpoints; messages and activity history are placeholders.
  - `frontend/components/admin-dashboard.tsx`: Admin navigation/shell exists for overview, devices, shifts/cash, staff/permissions, inventory, reports, and settings. The overview and all sections except device management explicitly state that live data/endpoints are not connected. It warns login is not enabled and `/admin` is not protected.
  - `frontend/components/admin-device-manager.tsx`: device list/default-device seeding and Single/Multi rate editing are connected to API. This is the current meaningful Admin workflow.
  - `frontend/lib/ghost-api.ts`: only device API calls are currently defined (GET devices, POST seed defaults, PUT device rates).
  - `frontend/README.md`: confirms unconnected workflows: shifts, session lifecycle, products/inventory, payments, persistent staff messages, activity history, and authentication.
- Backend API controllers present under `backend/src/GHOST.API/Controllers`: `BusinessDaysController`, `DevicesController`, `SessionsController`.
  - BusinessDaysController exposes business-day open and shift open/close endpoints. Its shift-open request currently accepts `StaffUserId` from the request body; this is not a trusted authenticated identity and must be replaced/secured before operational use.
  - SessionsController exposes start, pause, resume, transfer, and complete endpoints. Requests currently carry ShiftId/EndShiftId in payloads; these must be bound to the authenticated staff member's authorized current shift before production use. No evidence of cancel endpoint in this controller.
  - DevicesController exposes list/create/default seeding/rate update.
- `GHOST.API/Program.cs` currently registers EF SQL Server, repositories and handlers for business days, devices, sessions and shifts. It does not currently register ASP.NET Identity authentication/JWT/authorization or inventory/sales/report services.
- `GHOSTDbContext` currently has business DbSets for Devices, DeviceRates, BusinessDays, Shifts, Sessions, SessionSegments, SessionPauses and inherits IdentityDbContext. The AUTH migration is still only locally reviewed/planned; it has not been applied to the live database.
- No Products/Inventory, POS/Sales/Payments/Invoices, Expenses/CashTransactions, analytics/report endpoints or reporting dashboard data integration were found in the inspected current API controller set.
- Therefore, do not describe the Staff/Admin frontends as completed dashboards. They are visual/interaction foundations with limited device/rate integration; core operational flows and financial reporting remain to be implemented and tested.

### Immediate next action — read-only preflight before applying approved migration
- User should run the supplied SQLCMD preflight against `master` to verify original `GHOST2` is ONLINE, confirm the expected pre-migration business-table counts, confirm AUTH-01 tables/history are not already present, and verify the exact backup file is still present/readable with `RESTORE VERIFYONLY ... WITH CHECKSUM`.
- Review the full output before issuing the single migration-application command. If database identity/state, counts, migration history, or backup verification differs from expectations, STOP and investigate; do not proceed automatically.
- No migration has yet been applied. Original GHOST2 remains unchanged by this workflow; retain `GHOST2_AUTH01_RestoreTest` and its files.


## AUTH-01 live preflight passed; migration not yet applied (2026-11-17)

The user pulled the documentation update through commit `8c1a2f7` and executed the approved read-only SQLCMD preflight against the original `GHOST2` database. Observed output:

- `GHOST2` exists and is `ONLINE`.
- Business-table baseline is unchanged: BusinessDays=1, Devices=1, DeviceRates=2, Shifts=0, Sessions=0, SessionSegments=0, SessionPauses=0.
- All seven target ASP.NET Identity tables are absent: Users, Roles, UserClaims, UserLogins, UserRoles, UserTokens, RoleClaims.
- `__EFMigrationsHistory` contains only `20261007095934_InitialPersistence` (ProductVersion 10.0.12).
- `RESTORE VERIFYONLY ... WITH CHECKSUM` reports: "The backup set on file 1 is valid."

This satisfies the live preflight gate for the previously approved `AddIdentityPersistence` migration. The migration has NOT yet been applied. Before issuing the update command, confirm the exact local migration files/name are present and inspect working-tree status. Then apply only `AddIdentityPersistence` to `GHOST2`; do not run unrelated migrations or destructive cleanup. After successful application, verify migration history, all seven Identity tables, the Shifts-to-Users FK, and unchanged business-table row counts. Preserve the scratch restore database and backup.


## AUTH-01 migration readiness confirmed; awaiting apply (2026-11-17)

After the live preflight passed, the user ran `dotnet ef dbcontext info` with `GHOSTDbContext`. Observed output confirms:
- Context: `GHOST.Infrastructure.Persistence.GHOSTDbContext`
- Provider: `Microsoft.EntityFrameworkCore.SqlServer`
- Database: `GHOST2`
- Data source: `localhost`
- Build succeeded.

The user then ran `dotnet ef migrations list` using the Infrastructure project, API startup project, and `GHOSTDbContext`. EF successfully connected and read `__EFMigrationsHistory`. Exact result:
- `20261007095934_InitialPersistence` — applied
- `20261114053406_AddIdentityPersistence` — Pending

Therefore the exact approved AUTH-01 migration is present and pending against the intended original `GHOST2` database. Preflight and backup validation have passed; no migration has been applied yet.

### Next action (not yet executed)

The next step is to apply only the pending migration using:

```powershell
dotnet ef database update 20261114053406_AddIdentityPersistence --project .\backend\src\GHOST.Infrastructure\GHOST.Infrastructure.csproj --startup-project .\backend\src\GHOST.API\GHOST.API.csproj --context GHOSTDbContext
```

The user has already explicitly approved this migration. Ask them to run the command and provide the complete output. Do not claim success until the output confirms it. Then perform read-only verification of:
1. `__EFMigrationsHistory` contains `20261114053406_AddIdentityPersistence`.
2. All seven Identity tables exist: Roles, Users, RoleClaims, UserClaims, UserLogins, UserRoles, UserTokens.
3. FK `FK_Shifts_Users_StaffUserId` exists with expected relationship.
4. Original business row counts remain BusinessDays=1, Devices=1, DeviceRates=2, Shifts=0, Sessions=0, SessionSegments=0, SessionPauses=0.

Do not apply any other migration, do not drop the scratch restore database, and do not remove the backup. The NU1902/NU1903 warnings for `System.Security.Cryptography.Pkcs 9.0.11` were present during successful EF build/connection and remain a separate dependency-security task.


## AUTH-01 migration applied successfully; post-apply verification pending (2026-11-17)

The user executed the exact approved command against the confirmed `GHOST2` database:

```powershell
dotnet ef database update 20261114053406_AddIdentityPersistence --project .\backend\src\GHOST.Infrastructure\GHOST.Infrastructure.csproj --startup-project .\backend\src\GHOST.API\GHOST.API.csproj --context GHOSTDbContext
```

Observed output confirms:
- Build succeeded.
- EF acquired the migration application lock.
- EF applied migration `20261114053406_AddIdentityPersistence`.
- Created the seven Identity tables: Roles, Users, RoleClaims, UserClaims, UserLogins, UserRoles, UserTokens.
- Created their indexes.
- Added `FK_Shifts_Users_StaffUserId` from `Shifts.StaffUserId` to `Users.Id` with `ON DELETE NO ACTION`.
- Inserted `20261114053406_AddIdentityPersistence` into `__EFMigrationsHistory` with ProductVersion `10.0.12`.
- EF ended with `Done.`

This is evidence that the migration command completed successfully. However, a separate read-only post-apply verification has NOT yet been run. Do not yet mark the data-preservation verification complete.

### Immediate next step

Ask the user to pull this STATE update, then run the read-only SQLCMD verification against `GHOST2`:
- Confirm the migration history row.
- Confirm all seven Identity tables exist.
- Confirm FK `FK_Shifts_Users_StaffUserId` maps `Shifts.StaffUserId` to `Users.Id`.
- Confirm business row counts remain BusinessDays=1, Devices=1, DeviceRates=2, Shifts=0, Sessions=0, SessionSegments=0, SessionPauses=0.

Keep the pre-migration backup and scratch restore database. The NU1902/NU1903 warnings for `System.Security.Cryptography.Pkcs 9.0.11` remain a separate dependency-security issue. No Admin user has been bootstrapped and authentication/login flows are not yet implemented merely by adding Identity persistence tables.


## AUTH-01 database persistence verified complete (2026-11-17)

The user pulled the prior STATE update and ran the read-only post-migration SQLCMD verification against `GHOST2`. Observed output confirms all checks passed:

1. `__EFMigrationsHistory` contains:
   - `20261007095934_InitialPersistence` (ProductVersion 10.0.12)
   - `20261114053406_AddIdentityPersistence` (ProductVersion 10.0.12)
2. All seven Identity tables exist: Users, Roles, UserClaims, UserLogins, UserRoles, UserTokens, RoleClaims.
3. FK `FK_Shifts_Users_StaffUserId` exists: `dbo.Shifts.StaffUserId` references `dbo.Users.Id`, delete action `NO_ACTION`.
4. Original business table counts are unchanged from the pre-migration baseline:
   - BusinessDays=1
   - Devices=1
   - DeviceRates=2
   - Shifts=0
   - Sessions=0
   - SessionSegments=0
   - SessionPauses=0

AUTH-01 (Identity persistence schema migration) is now applied and post-apply verified on the original `GHOST2` SQL Server database. The pre-migration backup and scratch restore database remain preserved; do not remove either without explicit approval.

### Important boundary / next phase

This completes database persistence only. It does NOT mean an Admin account exists, Identity services are registered/configured, login/JWT/refresh-token flows work, or endpoint/resource authorization is implemented.

Next phase is AUTH-02: inspect the current branch's actual Identity user/role models, `Program.cs` DI/auth pipeline, configuration/secrets handling, and existing endpoint authorization before implementing Identity runtime services and a safe first-Admin bootstrap. Re-fetch current repository files and review current `docs/STATE.md` before starting. Do not seed a default password, expose credentials in source/control output, or trust client-supplied `StaffUserId`; authenticated principal must become the source of staff identity. Keep the existing Shifts FK and consider the pre-existing BusinessDays/Devices/rates records in all test and bootstrap decisions. Run tests/builds only with observed output; do not mark AUTH-02 complete until integration/security tests demonstrate it.

Separate outstanding issue: NU1902/NU1903 warnings for `System.Security.Cryptography.Pkcs 9.0.11` remain a dependency-security task and should be assessed/upgraded independently.


## AUTH-02 source audit and implementation planning (2026-11-17)

Fresh source review performed on branch `feature/products-inventory-billing` after AUTH-01 persistence was verified.

### Reviewed authoritative project decisions
- Read `docs/AUTHENTICATION-DECISIONS.md` (approved 2026-11-12) and relevant security/Identity portions of `docs/GHOST — SRS + ERD v0.2.md`.
- Approved role names are exactly `Admin` and `Staff`; no public self-registration.
- First Admin must be provisioned by a one-time secure server-side command, never an HTTP endpoint, with no hardcoded/default password. It must refuse if an Admin already exists.
- Password storage must use ASP.NET Core Identity hashing/policy/lockout. Audit must never include passwords, raw tokens, or temporary credentials.
- Authentication decisions prescribe implementation order: AUTH-02 bootstrap/recovery; AUTH-03 login/JWT/refresh; AUTH-04 password lifecycle/staff management; AUTH-05 claims-backed current user and authorization; AUTH-06 bind shifts/sessions to authenticated user; AUTH-07 SQL Server integration/security tests.
- The first Admin bootstrap and all future auth flows require audit events. Current SRS includes AuditLogs, but the currently applied AUTH-01 migration created only Identity tables; no AuditLogs table has been added yet. Decide and implement the durable security-audit persistence as a reviewed schema slice before claiming audit compliance.

### Current source facts confirmed
- `backend/src/GHOST.Infrastructure/Identity/ApplicationUser.cs` exists and derives from `IdentityUser<Guid>`. It adds FullName, IsActive, MustChangePassword, CreatedAt, UpdatedAt, CreatedByAdminId. Constructor generates Guid Id and security/concurrency stamps.
- No custom `ApplicationRole` was found in the Identity directory; current context uses framework `IdentityRole<Guid>`. Verify role key initialization and normalized role uniqueness before seeding Admin/Staff; do not assume role IDs are generated automatically.
- `GHOSTDbContext` derives from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, maps standard Identity entities to Users/Roles/UserRoles/UserClaims/UserLogins/RoleClaims/UserTokens, and retains business entities. AUTH-01 schema migration is already applied and verified on the original GHOST2 database.
- `backend/src/GHOST.API/Program.cs` currently registers controllers, CORS, SQL Server DbContext, business repositories and handlers. It does NOT register ASP.NET Core Identity services, authentication schemes/JWT bearer validation, authorization policies, or authentication/authorization middleware.
- Current API controllers (`BusinessDaysController`, `SessionsController`, `DevicesController`) have no `[Authorize]` attributes. BusinessDaysController accepts caller-supplied `StaffUserId`; SessionsController accepts caller-supplied `ShiftId` and `EndShiftId`. These remain untrusted and must not be used as actor identity.
- `appsettings.json` contains a local Windows trusted-connection SQL Server connection string to GHOST2. No JWT issuer/audience/signing configuration was found in the inspected appsettings files. Production secrets must be supplied through secure environment/server configuration and never committed.
- `GHOST.Tests.csproj` references Application only; no Infrastructure/SQL Server integration-test setup is currently configured. Existing tests are unit/fake based.
- The API project targets .NET 10; Infrastructure references EF Core, SQL Server, Identity EF Core 10.0.12. Existing NU1902/NU1903 warnings for System.Security.Cryptography.Pkcs 9.0.11 remain a separate dependency-security issue.

### AUTH-02 design gates before implementation
1. Confirm whether to introduce a custom `ApplicationRole` to guarantee non-empty Guid IDs and centralize allowed role names, or explicitly construct `IdentityRole<Guid>` with `Guid.NewGuid()` in the bootstrap. Verify actual Identity behavior; do not rely on assumption.
2. Design the bootstrap as a one-time server-side CLI/command path with interactive secret input (password must not be passed as a command-line argument, echoed, logged, or committed). Require explicit operator intent and fail closed if any Admin already exists.
3. Protect against two simultaneous bootstrap attempts using a database transaction and an appropriate serialization/locking strategy plus database uniqueness; a pre-check alone is not sufficient.
4. Use Identity RoleManager/UserManager and the configured password hasher/policy; create only Admin and Staff roles with normalized names; ensure role assignment and user creation are atomic or compensating safely.
5. Persist a durable append-only security audit event for bootstrap without storing secrets. Review whether a dedicated SecurityAuditEvents table is preferable to prematurely implementing the full general AuditLogs domain.
6. Keep bootstrap out of HTTP endpoints. Do not create default credentials or seed an Admin automatically at API startup.
7. Add tests for first bootstrap, second-run refusal, concurrent bootstrap race, invalid password, and absence of secrets in output/logs. SQL Server behavior requires integration tests, not only fake repositories.
8. Only after source/design review should code and any required migration be proposed. Do not modify the existing AUTH-01 migration or rewrite applied migrations.

### Immediate next action
Complete the AUTH-02 design review, including a second independent security review if available, then implement the smallest tested bootstrap slice on a dedicated feature branch or reviewed PR. Before implementation, inspect the exact current role/user EF model and available test infrastructure. After every meaningful implementation/test result, update this STATE file with observed evidence. AUTH-02 is NOT implemented; no Admin user exists; login/JWT/refresh and endpoint authorization remain unimplemented.


## AUTH-02 audit correction: AUTH-01 migration source is not committed (2026-11-17)

A deeper review of the actual GitHub branch tree uncovered a release-blocking repository consistency issue that must be resolved before AUTH-02 implementation:

- The original `GHOST2` SQL Server database has AUTH-01 applied and post-apply verified (as documented above).
- However, the current GitHub branch tree `feature/products-inventory-billing` does NOT contain `20261114053406_AddIdentityPersistence.cs` or its `.Designer.cs`.
- The remote `GHOSTDbContextModelSnapshot.cs` also does not include the Identity entity metadata (it contains only the business entities).
- This matches the user's earlier local `git status --short`, which showed the migration and designer as untracked and the snapshot modified. The user has not yet supplied evidence of committing/pushing these files.
- Consequence: the live local database schema is ahead of the migration history represented in source control. A fresh clone/CI/other environment cannot reliably reproduce the current schema, and EF migration discovery from the branch will not include AUTH-01. This is a blocker; do not start AUTH-02 or generate later migrations until reconciled.

### Immediate required action: preserve AUTH-01 migration source in Git

On the user's local working tree, inspect and stage ONLY these three source files:
- `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.cs`
- `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.Designer.cs`
- `backend/src/GHOST.Infrastructure/Persistence/Migrations/GHOSTDbContextModelSnapshot.cs`

Do NOT stage the entire `artifacts/` directory or `artifacts/AUTH-01.sql` unless separately reviewed and deliberately decided. Do not regenerate the migration or modify the already-applied migration. Then review the staged diff and commit/push these three files to `feature/products-inventory-billing`. After push, fetch the exact three files and snapshot from GitHub and confirm the branch tree contains them before resuming AUTH-02.

### Additional AUTH-02 audit corrections
- `GHOST.Application.Abstractions.CurrentUser.ICurrentUser` already exists with `UserId`, `IsAuthenticated`, `IsAdmin`, and `IsStaff`. The previous broad statement that no current-user abstraction exists would be inaccurate. No implementation or DI registration was found in the reviewed API/Program.cs and Infrastructure Identity directory; confirm repository-wide usage before implementing.
- `ShiftConfiguration` already maps `Shift.StaffUserId` to `ApplicationUser` with Restrict deletion; the FK was verified live after AUTH-01.
- SRS v0.2 explicitly defines a general `AuditLogs` table (Id, UserId, Action, EntityType, EntityId, OldValues, NewValues, At) and states audit records must not be updated/deleted. Prefer designing the general append-only AuditLog to serve bootstrap and future operational/security events, with a carefully modelled nullable/system actor for bootstrap before the first Admin exists, rather than introducing a duplicate one-off security-audit table without product approval.
- Current controllers still have no authorization attributes/middleware; `BusinessDaysController` accepts `StaffUserId` and `SessionsController` accepts `ShiftId`/`EndShiftId` from request bodies. Keep these as explicit AUTH-05/AUTH-06 security blockers; do not expose the API to untrusted networks before server-side authorization is implemented.

AUTH-02 remains unimplemented. Next step is source-control reconciliation of the already-applied AUTH-01 migration, not bootstrap coding.


## AUTH-01 source-control reconciliation verified (2026-11-17)

The user committed and pushed the already-applied AUTH-01 migration source. The push output was supplied in chat, then the exact remote branch and files were independently fetched from GitHub.

### Observed Git evidence
- Commit: `fc831cca0c51efef623919e289e78912b96d9655`
- Commit message: `feat(auth): add identity persistence migration`
- Branch: `feature/products-inventory-billing`
- Fetched branch HEAD: `fc831cca0c51efef623919e289e78912b96d9655`
- Commit changes exactly these three files:
  - Added `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.cs`
  - Added `backend/src/GHOST.Infrastructure/Persistence/Migrations/20261114053406_AddIdentityPersistence.Designer.cs`
  - Updated `backend/src/GHOST.Infrastructure/Persistence/Migrations/GHOSTDbContextModelSnapshot.cs`
- Each of the three exact paths was fetched successfully from the remote branch.
- The migration source contains the seven expected Identity table create/drop operations: Roles, Users, RoleClaims, UserClaims, UserLogins, UserRoles, UserTokens, and the FK operation. The Designer and Snapshot both contain mappings for all seven tables.
- The user's commit output reports exactly 3 files changed, 1194 insertions(+), and the push advanced the remote branch from `67023ac` to `fc831cc`.
- The previously reported untracked `artifacts/` directory was not part of this commit; GitHub commit metadata lists only the three intended migration source files.

AUTH-01 is now reconciled across the live GHOST2 database (migration applied and post-apply verified in the earlier section) and the GitHub source branch (migration, designer, and Identity model snapshot fetched and confirmed). This closes the source-control blocker. No new migration was generated or applied during this reconciliation.

### Next phase: AUTH-02 design review — not yet implemented
Resume AUTH-02 planning, not coding blindly. Re-fetch and inspect the exact current branch files before any implementation, with particular attention to:
1. `ApplicationUser`, role key generation/normalization, `GHOSTDbContext`, and Identity EF model configuration.
2. Existing test projects, package references, test seams, and SQL Server integration-test strategy.
3. General append-only `AuditLogs` design from SRS: actor nullability/system actor for first-Admin bootstrap, payload constraints, and database-level append-only enforcement.
4. Safe one-time CLI bootstrap: interactive password input (never CLI args or logs), explicit operator confirmation, refuse if Admin exists, atomic role/user/audit persistence, and a SQL Server concurrency strategy that prevents two simultaneous bootstrap attempts.
5. Confirm all repository usages of `ICurrentUser` before adding an implementation or DI registration.

Do not implement JWT/login, endpoint authorization, staff lifecycle, or shift/session identity binding as part of the bootstrap slice; those belong to later AUTH phases. Do not modify the already-applied AUTH-01 migration. Any additional schema change for AuditLogs must be proposed as a separate reviewed migration and must not be applied without explicit user approval.

### Immediate next action
Perform a fresh AUTH-02 source/design audit on the now-reconciled branch. Inspect the exact role/user EF model and available test infrastructure, and produce a concrete threat model and minimal implementation/test plan for first-Admin bootstrap plus the SRS-compliant AuditLog. Do not start implementation or create/apply a migration until the design and schema impact are reviewed. AUTH-02 remains NOT implemented; no Admin account has been confirmed as provisioned; login/JWT/refresh and endpoint authorization remain outstanding.


## AUTH-02 fresh source/design audit after AUTH-01 reconciliation (2026-11-17)

Fresh files and repository tree were fetched from branch `feature/products-inventory-billing` at branch HEAD `2fd5b7a94a90f5710cd47783e689770ad08b8747`.

### Confirmed source facts
- `ApplicationUser` derives from `IdentityUser<Guid>`; its constructor explicitly generates Guid Id, SecurityStamp, and ConcurrencyStamp. It has FullName, IsActive, MustChangePassword, CreatedAt, UpdatedAt, CreatedByAdminId.
- `GHOSTDbContext` derives from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`; all seven Identity entities are mapped to the custom table names already present in the verified AUTH-01 migration.
- The role type is framework `IdentityRole<Guid>`; no custom role type/entity was found in the repository tree. For bootstrap, explicitly assign a non-empty `Guid.NewGuid()` to each role ID and explicitly set Name/NormalizedName; do not depend on implicit key generation.
- `Program.cs` currently registers DbContext and business handlers/repositories only. It does not register Identity Core managers, authentication, authorization, or middleware. Keep this bootstrap slice separate from login/JWT and endpoint authorization.
- `ICurrentUser` exists with UserId/IsAuthenticated/IsAdmin/IsStaff. GitHub code search for repository usages returned zero results; no implementation or DI registration was found in the inspected source. Recheck with a local repository-wide search before adding any implementation, but do not pull AUTH-05 work into AUTH-02.
- `GHOST.Tests.csproj` currently references only `GHOST.Application` and has xUnit/Test SDK/coverlet. It does not reference Infrastructure, API, EF SQL Server test setup, or Testcontainers. The tree contains unit test files only; no SQL Server integration-test project or AuditLog entity/configuration was found.
- Infrastructure already references EF Core SQL Server 10.0.12 and ASP.NET Core Identity EF Core 10.0.12. API references Infrastructure. No new runtime packages are inherently required for a small CLI bootstrap host, though project shape/configuration still needs implementation design.
- Existing `ShiftConfiguration` confirms required FK to `ApplicationUser` with Restrict deletion and a SQL Server rowversion; the existing applied FK remains important historical attribution.
- SRS defines `AuditLogs(Id, UserId, Action, EntityType, EntityId, OldValues, NewValues, At)`, requires append-only behavior (no UPDATE/DELETE), and says enforcement should be at DB permissions or triggers. No AuditLog entity/table/configuration exists in the current repository/migration tree.

### Security/design assessment (proposal, not implementation)
1. Keep bootstrap in a dedicated server-side CLI project/command, not an API route and not API startup seeding. Password must be read interactively without echo and must never be supplied as a process argument, written to logs, or persisted except as the Identity hash. Require explicit operator confirmation and password confirmation.
2. Use Identity `RoleManager<IdentityRole<Guid>>` and `UserManager<ApplicationUser>` with explicit Identity options (password policy, unique username, lockout configuration where relevant). Create exactly the approved roles `Admin` and `Staff`; assign explicit Guid IDs and normalized names. Create the first Admin only; no Staff account is created by bootstrap.
3. Serialize bootstrap attempts in SQL Server using a transaction-scoped exclusive `sp_getapplock` resource dedicated to the GHOST first-admin bootstrap. Acquire the lock inside the same DB transaction before checking for an existing Admin. Then create/ensure roles, create user, assign Admin, persist audit event, and commit as one unit. A second invocation must fail closed after it acquires the lock and observes an Admin. A pre-check without serialization is not acceptable. Verify exact lock return codes and rollback behavior in real SQL Server integration tests.
4. Audit must be committed in the same transaction as bootstrap. Proposed general AuditLogs design: nullable `UserId` for a pre-user system actor, explicit actor-kind/system marker (so null does not ambiguously mean missing data), Action/EntityType/EntityId, bounded JSON OldValues/NewValues, UTC timestamp, and no secrets/credential material. For the bootstrap event, actor is System and target EntityType/EntityId identifies the created Admin; never include password, password hash, security stamp, token, or temporary credential.
5. AuditLogs is a new schema object absent from AUTH-01 and the live verified migration. It requires a separate additive migration, a reviewed FK/nullability/length/index design, and DB-level append-only enforcement (prefer explicit database permissions for the app principal or tested triggers; ordinary EF code conventions alone are insufficient). Do not generate/apply this migration until design approval and explicit user approval for the DB change.
6. Add a dedicated SQL Server integration-test project or equivalent isolated harness. Use a disposable test database, never the user's live `GHOST2`. Required tests: successful first bootstrap; second-run refusal; two concurrent attempts produce exactly one Admin; invalid password leaves no partial user/role/audit state; injected failure after user creation rolls back all changes; audit row committed with no secrets; password input never appears in captured output/logs. Existing unit tests alone cannot prove SQL Server application-lock and transaction behavior.
7. Recovery is separately approved by AUTHENTICATION-DECISIONS: implement as a separate server-side command, not as a bootstrap bypass. Define recovery proof/operator authorization and audit semantics before coding it; do not silently bundle it into first bootstrap.

### Explicit boundaries
- No code or project files changed in this audit.
- No migration generated or applied; no database queried or mutated.
- No Admin account existence check was run against live GHOST2 in this audit.
- A second independent reviewer was not available in this session; the security review above is the current single-review threat assessment, not a claim of independent approval.
- Existing dependency advisories for `System.Security.Cryptography.Pkcs 9.0.11` remain a separate task.

### Immediate next action
Review and approve/revise the proposed AUTH-02 design, especially the AuditLogs system-actor schema and append-only enforcement, and approve a separate additive AuditLogs migration before implementation. After design approval, implement only the dedicated CLI bootstrap + atomic audit persistence and SQL Server integration tests on a dedicated branch/PR. Do not add JWT/login, HTTP authorization, or shift/session actor binding in this slice. AUTH-02 remains NOT implemented.


## AUTH-02 implementation design approved (2026-11-17)

The user authorized proceeding with the assistant's recommended security design. The detailed implementation design is committed at `docs/AUTH-02-IMPLEMENTATION-DESIGN.md`.

### Approved decisions
- Dedicated server-side console bootstrap; no HTTP endpoint, API startup seed, default password, or password command-line argument.
- Reuse ASP.NET Core Identity EF Core already referenced by Infrastructure. Keep existing `ApplicationUser`, `IdentityRole<Guid>`, `GHOSTDbContext`, and all AUTH-01 mappings.
- Create the approved `Admin` and `Staff` roles and only the first Admin account during bootstrap.
- Add general SRS-aligned `AuditLogs` with nullable UserId and explicit ActorType System/User, bounded JSON payloads, UTC timestamp, entity/actor indexes, and a restricted FK to Users.
- Enforce append-only at SQL Server level with an UPDATE/DELETE-rejecting trigger; additionally configure least-privilege permissions for the actual runtime database principal before production.
- Commit roles, user, role assignment, and sanitized bootstrap audit event in one EF transaction.
- Serialize concurrent bootstrap attempts with transaction-owned exclusive `sys.sp_getapplock` before checking for an existing Admin. Negative lock return codes fail closed.
- Keep recovery separate. Do not include login/JWT/refresh, HTTP authorization, staff lifecycle, ICurrentUser, or shift/session actor binding in AUTH-02.

### Implementation safety gates
- Generate the additive AuditLogs migration locally with EF Core 10.0.12 tooling. Do not hand-author the model snapshot/designer.
- Review migration Up/Down and SQL to prove it only adds AuditLogs, constraints, indexes, and the append-only trigger; do not modify AUTH-01 or existing business tables.
- SQL Server integration tests must use a disposable database and explicitly reject the live database name `GHOST2`.
- Required tests include first/second bootstrap, concurrent race (exactly one success), invalid password/rollback, injected failure rollback, SQL constraints/trigger, lock failures, and secret-leak checks.
- No live GHOST2 migration application or bootstrap run is authorized by this general implementation approval. A separate explicit approval is required after migration review and backup/restore plan.
- No live Admin-existence check has been run; do not claim an Admin is or is not present.

### Observed documentation commits
- Design document: `d67980ff464ca569478b84775215999e283b3419`
- Branch: `feature/products-inventory-billing`

### Immediate next action
Implement the AuditLog entity and EF configuration, generate and inspect the additive migration locally (including SQL Server trigger), then build the CLI bootstrap on top of the reviewed persistence model. Add SQL Server integration tests against a disposable database. Do not apply the migration to live GHOST2.


## AUTH-02 implementation — AuditLog model foundation (2026-11-17)

### Changes committed and re-fetched from GitHub
- `backend/src/GHOST.Domain/Entities/AuditLog.cs`
  - Creation-only entity with private constructor/setters and static factory.
  - Enforces System/User actor consistency, non-empty required action/entity type, bounded entity ID, 16 KiB UTF-16 JSON payload size, generated Guid, and UTC timestamp.
- `backend/src/GHOST.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`
  - Maps dbo.AuditLogs.
  - Adds actor-kind and actor/user consistency check constraints.
  - Adds SQL Server ISJSON + 16 KiB checks for old/new payloads.
  - Adds restricted FK to ApplicationUser.
  - Adds entity-history and actor-history indexes.
- `backend/src/GHOST.Infrastructure/Persistence/GHOSTDbContext.cs`
  - Registers DbSet<AuditLog>; existing Identity/business mappings remain present.
- Detailed approved design remains in `docs/AUTH-02-IMPLEMENTATION-DESIGN.md`.

### Observed Git evidence
- Design document commit: `d67980ff464ca569478b84775215999e283b3419`
- AuditLog entity commit: `ae7a3cbce661da078c32cd6de6dea9c721cc1b15`
- AuditLog configuration commit: `15efc7a7cdabf66c4bce17faa81fc06edb6c4c8c`
- DbContext registration / current branch HEAD: `eca7be9423372d659ab338251a55db283877b5df`
- All four changed/new files were independently fetched from the branch after commit.

### Not yet complete / safety notes
- No build or tests were run: this environment only provided GitHub source access, not the user's local .NET runtime or SQL Server.
- The EF model snapshot has intentionally not been hand-edited. It is now expected to differ from the model until a local EF Core migration is generated.
- No migration source or SQL Server trigger exists yet. The next local step is to generate the additive migration with EF tooling, then add/review the SQL Server append-only trigger in that migration.
- No database was queried or changed. No migration was applied. Live GHOST2 remains untouched.
- The bootstrap CLI, transaction-scoped application lock, Identity DI registration in the CLI host, and SQL Server integration tests are not implemented yet.
- Do not run API migrations or expose the API while the model/snapshot are temporarily out of sync. Generate and review the migration before considering this persistence slice ready.

### Immediate next action
Synchronize the local branch to `eca7be9423372d659ab338251a55db283877b5df`, generate the AuditLogs migration using the installed EF Core 10.0.12 tooling, and share the generated migration output/files for review. Do not apply it to GHOST2. After reviewing generated Up/Down operations, add the append-only trigger and proceed to the CLI bootstrap and disposable SQL Server tests.


## AUTH-02 AuditLogs migration generated and reviewed (2026-11-17)

### Generated migration
- Local EF Core CLI 10.0.12 generated `20261117213727_AddAuditLogs.cs`, its Designer, and updated `GHOSTDbContextModelSnapshot.cs`.
- User committed and pushed the three generated files in commit `d57c349` on `feature/products-inventory-billing`.
- Source review confirmed the migration is additive: it creates only `dbo.AuditLogs`, its primary key, actor/JSON check constraints, restricted FK to `Users`, and the two intended indexes. The generated `Down()` drops only `AuditLogs`.
- Snapshot and Designer describe the same AuditLog model and FK. No existing business or Identity table changes appeared in the reviewed migration diff.
- `git diff --check` returned no output on the user's local checkout.

### Append-only enforcement added in review branch
- Add SQL Server trigger `dbo.TR_AuditLogs_AppendOnly` after the AuditLogs indexes. It rejects UPDATE and DELETE with SQL error 51000; INSERT remains permitted.
- Drop the trigger before dropping AuditLogs in `Down()`.
- Trigger protection is defense in depth, not a substitute for a least-privilege runtime SQL principal. A principal with schema/DDL or db_owner rights can alter or remove the trigger; runtime grants still need a separately reviewed deployment plan.

### Current verification and safety status
- Migration source and snapshot were reviewed; trigger source is added in a separate review branch/PR.
- No local build or SQL Server integration test was run by the assistant. User's earlier EF migration generation reported a successful build, but that does not test trigger execution.
- No database was queried or modified by this review. Do not apply this migration to live `GHOST2`; separate explicit approval and backup/restore plan remain required.
- The CLI bootstrap, transaction-owned `sp_getapplock`, atomic role/user/audit flow, and disposable SQL Server integration tests remain outstanding.
- Existing NU1902/NU1903 advisories for `System.Security.Cryptography.Pkcs 9.0.11` remain a separate dependency-remediation task.

### Immediate next action
Review the append-only trigger PR, then implement the dedicated bootstrap CLI and isolated SQL Server integration tests. Keep the live `GHOST2` database untouched until a separate migration-application approval.


## AUTH-02 — Bootstrap CLI implementation started (2026-11-17)

- Added dedicated `backend/src/GHOST.Bootstrap` .NET 10 console project and registered it in `backend/GHOST.slnx`.
- The CLI requires an explicit `GHOST_CONNECTION_STRING`; it does not inherit the API's development `GHOST2` connection string and does not run migrations.
- Requires an interactive terminal, explicit `BOOTSTRAP` confirmation, username/full name, and hidden password entry twice. Password is not accepted through command-line arguments, environment variables, or configuration.
- Uses existing ASP.NET Core Identity stores and the same scoped `GHOSTDbContext`; password policy is length 12, four unique characters, uppercase/lowercase/digit/non-alphanumeric; new account is active and marked `MustChangePassword`.
- Uses a transaction-owned exclusive `sp_getapplock` before checking Admin membership. Only nonnegative lock return codes are accepted.
- Creates only `Admin` and `Staff` roles, one Admin, and a sanitized `Admin.Bootstrapped` AuditLog row in one EF transaction. No Staff account is created.
- Added CLI README with invocation, safety constraints, and exit codes.
- Not yet built or executed. No database was connected to, queried, migrated, or modified. Do not run against live `GHOST2`; disposable SQL Server integration tests and local build review remain required.
- Review notes before build: verify the console host's Identity DI registrations, SQL Server app-lock command behavior, and the EF transaction-sharing behavior on the local SQL Server test instance. The current code does not attempt migrations.

- Hardening refinement: CLI now parses and displays the SQL Server/data source and requires the exact database-specific confirmation `BOOTSTRAP <database-name>`; it rejects connection strings without an explicit Initial Catalog. This reduces accidental execution against the wrong database.
- The CLI has not been compiled or run in this environment. Immutable .NET password strings cannot be reliably zeroed; the tool avoids writing them to logs, arguments, environment variables, configuration, and audit payloads.


## AUTH-02 — Local solution build verified (2026-11-17)

- User ran `dotnet build .\\backend\\GHOST.slnx --configuration Release` from the repository root.
- Result: **Build succeeded**, all five projects compiled (Domain, Application, Infrastructure, Tests, API) with **0 errors**.
- Restore/build reported **8 warnings**: NU1902 (moderate) and NU1903 (high) for transitive package `System.Security.Cryptography.Pkcs 9.0.11` in Infrastructure and API. These advisories remain unresolved and must be traced to the dependency chain and remediated with a compatible patched version; do not suppress the warnings.
- This build confirms the solution as currently configured compiles, but it does not verify runtime bootstrap behavior, SQL Server app-lock semantics, transaction rollback, or the AuditLogs trigger.
- No database connection, migration application, or bootstrap execution was performed as part of this build.
- Next: add isolated SQL Server integration coverage for AuditLogs constraints/trigger and bootstrap atomicity/concurrency. Before that, inspect and remediate the vulnerable transitive package version, then rebuild and run the existing test suite.


## AUTH-02 — Vulnerability advisory triage (2026-11-17)

- User ran `dotnet list .\\backend\\GHOST.slnx package --vulnerable --include-transitive`. It confirms `System.Security.Cryptography.Pkcs 9.0.11` is transitive in `GHOST.API` and `GHOST.Infrastructure`; Application, Domain, and Tests report no vulnerable packages.
- The report does not identify the parent dependency. Do not add a direct package override until the dependency path is known.
- Reviewed GitHub-reviewed advisories GHSA-846f-f764-76mx (High, CVE-2026-84008) and GHSA-2fmv-rv2c-59x7 (Moderate, CVE-2026-70162). Both list `System.Security.Cryptography.Pkcs 9.0.21` as patched for the 9.x line; 10.x is patched at 10.0.13. Preserve the dependency's intended major line; do not upgrade major versions without evidence.
- API directly references Microsoft.AspNetCore.OpenApi 10.0.12 and EF Design 10.0.12. Infrastructure directly references EF Core/SqlServer/Identity.EntityFrameworkCore 10.0.12. Neither project directly references Pkcs.
- Verified branch: `feature/auth-02-auditlog-append-only`; HEAD at review time: `20b34c3acd052c6f62a863e73bfb423bbe07c361` (`Record successful Release solution build and dependency advisories`).
- Next action: run `dotnet nuget why` for `System.Security.Cryptography.Pkcs` against both API and Infrastructure project files to identify the exact parent chain. Then choose the smallest compatible patched update, rebuild, rerun vulnerability scan and full tests. No dependency changes have been made.
- Advisories: https://github.com/advisories/GHSA-846f-f764-76mx and https://github.com/advisories/GHSA-2fmv-rv2c-59x7.


## AUTH-02 — Transitive SQL client vulnerability traced and dependency update prepared (2026-11-17)

### Dependency path confirmed by user
```text
GHOST.API
└── GHOST.Infrastructure
    └── Microsoft.EntityFrameworkCore.SqlServer 10.0.12
        └── Microsoft.Data.SqlClient 6.1.6
            └── System.Security.Cryptography.Pkcs 9.0.11
```

### Package metadata review
- NuGet metadata for `Microsoft.EntityFrameworkCore.SqlServer 10.0.15` (net10.0) confirms dependencies on `Microsoft.Data.SqlClient >= 6.1.7` and `System.Security.Cryptography.Pkcs >= 10.0.13`. Pkcs 10.0.13 is the patched .NET 10 line for both reported advisories.
- `Microsoft.EntityFrameworkCore 10.0.15`, `Microsoft.EntityFrameworkCore.Design 10.0.15`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.15`, and `Microsoft.AspNetCore.OpenApi 10.0.15` are available stable .NET 10 packages. Keep the EF package family aligned rather than mixing patch versions.
- No direct `Microsoft.Data.SqlClient` or `System.Security.Cryptography.Pkcs` PackageReference override is added. The SQL Server EF provider now supplies the patched Pkcs dependency through its own declared dependency graph.
- Advisory evidence: GHSA-846f-f764-76mx and GHSA-2fmv-rv2c-59x7; package metadata: NuGet Microsoft.EntityFrameworkCore.SqlServer 10.0.15.

### Source changes committed to this branch (not locally validated yet)
- `backend/src/GHOST.Infrastructure/GHOST.Infrastructure.csproj`: all four EF/Identity package references updated from 10.0.12 to 10.0.15.
- `backend/src/GHOST.API/GHOST.API.csproj`: Microsoft.AspNetCore.OpenApi and EF Design updated from 10.0.12 to 10.0.15.
- Commits: Infrastructure `08b909df3e60c011b7f6bc79ff81ac48b2298eb2`; API `affd7cd6bd67fd83ba1c6d76f87dee98fda3276a`.
- No source API, domain model, migration, database, or bootstrap behavior was changed. No database was accessed.

### Important verification correction
- The current `backend/GHOST.slnx` source includes six projects, including `GHOST.Bootstrap`. The earlier build summary recorded five compiled projects and did not list Bootstrap; the full local build must explicitly confirm all six projects compile. Do not count Bootstrap as verified until the new build output shows it.

### Immediate next action
- Synchronize local branch to latest remote commit, then run: `dotnet restore .\\backend\\GHOST.slnx`; `dotnet list .\\backend\\GHOST.slnx package --vulnerable --include-transitive`; `dotnet build .\\backend\\GHOST.slnx --configuration Release`; `dotnet test .\\backend\\GHOST.slnx --configuration Release`.
- Confirm the resolved SQL Server dependency graph contains a patched `System.Security.Cryptography.Pkcs 10.0.13` or later and that vulnerability scan has no remaining findings. Capture all warnings and test results. No Bootstrap execution, migration application, or live GHOST2 database access.


## AUTH-02 — Local dependency remediation verification and branch recovery (2026-11-17)

### Local verification reported by user
- After syncing the AUTH-02 commits, the user ran `dotnet restore .\\backend\\GHOST.slnx`: succeeded.
- User ran `dotnet list .\\backend\\GHOST.slnx package --vulnerable --include-transitive`: all six projects (API, Application, Bootstrap, Domain, Infrastructure, Tests) reported no vulnerable packages from the configured NuGet sources.
- User ran `dotnet build .\\backend\\GHOST.slnx --configuration Release`: succeeded; all six projects compiled, including the previously unverified GHOST.Bootstrap project.
- User ran `dotnet test .\\backend\\GHOST.slnx --configuration Release`: 43 passed, 0 failed, 0 skipped.
- This verifies restore, vulnerability scan, Release compilation, and the existing unit-test suite on the user's local environment. The output did not print the exact resolved System.Security.Cryptography.Pkcs version; record the clean vulnerability scan, but do not infer a specific resolved version from it.
- No Bootstrap execution, SQL Server integration test, migration application, or database connection/change was performed. These remain separate verification gates.

### Local branch recovery confirmed
- A mistaken pull had fast-forwarded the local `feature/products-inventory-billing` branch to the AUTH-02 tip. The user safely created local `feature/auth-02-auditlog-append-only` tracking `origin/feature/auth-02-auditlog-append-only`, then restored the local products branch pointer to `origin/feature/products-inventory-billing`.
- User's latest `git status -sb`: `feature/products-inventory-billing...origin/feature/products-inventory-billing`, clean tracked working tree, with only untracked `artifacts/`.
- Local branch tips reported:
  - `feature/products-inventory-billing` = `d57c349`, tracking `origin/feature/products-inventory-billing`.
  - `feature/auth-02-auditlog-append-only` = `b785823`, tracking `origin/feature/auth-02-auditlog-append-only`.
- The untracked `artifacts/` directory was not deleted, staged, or committed. Keep it untouched unless the user explicitly asks to manage it.

### PR status at review time
- PR #3, `AUTH-02: AuditLog append-only enforcement and first Admin bootstrap`, was still open and draft when checked. GitHub reported `mergeable=true` at that moment; this is not approval to merge and does not establish that required checks or runtime gates passed.
- Do not mark the PR ready or merge it yet. Bootstrap runtime behavior, disposable SQL Server integration coverage, and migration/trigger execution remain unverified.

### Next action
- Continue on the AUTH-02 branch for any AUTH-02 source/STATE changes. Before local AUTH-02 work, switch to `feature/auth-02-auditlog-append-only` and confirm `git status -sb`; preserve the untracked `artifacts/` directory. Next engineering gate is isolated SQL Server integration testing for AuditLogs constraints/append-only trigger and bootstrap transaction/app-lock atomicity. Do not run against live `GHOST2` or apply the migration without separate explicit approval.


## AUTH-02 — Static source review before SQL Server integration tests (2026-11-17)

### Files reviewed at current AUTH-02 branch tip
- `backend/src/GHOST.Bootstrap/Program.cs` and its README/project file.
- `GHOSTDbContext`, Identity `ApplicationUser`, AuditLog entity/configuration, and `20261117213727_AddAuditLogs` migration.
- API `Program.cs` and the current `GHOST.Tests.csproj`.

### Source-level observations (not runtime verification)
- Bootstrap registers `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<GHOSTDbContext>()`, resolves `ILookupNormalizer`, and uses one scoped `GHOSTDbContext` for Identity stores and AuditLogs.
- Bootstrap begins a Serializable EF transaction, acquires `sp_getapplock` with `LockOwner = Transaction` using that transaction, then performs user creation, role membership, audit insert, and commit through the same scoped context. Negative app-lock return codes fail closed.
- CLI requires an explicit SQL Server Initial Catalog and exact database-specific confirmation; it does not run migrations. It refuses redirected input/output and reads the password without echoing it.
- AuditLog has application-level factory validation plus SQL check constraints for actor consistency, actor type, JSON validity, and 16 KiB payload limits. The migration adds an AFTER UPDATE, DELETE trigger that throws SQL error 51000.
- No obvious transaction/context mismatch was identified by static inspection. This is not proof of SQL Server runtime behavior, lock serialization, rollback atomicity, or trigger enforcement.

### Required integration-test work
- Current `GHOST.Tests.csproj` references only `GHOST.Application`; it has no Infrastructure project reference or SQL Server integration-test fixture.
- Bootstrap orchestration is currently embedded in top-level `Program.cs`, so it is not directly callable from tests. Extract the provisioning operation into a testable service while keeping the CLI as a thin interactive input/confirmation shell.
- Add SQL Server integration coverage for: migration creates AuditLogs and trigger; INSERT succeeds; UPDATE and DELETE fail with SQL error 51000; actor/JSON/size constraints reject invalid rows; first-admin creation persists user + roles + audit atomically; duplicate/concurrent bootstrap cannot create a second Admin; failures roll back all related rows.
- Integration tests must require an explicitly configured, dedicated disposable test database and fail closed if the catalog is not the designated test database. Never point tests at `GHOST2`. Applying migrations to the dedicated test database requires separate explicit operator approval.
- No source code or database was changed as part of this static review; no SQL Server integration tests were run.

### Immediate next action
Obtain explicit approval to provision/use a dedicated SQL Server test database (separate from `GHOST2`) before adding/running migration-backed integration tests. Keep PR #3 draft until those tests and runtime gates pass.


## AUTH-02 — Reconciliation of external review (2026-11-18)

### Branch and migration verification
- Reviewed the current GitHub contents of `feature/auth-02-auditlog-append-only` and `feature/products-inventory-billing`, not the older review snapshot at commit `156c988`.
- Both current branches contain `20261114053406_AddIdentityPersistence.cs` and its Designer file. The migration source blob is identical on both branches.
- The current `GHOSTDbContextModelSnapshot.cs` on both branches includes `GHOST.Infrastructure.Identity.ApplicationUser`, `IdentityRole<Guid>`, and the Identity tables (`Users`, `Roles`, `UserRoles`, claims, logins, and tokens).
- The AUTH-02 branch also contains `20261117213727_AddAuditLogs.cs`, its Designer, and a snapshot including AuditLogs. Therefore the review claim that AUTH-01 migration files are absent and the snapshot contains no Identity model is not true for the current branch tips inspected. Do not regenerate or duplicate AUTH-01 migration.
- The AUTH-01 migration creates Identity tables and the `Shifts.StaffUserId -> Users.Id` FK. The current AUTH-02 migration creates AuditLogs and its append-only trigger.

### External review items reconciled
- C1 (missing AUTH-01 migration/snapshot): not reproducible on current branch tips; likely based on an earlier commit/tree. Confirm local checkout is synchronized before any EF tooling.
- C2 (API endpoints are anonymous and trust request-supplied staff/shift IDs): valid scope/security gap. AUTH-02 bootstrap does not secure API endpoints; do not expose API beyond loopback/trusted local host until authentication/authorization work is completed in its planned slice.
- H1 (no SQL Server integration-test project/fixture): valid. Add migration-backed SQL Server integration tests; never point them at GHOST2.
- H4 (no audit table): not true on current AUTH-02 branch; AuditLogs entity/configuration/migration exist. The current bootstrap writes a sanitized `Admin.Bootstrapped` System event.
- H2, H5/M5, M6 and M7 are valid follow-up concerns for account lifecycle, role protection, deployment hardening, and device seeding; they are not reasons to broaden AUTH-02 into API authentication or unrelated device workflows.
- Current bootstrap source explicitly assigns a new Guid to IdentityRole, sets password policy (12 chars, four unique characters, upper/lower/digit/non-alphanumeric), and configures lockout (5 attempts / 15 minutes). These should still receive integration/unit coverage.
- The current CLI does not verify pending migrations before proceeding, does not impose a maximum password length, and embeds orchestration in top-level Program.cs. These are concrete AUTH-02 improvements before SQL integration tests.
- Current bootstrap uses a Serializable transaction plus transaction-owned SQL application lock (10-second timeout). Keep the application lock as the primary cross-process serialization mechanism; review whether Serializable is necessary after tests.

### Design direction for next implementation slice
- Keep the existing SRS-aligned append-only AuditLogs table for the bootstrap event; do not add a parallel SecurityAuditEvents table without a demonstrated requirement. Continue to store only allow-listed, non-secret metadata.
- Extract bootstrap orchestration from the CLI into a testable Infrastructure service while leaving interactive confirmation and hidden password input in the CLI. Keep the service out of API DI registration in AUTH-02.
- Add a migration preflight that refuses to run when pending migrations exist; never auto-apply migrations from the bootstrap command.
- Add a filtered unique database index protecting the single successful `Admin.Bootstrapped` event, subject to checking current AuditLogs indexes/collation.
- Build a dedicated SQL Server integration-test harness using only the explicitly designated `GHOST_AUTH02_IT` database. No GHOST2 connection, automatic database deletion, or cleanup of existing databases.
- User explicitly approved creating `GHOST_AUTH02_IT` and applying migrations to it. This approval does not authorize dropping/recreating it or creating additional databases without asking first.
- No database was connected to or modified during this review; no code was changed in this reconciliation.

### Immediate next action
Synchronize the local AUTH-02 branch and verify migration files/snapshot are present. Then implement the testable bootstrap service, migration preflight, and SQL Server integration-test harness before creating/applying migrations to the approved test database.


## AUTH-02 — Bootstrap implementation deep review and execution plan (2026-11-18)

### Current source baseline
- Reviewed current branch HEAD `3119778d211ec1186ba265717ab8bff6b023d637` (confirmed from GitHub branch metadata).
- User's local checkout is synchronized to the same commit; tracked worktree is clean and untracked `artifacts/` is intentionally preserved.
- Re-read the current Bootstrap CLI, project references, `GHOSTDbContext`, `AuditLogConfiguration`, `AuditLog`, `ApplicationUser`, and test project.

### Concrete source findings
- Bootstrap orchestration is entirely in top-level `GHOST.Bootstrap/Program.cs`; the Infrastructure project is already referenced by the CLI, but the test project currently references Application only.
- The CLI currently has no `Database.GetPendingMigrationsAsync` preflight. It prints that migrations are not run, but does not verify schema readiness before attempting Identity operations.
- `ReadSecret()` accumulates an unbounded `StringBuilder`; there is no 128-character maximum. Enforce a bounded read while continuing to avoid echo, args, environment variables, and logs.
- Identity failure paths currently include `IdentityError.Description` in exception messages. Replace this with stable error codes only; do not emit descriptions or raw exception messages to console.
- The service must preserve one scoped `GHOSTDbContext` for UserManager, RoleManager, and AuditLogs so the EF transaction covers all writes.
- The existing transaction-owned `sp_getapplock` is retained as the cross-process serialization mechanism. Its negative return codes are treated as failure. Integration tests must prove lock behavior on SQL Server.
- Existing `AuditLogs` index names and SQL collation must be inspected before adding a filtered unique index for the one successful `Admin.Bootstrapped` action. The filtered predicate must match the actual Action column semantics and SQL Server provider.
- `AuditLog.Create` generates UTC timestamps directly; `ApplicationUser.CreatedAt` also uses `DateTimeOffset.UtcNow`. Introducing a clock abstraction is a broader model/API decision; do not add it opportunistically unless needed for deterministic AUTH-02 tests and migration impact is understood.
- The current Admin-existence check uses `GetUsersInRoleAsync("Admin")`, which includes disabled users; preserve the rule that a disabled Admin still blocks first-admin bootstrap. Test it explicitly.
- Existing Identity options are currently configured in the CLI host only. Keep this slice's service independent of API authentication registration; AUTH-03 will centralize runtime Identity setup when sign-in is implemented.

### Implementation decisions for this slice
1. Extract an `IAdminBootstrapService` contract and result/failure codes into Application; implement it in Infrastructure.
2. Keep terminal input, exact database confirmation, hidden password collection, and process exit-code mapping in the CLI.
3. Add an explicit migration preflight before any bootstrap writes. Pending migrations or inability to read migration state must fail closed; the CLI must never invoke `Migrate()`.
4. Bound secret input to 128 characters and reject excess input without echoing it. Return only stable Identity error codes to the operator.
5. Retain the current `AuditLogs` table and sanitized `Admin.Bootstrapped` event. Do not add `SecurityAuditEvents`.
6. Inspect collation/index behavior, then add a filtered unique index for exactly one successful bootstrap event in an additive migration if the predicate is robust. No hand-edited model snapshot.
7. Add a dedicated SQL Server integration-test project. Its connection string must come only from `GHOST_AUTH02_TEST_CONNECTION_STRING`; validate server/database metadata and require Initial Catalog exactly `GHOST_AUTH02_IT`. No fallback to application configuration, no GHOST2, no automatic DROP/CREATE database, and no destructive fixture cleanup.
8. Apply migrations only after an explicit safety preflight confirms the exact approved catalog. Preserve all existing data in the test database; tests must use unique usernames and non-destructive assertions. Repeated concurrency stress requiring database reset is deferred until an isolated reset strategy is separately approved.

### Verification boundary
- This was a GitHub source review only. No local build, tests, SQL Server connection, migration application, or database operation was performed in this step.
- PR #3 remains open and draft. Do not mark ready or merge until SQL Server integration tests and runtime gates pass.
- `GHOST2`, existing databases, backups, and the local untracked `artifacts/` directory remain untouched.

### Immediate next action
- Implement the Application bootstrap contract/result codes and Infrastructure service extraction, then reduce the CLI to a thin host. Add tests for pure validation/result mapping first. After local build and unit tests pass, implement migration preflight and the dedicated SQL Server integration harness guarded to `GHOST_AUTH02_IT`. Only then apply migrations to that approved database.


## AUTH-02 — Bootstrap service extraction milestone (2026-11-18)

### Implemented on the branch
- Added Application contracts under `GHOST.Application.Identity.Bootstrap`: `AdminBootstrapRequest`, `AdminBootstrapResult`, `AdminBootstrapStatus`, and `IAdminBootstrapService`.
- Added `GHOST.Infrastructure.Identity.AdminBootstrapService`. It now owns the Serializable transaction, transaction-owned `sp_getapplock`, existing-Admin check, Admin/Staff role creation, Identity user creation, role assignment, and sanitized `Admin.Bootstrapped` audit insert.
- The service returns explicit outcomes for success, existing Admin, Identity rejection, and role conflict. Identity error descriptions are no longer returned; only stable error codes are surfaced.
- Reduced `GHOST.Bootstrap/Program.cs` to interactive input/confirmation, DI composition, service invocation, and process exit-code mapping. The CLI still does not run migrations.
- The CLI and service use the same scoped DI graph; `GHOSTDbContext` is registered once and the Identity stores and bootstrap service resolve from that scope.

### Review notes / verification boundary
- Static source review confirms the transaction and lock logic moved into Infrastructure and the CLI calls the Application contract.
- The existing disabled-Admin blocking behavior is preserved because the service still checks role membership without filtering `IsActive`.
- Early non-success results leave the transaction uncommitted; disposal rolls it back. Identity and audit writes remain in the same scoped context/transaction.
- No local .NET build or tests have been run after these changes. Do not treat this milestone as compile-verified.
- No SQL connection, migration, or database operation was performed. The approved `GHOST_AUTH02_IT` database remains untouched.
- The local `artifacts/` directory remains untracked and must be preserved.

### Next implementation steps
1. User pulls the latest branch and runs a Release build of `GHOST.Bootstrap` plus the existing test suite. Fix compile/test failures before adding more behavior.
2. Add bounded 128-character hidden-secret input and reject overlength input without echoing it.
3. Add fail-closed pending-migration preflight (read-only; never call `Migrate()`) before service invocation.
4. Add the isolated SQL Server integration-test project guarded exclusively to `GHOST_AUTH02_IT`; then inspect AuditLogs indexes/collation before deciding on the filtered unique bootstrap-event index.
5. Only after build, tests, and safety checks pass, apply the existing migrations to the explicitly approved test database. Never target `GHOST2`, drop/recreate a database, or clean existing data.



## AUTH-02 — Local build/test confirmation and safety gates (2026-11-18)

### Verified by the user on the local checkout
- Pulled the bootstrap-service extraction commit successfully.
- `dotnet build backend/src/GHOST.Bootstrap/GHOST.Bootstrap.csproj --configuration Release`: succeeded.
- `dotnet test backend/tests/GHOST.Tests/GHOST.Tests.csproj --configuration Release`: 43 passed, 0 failed, 0 skipped.
- Tracked worktree clean; untracked `artifacts/` preserved.

### Follow-up hardening added after that verification
- Added `InvalidRequest` result status and defensive service validation: username required/max 256, full name required/max 150, password required/max 128.
- Added hidden console password input bound of 128 characters. A 129th character aborts input without echoing the secret.
- Added a read-only EF pending-migrations preflight before resolving/invoking the bootstrap service. If migrations are pending, CLI exits with code 4 and does not call `Migrate()`.
- Changed the generic CLI exception message so it does not incorrectly claim a transaction was rolled back for failures that occur during the preflight.
- These latest hardening edits have not yet been compiled or tested locally. The previously reported 43 passing tests and build apply to the prior extraction commit only.

### Safety / execution status
- The migration preflight will open a connection to the database named by `GHOST_CONNECTION_STRING` when the CLI is run. Do not run the CLI until the connection string is independently verified to target exactly `GHOST_AUTH02_IT`.
- No CLI execution, SQL connection, migration application, or database mutation has occurred in this milestone.
- `GHOST2` remains explicitly out of scope. No database drop/recreate or cleanup is authorized.
- The untracked local `artifacts/` directory remains untouched.

### Next action
- Pull the latest branch, rerun the Bootstrap Release build and existing test suite. Inspect any compile/test failure before continuing.
- Then add the dedicated SQL Server integration-test project and strict connection-string guard for exactly `GHOST_AUTH02_IT`. Keep test fixtures non-destructive and never run against `GHOST2`.
- Before any database operation, inspect the configured connection metadata and confirm the exact target. Only then apply existing migrations to the approved test database.



## Project governance and roadmap checkpoint (2026-11-18)

- Added `docs/DELIVERY-ROADMAP.md` as the phase-by-phase delivery map, including exit gates, security/financial quality requirements, and the commitment to make a runnable Next.js shell visible early after the AUTH-02 SQL gate.
- `docs/STATE.md` remains the live source of truth for the current branch, verified work, blockers, and next action. Update it after each meaningful code/test milestone; do not rely on chat history as the only record.
- Current verified local baseline reported by the user: Bootstrap Release build succeeded; existing unit suite 43 passed, 0 failed, 0 skipped. This does not verify SQL Server behavior.
- Current immediate task: inspect repository test conventions and implement a safe, opt-in SQL Server test harness. The connection must come only from `GHOST_AUTH02_TEST_CONNECTION_STRING` and must reject any catalog except exactly `GHOST_AUTH02_IT`.
- No SQL Server connection, migration, schema mutation, database creation/drop/reset, or destructive test cleanup is authorized by this roadmap. Any specific database operation requires its own preflight and explicit confirmation.
- Preserve local untracked `artifacts/`; no destructive Git operations or unreviewed branch merges.
- After the SQL gate, prioritize a runnable Next.js application shell (Staff/Admin navigation, RTL layout, API connection state) so the product becomes visible and testable early, then proceed through AUTH-03 and the documented vertical workflows.


## AUTH-02 — isolated SQL Server test harness foundation (2026-11-18)

### Added to the branch
- Added `backend/tests/GHOST.SqlServer.Tests` as a separate .NET 10 xUnit project and registered it in `backend/GHOST.slnx`.
- Added a connection-string safety boundary that reads only `GHOST_AUTH02_TEST_CONNECTION_STRING`; it requires a non-empty server and exact ordinal catalog `GHOST_AUTH02_IT`, and rejects attached database files and failover partners.
- Added always-runnable unit tests for the guard, including explicit rejection of `GHOST2`, `master`, wrong casing, whitespace differences, missing server/catalog, and unsafe connection modes.
- Added an opt-in SQL Server Fact attribute: integration tests are skipped only when the dedicated environment variable is absent. If a value is present but unsafe, validation fails closed.
- Added initial integration checks for the dedicated catalog, AuditLogs table, append-only trigger, and trigger rejection of UPDATE/DELETE. Mutation tests insert a uniquely identified row inside a transaction and roll back; they do not run cleanup DELETE statements.
- Added project README with safe invocation guidance and the no-create/no-drop/no-migrate/no-reset boundary.

### Verification status
- Source files and solution registration have been committed to the active branch.
- **Not yet compiled or executed locally.** Package restore, build, and test results remain pending user-side/local verification.
- No SQL Server connection was opened. No database, schema, or migration was created/applied/changed.
- SQL integration tests require the already-provisioned dedicated `GHOST_AUTH02_IT` database and its migrations; the harness does not provision or migrate it.
- No test or source reads application configuration or `GHOST_CONNECTION_STRING`; the dedicated variable is the sole configured source.
- Local untracked `artifacts/` remains outside GitHub edits and must be preserved.

### Immediate next action
1. Pull the latest active branch locally.
2. Remove any existing `GHOST_AUTH02_TEST_CONNECTION_STRING` from the current PowerShell session, then run `dotnet test backend/GHOST.slnx --configuration Release`.
3. Confirm the local test/build output matches green CI and inspect `git status -sb`; preserve untracked `artifacts/`.
4. Do not configure the SQL integration connection yet. First perform a separate read-only preflight of the proposed server/catalog and confirm the dedicated database is the intended test target.


### CI wiring (2026-11-18)
- Added `.github/workflows/backend-ci.yml` to restore, Release-build, and test the full `.NET 10` solution on backend pull requests and the active AUTH-02 branch.
- The workflow does not receive a SQL connection secret. Therefore the SQL Server integration tests remain skipped in CI by default while the connection-guard unit tests execute.
- The first GitHub Actions run initially failed on an incorrect trailing-whitespace test expectation; that test was corrected and the successful rerun is recorded below.


### Backend CI verification (2026-11-18)
- GitHub Actions run [#44324909301](https://github.com/youssef-elqady/GHOST-2.0/actions/runs/44324909301) completed successfully on commit `473d61b003867bc72771bd87503fb423389d3f18`.
- Full `.NET 10` solution restore and Release build succeeded.
- Existing unit suite: 43 passed, 0 failed.
- SQL test project: 10 passed, 0 failed, 3 SQL integration tests skipped because no dedicated SQL connection was configured.
- The first CI attempt exposed an incorrect test expectation: SqlClient normalizes trailing whitespace in the catalog value. The guard compares the parsed catalog actually used by SqlClient; the invalid expectation was removed, and the rerun passed.
- This green CI proves compilation and the connection-guard unit tests. It does **not** prove live SQL Server trigger/constraint behavior because all 3 database integration tests were skipped.


### Pull request review note
- PR #3 is still draft/open and currently targets `feature/products-inventory-billing` as its base branch, not `main`.
- No base-branch change, merge, or PR readiness change was made. Confirm the intended integration target and review the full diff before any merge; keep the PR draft until SQL Server integration tests and remaining AUTH-02 gates pass.


## AUTH-02 — Identity composite-key index correction (2026-11-18)

### Root cause confirmed
- EF Identity's design-time context reads `IdentityOptions` from the EF options service provider. When that provider is absent, it falls back to Identity schema version 1; that path does not apply the 128-character key-length default unless `MaxLengthForKeys` is explicitly supplied.
- The design-time model therefore left the four composite-key strings unbounded, and the SQL Server provider mapped them to `nvarchar(450)`.
- This produced oversized clustered primary keys: UserLogins (1800 bytes) and UserTokens (1816 bytes), above SQL Server's 900-byte clustered-index key limit.

### Corrective change in progress
- Explicitly cap `IdentityUserLogin<Guid>.LoginProvider`, `ProviderKey`, and `IdentityUserToken<Guid>.LoginProvider`, `Name` at 128 characters in `GHOSTDbContext.OnModelCreating`; this is the schema-level source of truth and does not depend on the host's DI configuration.
- Set `IdentityOptions.Stores.MaxLengthForKeys = 128` in the Bootstrap host so runtime Identity options agree with the EF model.
- Add a corrective migration that checks existing values before narrowing columns, drops/recreates the two composite primary keys around the ALTER COLUMN operations, and restores the previous schema on Down.
- Add a no-database model test asserting all four max lengths and composite-key byte sizes remain below SQL Server's 900-byte limit.

### Verification and safety boundary
- Source-level root cause confirmed against the ASP.NET Core Identity EF implementation and the current migration/snapshot.
- The existing `GHOST_AUTH02_IT` database already has the previous schema. This code change does not connect to or modify any database.
- Migration is for review only; it must not be applied until its generated SQL and data preflight are reviewed and the user separately approves execution.
- No build or tests have been run by this GitHub-only edit. CI and user-side Release tests remain required.
- No changes to `GHOST2`, no branch merge, and no destructive database operation.

### Next gate
- Review the PR diff and generated migration SQL; run the backend CI workflow and local Release tests.
- Only after those gates pass, request separate explicit approval before applying the new migration to `GHOST_AUTH02_IT`.


## Dependency security follow-up (2026-11-18)
- User ran Release build and tests from the PR worktree: build succeeded; 54 tests passed, 0 failed, 3 SQL Server integration tests skipped because `GHOST_AUTH02_TEST_CONNECTION_STRING` was not configured.
- Restore reported `System.Security.Cryptography.Pkcs 9.0.4` with GHSA-2fmv-rv2c-59x7 (Moderate) and GHSA-846f-f764-76mx (High).
- The vulnerable transitive dependency comes from the SQL Server test project's `Microsoft.Data.SqlClient 6.1.3` dependency graph. Upgrade to SqlClient 6.1.8, whose .NET 9 dependency group requires Pkcs 9.0.21, the patched release for both advisories.
- This dependency update is a targeted servicing update; restore/build/tests must be rerun to verify the resolved graph and compatibility.
- The three SQL Server tests remain unverified until run against the dedicated test database.


## AUTH-02 — Identity key-length migration SQL review (2026-11-18)

### User-side verification reported after the dependency update
- `dotnet ef --version` reports EF Core Tools 10.0.15, aligned with EF Core runtime 10.0.15.
- Release build succeeded and the complete solution test run reported 54 passed, 0 failed, 3 skipped (the SQL Server integration tests were skipped because the dedicated connection variable was absent).
- With `GHOST_AUTH02_TEST_CONNECTION_STRING` configured for the dedicated `GHOST_AUTH02_IT` catalog, `GHOST.SqlServer.Tests` reported 13 passed, 0 failed, 0 skipped. This includes live verification of the AuditLogs table and append-only trigger rejecting UPDATE and DELETE, plus connection-target safety checks.
- NuGet's vulnerable-package check for `GHOST.SqlServer.Tests` reported no vulnerable packages.
- GitHub Actions Backend CI run [#44369739626](https://github.com/youssef-elqady/GHOST-2.0/actions/runs/44369739626) completed successfully for the reviewed branch head `5dd7a691ddf6883d12b621c789abd402f01002c9`.

### Generated SQL review
- User generated `FixIdentityCompositeKeyLengths.sql` with EF Tools 10.0.15 (3,057 bytes) in the separate local worktree `E:\\GHOST\\GHOST-2.0-authfix`; this path is outside the primary `E:\\GHOST\\GHOST-2.0` checkout and is intentionally preserved as a separate worktree artifact.
- Reviewed the supplied script: it starts a transaction; checks `UserLogins` and `UserTokens` for values over 256 bytes using `DATALENGTH`; takes `TABLOCKX, HOLDLOCK` locks; drops the two primary keys; narrows the four Identity key columns to `nvarchar(128)`; recreates both primary keys; inserts only the corrective migration ID into `__EFMigrationsHistory`; and commits.
- Calculated target composite-key widths are 512 bytes for `UserLogins` and 528 bytes for `UserTokens`, below SQL Server's 900-byte clustered-key limit.
- No row-copy, table-drop, or explicit data-delete operation appears in the supplied SQL.

### Blocking hardening item — preflight error transaction behavior
- The generated script's transaction begins before the preflight, but the preflight raises `THROW` without an explicit `SET XACT_ABORT ON` or `TRY/CATCH` rollback path.
- SQL Server documents `XACT_ABORT OFF` as the default and states that with it, some run-time errors roll back only the failing statement while the transaction remains active. Since `THROW` honors `XACT_ABORT`, a failed preflight may leave the script session's transaction and table locks open until explicitly rolled back or the connection closes. See Microsoft Learn: https://learn.microsoft.com/sql/t-sql/statements/set-xact-abort-transact-sql
- This is a script-execution safety issue, not evidence that the successful migration path is incorrect. Do not execute the script yet. Harden and verify the failure path, then regenerate and review the SQL output.
- No migration has been applied to `GHOST_AUTH02_IT`. No connection or operation against `GHOST2` was authorized or performed.

### Immediate next action
- Correct the migration's preflight failure handling without weakening the transaction/locking guarantee. Validate the generated script's success and failure paths before requesting any separate approval to apply it to `GHOST_AUTH02_IT`.
- Keep PR #4 draft/open and unmerged. Preserve the local `artifacts/` directory and the separate authfix worktree.


## AUTH-02 — Migration preflight rollback hardening (2026-11-18)

- Updated `20261118190000_FixIdentityCompositeKeyLengths.Up` to execute `SET XACT_ABORT ON;` before either data preflight query, inside the migration's transaction.
- Rationale: SQL Server defaults `XACT_ABORT` to OFF; `THROW` honors this setting. Enabling it ensures a runtime error from the preflight aborts and rolls back the active transaction rather than leaving the manual script session holding the table locks. Microsoft reference: https://learn.microsoft.com/sql/t-sql/statements/set-xact-abort-transact-sql
- The existing `TABLOCKX, HOLDLOCK` protection remains in place, so the check-to-ALTER race is still guarded by transaction-held locks.
- This is a source change only. It has not yet been built or tested locally, and the regenerated SQL has not yet been inspected.
- No migration was applied; no database was accessed; `GHOST2` remains untouched.

### Immediate next action
- Pull the latest PR branch, rebuild/re-run tests, regenerate `FixIdentityCompositeKeyLengths.sql`, and send the regenerated SQL for review. Confirm it contains `SET XACT_ABORT ON;` after `BEGIN TRANSACTION` and before the preflight. Do not execute it yet.


## AUTH-02 — Post-hardening local verification (2026-11-18)

Reported by user after fast-forward pull to commit `761f98ce4ead4dead43af76f262e1ebda7b60a0d`:
- `git pull --ff-only origin fix/auth-identity-composite-key-lengths`: succeeded; fast-forward from `5dd7a69` to `761f98c`.
- `dotnet build backend/GHOST.slnx --configuration Release`: succeeded.
- `dotnet test backend/GHOST.slnx --configuration Release`: 57 total, 54 passed, 0 failed, 3 skipped. The 3 skipped are the SQL Server integration tests because `GHOST_AUTH02_TEST_CONNECTION_STRING` was not set in this invocation.
- Regenerated `FixIdentityCompositeKeyLengths.sql` after the `SET XACT_ABORT ON` source change; EF build succeeded. The regenerated SQL content has not yet been reviewed.
- The dedicated SQL Server test project has not yet been rerun after this migration source change.
- No migration was applied and no database was modified.

### Immediate next action
- Review the regenerated SQL output and confirm `SET XACT_ABORT ON;` appears after `BEGIN TRANSACTION;` and before both preflight checks. Then validate the SQL Server failure path safely without leaving an open transaction. Keep PR #4 draft and unmerged.


## AUTH-02 — Identity migration preflight syntax correction (2026-11-18)

- Reviewed the user's regenerated SQL output and found that both preflight checks used `;THROW` as the statement immediately following a single-line `IF`. This syntax is unsafe because the leading semicolon terminates an empty statement; the THROW is not reliably scoped as the IF body and can execute unconditionally.
- Corrected the migration source `20261118190000_FixIdentityCompositeKeyLengths.cs`: both preflight checks now use explicit `BEGIN ... END` blocks around their respective `THROW 51001` / `THROW 51002` statements.
- The migration source still includes `SET XACT_ABORT ON;` before both preflight checks, and retains `TABLOCKX, HOLDLOCK` on both tables.
- The SQL pasted by the user did not include `SET XACT_ABORT ON;`, despite that statement being present in the GitHub migration source. Treat the pasted SQL as not matching the current source/branch output; regenerate it from the latest branch commit before any further review.
- Source-only GitHub change committed as `b0dbc4c1a4505b24da7332fd2cd0c2bf11f6fae0`. No database was connected to or modified; no migration was applied; PR #4 remains draft/open and unmerged.
- **Next action:** user pulls this commit in the separate `E:\\GHOST\\GHOST-2.0-authfix` worktree, regenerates `FixIdentityCompositeKeyLengths.sql`, and shares the complete output. Verify the generated script contains `SET XACT_ABORT ON;` after `BEGIN TRANSACTION;`, each `IF EXISTS` wraps its THROW in BEGIN/END, and the remaining DDL/history/COMMIT sequence is unchanged. Then rerun the SQL Server integration test project after this source change. Do not execute the migration script.


## AUTH-02 — Regenerated Identity migration SQL review (2026-11-18)

- User supplied the regenerated `FixIdentityCompositeKeyLengths.sql` from the latest source correction.
- Verified the script starts its migration transaction, enables `SET XACT_ABORT ON` before either preflight, and scopes both `THROW 51001` / `THROW 51002` inside explicit `IF EXISTS ... BEGIN ... END` blocks.
- Both preflights retain `TABLOCKX, HOLDLOCK` and check `DATALENGTH(...) > 256` before any schema changes.
- Verified the script drops only `PK_UserLogins` and `PK_UserTokens`, narrows exactly the four intended columns to `nvarchar(128) NOT NULL`, recreates the same composite primary keys, inserts the corrective migration history row, and commits.
- No table drops, explicit row deletes, or unrelated schema changes appear in the supplied script. Expected composite key widths remain 512 bytes for UserLogins and 528 bytes for UserTokens, below SQL Server's 900-byte clustered-index key limit.
- Backend CI run #17 for source-fix commit `b0dbc4c1a4505b24da7332fd2cd0c2bf11f6fae0` completed successfully.
- Remaining verification: rerun the dedicated SQL Server test project against the exact pre-approved `GHOST_AUTH02_IT` target after this latest source change. These tests validate the existing SQL Server harness and do not apply the Identity migration. Do not execute the migration SQL. Any migration application still requires separate explicit approval.


## AUTH-02 — Dedicated SQL Server tests after migration source correction (2026-11-18)

- User reran `backend/tests/GHOST.SqlServer.Tests/GHOST.SqlServer.Tests.csproj` in Release configuration from the separate `E:\\GHOST\\GHOST-2.0-authfix` worktree, with `GHOST_AUTH02_TEST_CONNECTION_STRING` configured for the approved dedicated `GHOST_AUTH02_IT` catalog.
- Result: **13 passed, 0 failed, 0 skipped**; test duration 2.9 seconds; project build succeeded.
- Live SQL Server checks passed for the AuditLog table and append-only trigger rejecting UPDATE and DELETE.
- Connection-safety checks passed, including rejection of `master`, `GHOST2`, wrong catalog casing, missing/unsafe connection values, attach-file mode, and failover partner; exact dedicated catalog validation passed.
- This run verifies the SQL Server integration-test harness and existing AuditLog behavior after the latest migration source correction. It does **not** execute or apply `20261118190000_FixIdentityCompositeKeyLengths`.
- No Identity migration was applied and no schema change was performed by this test run. `GHOST2` remains untouched. Preserve the separate authfix worktree and local artifacts.
- **Next gate:** optionally rerun the full solution Release build/test after the latest source correction if not already done on this exact commit; then review PR #4 status and remaining AUTH-02 exit criteria. Keep PR #4 draft/open and unmerged pending explicit user approval. Applying the Identity migration still requires separate explicit approval.

## AUTH-02 — Full-solution Release verification after SQL migration correction (2026-11-18)

- User ran both commands from the separate `E:\\GHOST\\GHOST-2.0-authfix` worktree:
  - `dotnet build backend/GHOST.slnx --configuration Release`: succeeded; all solution projects compiled.
  - `dotnet test backend/GHOST.slnx --configuration Release`: **57 total, 54 passed, 0 failed, 3 skipped**; test duration 6.0 seconds.
- The three skipped tests are the live SQL Server integration tests because `GHOST_AUTH02_TEST_CONNECTION_STRING` was not configured in this full-solution invocation.
- This full-solution run complements, and does not replace, the separate dedicated SQL Server test run recorded above: **13 passed, 0 failed, 0 skipped** against the approved `GHOST_AUTH02_IT` target after the latest migration source correction.
- Build and test outputs were user-reported from the correct AUTH-02 worktree. No migration was applied and no database schema was changed by these build/test commands.
- `GHOST2` remains untouched. The separate authfix worktree and local artifacts remain preserved.
- PR #4 remains draft/open/unmerged. Applying the Identity migration still requires separate explicit approval.

## AUTH-02 — PR #4 merged (2026-11-18)

- PR #4 was marked ready for review and squash-merged successfully into `feature/auth-02-auditlog-append-only`.
- Merge commit: `171fbd216cfaf6221b1bf18bb89665e282d54e3c`; GitHub reports the PR closed and merged.
- The merged commit includes the Identity composite-key length correction, runtime Identity option alignment, guarded corrective migration, model snapshot/test updates, SQL Client servicing update, and AUTH-02 verification documentation.
- Latest PR-head Backend CI run #21 (run ID `44375104887`) completed successfully.
- User-reported local verification from `E:\\GHOST\\GHOST-2.0-authfix`: Release solution build succeeded; 57 tests total, 54 passed, 0 failed, 3 skipped (the 3 live SQL Server tests were skipped in that full-suite invocation because the connection variable was absent).
- Separate live SQL Server test run after the migration source correction: 13 passed, 0 failed, 0 skipped against the dedicated `GHOST_AUTH02_IT` target. This validated the SQL Server test harness and existing AuditLog behavior; it did not apply the Identity migration.
- **Migration status: not applied.** No database schema was modified by the merge or test runs. Before applying, confirm the exact target database, take/verify a restorable backup, check preflight values, and schedule a controlled migration window. Do not target `GHOST2`.
- The separate local authfix worktree and its artifacts remain preserved. No local worktree was altered by this GitHub merge.

## AUTH-02 — Database rollout gate (2026-11-18)

- Created tracking issue #5: [Controlled rollout of Identity composite-key migration](https://github.com/youssef-elqady/GHOST-2.0/issues/5).
- The migration source was reviewed after merge. Its preflight checks reject values whose `DATALENGTH` exceeds 256 bytes before the schema changes; it then narrows the four Identity key columns to `nvarchar(128)` and recreates the two composite primary keys.
- No SQL Server database connection, backup inventory, or restore-verification capability is available in this execution environment. Therefore the exact target instance, backup existence/restorability, current migration history, and preflight data values could not be independently verified.
- **Decision: do not apply the migration yet.** This is a deliberate safety hold, not a code defect or a claim that rollout is complete.
- Issue #5 records the required preflight, backup/restore rehearsal, reviewed script, controlled execution, and post-migration verification steps. Target scope is only the dedicated `GHOST_AUTH02_IT`; `GHOST2` and production are explicitly excluded.


## AUTH-03 — Runtime authentication planning checkpoint (2026-11-18)

- Re-read `docs/STATE.md`, `docs/GHOST — SRS + ERD v0.2.md`, `ai/GHOST-master-prompt.md`, `docs/DELIVERY-ROADMAP.md`, and `docs/AUTHENTICATION-DECISIONS.md` on `feature/auth-02-auditlog-append-only` after AUTH-02 PR #4 was merged.
- Verified current branch tip from the branch comparison metadata: `3392c23e697ddf18cc17973ed207b665d22fa0e4` (`docs: record AUTH-02 database rollout safety gate`). Backend CI run #25 (run ID `44375664551`) completed successfully.
- Verified the existing Next.js Staff/Admin frontend is already present in this branch; `frontend/package.json`, `frontend/app/*`, `frontend/components/*`, and `frontend/lib/ghost-api.ts` are part of the current foundation. Device listing and Admin rate setup are connected, while authentication, shift/session operations, sales, inventory, and persistent messages remain incomplete.
- Security review reconfirmed that `GHOST.API/Program.cs` currently has no runtime JWT authentication/authorization middleware or policies, and `DevicesController` exposes device seeding and rate updates without server-side authorization. CORS and hidden UI controls are not security boundaries. Keep the API local/trusted-only until these endpoints are protected.
- Created Issue #6: [AUTH-03 — Runtime authentication, authorization, and actor scoping](https://github.com/youssef-elqady/GHOST-2.0/issues/6), defining login/JWT/refresh rotation, lockout, disabled-account revocation, MustChangePassword, claims-backed `ICurrentUser`, role/resource ownership policies, audit events, frontend integration, and required tests.
- Issue #5 remains open for the Identity composite-key migration rollout. The migration is **not applied**; exact SQL target, backup/restore capability, migration history, and data preflight remain unverified in this environment. Do not apply it or target `GHOST2` without the documented operational gates.
- Branch comparison confirms `feature/frontend-v0-foundation` is an ancestor of the current AUTH branch (0 commits ahead, 118 behind); it is not a separate feature to merge forward. The inventory branch is divergent and remains unapproved.
- **Next implementation action:** begin AUTH-03 on a new feature branch from the current AUTH-02 tip. First inspect Identity registrations, existing bootstrap/password policy, API request DTOs, and current-shift/session ownership contracts. Implement the runtime authentication foundation in small slices with unit and SQL Server integration tests. Protect existing device write endpoints before any frontend operation is considered safe. Keep AUTH-02 database rollout as a separate gated operation; do not block all code work on the unavailable backup/SQL execution environment.
- No application code, migration, or database was changed in this checkpoint. No local worktree or untracked artifact was accessed or modified.


## AUTH-02 — Migration rollback review finding (2026-11-18)

- Reviewed the merged migration source, `20261118190000_FixIdentityCompositeKeyLengths`, including its `Down` method.
- **New blocker:** `Down` widens the four Identity key columns back to `nvarchar(450)` and recreates the original composite primary keys. Those definitions total 1,800 bytes for `UserLogins` and 1,816 bytes for `UserTokens`. Microsoft documents a 900-byte maximum for SQL Server primary keys; the Down path therefore attempts to recreate an invalid schema and is not a viable rollback plan. [SQL Server primary/foreign key limits](https://learn.microsoft.com/en-us/sql/relational-databases/tables/primary-and-foreign-key-constraints?view=sql-server-ver17).
- Created issue #7: [Make Identity key-length migration explicitly forward-only](https://github.com/youssef-elqady/GHOST-2.0/issues/7). Required: make Down fail explicitly before DDL, test the fail-closed behavior, and document backup restore as the only rollback mechanism. Keep Up/preflight/locking unchanged.
- **Execution gate updated:** issue #5 must remain open until issue #7 is resolved, the exact target is verified, and a backup restore rehearsal succeeds. No migration has been run; no database was accessed or changed; `GHOST2` remains excluded.

### Immediate next action
1. Resolve issue #7 in a focused branch/PR and pass Release build/tests/CI.
2. Update issue #5's rollout procedure to use verified backup restore for rollback, then verify the exact `GHOST_AUTH02_IT` target, migration history, and data preflight.
3. Only then consider a controlled migration window. Do not apply to production or `GHOST2`.


## AUTH-02 — Identity migration made explicitly forward-only (2026-11-18)

### Implemented on a focused fix branch
- Branch: `fix/auth02-identity-migration-forward-only`, based on the latest AUTH-02 tip `e8cfdd3a75ecdb0d858280cfb051257e4997e27e`.
- Updated only the `Down` method of `20261118190000_FixIdentityCompositeKeyLengths`: it now throws an explicit `InvalidOperationException` before adding any migration operations.
- The exception explains that the old composite keys exceed SQL Server's 900-byte primary-key limit and that rollback requires restoring a verified pre-migration backup.
- The `Up` path, its `SET XACT_ABORT ON`, both `TABLOCKX, HOLDLOCK` preflights, all four `nvarchar(128)` alterations, and primary-key recreation were left unchanged.
- Added `IdentityMigrationRollbackTests`, which invokes the protected Down method through a test adapter and asserts that it throws and leaves `MigrationBuilder.Operations` empty.

### Verification status / safety
- Source diff is limited to the migration Down implementation, its focused unit test, and this state update.
- Backend CI run #32 (run ID `44377059343`) completed successfully; both Release Build and Test steps passed. The exact test count was not available from the workflow summary. No local test run was performed in this environment.
- No migration script was executed, no database was connected to or modified, and no data was changed.
- Issue #5 remains blocked until this fix is reviewed and green, then the exact `GHOST_AUTH02_IT` target and verified backup/restore rehearsal are independently confirmed. `GHOST2` and production remain excluded.

### Immediate next action
1. PR #11 was squash-merged into `feature/auth-02-auditlog-append-only` as commit `d5fdf30ed1a5b6380feb372b3e8a599fbe58081d` after the Up/Down diff review and successful Backend CI.
2. Issue #7 is resolved. Issue #5 remains open for the exact `GHOST_AUTH02_IT` target verification, verified backup/restore rehearsal, schema/history/data preflight, and controlled execution.
3. Do not apply the migration until every Issue #5 operational gate passes; do not use EF Down as rollback.


## AUTH-03 — shared Identity registration slice rebased after AUTH-02 fix (2026-11-18)

- Recreated the AUTH-03 working branch as `feature/auth-03-runtime-authentication-v2` from the updated AUTH-02 tip `bc042285179add3c0ca5217106251a6add409d92`, which includes the forward-only migration correction and its CI verification.
- Reapplied the shared Identity registration slice: `AddGhostIdentity` centralizes the 128-character key setting, approved password complexity, 5-attempt/15-minute lockout, Roles and EF stores.
- API and Bootstrap now use the same registration; a focused options unit test is included.
- This branch is a clean descendant of the updated AUTH-02 tip. The earlier PR #10 branch is retained but paused as stale; it is not to be merged.
- Initial Backend CI run #36 exposed a compile error: `AddSignInManager()` is not available from the current Infrastructure package graph. Removed that unnecessary registration because this slice does not yet use SignInManager; login implementation will add only the required dependency. Corrected head passed Backend CI runs #39 and #40 (run IDs `44377914844` and `44377955129`): Release Build and Test steps succeeded in both.
- No JWT, login, refresh tokens, claims-backed actor identity, or endpoint authorization has been implemented yet. Keep API local/trusted-only.
- No database connection or migration was performed; Issue #5 remains operationally blocked. `GHOST2` and production remain excluded.

### Merge and verification
- PR #12 was squash-merged into `feature/auth-02-auditlog-append-only` as `3f04105dea4e785abd034c02a878d959ee9bc53b`.
- Backend CI runs #39 and #40 passed on the corrected PR head; both Release Build and Test steps succeeded. Post-merge Backend CI run #43 (run ID `44378356448`) also passed Build and Test.
- The earlier failed run #36 was fixed by removing the unnecessary `AddSignInManager()` registration. The shared Identity setup now registers IdentityCore, Roles, and EF stores only; SignInManager will be added with the actual login implementation if needed.
- No migration was applied and no database was accessed or modified.

### Immediate next action
1. Continue AUTH-03 with fail-closed JWT configuration: validate required issuer, audience, signing key, and lifetime settings at startup; never commit secrets or use a development fallback in production.
2. Add login and short-lived access-token issuance using Identity password verification and lockout, rejecting disabled accounts and enforcing `MustChangePassword`.
3. Then implement hashed refresh-token persistence/rotation/replay detection, claims-backed `ICurrentUser`, and endpoint/resource authorization with integration tests.
4. Keep the API restricted to trusted local development until every mutating endpoint is protected and anonymous/role/ownership tests pass.


## AUTH-03 — fail-closed JWT configuration slice (2026-11-19)

- Tracking issue: [#14 — GHOST execution plan](https://github.com/youssef-elqady/GHOST-2.0/issues/14).
- Working branch: `feature/auth03-jwt-configuration`, based on `feature/auth-02-auditlog-append-only`.
- Added explicit `Authentication:Jwt` options for issuer, audience, signing key, and access-token lifetime. No secret or weak development fallback is committed.
- Added startup validation: issuer/audience required; signing key must contain at least 32 UTF-8 bytes; access-token lifetime must be 1–30 minutes. `ValidateOnStart` makes invalid/missing settings fail startup.
- Registered JWT bearer validation for issuer, audience, signature, expiration, and HMAC-SHA256 only; expiration is required, clock skew is 30 seconds, inbound claim remapping/token saving/detailed bearer errors are disabled. Added authentication/authorization middleware to API pipeline.
- Added validator tests and host-startup tests for missing versus valid explicit settings. Added environment-variable configuration instructions in `docs/AUTH-03-JWT-CONFIGURATION.md`.
- **Scope limitation:** this slice validates incoming JWTs only. It does not issue tokens, implement login/refresh, or add `[Authorize]` to endpoints. The API is not yet secured for operational use; keep it trusted-local-only until AUTH-03 endpoint protection and tests are complete.
- **Verification status:** source changes committed to the feature branch; automated CI and Release test results are pending PR checks. No local build/test was run by this environment. Do not mark VERIFIED until CI and review pass.
- **Database safety:** no database was connected to or modified. Existing `appsettings.json` contains a default connection string naming `GHOST2`; do not start API health/database operations or use that connection. A separate configuration-hardening action must remove accidental default targeting before any DB-connected runtime test.

### Next actions
1. Review PR checks and fix any compile/test issues before considering merge.
2. Inspect Staff/Admin reference repositories and current Next.js components (B0 discovery) without rewriting UI.
3. Implement login/access-token issuance only after this configuration slice passes.


### AUTH-03 JWT CI correction — 2026-11-19

- First CI attempt on PR #16 (run #47, run ID `44462682911`) restored dependencies and built the API successfully, then failed compiling the test project because the test referenced `DenyAnonymousAuthorizationRequirement` from the wrong namespace. It also reported an xUnit analyzer warning for a synchronous wait in an async-policy test.
- Corrected the test to import `Microsoft.AspNetCore.Authorization.Infrastructure` and await `GetFallbackPolicyAsync()`. This is committed on `feature/auth03-jwt-configuration` as `f0a54daec91cbea9b5490f1ebfa915cb94c67c91`.
- **Status remains IN REVIEW / CI PENDING** until a workflow run for the corrected head passes Build and Test. The failed attempt is not counted as verification.


### AUTH-03 JWT CI follow-up — 2026-11-19

- Replacement PR #17 CI run #49 (run ID `44462949680`) confirmed the prior namespace fix, then exposed that `IAuthorizationPolicyProvider` was not available to the test project's compile-time references. API project itself built successfully; tests did not compile.
- Simplified the test to inspect the registered `IOptions<AuthorizationOptions>.Value.FallbackPolicy` directly, retaining the assertion that anonymous users are denied by default. The test now imports the public authorization namespaces only.
- This correction is committed on `feature/auth03-jwt-configuration-reviewed`; CI must be rerun on the corrected head. No successful test claim is made yet.


### AUTH-03 JWT configuration — CI verified (2026-11-19)

- Corrected-head Backend CI run #51 (run ID `44463074527`) passed.
- Release solution build: **succeeded, 0 warnings, 0 errors**.
- `GHOST.Tests`: **58 passed, 0 failed**.
- `GHOST.SqlServer.Tests`: **10 passed, 3 skipped**. The skipped cases require the separately approved SQL Server integration environment; no SQL Server database was connected by this CI run.
- The two earlier failed CI attempts were test-source compile issues; both were corrected before run #51. Do not count those earlier attempts as successful verification.
- Issue #19 created to remove the committed `GHOST2` default connection string and require explicit environment configuration before any API DB operation. No database was accessed or changed.
- AUTH-03 JWT configuration slice is **VERIFIED at build/unit-test level**; JWT login/token issuance/refresh, endpoint authorization integration and full security matrix remain NOT STARTED. The fallback policy already requires authentication by default, so existing API routes are denied anonymously; no login endpoint exists yet.


## Execution plan and frontend discovery — 2026-11-19

- User-approved working agreement: execute GHOST in explicit phases, track each status and evidence, and keep this file current. Engineering lead owns technical sequencing, review, risk reporting, and honest completion criteria.
- Tracking issue: [#14 — GHOST execution plan](https://github.com/youssef-elqady/GHOST-2.0/issues/14).
- Detailed plan: [docs/EXECUTION-PLAN-2026-11-19.md](./EXECUTION-PLAN-2026-11-19.md).
- Frontend discovery and KEEP/MODIFY/ADD/DEFER: [docs/FRONTEND-DISCOVERY-2026-11-19.md](./FRONTEND-DISCOVERY-2026-11-19.md).

### Status after AUTH-03 JWT configuration merge

| Workstream | Status | Evidence / next action |
|---|---|---|
| AUTH-02 shared Identity registration | VERIFIED | PR #12 merged earlier; pending database migration remains unapplied. |
| AUTH-03 JWT configuration | VERIFIED — configuration slice only | PR #18 merged as `48b0d564b1db125aed6d1cdfc7c2031ee23b9f01`. Backend CI run #51 passed: Release build 0 warnings/0 errors; GHOST.Tests 58/58; GHOST.SqlServer.Tests 10 passed/3 skipped (SQL integration cases skipped without approved SQL connection). |
| AUTH-03 login/access-token issuance | NOT STARTED | Implement password verification, lockout, disabled-account checks, MustChangePassword and short-lived token creation. |
| AUTH-03 refresh-token lifecycle and claims-backed actor | NOT STARTED | Hashed persistence, rotation, replay detection, revocation, ICurrentUser. |
| AUTH-03 endpoint/resource authorization | IN PROGRESS — foundation in place | Fallback policy requires authentication by default. Protect/verify each workflow and test 401/403/ownership; no login endpoint exists yet. |
| Frontend B0 discovery | VERIFIED | Existing Next.js app and Staff/Admin reference repos reviewed; decisions recorded in discovery document. No UI code changed in discovery. |
| Frontend B1 Design System | NOT STARTED | Next frontend implementation: shared theme provider, semantic tokens, Light/Dark, RTL and reusable primitives. |
| AUTH-02 migration rollout | BLOCKED | Exact target and backup/restore/data preflight remain unverified; no migration applied. |
| API database configuration safety | BLOCKED / HIGH PRIORITY | Issue #19: committed API appsettings currently names `GHOST2`. Do not run DB health checks or database operations until the default is removed and explicit configuration is required. No DB was accessed. |

### Immediate next actions

1. Resolve Issue #19 before any API runtime operation that can touch the database: remove hard-coded `GHOST2` and require explicit environment-specific connection strings.
2. AUTH-03 login/access-token slice with Identity verification, lockout, account state, MustChangePassword and tests.
3. Frontend B1 shared theme provider/design tokens and reusable components on a focused branch.
4. Continue refresh tokens, claims-backed actor identity, resource/shift ownership and endpoint security tests.
5. Only after authentication and authorization are complete, connect the Admin and Staff shells to protected operational workflows.

### Safety and verification notes

- PR #18 CI run #51 was green before merge. It did not connect to SQL Server: 10 SQL guard/unit tests passed and 3 SQL integration tests were skipped due to no approved connection string.
- JWT configuration validates required issuer, audience, signing key (minimum 32 UTF-8 bytes), and access-token lifetime (1–30 minutes) at startup; only HMAC-SHA256 is accepted; expiration is required; fallback authorization requires authenticated users by default.
- This does **not** mean login or full API authorization is complete. No token issuance, refresh flow, claims-backed user context, role/ownership policies, or login UI exists yet.
- No database was connected to or modified. Do not use `GHOST2`, production, or apply the pending AUTH-02 migration.


## API database connection hardening — Issue #19 (2026-11-19)

- Tracking issue: [#19 — remove hard-coded GHOST2 database target](https://github.com/youssef-elqady/GHOST-2.0/issues/19).
- Working branch: `fix/api-database-connection-fail-closed`, based on the latest delivery branch after PR #18.
- Removed the committed `ConnectionStrings:DefaultConnection` value that named `GHOST2` from API `appsettings.json`.
- Added explicit `DatabaseConnectionOptions` with startup validation. API requires `ConnectionStrings__DefaultConnection`; there is no committed fallback.
- Validation parses the SQL Server connection string without opening a connection; requires explicit data source and catalog; rejects `GHOST2`, `GHOST_AUTH02_IT`, SQL system catalogs, and attached database files.
- API DbContext now consumes the validated options. No database connection was made by these changes or tests.
- Added validator tests for missing/malformed values, reserved catalogs, missing server/catalog, attached files, and valid safe config; added startup validation tests.
- **Verification status:** PR CI pending. No local build/test was run in this environment. Do not mark verified until CI passes.

### Next action
1. Review CI and fix any build/test issues.
2. Merge only to `feature/auth-02-auditlog-append-only` after green checks.
3. Then continue AUTH-03 login/access-token issuance; keep all database operations blocked until a user-approved exact target and backup/restore gates are met.


### API database configuration safety — CI verified (2026-11-19)

- PR #21 Backend CI run #57 (run ID `44463594101`) passed.
- Release solution build: **succeeded, 0 warnings, 0 errors**.
- `GHOST.Tests`: **71 passed, 0 failed**.
- `GHOST.SqlServer.Tests`: **10 passed, 3 skipped** because the isolated SQL integration connection was not configured. No database connection was made by CI.
- The API no longer has a committed `GHOST2` connection string. Startup requires explicit `ConnectionStrings__DefaultConnection` and validates server/catalog without connecting. `GHOST2`, `GHOST_AUTH02_IT`, system catalogs and attached database files are rejected.
- This code/configuration slice is **VERIFIED at build/unit-test level**; merge remains pending review. No DB was accessed or changed.


## Post-merge checkpoint — 2026-11-19

- PR #18 merged JWT configuration as `48b0d564b1db125aed6d1cdfc7c2031ee23b9f01`. Backend CI run #51 passed: Release build 0 warnings/0 errors; `GHOST.Tests` 58 passed; `GHOST.SqlServer.Tests` 10 passed / 3 skipped. The SQL integration environment was not connected.
- PR #20 merged the execution plan and frontend discovery docs as `07a83f1a985719ada47238ac383453c7b61f0c00`.
- PR #21 merged API database configuration hardening as `bfd2f4f7a67238d1c27a155ae84c89bcefb4fbac`. Backend CI run #57 passed: Release build 0 warnings/0 errors; `GHOST.Tests` 71 passed; `GHOST.SqlServer.Tests` 10 passed / 3 skipped. No database connection was made.
- Issue #19 is closed. The API no longer has a committed `GHOST2` target; it requires explicit `ConnectionStrings__DefaultConnection` and rejects `GHOST2`, `GHOST_AUTH02_IT`, system catalogs and attached database files at startup.
- **No database was accessed or modified; no migration was applied.** `GHOST2` and production remain excluded.

### Current delivery statuses

| Area | Status | Next action |
|---|---|---|
| AUTH-02 Identity registration + migration rollback safety | VERIFIED in code/CI | Database migration remains unapplied pending separate operational gates. |
| AUTH-03 JWT validation/configuration | VERIFIED in code/CI | Login and access-token issuance. |
| AUTH-03 login/refresh/actor/endpoint authorization | NOT STARTED beyond JWT validation/fallback policy | Implement in focused slices; test account states, refresh replay, 401/403 and ownership. |
| API DB configuration safety | VERIFIED in code/CI | Provide a specifically approved environment connection string only when DB work is authorized. |
| Frontend B0 discovery | VERIFIED | Completed and documented. |
| Frontend B1 Design System | NEXT | Shared theme provider, semantic tokens, reusable primitives, Light/Dark, RTL/responsive. |
| AUTH-02 database rollout | BLOCKED | Exact target, verified backup/restore, schema/history/data preflight. |

### Immediate next actions
1. Start Frontend B1 on a focused branch: shared Light/Dark theme provider, unify tokens and theme toggle, reusable Button/Card/Badge/PageHeader primitives; preserve existing Staff/Admin screens and behavior.
2. Start AUTH-03 login/access-token issuance on a separate focused branch: Identity password verification, lockout, inactive-account denial, MustChangePassword, short-lived JWT issuance, and tests.
3. Do not connect to any database or apply migrations without explicit target approval and verified backup/restore gates.


## Frontend B1 — shared Design System foundation (2026-11-19)

- Working branch: `feature/frontend-design-system`, based on the current delivery branch after PR #22.
- Added a shared `ThemeProvider` and `ThemeToggle`; the root layout bootstraps the saved/system theme before hydration. Staff and Admin now use the same theme state rather than maintaining duplicate localStorage effects.
- Added reusable Button, Card, Badge and PageHeader primitives. Admin device management now uses shared Button and Badge components for add/refresh/save actions and rate-configuration status.
- Extended semantic CSS tokens for raised surfaces, focus ring, violet, and chart series. Added keyboard focus styling, selection styling, and reduced-motion support.
- Design contract: [docs/FRONTEND-DESIGN-SYSTEM-B1.md](./FRONTEND-DESIGN-SYSTEM-B1.md).
- **Scope:** this is a foundation only. No financial charts, dashboard metrics, login flow, or fake data were added. Existing Staff/Admin layout and API workflows are preserved.
- **Verification:** Frontend CI is pending; no local pnpm build was run in this environment. Do not mark B1 VERIFIED until the Next.js production build passes and the theme is visually checked in both modes.

### Next actions
1. Review Frontend CI and fix TypeScript/Next.js issues.
2. If CI passes, visually inspect Staff and Admin in Light/Dark and verify persistence across routes.
3. Continue with Admin shell and dashboard skeleton without adding fake figures; keep AUTH-03 login/access-token work on a separate branch.


### Frontend B1 build verification — 2026-11-19

- Frontend CI run #27 (run ID `44464329267`) passed both jobs:
  - Next.js production build: **compiled successfully**; routes emitted.
  - Backend API Release build: **passed**.
- No local browser/visual QA was performed in this environment. The B1 code is **build-verified**, while visual inspection of Light/Dark, route-to-route preference persistence, and responsive breakpoints remains pending before marking the whole design-system phase VERIFIED.
- Node/pnpm ecosystem deprecation notices appeared in CI logs; they did not fail the build and are not application compile errors.

## Admin dashboard skeleton — B2/B3 (2026-11-19)

- Working branch: `feature/admin-dashboard-skeleton`, based on the delivery branch after PR #23.
- Added an Admin Overview skeleton with KPI cards, financial revenue panel, separate time/usage panel, financial source summary, shift/cash summary, product table, device/room table, insights placeholder and data-integrity notice.
- Color contract is visible in the UI: gaming blue, products green, total collected gold, expenses/refunds red, gaming duration violet in a separate time panel. EGP and time are not mixed.
- Every metric remains — and every table/chart shows a truthful empty state. No sample revenue, session counts, percentages, cash balances, or fabricated chart bars.
- Existing Admin Device Management behavior is preserved.
- Design and backend data requirements: [docs/ADMIN-DASHBOARD-SKELETON.md](./ADMIN-DASHBOARD-SKELETON.md).
- Verification: Frontend CI pending. No local browser QA was performed. Do not mark this phase VERIFIED until production build and visual checks pass.

### Admin dashboard skeleton — build verification (2026-11-19)

- Frontend CI run #30 (run ID `44464844197`) passed both jobs: Next.js production build and API Release build.
- The dashboard skeleton is **build-verified**. Visual inspection in browser at desktop/tablet/mobile and both themes remains pending because no local browser session was available.
- The dashboard is intentionally a layout skeleton: metrics show `—`, charts/tables show empty states, and no demo financial values are present.
- B1 shared Design System PR #23 was merged as `9eba817074b6205fb5e0c2d1ec477f1f1bed8ca0`; its Frontend CI run #27 passed Next.js production build and API Release build. Visual QA remains a separate acceptance item.


## Post-merge Admin dashboard checkpoint — 2026-11-19

- PR #23 merged shared frontend Design System as `9eba817074b6205fb5e0c2d1ec477f1f1bed8ca0`. Frontend CI #27 passed Next.js production build and API Release build.
- PR #24 merged Admin dashboard skeleton as `18f00db404b2e6da83ebcbde2f352e72546f2a8c`. Frontend CI #30 passed Next.js production build and API Release build.
- Dashboard skeleton includes distinct gaming/product/total/expenses colors and a separate violet time-usage panel. All metrics remain unknown (`—`) until real API contracts are available; no demo values are shown.
- **Visual QA remains pending:** inspect Light/Dark and responsive layouts in an actual browser before calling the UI phase fully VERIFIED. Build success is not visual acceptance.
- No database was connected or modified.

### Next work
1. AUTH-03 login and short-lived access-token issuance using the existing Identity setup, with lockout, inactive-account and MustChangePassword handling.
2. AUTH-03 refresh-token lifecycle and claims-backed actor context.
3. Frontend login/session integration and protected Staff/Admin route experience.
4. Build real analytics APIs only after business-day, shift, invoice/payment and session source-of-truth contracts are defined and tested.

## AUTH-03 — login and access-token issuance (2026-11-19)

- Working branch: `feature/auth03-login-access-token`, based on the active delivery line after the Admin dashboard skeleton.
- Added POST /api/auth/login with generic invalid-credential handling, inactive-account denial, Identity lockout (5 attempts / 15 minutes), and short-lived access-token issuance.
- Existing accounts are explicitly lockout-enabled during login if they predate the lockout setting. New ApplicationUser instances and first Admin bootstrap enable lockout.
- Accounts flagged MustChangePassword receive a five-minute restricted token only; POST /api/auth/change-password requires that token, validates current/new password, clears the flag with the Identity password update, and returns a normal access token.
- Authorization now distinguishes token purpose: fallback API policy requires token_use=access; PasswordChangeOnly requires token_use=password_change. Restricted tokens cannot access normal API endpoints.
- Access tokens include sub, unique_name, role, full_name, jti, issuer, audience and expiry. Refresh tokens, revocation, claims-backed actor abstraction and resource/shift policies remain later work.
- Added EF Core InMemory controller tests for successful login/role claims, MustChangePassword flow, password change, inactive account and five-attempt lockout; policy tests verify the two token-purpose policies.
- Detailed flow and limitations: [docs/AUTH-03-LOGIN.md](./AUTH-03-LOGIN.md).
- **Verification:** CI pending. No database connection was made; tests use EF Core InMemory. Do not mark verified until Build and Test pass.

### AUTH-03 login CI review — 2026-11-19

- Initial PR #27 CI run #73 passed compilation and tests: `GHOST.Tests` 77/77; `GHOST.SqlServer.Tests` 10 passed / 3 skipped; 0 build errors.
- The build emitted two nullable-analysis warnings in the authorization-policy tests because `AllowedValues` is nullable. The tests were corrected to use a null-safe check. This is a quality correction; a clean rerun is required before merge.
- No database was connected. The 3 SQL integration tests remained skipped.


### AUTH-03 authorization safety refinement — 2026-11-19

- During final review of the login flow, identified that an authenticated non-Admin token would otherwise satisfy the fallback policy before Manager/Cashier/Staff endpoint policies exist.
- Tightened the fallback policy to require all three: authenticated principal, `token_use=access`, and `Admin` role. The restricted password-change policy remains separate and accepts only `token_use=password_change`.
- Added a policy test asserting the temporary Admin-only fallback. This deliberately blocks non-Admin API access until explicit role/resource/shift policies and ownership tests are implemented.
- Updated `docs/AUTH-03-LOGIN.md`. CI must rerun on this refinement before merge.


### AUTH-03 login/access-token CI verified — 2026-11-19

- Corrected-head CI run #83 (run ID `44466252139`) passed.
- Release solution build: **succeeded, 0 warnings, 0 errors**.
- `GHOST.Tests`: **77 passed, 0 failed**.
- `GHOST.SqlServer.Tests`: **10 passed, 3 skipped** because no isolated SQL integration connection was configured. No database connection was made.
- The fallback policy is intentionally Admin-only until explicit Manager/Cashier/Staff role and resource ownership policies are implemented. Restricted password-change tokens cannot access normal endpoints.
- Login and required password-change flow are **VERIFIED at build/unit-test level**. Refresh tokens, replay detection/revocation, current-user abstraction, fine-grained policies, and frontend login remain NOT STARTED.


## Post-merge AUTH-03 login checkpoint — 2026-11-19

- PR #29 merged login and restricted password-change flow as `d7f9ec576a41f4b8621928b6d45d611f34962f91`.
- Corrected-head Backend CI run #83 passed: Release build 0 warnings/0 errors; `GHOST.Tests` 77 passed; `GHOST.SqlServer.Tests` 10 passed / 3 skipped (no SQL integration connection configured).
- Login supports Identity password verification, disabled-account denial, 5-attempt/15-minute lockout, short-lived access tokens, and a five-minute password-change-only token for bootstrap accounts. Password change returns a normal access token.
- Fallback API access is intentionally Admin-only until explicit Manager/Cashier/Staff role and resource/shift ownership policies are implemented. The password-change token is purpose-restricted and cannot access operational endpoints.
- Refresh tokens, rotation, replay detection, revocation, ICurrentUser, fine-grained endpoint policies and frontend login remain outstanding.
- No database was connected or modified. No migration was applied.

### Current priority order
1. AUTH-03 refresh-token persistence, hashing, rotation, replay detection and revocation.
2. Claims-backed current-user context and explicit role/resource/shift policies; expand beyond Admin only after endpoint-by-endpoint tests pass.
3. Frontend login and required password-change screen, then session integration. Do not store long-lived refresh secrets in localStorage.
4. Visual QA for the Admin dashboard skeleton in Light/Dark and responsive breakpoints.
5. Real dashboard analytics APIs only after financial/session/shift source-of-truth contracts are implemented and reconciled.


## AUTH-03 authentication audit hardening — 2026-11-19

- PR #29 is the authoritative merged AUTH-03 login/password-change implementation. The earlier audit-writer PR #26 was closed as superseded; none of its duplicate login/token code was merged.
- New focused branch: `feature/auth03-login-audit-hardening`, based on the delivery branch after PR #29.
- Added `IAuthenticationAuditWriter` and `AuthenticationAuditWriter` using the existing immutable `AuditLog` entity and `GHOSTDbContext`.
- Wired audit events into the authoritative PR #29 login and password-change flows: successful login, unknown-user failure, inactive-account denial, locked-account denial, failed password, lockout, restricted password-change token issuance, password-change success/failure/denial.
- Authentication audit entries never include usernames from failed attempts, passwords, raw JWTs, or temporary credentials. Unauthenticated attempts use a System actor; authenticated success/change events use the user GUID.
- Extended EF Core InMemory controller tests to assert the audit events and verify that unknown usernames/passwords are not persisted.
- Added a guarded SQL Server integration test for actual audit persistence, using only the exact `GHOST_AUTH02_IT` database via `GHOST_AUTH02_TEST_CONNECTION_STRING`, and rolling back its row.
- No login/token implementation was duplicated or overwritten. No DB connection or migration was performed.
- **Verification:** CI pending on this audit-only hardening branch. Keep PR separate from the already merged login/password-change flow until build/tests pass. SQL persistence remains unverified unless the isolated test connection is explicitly configured.
