# GHOST State Machines

From enums and guards in `GHOST.Domain`. "Server" means enforced in domain code; "DB" means also enforced by a constraint or trigger (only where stated). Failure handling for all: the domain throws `BusinessRuleValidationException`, the handler returns a failure, nothing is saved; concurrent writers lose through RowVersion (`PersistenceConflictException`) and may retry.

## Device (`DeviceStatus`)
States: Available, Running, Paused, Reserved, Maintenance, Offline.
| From | To | Condition | Who | Side effects |
|------|----|-----------|-----|--------------|
| Available (or Reserved) | Running | session start | Staff, Admin | session + segment created, audit `Session.Started` |
| Running | Paused | pause | Staff, Admin | pause row opened |
| Paused | Running | resume | Staff, Admin | pause row closed |
| Running, Paused | Available | complete, cancel, void, transfer away, outage approval | Staff, Admin (outage approval: Admin) | session closed |
| Available | Reserved, Maintenance, Offline | admin action | Admin | none |
Forbidden: Running to Reserved/Maintenance/Offline; Paused to anything except Running/Available. Outage: a device stays Running until the Admin approves the session (design choice, no invented end).

## Session (`SessionStatus` + `NeedsReview`)
States: Active, Active+NeedsReview, Completed, Cancelled (Cancelled with `IsVoided`).
| From | To | Condition | Who | DB change | Audit |
|------|----|-----------|-----|-----------|-------|
| (new) | Active | device Available, open shift | Staff, Admin | Session, Segment | Session.Started |
| Active | Completed | open end shift | Staff, Admin | segment ended | Session.Completed |
| Active | Cancelled | inside window: any operator; after window: Admin with reason (voided) | Staff, Admin | Cancelled, no charge | Session.Cancelled / Voided |
| Active | Active+NeedsReview | start-up heartbeat gap over threshold | System | closed "Power outage" pause, outage times | Session.PowerOutageDetected |
| Active+NeedsReview | Completed | Admin gives end time not in future and not before start, open shift | Admin only | segment ended at that time, reviewer recorded | Session.OutageApproved |
Forbidden: any transition from Completed or Cancelled; any action except Admin approval while NeedsReview; second approval.

## Shift (`ShiftStatus`)
Open to Closed only: actual cash counted, difference needs reason. Opening needs open business day and no other open shift for that user. Who: Staff (own shift) or Admin. Audit: Shift.Opened, Shift.Closed. After close: background backup (never fails the close).

## Invoice (`SaleStatus`)
Draft, Paid, Voided, Refunded. Checkout creates Paid directly (cash only). Paid to Refunded: Admin, reason required, once. Voided is reserved and has no path in code today (document, not used). Print: any time, counts original then copy, audited.

## Refund
Not a separate entity: a refund is the Paid to Refunded transition plus an optional return to stock and a ledger row of type Refund. Admin only. Atomic with stock and cash.

## Purchase / Stock receipt (`StockReceiptStatus`)
Pending to Approved (Admin) or Rejected (Admin, reason, outcome ReverseCash or ChargeToStaff). Receiving creates Pending with stock and cash effects per code; rejection reverses them. Once decided, no further change.

## Stocktake (`StocktakeStatus`)
Open, Submitted, Approved, Rejected. Submit needs every product counted; approve (Admin) writes StocktakeAdjustment movements; reject needs a reason.

## Cash transaction
Immutable once written (append-only trigger). Corrections are new rows of type Adjustment, Refund or StockPurchaseReversal. No states.

## Refresh token family
Issued, Consumed (rotated to a replacement), Revoked (logout, reuse detected, family cap). Reuse of a consumed token revokes the family. Expiry: 7 days sliding, family cap 30 days.
