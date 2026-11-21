# GHOST Transaction Matrix

Every operation below is one `SaveChangesAsync` (one SQL transaction) in one handler. No in-memory locks anywhere. Concurrency is decided by RowVersion columns, unique indexes and the database. Status column: tests that prove it. SQL items are scripted (SC-xx) and NOT yet run.

| Operation | Tables written (same transaction) | Concurrency guard | Audit | Proof |
|---|---|---|---|---|
| Start session | Sessions, SessionSegments, Devices(status) | Device RowVersion, unique active-segment index | Session.Started | U; SQL not run |
| Pause / resume | Sessions pauses, Devices | RowVersion | Session.Paused/Resumed | U |
| Transfer | SessionSegments (close+open), 2 Devices | RowVersion, unique active index | Session.Transferred | U |
| Complete session | Sessions, SessionSegments, Devices | RowVersion | Session.Completed | U |
| Cancel / void | Sessions, Devices | RowVersion | Session.Cancelled/Voided | U |
| Power-cut detect | Sessions (NeedsReview, pause), SystemHeartbeat | one row, RowVersion | Session.PowerOutageDetected (System) | U; SC-09 |
| Outage approve | Sessions, SessionSegments, Devices | RowVersion, status check | Session.OutageApproved | U; SC-09 |
| Checkout (cash) | Sales, SaleItems, StockMovements, Products(stock), CashTransactions, IdempotencyKey+Fingerprint | Product RowVersion, unique idempotency key | Sale.Paid / PaidWithDiscount | U; SC-03..07 |
| Refund | Sales(status), CashTransactions(Refund), StockMovements(SaleReturn), Products | Sale RowVersion | Sale.Refunded | U; SC-08 |
| Print invoice | Sales (print count) | RowVersion | Sale.Printed/Reprinted | U |
| Open / close shift | Shifts, CashTransactions(opening) | RowVersion, one-open-shift index | Shift.Opened/Closed | U |
| Shift handover | Shifts, CashTransactions HandoverOut/In | RowVersion | Shift.HandoverAcknowledged | U |
| Stock receipt create | StockReceipts, items, StockMovements(Purchase), Products, CashTransactions(StockPurchase) | RowVersion | StockReceipt.Created | U |
| Receipt approve / reject | StockReceipts, (reject: reversal movements, cash reversal or staff charge) | RowVersion | StockReceipt.Approved/Rejected | U |
| Stocktake submit/approve | Stocktakes, StockMovements(StocktakeAdjustment), Products | RowVersion | Stocktake.* | U |
| Stock adjust | StockMovements, Products | RowVersion | Stock.Adjusted | U |
| Product create/price/disable | Products | RowVersion | Product.* | U |
| Refresh rotation | RefreshTokens (consume old, insert new) | RowVersion on token | Authentication.Refresh* | U |
| Backup record | BackupRecords (after shift close, separate save) | none, failure isolated | Backup.Created/Failed | U |

## The 8 required scenarios
1. Two sales of the last unit: Product RowVersion, one wins, one fails with stock error. SC-03 (not run).
2. 20 concurrent sales, stock 5: exactly 5 succeed, stock 0. SC-04 (not run).
3. Same key 8 times: one invoice. SC-05 (not run).
4. Identical carts, different keys: two invoices. SC-06 (not run); unit test passes.
5. Same key, different cart: rejected. SC-07 (not run).
6. Double refund by two admins: one wins. SC-08 (not run).
7. Power cut: NeedsReview, no billing for the gap, Admin approves once. SC-09 (not run).
8. Backup failure during close: close saved, failure recorded. Unit test passes; real drive not tested.
