# GHOST Financial Design: Owner Decisions Walkthrough (v2)
Status: DESIGN ONLY, for owner approval. Nothing is implemented. No code, table or migration until final approval. Extends FINANCIAL-ARCHITECTURE-GAP-ANALYSIS.md.

## 0. Changes from the owner's last message (adopted in this design)
- **Paid-now defaults to ZERO.** Credit purchase is the default intake. The drawer is charged only the amount actually entered as paid. An omitted field means 0, never "full". This replaces the "keep today's behavior" line in the gap analysis (section 4 and 9). Existing receipts are not changed (they already took cash, see migration).
  - Consequence to flag: any existing screen or API client that relied on automatic full deduction will now create unpaid invoices. The staff intake screen must show the paid-now field and a clear "unpaid, owed to supplier" label, and the shift close report must list intakes with unpaid amounts so nobody believes the drawer paid.
- **Session invoice rule (decision 1 final):** unique index stays on live invoices (Paid/Pending). A new invoice after a refund is a **Rebill**: it stores `ReplacesSaleId` (the old invoice, kept as is), reason (mandatory), employee and time. At most one live invoice per session (the existing index already guarantees it). Needs a small additive column; scheduled as non-financial approved work, separate from the financial module.

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

### Worked example D: quantity correction larger than what remains
Intake 10, all 10 sold, then Admin says "only 6 were delivered". Blocked: on hand is 0, the sold units physically existed. Options shown to Admin: (1) correct the invoice only (credit note, supplier price dispute) with variance recognised; (2) record a stock adjustment (shortage) with reason; (3) cancel. Stock is never allowed to go negative by a correction.

### Worked example E: rejecting a whole intake
- Nothing sold: stock out, AvgCost recomputed, invoice Cancelled (credit note for the full amount), no cash unless refunded.
- Part sold: rejection blocked for the whole receipt. Admin must correct the unsold part (example C/D) and settle the sold part by credit note and variance.
- Rejected with "ChargeToStaff": creates an employee charge for the amount (what the staff member owes), shown in the employee report and settled later; never mixed with the supplier invoice.

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

### Decision 3: Staff keep a "paid now" field (default 0) and petty cash expenses become Pending Admin review
- Intake of 300 with paid now 100 by Staff: drawer 1,000 -> 900, payable +200, invoice PartiallyPaid.
- Paid now above drawer cash is blocked ("drawer has 900"): paying more than what is in the drawer is not possible, credit is.
- Petty cash: Staff pays 40 for cleaning supplies from the drawer: drawer -40 immediately (cash is real), expense recorded as Pending Admin review (category empty). Admin classifies it (Maintenance) and it enters profit by expense date. Until classified it shows in an "unclassified" line, so profit is not silently overstated. If Admin rejects it: reversal entry with reason and a charge to the staff member or cash-over/short note.
- Reports: expenses list, unclassified total, shift report shows both.

### Decision 4: Payment method "OtherExternal" (owner pays from bank/own money outside the drawer)
Example: rent 5,000 paid by the owner by transfer. Expense +5,000 (accrual, month), obligation Paid, no drawer movement. Report: "paid outside drawer 5,000". If not allowed, such a payment must be entered as drawer cash in plus drawer cash out, which fakes the drawer history. Recommendation: allow, label clearly.

### Decision 5: Fix AvgCost on reversal and correction
Without the fix (example A): after removing 4 units at 30, AvgCost stays 25, so inventory value is 16x25 = 400 instead of the real 380, and later sales carry 1.25 too much cost per unit. With the fix AvgCost 23.75 and value 380 = real cost of what remains. Impact: future COGS and profit are accurate. Historical COGS unchanged. Risk: changes AvgCost values visible on the product screen after a correction; needs scratch tests with sequences of purchase, sale, correction, purchase.

### Decision 6: Refund presentation
Refund of a sale: revenue 200, COGS 100, refund 200, returned stock 4 units at snapshot cost 25.
- Today: refund shown separately, product cost keeps 100 (profit looks 100 lower than real after the refund).
- Proposed: net sales = 200 - 200 = 0 in the refund period, COGS = 100 - 100 (returned units at their snapshot cost) = 0, profit 0 for that item. If the product is not returned to stock (damaged): COGS stays 100 and a loss of 100 shows. Drawer: -200 on the open shift. Stock: +4 only when returned.
Past periods are not edited: the refund affects the period it happens in.

### Decision 7: Rent and utilities recognised by expense month (accrual)
Rent 5,000 for November, due Dec 5, paid Dec 3 from the drawer.
- November profit: -5,000 (expense dated November). December profit: unaffected by this rent.
- Payable (expenses owed): 5,000 from Nov 1 until paid. Drawer: -5,000 on Dec 3 (December cash flow).
- Reports: accrual profit shows rent in November; cash flow shows it in December; reconciliation links both.
If cash basis were chosen: profit would show the rent in December, and November would look 5,000 better than it was. Recurring rent: template generates the November expense on Nov 1 automatically (idempotent); changing rent to 5,500 from January leaves earlier months at 5,000.

## 4. Additional tests required by these rulings
Paid-now omitted creates zero drawer rows; paid-now > drawer cash is rejected; credit invoice reject; examples A to E as unit tests with exact numbers (stock, AvgCost, payable, drawer, variance); correction capped at on-hand; past COGS snapshots unchanged after correction (assert equality before and after); concurrent correction and sale of the same product (SQL scratch: no negative stock, no lost update on AvgCost); concurrent two payments; rebill creates link and keeps old invoice; second live invoice for the same session fails; replay of the same idempotency key.

## 5. Open items for the owner
1. Confirm weighted-average treatment for the sold share in example C (variance in correction period) rather than editing history.
2. Confirm that correction of more than on-hand is blocked with the three choices in example D.
3. Confirm default paid-now = 0 also for Admin-created intakes.
4. Confirm decisions 3 to 7 as recommended above.
After final approval the plan and estimate in the gap analysis stay at about 38 h, plus about 3 h for these additional cases.
