# Next steps and the reasoning behind the order

## How the work was ordered

Rule used: build what other things depend on first, and keep money and stock correct before screens.

1. **Auth before everything.** Without role policies, Staff could not use any endpoint (the fallback policy was Admin-only). Refresh tokens come next, because staff must not log in every few minutes while a customer is playing.
2. **Cash ledger before sales.** Every cash event (sale, expense, withdrawal, stock purchase, refund) must land in one append-only ledger, so the drawer total is always explainable. `Shift.RecordCash` is the only way to move expected cash.
3. **Products and stock before sales.** A sale needs a price, an average cost and a stock balance that cannot go negative.
4. **Receipts, then sales, then stocktake.** Receipts feed stock and the ledger. Sales consume them. Stocktake corrects reality.
5. **Reports last in the backend.** They only read data that the earlier steps now record correctly.

## Design rules applied everywhere

- The server owns money, stock, permissions and session state. The frontend never sends a user id, a price or a total.
- Every change that touches stock, cash and an invoice happens in one `SaveChanges` call, with row-version checks where two people could collide (shift, product, stocktake).
- Append-only tables (cash ledger, stock movements, price history, audit log) are protected by database triggers, not only by code.
- Every sensitive action writes an audit entry in the same transaction.
- No endpoint deletes a paid invoice. Refund is the only way back.

## What is next, in order

1. **Settings table and alert thresholds** (about 0.5 day). `SystemSettings` with an allow-list of keys (`CancelWindowSeconds`, `WithdrawalAlertThreshold`, `CashShortageAlertThreshold`, discount and cancellation alert limits). Admin edit is audited.
2. **Smart alerts** (about 1 day). Computed from existing data: cash shortage above a limit, repeated cancellations per staff, large discounts, large withdrawals, negative stocktake adjustments.
3. **Backup** (about 1 day). `BackupRecords`, a manual endpoint, a daily background job, `RESTORE VERIFYONLY` after each backup, keep the last 7. Needs a backup folder the SQL Server service can write to.
4. **Read endpoints the frontend needs** (about 1.5 days): current open shift for the logged-in staff, list and detail for sessions and shifts, audit log list, users list and admin actions, session events timeline.
5. **Frontend wiring** (5 to 8 days). See `FRONTEND-WIRING-PLAN.md`.
6. **SQL Server integration tests** (about 1 day): apply all migrations on a scratch database, test triggers, sequences, concurrency, the invoice flow.
7. **PDF and real Excel exports** (about 1.5 days), after the numbers are checked on a real database.
8. **P2/P3**: suppliers, customers, recipes, bundles.

## What the user should run on SQL Server first (scratch database, not GHOST2)

1. `dotnet ef database update` with all 8 new migrations. Expect no errors.
2. `dotnet test` on `GHOST.SqlServer.Tests`.
3. Try `UPDATE` and `DELETE` on `CashTransactions`, `StockMovements` and `ProductPriceHistory`. Each must fail.
4. One checkout with stock: the invoice number rises and the stock drops. Repeat the same call with the same idempotency key: no second charge.
5. A stocktake from start to approve. A second stocktake while one is active must be refused.
6. Dashboard numbers against a few known invoices.
7. Log in, wait past the access-token lifetime, call refresh, then replay the old cookie: the session must be revoked.

## Open questions

- Which backup folder should the API use, and is SQL Server on the same machine as the API?
- Is Staff discount approval needed at the counter, or is Admin-only fine?
- Should the staff app be a separate route group (`/staff`) in the same Next.js app? The plan assumes yes.


## Update (Nov 21): new order
Done in code: settings, alerts, backup, Sales/Inventory/Cash review fixes, Read APIs (shifts, sessions, audit logs, users).
Next: (1) refresh-on-401 in the frontend, then wire Staff screens to real APIs; (2) scratch-DB verification S1..S10 (docs/CODE-REVIEW-SALES-INVENTORY-CASH.md); (3) per-endpoint role tests for the older controllers; (4) local-deployment code changes ONLY after the owner answers the decisions in docs/LOCAL-DEPLOYMENT-REVIEW.md; (5) P2/P3.
