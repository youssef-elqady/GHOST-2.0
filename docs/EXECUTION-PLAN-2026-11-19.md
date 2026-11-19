# GHOST Execution Plan — 2026-11-19

Tracking issue: [#14 — AUTH-03 security and premium Staff/Admin frontend](https://github.com/youssef-elqady/GHOST-2.0/issues/14)

## Product objective

Deliver GHOST as a reliable commercial PlayStation venue operations system with server-authoritative financial/security workflows and a premium Arabic RTL Staff/Admin experience.

## Operating agreement

- Active delivery line: `feature/auth-02-auditlog-append-only`. Do not merge to `main` without explicit instruction.
- Use focused feature branches and PRs. No destructive Git operations.
- Update `docs/STATE.md` after each meaningful milestone.
- Never apply migrations or access production/`GHOST2`; database work requires exact-target verification and backup/restore gates.
- Backend owns money, billing, inventory, permissions, and session state.
- Demo data must be visibly labeled; never show placeholders as real revenue.
- A phase is `VERIFIED` only with build/test/review evidence. Report unverified items explicitly.

## Status vocabulary

- `NOT STARTED`
- `IN PROGRESS`
- `BLOCKED` — reason, owner, and exit condition required
- `IN REVIEW`
- `VERIFIED` — evidence required
- `DEFERRED` — reason recorded

## Workstream A — AUTH-03 runtime security

| Step | Scope | Exit evidence | Initial status |
|---|---|---|---|
| A1 | Fail-closed JWT configuration and startup validation | Missing/weak issuer, audience, key and lifetime rejected; valid config builds | VERIFIED — PR #18 merged; CI #51 passed |
| A2 | Login, password verification, lockout, disabled-account checks, MustChangePassword, short-lived access tokens | Unit/API tests for valid and invalid credentials and account states | VERIFIED — PR #29 merged; CI #83 passed. Audit-event hardening is in progress separately. |
| A3 | Hashed refresh-token persistence, rotation, replay detection and revocation | Persistence tests and replay/revocation scenarios | NOT STARTED |
| A4 | Claims-backed current-user context and role/resource/shift ownership policies | Actor identity comes only from claims; ownership tests | NOT STARTED |
| A5 | Protect every endpoint, including existing device mutations | Endpoint inventory + anonymous 401, role 403, ownership and integration tests | NOT STARTED |

**Safety gate:** keep API trusted-local-only until every mutating endpoint is protected and the security test matrix passes.

## Workstream B — Premium frontend

| Step | Scope | Exit evidence | Initial status |
|---|---|---|---|
| B0 | Inspect existing Next.js app and Staff/Admin reference repositories; document KEEP/MODIFY/ADD/DEFER | File inventory, architecture map, visual reference review, no blind rewrite | VERIFIED — discovery document published |
| B1 | Design System: light/dark tokens, Arabic RTL, responsive behavior, typography, buttons, cards, tables, forms, badges, charts and UI states | Both themes verified on core components and common screen sizes | BUILD VERIFIED — PR #23 merged; visual QA in both themes and breakpoints pending |
| B2 | Admin shell: sidebar, topbar, navigation, profile, theme switch | Navigation and theme persist; keyboard/mobile behavior checked | NOT STARTED |
| B3 | Separate Staff shell optimized for shift/session workflows | Staff tasks are distinct and efficient; permission-aware navigation | NOT STARTED |
| B4 | Admin dashboard skeleton | KPI layout, revenue panels, venue status, insights, alerts, recent activity; no fake live values | BUILD VERIFIED — PR #24 merged; visual QA pending |
| B5 | Real dashboard API integration | Figures reconcile with source endpoints/ledgers; loading/empty/error/permission states | NOT STARTED |
| B6 | Analytics and reports | Date filters, gaming/product revenue separation, device/room/product/time analysis | NOT STARTED |

### Visual and data rules

- GHOST identity: premium charcoal/warm neutral surfaces with restrained gold accents; light and dark are first-class themes, not simple inversion.
- Arabic RTL is the default layout; maintain responsive desktop/tablet behavior.
- Financial series use distinct, consistent colors (e.g. gaming revenue blue, product revenue green, total revenue gold, expenses red); session duration/usage time uses a separate chart and time axis. Never mix EGP and minutes/hours on one axis.
- Every chart has clear units, period, legend, empty/loading/error states, and drill-down where useful.
- Never infer gross profit without reliable cost data. Never render missing API data as zero.
- No browser-side authoritative billing, money calculation, stock mutation, or authorization.

## Workstream C — Business verticals

1. Business day, shifts, staff scoping, immutable cash ledger and reconciliation.
2. Device/session lifecycle, cancellation/void policy, idempotency, concurrency and server-side billing.
3. Inventory/POS with atomic stock and financial ledger entries.
4. Admin reports reconciled to source ledgers.
5. Backup/restore rehearsal and controlled venue pilot.

The divergent inventory branch remains review-only until its model snapshot/migrations, cash ledger, idempotency, and tests are reconciled.

## Immediate execution order

1. Complete the current AUTH-03 authentication audit hardening slice and verify its audit persistence test path.
2. Implement A3 refresh-token hashing, rotation, replay detection, session-family revocation, and tests.
3. Implement A4 claims-backed current-user context and role/resource/shift ownership policies.
4. Finish A5 endpoint inventory and protection with anonymous 401, role 403, ownership, and shift/session tests; keep operational access closed until this passes.
5. Perform browser visual QA for B1 and B4 in Light/Dark at desktop/tablet/mobile breakpoints.
6. Build the dedicated Staff shell and integrate login/password-change/session lifecycle only after A3–A5 contracts are stable.
7. Connect real dashboard analytics after source-of-truth financial, session, inventory, and shift contracts are implemented and reconciled.

## Definition of done for every vertical

- Business rules and acceptance examples documented before coding.
- Validation and meaningful error handling.
- Server-side authorization and ownership checks.
- Database constraints for concurrency-sensitive invariants.
- Transaction boundaries and idempotency for money/stock/session writes.
- Unit tests for domain rules/calculations.
- Integration tests for persistence, constraints, transactions, and concurrency where relevant.
- UI loading, empty, success, validation, server-error, and permission-denied states.
- Audit trail for sensitive/financial actions.
- Build, tests, code review, STATE update, and evidence before marking VERIFIED.

## Guardrails

- No merge to `main`.
- Do not apply the pending AUTH-02 Identity migration as part of these code tasks.
- No production or `GHOST2` access.
- No unreviewed inventory branch merge.
- Do not claim local build/test success without an actual run and output.
