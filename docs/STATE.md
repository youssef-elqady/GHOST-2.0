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
- Local branch `feature/products-inventory-billing` was synchronized with origin at commit `dcc07c5`.

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

