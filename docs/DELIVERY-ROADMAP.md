# GHOST-2.0 Delivery Roadmap

Last updated: 2026-11-18
Status source: `docs/STATE.md` and the verified branch state. This roadmap is the delivery map; `docs/STATE.md` remains the live execution log.

## Product outcome

Deliver a dependable, production-minded management system for the real GHOST PlayStation venue. The system must protect money, shifts, sessions, inventory, and staff access—not merely provide attractive screens.

## Operating rules

1. **One source of truth:** update `docs/STATE.md` after every meaningful milestone. Record commit/branch, what changed, verification performed, what was not verified, blockers, and the next exact action.
2. **No silent scope changes:** record important business and architecture decisions in the relevant decision document before implementation.
3. **Definition of done:** a feature is not complete because it compiles. It needs acceptance criteria, validation, failure-path coverage, persistence/concurrency checks where relevant, and user-visible workflow verification.
4. **Money and authority stay on the server:** browser code never calculates authoritative charges, decides permissions, or mutates stock balances directly.
5. **No unsafe database work:** never drop/recreate databases, run destructive cleanup, or apply migrations to an unverified connection. The dedicated AUTH-02 test database is `GHOST_AUTH02_IT`; never use `GHOST2`.
6. **Preserve user work:** do not overwrite the existing v0 frontend direction, reference repositories, or untracked `artifacts/` content.
7. **Build visible value early:** backend quality work must not become an endless detour. After the current AUTH-02 SQL safety gate, establish a runnable frontend shell and then deliver vertical workflows incrementally.

## Delivery phases

### Phase 0 — Baseline and product reconciliation
- Reconcile backend and frontend against the SRS/ERD and approved business decisions.
- Maintain KEEP / MODIFY / ADD / DEFER findings.
- Preserve the existing Next.js direction and Staff/Admin visual references.
- **Exit gate:** agreed scope, architecture, critical business rules, and known gaps are documented.

### Phase 1 — AUTH-02 bootstrap and audit integrity (current)
- Keep first-admin creation offline/console-only, explicit, bounded, and fail-closed.
- Keep role/user/audit writes atomic and serialized.
- Verify migration preflight and secret handling.
- Add isolated SQL Server integration coverage guarded by `GHOST_AUTH02_TEST_CONNECTION_STRING` and exact catalog `GHOST_AUTH02_IT`.
- Verify append-only trigger and database constraints using rollback-safe test transactions.
- Do not auto-run migrations from the bootstrap CLI; do not create/drop/reset databases.
- **Exit gate:** Release build + unit tests + SQL Server integration tests pass; exact database target is independently verified; bootstrap failure and concurrency behavior are covered.

### Phase 2 — First runnable product shell (start immediately after AUTH-02 gate; UI foundation may be prepared in parallel)
- Establish the existing Next.js app as the visible Staff/Admin product, not a second frontend.
- Deliver login shell, protected app layout, Arabic RTL navigation, responsive layout, loading/empty/error states, and a real API health/connection state.
- Admin shell: dashboard structure and navigation.
- Staff shell: shift/session workspace structure.
- Use explicit mock/demo data only where clearly labelled; never present mock values as real venue figures.
- **Exit gate:** user can run the app locally, sign in when auth is connected, navigate the main shell, and distinguish real API data from placeholders.

### Phase 3 — Runtime authentication and authorization (AUTH-03)
- Centralize Identity configuration for runtime API use.
- Implement secure sign-in/sign-out, session/token lifecycle, password change/reset policy, lockout, and account activation.
- Derive actor identity from authenticated claims; stop trusting staff/user IDs supplied by clients.
- Enforce role and resource ownership on every protected endpoint.
- **Exit gate:** integration tests prove anonymous denial, role boundaries, disabled-account denial, and actor scoping.

### Phase 4 — Business day, shifts, staff, and cash foundation
- Business-day open/close, shift open/close, handover, staff assignment, opening cash, and immutable cash ledger.
- Derive expected cash from opening balance plus recorded transactions; no arbitrary client-side expected-cash updates.
- Make cash movements auditable, transactional, idempotent, and concurrency-safe.
- **Exit gate:** complete open/operate/handover/close workflow reconciles expected cash from ledger entries under duplicate and concurrent requests.

### Phase 5 — Devices and gaming sessions
- Device configuration and rates; start, pause, resume, transfer, complete, cancel, and permitted void workflows.
- Enforce one active occupancy per device and correct shift/business-day policy.
- Preserve historical rates per segment; apply approved BD-04 rounding exactly once to the total charge.
- Link completion to a real sale/payment/cash transaction; a calculated amount alone is not collected revenue.
- Add idempotency keys and database-backed concurrency tests.
- **Exit gate:** full session lifecycle survives retries, simultaneous requests, app restart, and device transfer without duplicate charges or overlapping occupancy.

### Phase 6 — Products, inventory, and POS
- Reconcile the inventory foundation branch before cherry-picking/merging anything.
- Complete products, SKU, stock movements, receiving, adjustments, sales, returns/voids, and price history as approved by the SRS.
- Persist stock changes and matching financial entries atomically; prevent negative stock unless an explicit rule allows it.
- **Exit gate:** stock ledger, on-hand balance, sale lines, and cash ledger reconcile after success, failure, retry, and concurrent sales.

### Phase 7 — Admin reporting and operational controls
- Daily/weekly/custom-range reporting, shift/staff/device/product filters, cash reconciliation, audit search, and exports.
- Make report definitions explicit (gross/net, gaming vs product sales, refunds, expenses, withdrawals, and time zone).
- Add backup/restore procedures, operational logs, and failure alerts.
- **Exit gate:** report totals reconcile to source ledgers and backup restore is demonstrated on a separate safe target.

### Phase 8 — Production readiness and venue pilot
- Security review, permission matrix, migration rehearsal, backup/restore rehearsal, deployment configuration, printer/device compatibility, and operator training.
- Run a controlled pilot with test data first, then an explicitly approved production cutover plan.
- **Exit gate:** all critical workflows pass acceptance tests; no unresolved critical security/financial defects; recovery and support procedures are documented.

## Required quality gates for every vertical workflow

- Business rules and acceptance examples documented before coding.
- Request validation and meaningful error handling.
- Authorization and ownership checks on the server.
- Database constraints/indexes for invariants that must survive concurrency.
- Transaction boundaries and idempotency for money/stock/session mutations.
- Unit tests for calculations/domain rules.
- Integration tests for EF mappings, SQL constraints, transactions, and concurrency.
- UI states: loading, empty, success, validation error, server error, and permission denied.
- Audit trail for sensitive and financial actions.
- Update `docs/STATE.md` and relevant decision docs before moving the phase to Done.

## Current checkpoint — 2026-11-18

- Branch: `feature/auth-02-auditlog-append-only`.
- Latest known source commit at checkpoint: `28db7145b1e4fc29c9e6f87d70804d36318cb168`.
- User-confirmed local Release build of `GHOST.Bootstrap`: succeeded.
- User-confirmed existing unit tests: 43 passed, 0 failed, 0 skipped.
- The above confirms the current bootstrap source/build and existing unit-test suite as reported by the user; it does **not** prove SQL Server behavior or production readiness.
- SQL Server integration tests are the immediate engineering gate. No database connection or migration application is part of this checkpoint.
- The local untracked `artifacts/` directory must remain untouched.
- Once the SQL test harness is safe and green, prioritize the runnable Next.js shell so the user can see and exercise the product while later backend workflows are delivered.

## Immediate next actions

1. Inspect the exact test and infrastructure project conventions on the current branch.
2. Add an opt-in SQL Server test project. It must read only `GHOST_AUTH02_TEST_CONNECTION_STRING`, reject any Initial Catalog other than exactly `GHOST_AUTH02_IT`, and never fall back to appsettings or `GHOST_CONNECTION_STRING`.
3. Add always-runnable unit tests for the connection guard; integration tests must not connect when the environment variable is absent.
4. Do not create, drop, reset, or migrate a database automatically. Before any SQL operation, display/verify the parsed server and catalog and obtain confirmation for that specific operation.
5. After the SQL gate, begin the runnable frontend shell and continue with AUTH-03 and vertical workflows.
