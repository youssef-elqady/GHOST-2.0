# GHOST STATE

Last updated: 2026-11-12
Current sprint: 01 - Foundation review (codebase review in progress)

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
