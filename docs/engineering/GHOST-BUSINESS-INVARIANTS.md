# GHOST Business Invariants

Extracted from the code on branch `feature/auth03-refresh-tokens`. Each invariant names where it is enforced and what proves it.
Proof levels: **U** = automated unit test passes, **S** = SQL Server scratch test (script ready, NOT yet run), **C** = DB constraint or trigger (migration, NOT yet applied on a real SQL Server), **-** = no proof yet.
Rule: an invariant marked only "-" or "S/C not run" is UNKNOWN, and UNKNOWN counts as FALSE for readiness.

## Authentication and authorization
| ID | Invariant | Enforced in | Proof |
|----|-----------|-------------|-------|
| AU-1 | No permission comes from hiding a UI element. Every action has a server policy. | `[Authorize(Policy=...)]` per action | U: `EndpointRoleMatrixTests` (every controller action listed with its expected Admin/Staff/anonymous result; a new action without a row fails the test) |
| AU-2 | Stacked class and action `[Authorize]` attributes must all pass | per-action policies | U: same test (it caught the Staff lockout bug) |
| AU-3 | Refresh token: single use, rotation, reuse revokes the whole family, 7-day sliding with 30-day cap | `RefreshToken`, `RefreshTokenService` | U (token service tests); S/real restart behavior not run |
| AU-4 | Refresh and logout accept only configured local origins and a custom header | `RefreshRequestGuard`, `LocalOriginPolicy` | U: `LocalOriginPolicyTests`, guard tests |
| AU-5 | API listens on loopback only; wildcard or LAN address fails startup | `LocalBindingPolicy` | U: `LocalBindingPolicyTests` (13 cases); run: wildcard URL refused at startup |
| AU-6 | Inactive users cannot act | JWT validation `SessionTokenValidator` | U: identity tests; not run against real DB |
| AU-7 | Read APIs expose DTOs only: no password hashes, token hashes or security stamps | read DTOs in `OperationsReadQueries` | U: DTO shape tests; review only for overexposure |
| AU-8 | Forced password change blocks other operations | MustChangePassword flag | NOT VERIFIED here (needs review, see Not Tested) |

## Devices and sessions
| ID | Invariant | Enforced in | Proof |
|----|-----------|-------------|-------|
| DS-1 | A device has at most one active session | `Device.MarkAsRunning` needs Available; start handler | U; S: not covered by a concurrent start test yet (gap, see TEST-STRATEGY) |
| DS-2 | A session cannot start on a device that is not available | `Device.MarkAsRunning` | U |
| DS-3 | Pause only when running, resume only when paused | `Device.MarkAsPaused/Resumed`, `SessionSegment` | U |
| DS-4 | Transfer only to an available device | `TransferSessionHandler` | U |
| DS-5 | Billing is recomputable: segments x pauses, rounded up once to a whole EGP | `SessionBillingCalculator` | U |
| DS-6 | A session waiting for power-cut review cannot be paused, resumed, completed, transferred or invoiced | `Session.EnsureSessionIsActive`, `SaleHandler` | U: `PowerOutageTests` |
| DS-7 | Outage time is never billed and no end time is invented; Admin sets the real end | `Session.MarkPowerOutage/ApproveAfterOutage` | U; S: SC-09 |
| DS-8 | Outage review can be approved only once | `Session.ApproveAfterOutage` (status and flag) | U; S: SC-09 |

## Shifts and cash
| ID | Invariant | Enforced in | Proof |
|----|-----------|-------------|-------|
| SH-1 | One open shift per staff user | `BusinessDay.OpenShift` | U |
| SH-2 | Closing needs an actual cash count; a difference needs a reason; staff never see expected cash before counting | `Shift.Close`, read DTO | U |
| SH-3 | Cash ledger is append-only | trigger `TR_CashTransactions_AppendOnly` | C+S: SC-02 (not yet run) |
| SH-4 | Every ledger row has type, amount, user, time, shift | `CashTransaction` | U |
| SH-5 | A failed backup never blocks shift close | background trigger after save | U |

## POS and inventory
| ID | Invariant | Enforced in | Proof |
|----|-----------|-------------|-------|
| PI-1 | Price, stock and totals come from the server | `SaleHandler`, `Sale.CheckoutCash` | U |
| PI-2 | Stock never goes below zero; sale and stock movement are one transaction | `Product.ApplyMovement`, RowVersion, single SaveChanges | U; S: SC-03, SC-04 (not yet run) |
| PI-3 | Same idempotency key returns the original invoice; same key with different content is rejected; identical carts with different keys are two sales | key + `RequestFingerprint` | U: `SaleHandlerIdempotencyTests`; S: SC-05, SC-06, SC-07 |
| PI-4 | Cart content is never the checkout identity | frontend creates a random key per checkout, regenerated after success or any cart change | code review; no browser test |
| PI-5 | An invoice can be refunded once | `Sale.RefundAll` needs Paid, RowVersion | U; S: SC-08 |
| PI-6 | Discount: Admin only, with a reason | `SaleHandler` | U |
| PI-7 | A session is invoiced once | `SessionIsInvoicedAsync` | U (logic); unique filtered index UX_Sales_SessionId_Live (live invoices Paid/Pending only, owner-approved 12:33; Rebill link ReplacesSaleId pending) |
| PI-8 | A receipt is approved or rejected once | `StockReceipt` status checks, RowVersion | U; S: not covered by a concurrent test yet |
| PI-9 | Stock movements are append-only | domain (no setter) + trigger TR_StockMovements_AppendOnly (migration 16) | C+S: SC-02 (not run) |

## Audit
| ID | Invariant | Enforced in | Proof |
|----|-----------|-------------|-------|
| AD-1 | Audit rows are written in the same save as the business change | `IAuditTrail`/`ISystemAuditTrail` stage rows | U |
| AD-2 | Audit log is append-only | trigger `TR_AuditLogs_AppendOnly` | S: existing AUTH-02 test (opt-in), SC-02 |
| AD-3 | Actor, time, entity, outcome recorded; system events (outage, backup failure) have actor type System | `AuditLog.Create` | U |
| AD-4 | Rejected and failed attempts are distinguishable | success is audited; rejections are not stored | GAP (decision needed: audit rejected attempts?) |

## Missing business rules found (need owner decision before changing money behavior)
1. Is a refund allowed in a different shift than the sale, and from which drawer does the cash come out? Current code takes `cashShiftId` from the request.
2. No unique database constraint stops two invoices for one session under a race (check is in code only). Proposed: filtered unique index on SessionId. Owner decision requested.
3. Rejected operations are not audited. Proposed: audit only security-relevant rejections (forbidden, discount denied).
4. Staff can create products and adjust stock. Keep?
