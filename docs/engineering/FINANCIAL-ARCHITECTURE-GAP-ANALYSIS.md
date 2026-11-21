# GHOST Financial Architecture: Gap Analysis and Proposed Design
Status: DESIGN ONLY, for owner review. No code, tables or migrations were created for this. Nothing here touches any database. Every number below was read from the code on branch `feature/auth03-refresh-tokens`.

## 0. Summary in five lines
1. Today GHOST is a drawer ledger plus stock ledger. It has no supplier, no payable, no accrual expense, no recurring expense and no net profit.
2. The conflict he found is real and bigger than "cash at intake": `StockReceipt.Receive` calls `shift.RecordCash(StockPurchase, Out, TotalCost)`. A credit purchase cannot exist, and a receipt larger than the drawer is blocked by the "drawer does not hold enough cash" rule.
3. The fix is three layers that never share a number: **Inventory** (asset, cost per unit), **Obligations** (supplier invoices and expenses, recognised once), **Settlements** (payments, each linked to one shift and one cash movement or one employee reimbursement).
4. Profit is accrual: Net sales minus COGS minus operating expenses minus inventory losses. Purchases are never an expense. Cash flow is cash basis from the drawer ledger. A reconciliation line proves the two agree.
5. Answer to his open question (correcting a costed intake): no automatic cash return. The correction creates a supplier credit. Cash returns to the drawer only when the supplier actually refunds, recorded as an explicit supplier refund on the open shift.

## 1. What exists (evidence)
| Area | Facts from code |
|---|---|
| StockReceipt | Staff or Admin create. `Receive`: validates lines, sets TotalCost, takes TotalCost out of the shift drawer (type StockPurchase, key `receipt:{id}`), writes a Purchase movement per line (updates AvgCost), status Pending. Fields: shift, creator, SupplierName (free text), InvoiceReference, Notes (added in migration 16). No supplier entity, no invoice date, no due date, no paid or remaining amount. |
| Approve / Reject | Approve only flips status (no money effect). Reject writes PurchaseReversal per line (fails if sold), then ReverseCash (full TotalCost back into a given open shift) or ChargeToStaff. No partial case. |
| Correction | New IntakeCorrection movement (stock only, no cash, no AvgCost change). |
| CashTransaction | Append-only (trigger). Types: OpeningBalance, SaleCash, Expense, Withdrawal, StockPurchase, StockPurchaseReversal, HandoverOut, HandoverIn, Refund, Adjustment. Fields: shift, type, direction, amount, category, reason, beneficiary user, SourceType/SourceId, idempotency key, creator, time. `Shift.RecordCash` keeps ExpectedCash and blocks a negative drawer. |
| Expenses today | `POST shifts/{id}/expenses`: Staff on own open shift or Admin. A cash Expense row with free-text category and reason. Paid at the moment it is recorded. No payee, no expense date different from now, no receipt image, no recurrence, no accrual. |
| Sales | Sale, SaleItems with `UnitCostSnapshot = Product.AvgCost` at sale time (so COGS is frozen per line), Payment (cash only), Refund (Admin, whole invoice, optional return to stock, cash out of an open shift, now blocked unless the shift is open). |
| AvgCost | Moving weighted average, updated only on Purchase movements. NOT updated on PurchaseReversal or IntakeCorrection (they remove units at the old average). |
| Reports | `DashboardSummary`: gross revenue, gaming, product revenue, discounts, refunds, net revenue, product cost (sum of snapshots including refunded invoices), product profit, Expenses (cash Expense rows), Withdrawals, StockPurchases (cash StockPurchase rows only), counts, top products, low stock. Shift and employee reports. No net profit, no payables, no cash-flow statement, no per-category expenses. |
| Permissions and audit | Reports Admin only. Receipts and expenses open to Staff for their own shift. Audit rows exist for receipt, expense (cash), refund, adjustments. Rejected attempts now audited (this batch). |

## 2. What is missing
Supplier master data; supplier invoice with number, date, due date, total; payments (partial, full, on account); supplier balance and statement; supplier credit and refund; expense master (category list, payee, receipt image, period); recurring expense templates with amount history and pause; payment method concept (drawer, employee pocket, other); employee reimbursement balance; net profit and cash-flow reports; inventory value report; reconciliation.

## 3. Where credit purchases conflict (all must change together)
1. `Receive` always deducts full cost from the drawer (credit impossible, partial impossible).
2. Drawer sufficiency check blocks a large purchase even when it is not paid in cash.
3. `Reject` + ReverseCash returns the full TotalCost, wrong when only part was paid or nothing was paid.
4. Dashboard `StockPurchases` and the shift report use cash rows, so an unpaid purchase would be invisible in "purchases" and in debts.
5. AvgCost is not reversed on rejection or correction, so inventory cost and later COGS drift upward after any reversal.
6. "ChargeToStaff" outcome has no ledger for what the staff member owes.
7. Staff can record any cash expense; the new rule says Admin manages expenses.

## 4. Proposed model: three layers
### Layer A, Inventory (asset). Exists. Keep.
StockMovements carry quantity and unit cost; Product has quantity and AvgCost. Value of stock = sum(quantity x AvgCost). Purchases increase this asset. They are never an expense.

### Layer B, Obligations (what we owe, recognised once)
- **Supplier**: name, phone, notes, active flag, optional default payment terms (days).
- **SupplierInvoice**: supplier, supplier invoice number, invoice date, due date, total, receipt id (one receipt maps to one invoice), status Open / PartiallyPaid / Paid / Cancelled, created by, created at. Unique (supplier, invoice number). Total is the gross payable; derived fields (paid, remaining) are computed from payments, never typed.
- **SupplierInvoiceAdjustment** (append-only): credit note or correction with signed amount, mandatory reason, link to the original invoice and, for intake corrections, to the correction movement. Remaining = total + adjustments (negative for credits) minus payments.
- **OperatingExpense**: category (Rent, Maintenance, Electricity, Water, Internet, Operations, Other, extensible list), amount, expense date and period month, payee text, notes, receipt file reference, optional recurring template id, status Open / PartiallyPaid / Paid / Cancelled. Cancelling is a reversal row, never a delete.
- **RecurringExpenseTemplate**: category, payee, frequency (monthly first), day of month, start, optional end, paused flag. **TemplateAmountHistory** (effective from, amount, who, why): editing the amount creates a new history row and only affects periods not yet generated. A background job creates the Open expense for each period; unique (template, period) makes it idempotent after restarts or power cuts.

### Layer C, Settlements (money actually moving)
- **Payment** (one table or two, same shape): target (SupplierInvoice or OperatingExpense), amount, date, **method**, shift id (only for Drawer), cash transaction id (only for Drawer), paid-by employee (only for EmployeePocket), notes, created by. Append-only. A correction is a Reversal row with reason, not an edit.
- Methods:
  - **Drawer**: must have an open shift. Writes exactly one `CashTransaction` (Out) on the shift open at payment time, with SourceType = payment type and SourceId = payment id, idempotency key `payment:{id}`. Unique index payment to cash row, so one payment can never create two drawer movements and one drawer movement can never pay two things.
  - **EmployeePocket**: no drawer movement. Creates an **EmployeeReimbursable** (employee, amount, source payment, status Due / Settled). The obligation (invoice or expense) is reduced, because the supplier or landlord has been paid.
  - **OtherExternal** (decision needed): paid outside the drawer by the owner, for example a bank transfer. Reduces the obligation, no drawer effect, labelled in reports.
- **Reimbursement settlement**: Admin pays the employee from an open shift: one cash Out row (type Withdrawal, category Reimbursement, beneficiary = employee) that settles the EmployeeReimbursable. Never counted again as an expense.
- **SupplierRefund** (payment in): when a supplier returns money for a returned or corrected invoice, one cash In row on the open shift, linked to the adjustment. This is the only way cash returns for a purchase.

### What happens at intake (replaces today's behavior)
Staff register the receipt as today (supplier, items, cost, invoice number, notes). The receipt can carry "paid now from my drawer: amount". If paid now > 0, one Drawer payment is created in the same transaction (this keeps the common case where staff buy with drawer cash, and keeps today's numbers for that case). If paid now is 0 the purchase is on credit and the drawer is untouched. Admin sees open invoices and pays later. The drawer check applies only to the paid-now amount.

### Rejection and correction, coherent
- Reject unpaid receipt: stock reversed, invoice Cancelled, no cash.
- Reject partly or fully paid receipt: stock reversed, invoice Cancelled with a supplier-credit adjustment for what was paid. Cash returns to the drawer only if Admin records a SupplierRefund (or the credit is later used against another invoice). The old automatic ReverseCash option becomes "record the supplier refund now" in one step on the open shift, so Admin still has the quick path.
- ChargeToStaff: creates an **EmployeeCharge** (what the staff member owes the venue), settled by deduction or cash, visible in the employee report.
- Intake correction (his open question): stock reduction plus a credit-note adjustment on the invoice for quantity times unit cost. Remaining balance drops, or a supplier credit appears if already paid. No automatic cash move.

## 5. Profit and cash flow without double counting
Definitions (period P, Egypt time):
- **Net sales** = sum of Paid sale totals in P minus refunds recorded in P (refunds by refund date). Discounts already inside totals.
- **COGS** = sum(quantity x UnitCostSnapshot) of sold lines in P minus the cost of items returned to stock by refunds in P (the SaleReturn movement carries the snapshot cost). Today refunded invoices keep their cost in "product cost"; this fixes it.
- **Operating expenses** = OperatingExpense amounts with expense date in P (accrual, by category), not their payments. Credit notes reduce them.
- **Inventory losses** = Waste + Expired + negative stocktake adjustments, valued at AvgCost (they are expenses; positive stocktake surplus is a gain).
- **Net profit (accrual)** = Net sales - COGS - Operating expenses - Inventory losses.
- **Purchases** appear in their own lines (invoiced in P, paid in P, owed at end of P) and are NOT in profit. Stock bought and unsold sits in inventory value.
- **Cash flow (cash basis, from the drawer ledger)**: In = sales cash, supplier refunds, other cash in. Out = refunds to customers, supplier payments from drawer, expense payments from drawer, reimbursements, withdrawals and advances. Closing = sum of shift ledgers. Employee-pocket and external payments are shown below the line as "paid outside the drawer".
- **Reconciliations shown on the dashboard** (any mismatch is flagged red):
  1. Opening stock value + purchases invoiced (net of credits) - COGS (at cost) - losses +/- stocktake = closing stock value.
  2. Payables: opening + invoiced + adjustments - payments (all methods) = closing payables.
  3. Drawer: opening + cash in - cash out = expected cash (already enforced per shift).
Why no double count: a purchase touches only inventory and a payable; its payment touches only the payable and the drawer; COGS is taken when the item is sold; expenses are recognised once at expense date and settled separately.

## 6. AvgCost issue to fix inside this design
Reversal and correction should remove units at the cost of the movement being reversed and recompute the average: newAvg = (qty x avg - q x cost) / (qty - q), guarded for zero stock. Without it, COGS after any reversal is overstated. This is a cost-accuracy change, so it needs his approval and tests.

## 7. Linking payments to shifts and cash rows
Rule (same as his refund decision): a Drawer payment is posted to the shift open at payment time, by the acting Admin, and records the invoice or expense reference. Blocked when no valid open shift. The original invoice or expense date is kept separately. One transaction writes payment row, cash row, shift ExpectedCash change, invoice status and audit.

## 8. Permissions and audit
- Admin only: suppliers, invoices, adjustments, payments, refunds from suppliers, expenses, recurring templates, reimbursements, financial reports.
- Staff: register intake (with supplier and optional paid-now from own open shift drawer, as today). Decision needed: may Staff still record small drawer expenses (petty cash)? Proposal: yes but created as "Pending Admin review" expense with the drawer movement, so the drawer stays correct and Admin classifies it.
- Audit actions (examples): Supplier.Created/Changed, SupplierInvoice.Created/Adjusted/Cancelled, Payment.Recorded/Reversed, Expense.Created/Cancelled, RecurringExpense.Created/AmountChanged/Paused/Generated, Reimbursement.Due/Settled. Rejected attempts audited. All financial rows append-only (triggers) and corrected only by reversal rows with reason.

## 9. Integration and data safety
- Existing receipts already took cash at intake. Migration plan (scratch DB first, never the live DB without approval): for each existing receipt create an invoice with a fully-paid Drawer payment that points at the EXISTING cash row (no new cash rows), supplier from the free-text name (new Supplier rows, merged by Admin later). Totals of the drawer do not change.
- Existing Expense cash rows stay valid: they appear as expenses with category "Uncategorised (legacy)" and a payment linked to the existing row.
- Existing handlers keep their routes; `CreateReceipt` gains optional fields (supplier id, invoice date, due date, paid now). Old clients keep working with paid now = total, the current behavior.
- Existing tests stay valid except those asserting full drawer deduction when paid-now is omitted; the default is kept equal to today's behavior to avoid silent change.

## 10. Required tests (all on scratch databases for SQL ones)
Unit: credit purchase leaves drawer untouched; partial then full payment, status transitions and balance; overpayment rejected; payment without open shift rejected; supplier balance and statement; invoice credit note from intake correction; reject unpaid, partly paid and paid receipt; supplier refund returns cash once; employee pocket payment creates a reimbursable and no drawer row, settlement creates exactly one row; recurring generation is idempotent per period, amount change affects only future periods, pause stops generation; expense with attachment reference; reversal of a payment; AvgCost recompute on reversal and correction; profit formula (no purchase counted as expense, COGS only at sale, refund returns COGS); cash-flow totals; the three reconciliations; Admin-only authorization for every new endpoint (role matrix); rejected attempts audited.
SQL Server scratch: two concurrent payments on one invoice (never overpay); concurrent payment and cancellation; same idempotency key twice creates one payment and one cash row; unique payment-to-cash-row index; recurring generator run twice or from two processes creates one expense; append-only triggers on payment, adjustment and ledger tables; migration of legacy receipts leaves drawer totals unchanged (before and after comparison).

## 11. Implementation plan (after approval) and effort
1. Entities, migration on scratch, invariants and domain tests (Supplier, Invoice, Adjustment, Payment, Expense, Template, Reimbursable): about 8 h.
2. Receipt integration, reject and correction paths, legacy backfill, AvgCost fix: about 6 h.
3. Recurring generator, reports and dashboard changes, reconciliations: about 7 h.
4. Admin screens (suppliers, invoices and payments, expenses, recurring, reimbursements, financial dashboard): about 12 h.
5. SQL scratch tests, docs, status report: about 5 h.
Total about 38 h of my work, mostly sequential on step 1.

## 12. Decisions needed from the owner
1. Approve the three-layer model (inventory, obligations, settlements) and accrual profit definition.
2. Intake correction on a costed receipt: supplier credit, cash back only on explicit supplier refund (recommended) or immediate cash return?
3. Keep Staff "paid now from my drawer" at intake (recommended), and Staff petty-cash expenses as Pending Admin review?
4. Payment method "OtherExternal" (not drawer, not employee) allowed?
5. Fix AvgCost on reversal and correction (changes inventory cost and later profit)?
6. Dashboard refund presentation: keep "gross shown, refund shown" or move to net-only with refunds netted in the period they happen (recommended, plus COGS return)?
7. Rent and utilities: accrual by expense month (recommended) even when paid later?
