# GHOST Financial Design: Owner Decisions Walkthrough (v3, revised after owner review of 670be9b)
Status: DESIGN ONLY, for owner approval. Nothing is implemented. No code, table or migration until final approval. Extends FINANCIAL-ARCHITECTURE-GAP-ANALYSIS.md.

## 0. Owner rulings applied in v3 (message after his review of 670be9b)
1. **Paid-now = 0 in every case, including old/legacy API clients.** No path deducts the receipt total automatically. A client that omits the field, or an old client that never knew it, creates an unpaid (credit) invoice. The drawer is touched only by an explicit payment of an actual amount.
2. **Admin only:** expenses, supplier invoices, payments, financial categories. A Staff cash movement is **not** an expense approval. Staff intake has no payment fields at all. If Staff hand over cash from the drawer on the spot, they record a plain cash-out (Unclassified, a drawer fact only). It becomes a payment or expense only when Admin links or classifies it (section 2A).
3. **OtherExternal allowed**, but it must document payment method, reference, beneficiary and payer, and it is kept apart from the GHOST drawer (no CashTransaction row, shown as "paid outside drawer").
4. **Supplier Credit on correction: approved.** Cash returns to the drawer only when money is actually received (explicit supplier refund entry).
5. **AvgCost after partial sale: more detail below (section 2, cases C, F, G, H)** with UnitCostSnapshot and past COGS untouched, and an explicit line for any inventory cost difference.
6. **Expenses by due month (accrual)**, with the payment date shown separately.
7. **Net sales and refunds by the date each happened**, with a visible refunds line.
No financial code and no migration 17+ will be written until he approves THIS revised document. The Rebill column (migration 17) was written earlier as non-financial work; it is moved off the working branch to a local branch (`hold/rebill-migration17`, not pushed) until he says go.

## 0.1 Staged readiness (never one stage as evidence of the next)
| Stage | Meaning | Status today |
|---|---|---|
| S1 Build successful | compiles, unit tests pass | Yes: 275 unit tests on the working branch |
| S2 Package assembled | release folder and zip produced with checksums | Pieces built and zipped; script not run end to end |
| S3 Installer executed | installer scripts run on a Windows machine | NOT RUN |
| S4 Clean Windows installation passed | clean PC without VS, Node or dev tools, offline, after restart, service restart, backup/restore, uninstall keeps DB and backups | NOT RUN |
| S5 SQL Server migrations applied and verified | migrations 13-16 on a scratch SQL Server with verification queries; the real GHOST DB never | NOT RUN |
Production Ready needs S3 to S5 on a real machine. UNKNOWN counts as FALSE.

## 1. Notation used in examples
Amounts in whole EGP. Product P. "AvgCost" is the moving weighted average. Drawer = expected cash of the open shift. Payable = owed to suppliers.

## 2. The hard case: rejection or correction after some units were sold
### Rules (proposed)
1. **History never changes.** Past sales keep their `UnitCostSnapshot`. Past COGS and past profit are never recalculated.
2. **Stock can only be reduced by what is still on hand.** Correction or rejection of q units is blocked for the part already sold (today's reject already refuses when any product was sold, and the correction is capped by the original quantity but not by on-hand stock: tightened to on-hand).
3. **AvgCost is recomputed when unsold units are removed**, using the cost of the receipt line: newAvg = (onHand x avg - u x lineCost) / (onHand - u). If onHand - u = 0, AvgCost is kept as last known cost. This stops reversed units from leaving a wrong average that later COGS would inherit.
4. **Money difference on the sold part is recognised once, now, as a separate line "Purchase correction variance"** in the period of the correction. It is not pushed back into past COGS. It appears on the profit report next to COGS so total profit over time stays right and past months remain untouched.
5. **The supplier invoice is adjusted by a credit note** for the corrected amount (quantity x line cost). Cash moves only if the supplier really refunds (explicit supplier refund on the open shift).
6. Every step is an append-only entry with mandatory reason, employee and time.

### Worked example A: intake overstated, none sold
Start: P on hand 10, AvgCost 20. Intake of 10 at 30 on credit (paid now 0). Invoice 300. After intake: on hand 20, AvgCost 25, inventory value 500, payable 300, drawer unchanged.
Admin finds only 6 were really delivered. Correction of 4 units, reason "short delivery".
- Stock: 20 -> 16. AvgCost: (20x25 - 4x30)/16 = 23.75 (rounded to 2 decimals in cost, whole EGP in prices). Inventory value 380.
- Invoice: credit note 4x30 = 120. Payable 300 -> 180.
- Drawer: unchanged (nothing was paid). Profit: unchanged (inventory and payable moved together).
- Reports: purchases invoiced net 180; stock value 380; payables 180.

### Worked example B: same, but invoice was paid 300 from the drawer
Same stock effects. Credit note 120, invoice becomes overpaid by 120: shown as **supplier credit 120** (owed to us). Drawer unchanged until the supplier hands back 120. When Admin records a supplier refund of 120 on the open shift: drawer +120. Payables: -180 net vs. credit closed. This answers the open question: no automatic cash return.

### Worked example C: 6 of the 10 received units were already sold, then the intake is found wrong (supplier price was 25, not 30)
Start as A: after intake on hand 20, AvgCost 25. Sold 6 at AvgCost 25: COGS snapshots 150 (history, stays). On hand 14, value 350. Invoice 300 (paid 0).
Price correction of the whole intake: line cost 30 -> 25, invoice total 300 -> 250, credit note 50.
- Real cost of the 10 received units dropped by 50. 6 of those units are sold, 4 are in stock-share terms unsold of that receipt (10 units less 6 sold = 4 attributed to the receipt under weighted average).
- Unsold share: 4 units x 5 = 20 reduces inventory value: 350 -> 330, AvgCost 330/14 = 23.57.
- Sold share: 6 x 5 = 30 recognised as **Purchase correction variance +30** (gain) in the correction period. Past COGS (150) is NOT edited.
- Payable: 300 -> 250. Drawer unchanged. Total effect on profit across time: COGS 150 + variance -30 = 120 at the true cost of 20+... (true cost of those six units = 6x(weighted at true cost)); the combined result matches what the real cost would have produced, without rewriting any past month.
Why not edit past COGS: past reports, shift reports and any figures already shown to the owner would silently change.

### Worked example F: quantity correction when another receipt came after (mixed average)
Start: on hand 10 avg 20 (200). R1: 10 at 30 -> 20 on hand, avg 25 (500). Sell 6 at 25 (COGS 150, snapshots frozen) -> 14, value 350. R2: 6 at 40 -> 20 on hand, value 590, avg 29.50.
R1 found short by 2 units (cost 30). 20 on hand covers 2, so allowed: remove 2 at R1's line cost 30: value 590 - 60 = 530, on hand 18, avg 29.44. R1 invoice credit note 60. Variance: none, because the missing units really are missing from stock now. Past COGS 150 untouched. Check: value 530 = 14 x 25 + 6 x 40 - 60 = 350 + 240 - 60 = 530.

### Worked example G: price correction after everything was sold
Start as C but all 20 units sold before the correction (6 + 14). R1 price found to be 25 instead of 30 (credit note 50 for 10 units). On hand 0, so no stock value to change. All 50 is booked as Purchase correction variance +50 in the correction period (a gain). AvgCost: kept as last known cost, then follows the next purchase. Past COGS and past profit are NOT edited, so the month those sales were reported still shows its original figures; the correction month shows +50 clearly labelled.

### Worked example H: how the unsold share is decided
Rule (assumption, owner to confirm): units of a receipt still unsold = receipt quantity minus units sold since that receipt, capped at what is on hand (FIFO attribution). Example C: 10 received, 6 sold since, so 4 unsold (value drops 4 x 5 = 20) and 6 sold (variance 6 x 5 = 30). Weighted average cannot trace individual units, so this is an approximation by design. Alternative if the owner prefers: put the entire difference into the variance line and leave stock value alone (simpler, slightly less exact). Either way UnitCostSnapshot and past COGS never change. The dashboard reconciliation: inventory value = sum(quantity x AvgCost) must equal opening value + purchases net of credit notes - COGS - losses +/- correction effects.

### Worked example D: quantity correction larger than what remains
Intake 10, all 10 sold, then Admin says "only 6 were delivered". Blocked: on hand is 0, the sold units physically existed. Options shown to Admin: (1) correct the invoice only (credit note, supplier price dispute) with variance recognised; (2) record a stock adjustment (shortage) with reason; (3) cancel. Stock is never allowed to go negative by a correction.

### Worked example E: rejecting a whole intake
- Nothing sold: stock out, AvgCost recomputed, invoice Cancelled (credit note for the full amount), no cash unless refunded.
- Part sold: rejection blocked for the whole receipt. Admin must correct the unsold part (example C/D) and settle the sold part by credit note and variance.
- Rejected with "ChargeToStaff": creates an employee charge for the amount (what the staff member owes), shown in the employee report and settled later; never mixed with the supplier invoice.

## 2A. Payment examples: drawer, employee pocket, external
Supplier invoice 1,000 on credit (stock +1,000, payable 1,000). Drawer 2,000 (open shift). No profit effect at any step below.
1. **Drawer payment by Admin, 400:** one cash-out row on the open shift, linked to payment 1. Drawer 2,000 -> 1,600. Payable 600. Cash flow out 400.
2. **Employee pocket, 300:** Ahmed pays the supplier 300 from his own money. No drawer row. Payable 600 -> 300. A "due to employee Ahmed 300" item is created. Drawer 1,600.
   - Later Admin reimburses Ahmed 300 from the drawer: one cash-out row (Reimbursement). Drawer 1,600 -> 1,300. Due to employee 0. Payable unchanged (300). Not an expense, so profit is not charged twice.
3. **External, 300 (owner bank transfer):** method OtherExternal, reference "TRX-8841", beneficiary "Supplier X", payer "Owner". No drawer row. Payable 300 -> 0. Drawer 1,300.
Result: invoice Paid (400 + 300 + 300), payable 0, drawer 1,300 (= 2,000 - 400 - 300 reimbursement), profit unchanged, stock value +1,000. Cash flow report: drawer out 700 (400 payment + 300 reimbursement); "paid outside drawer" 300.
4. **Staff on-the-spot cash buy (no payment rights):** Staff pays supplier 150 cash from the drawer and records a cash-out "supplier cash". Drawer -150 at once (Unclassified). Admin links that same cash row to the invoice as a Drawer payment: payable -150, still one drawer row, never two. If Admin never links it, it stays visible as Unclassified.
5. **Over-payment** is rejected (remaining 0). **No open shift:** a Drawer payment is blocked; OtherExternal and EmployeePocket are not blocked (they do not touch the drawer).

## 3. The seven owner decisions with practical impact
Base example for all: P on hand 10, AvgCost 20, drawer 1,000, payables 0.

### Decision 1: Approve the three-layer model and accrual profit
Intake of 10 units at 30 on credit (paid now 0).
- Stock: +10, AvgCost 25, stock value 200 -> 500 (+300).
- Drawer: 1,000 unchanged. Payable: +300.
- Profit/reports: purchases are not an expense. Profit unchanged. Stock value report +300.
Later sale of 4 units at 50: revenue 200, COGS 4x25 = 100, profit 100. Drawer +200 (sale). Supplier paid 100 from the drawer later: drawer 1,100, payable 200, profit unchanged (payment is not an expense). Net profit changes only through sales, COGS and expenses.
Under the old way (cash at intake) you would have expensed 300 at purchase and 100 again at sale: that double counting is what this model prevents.

### Decision 2: Correcting a costed intake: supplier credit, cash only on supplier refund
See examples A and B. Impact: stock down, payable down (or credit up), drawer only moves when real money comes back, so the drawer always matches the physical cash.

### Decision 3 (revised): Admin-only finance, paid-now always 0, Staff cash-out is not an approval
- Staff registers intake of 300 (credit). Drawer 1,000 unchanged. Payable +300. No payment field on the Staff screen.
- Staff buys cleaning supplies for 40 and takes 40 from the drawer: Staff records a **cash-out entry** (Unclassified, with reason). Drawer 1,000 -> 960 immediately (the cash is gone). It is **not** an expense yet. It shows in the shift report under "Unclassified cash-outs 40" and the dashboard shows it as a warning line.
- Admin later classifies it: as expense (Maintenance, month, payee), or as a payment to a supplier invoice (links the SAME cash row, no new drawer movement), or rejects it (reversal entry with reason, charged to staff or recorded as shortage). Until then it never enters profit or payables.
- Why: a Staff hand on the drawer cannot create a cost approved for profit purposes.

### Decision 4 (revised): OtherExternal with full documentation
Supplier invoice 1,000. The owner pays 300 by bank transfer. Entry: method OtherExternal, reference "TRX-8841", beneficiary "Supplier X", payer "Owner", date, notes, Admin only. Payable 1,000 -> 700. Drawer unchanged (no cash row). Reports: payables -300, "paid outside drawer +300", profit unchanged. Reversal only by a reversal entry with reason. Full three-way example in section 2A.

### Decision 5: Fix AvgCost on reversal and correction
Without the fix (example A): after removing 4 units at 30, AvgCost stays 25, so inventory value is 16x25 = 400 instead of the real 380, and later sales carry 1.25 too much cost per unit. With the fix AvgCost 23.75 and value 380 = real cost of what remains. Impact: future COGS and profit are accurate. Historical COGS unchanged. Risk: changes AvgCost values visible on the product screen after a correction; needs scratch tests with sequences of purchase, sale, correction, purchase.

### Decision 6 (approved as net-by-event-date): Refund presentation
Refund of a sale: revenue 200, COGS 100, refund 200, returned stock 4 units at snapshot cost 25.
- Today: refund shown separately, product cost keeps 100 (profit looks 100 lower than real after the refund).
- Proposed: net sales = 200 - 200 = 0 in the refund period, COGS = 100 - 100 (returned units at their snapshot cost) = 0, profit 0 for that item. If the product is not returned to stock (damaged): COGS stays 100 and a loss of 100 shows. Drawer: -200 on the open shift. Stock: +4 only when returned.
Past periods are not edited: the refund affects the period it happens in. The dashboard keeps a visible 'Refunds' line (gross sales, refunds, net sales) so refunds are never hidden inside net.

### Decision 7 (approved): Rent and utilities by due month, payment date shown separately
Rent 5,000 for November, due Dec 5, paid Dec 3 from the drawer.
- November profit: -5,000 (expense dated November). December profit: unaffected by this rent.
- Payable (expenses owed): 5,000 from Nov 1 until paid. Drawer: -5,000 on Dec 3 (December cash flow).
- Reports: accrual profit shows rent in November; cash flow shows it in December; reconciliation links both.
If cash basis were chosen: profit would show the rent in December, and November would look 5,000 better than it was. Recurring rent: template generates the November expense on Nov 1 automatically (idempotent); changing rent to 5,500 from January leaves earlier months at 5,000.

## 4. Additional tests required by these rulings (legacy clients: omitted paid-now creates credit; Staff has no payment endpoint; external payment requires method, reference, beneficiary)
Paid-now omitted creates zero drawer rows; paid-now > drawer cash is rejected; credit invoice reject; examples A to E as unit tests with exact numbers (stock, AvgCost, payable, drawer, variance); correction capped at on-hand; past COGS snapshots unchanged after correction (assert equality before and after); concurrent correction and sale of the same product (SQL scratch: no negative stock, no lost update on AvgCost); concurrent two payments; rebill creates link and keeps old invoice; second live invoice for the same session fails; replay of the same idempotency key.

## 5. Legacy receipt migration: proof required (before it may touch any DB)
Existing receipts already took cash at intake. The migration (scratch SQL Server only, then the real DB only on his explicit approval) creates, per legacy receipt, one supplier invoice and one fully-paid Drawer payment that POINTS AT the existing cash row. It creates no cash rows and no stock movements.
Comparison tests (run on a scratch DB loaded through the real handlers, not hand-written rows), before and after, all differences must be zero:
- per shift: ExpectedCash, opening, sum of cash rows by type;
- per product: StockQuantity, AvgCost, count and sum of StockMovements;
- total count of CashTransactions and StockMovements unchanged;
- every legacy receipt has exactly one invoice and one payment, each payment linked to exactly one existing cash row, no cash row linked twice (a unique index proves it);
- running the migration twice changes nothing (idempotent);
- reports before and after for the same period: revenue, COGS, expenses, drawer totals identical (payables show 0 owed for legacy);
- a failed or rolled-back run leaves the DB as before (transaction test).
Reject of a legacy (already-paid) receipt after migration produces a supplier credit, not an automatic cash return.

## 6. Open items for the owner
1. Confirm the unsold-share rule (FIFO attribution) or choose "whole difference to variance" (example H).
2. Confirm that a correction larger than what is on hand is blocked with the three choices in example D.
3. Confirm the Unclassified cash-out flow for Staff (2A item 4) is acceptable, since Staff have no payment rights.
4. Confirm that Rebill (migration 17, parked) can resume now, or waits with the financial migrations.
After final approval the estimate stays about 38 h plus about 4 h for these cases and the legacy comparison tests.
