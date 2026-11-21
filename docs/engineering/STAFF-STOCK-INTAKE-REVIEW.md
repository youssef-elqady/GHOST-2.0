# Staff Stock Intake: review and plan

Owner requirement (WhatsApp, Nov 21 12:14): Staff register supply for existing or new products, every intake recorded with employee, product, quantity, cost, time, notes or invoice reference; corrections only as a separate linked entry with mandatory reason; employee and time immutable; Admin sees totals per employee, per operation, reports by employee, product, period; Intake / Sales / Adjustments / Returns kept separate; quantity belongs to the venue; no duplicate or double deduction.

## What exists (read from code)
- `StockReceipt` (+ items): Staff create it (StaffOrAdmin), records shift, creator, supplier, unit cost, expiry. Creation writes `Purchase` StockMovements and updates stock in the same transaction; cash for purchase is a ledger row. Admin approves or rejects; rejection writes `PurchaseReversal` and either reverses cash or charges staff.
- `StockMovement`: product, type, signed quantity, unit cost, balance after, shift, user, source type and id, reason, created time. No setters, so immutable in domain code. No DB trigger yet (gap).
- Staff can quick-add a product (stays PendingPricing; only Admin prices or enables) and record Waste, Expired and ManualAdjustment with a reason.
- Audit: StockReceipt.Created/Approved/Rejected, Stock.Adjusted, Product.Created.

## Decision: EXTEND, do not build new
The receipt flow already covers points 1, 2, 3 (partly), 5, 10. Gaps and planned changes:
1. Add `InvoiceReference` and `Notes` to StockReceipt (migration, validated length, shown in reads).
2. Correction: new movement type `IntakeCorrection` created by a new endpoint, mandatory reason, linked to the original movement (SourceType "StockMovement", SourceId original), only the original intake author or Admin, cannot exceed the original quantity, cannot be corrected twice beyond the remaining quantity, cannot take stock below zero. Original is never changed.
3. Append-only trigger on StockMovements (UPDATE and DELETE blocked), proven on a scratch DB (new SC test).
4. Reports (Admin): intake totals per employee, per product, per period, per operation; separate sections for Intake, Sales, Adjustments, Returns, based on movement type.
5. Frontend: Staff intake screen (existing or new product, quantity, cost, reference, notes) and correction action; Admin report view.
6. Concurrency: Product RowVersion already guards stock; add test on scratch DB for two simultaneous intakes and an intake racing a sale.
Open decision for owner: does a Staff intake need Admin approval before it counts (today stock is added immediately and Admin can reject afterwards, which reverses it)? Plan keeps the current "counts immediately, Admin can reject" behavior unless told otherwise. Purchase cost with cash from the drawer stays as is.

## Implemented so far (commit after 236442e)
- Receipts carry InvoiceReference and Notes (migration 16).
- Correction: POST /api/stock-intake/movements/{id}/correct (Staff own intakes, Admin any), new movement type IntakeCorrection, reason mandatory, linked to the original, capped by the original quantity, never below zero. It reduces stock only and does not move cash; fixing a too-small quantity is a new intake. Cash effect of correcting a receipt that took money from the drawer is an OWNER DECISION (not changed).
- Trigger TR_StockMovements_AppendOnly (migration 16, SC-02 extended, not yet run on SQL Server).
- Admin report: GET /api/stock-intake/report?rangeFrom&rangeTo (per employee and product: operations, net quantity, net cost). Per-operation detail is the existing receipt list and product movements.
- Tests: 6 unit tests (all pass). Total backend unit now 267 passed.
- Still to do: Staff intake and correction screens, Admin report screen, SC concurrency tests for intakes.
