# Code review: Sales, Inventory, Cash ledger (2026-11-21)

Read line by line: `SaleHandler`, `Sale`, `Product.ApplyMovement`, `StockReceipt`, `StockReceiptHandler`, `Shift.RecordCash`, `CashTransaction`, `UnitOfWork`, repositories and EF configurations. Rule used: document the problem and its impact, then fix, then test.

## Problems found and fixed

| # | Problem | Impact | Fix | Test |
|---|---|---|---|---|
| R1 | Two identical checkout requests at the same instant (double tap, retry after a timeout): both pass the "key already used" check, the second fails on the unique index and the user saw "stock or drawer changed". | No double charge (the index protects it), but the cashier sees an error for a sale that did succeed and may ring it up again with a new key. | On the unique-key conflict, the handler reloads the winner and returns its invoice if it belongs to the same staff and shift (`SaleHandler.CheckoutCashAsync`). | `SaleHandlerIdempotencyTests.ConcurrentDuplicate_LosingRequest_GetsTheWinnersInvoice`, `ConcurrentConflictWithoutWinner_ReportsRetryMessage` |
| R2 | `Sale` had no row version. Two Admins refunding the same invoice at once, each into a different open shift, with "do not return to stock": nothing shared is updated, so both refunds could succeed. | Cash paid out twice for one invoice. | Row version on `Sale`: the second save fails with a concurrency conflict and the user is told to retry, where the state check then refuses the refund. | `SecondRefund_IsRejected_NoSecondCashOutOrStockReturn` covers the state rule. The race itself needs SQL Server (see scratch-DB step S6). |
| R3 | `StockReceipt` had no row version. Approve and Reject at the same time: Reject changes products, Approve changes only the receipt, so there is no conflict and the receipt could end as Approved after its stock was reversed. | Receipt status disagrees with stock and cash. | Row version on `StockReceipt`. | Needs SQL Server (S7). Domain state rule is already covered by `StockReceiptTests`. |
| R4 | Invoice lists loaded Items, Payments and Refunds in one query for up to 200 invoices. | Result rows multiply (items x payments x refunds). Slow on a busy day. | `AsSplitQuery()` in `SaleRepository`. | Build and existing tests pass. Performance is not measured. |
| R5 | `Alerts`, reports and dashboard filter by `Sales.PaidAt`, `CashTransactions(Type, CreatedAt)`, `Shifts.ClosedAt`, `Sessions.CancelledAt` with no index. | Full table scans as data grows. | Four indexes added in migration `AddSettingsBackupsAndReportIndexes`. | Needs SQL Server to confirm the plan. |

## Checked and found correct

- A checkout is one `SaveChanges`: invoice, items, payment, stock movements, cash ledger row, and audit row commit together or not at all (`SaleHandler`, `Sale.CheckoutCash`). Test: `SaleTests` (stock, cash, price and cost snapshot), `SameKeyTwice_ReturnsSameInvoice_ChargesAndDecrementsOnce` (one save, one audit row).
- Same key again returns the original invoice, with the discount applied once: `SameKeyTwice_WithDiscount_AppliesDiscountOnce`. A key reused by a different staff member is refused: `KeyUsedByAnotherStaff_IsRejected_NotReplayed`.
- Stock cannot go below zero (`Product.ApplyMovement`) and the product row version stops two simultaneous sales from both using the same stock (needs SQL Server to prove, S5).
- Drawer cannot go below zero (`Shift.RecordCash`) and has its own row version.
- Ledger idempotency key is unique per shift, with a unique filtered index.
- Refund returns stock at the sale-time cost, so average cost is not distorted.

## Known weaknesses not changed (documented decisions or future work)

- The sale idempotency key is global, not per staff. Clients must send a GUID per cart.
- Invoice numbers use one sequence (`GS-<year>-<number>`); the number does not restart each year.
- Refund is full-only.
- A voided or failed insert can leave a gap in the invoice number (sequences do not roll back). Normal for SQL Server; document for the accountant.

## Acceptance checklist, with evidence status

| Item | Evidence | Status |
|---|---|---|
| Repeated sales request: no duplicate invoice or discount | `SaleHandlerIdempotencyTests` (6 tests, all pass) | Done at unit level. Unique index behaviour: S4 on SQL Server, not yet |
| Stock never deducted twice, correct under two concurrent orders | Unit tests for single deduction. Concurrency: row version | Concurrency not yet verified (S5) |
| Protected ledger rows cannot be updated or deleted | Triggers exist in migrations | Not yet verified on SQL Server (S2) |
| Linked financial and stock operations are atomic | One `SaveChanges` per action (code read), handler tests show one save | Atomic rollback under failure not yet verified on SQL Server (S8) |
| Authorization of every read API tested | A test checks that every controller action has a policy. No per-endpoint role tests yet | Partial. Not done |
| All 10 migrations on a clean scratch DB | Not run. No SQL Server in the build environment | Not yet (S1) |
| Frontend on real APIs | No frontend changes yet | Not yet |

## Scratch-database steps (use a new empty database, never GHOST2)

- S1. Apply all migrations to an empty database. Expect no errors. Compare `dotnet ef migrations list` with `__EFMigrationsHistory`.
- S2. Try `UPDATE` and `DELETE` on `CashTransactions`, `StockMovements`, `ProductPriceHistory`, `AuditLogs`. Each must fail.
- S3. `SELECT NEXT VALUE FOR InvoiceNumbers` twice: numbers rise. Same for `InternalBarcodes`.
- S4. Insert two sales with the same `IdempotencyKey`: the second fails (`UX_Sales_IdempotencyKey`). Two live sales for one session: fails (`UX_Sales_SessionId_Live`).
- S5. Two parallel checkouts of the last unit of one product: exactly one succeeds, stock is 0, never negative.
- S6. Two parallel refunds of one invoice, different shifts: exactly one succeeds.
- S7. Parallel approve and reject of one receipt: exactly one succeeds, stock matches the status.
- S8. Force an error after the invoice insert (for example a check constraint) and confirm nothing from that checkout remains (no stock movement, no cash row).
- S9. Run the report and alert endpoints with a few rows and compare to hand-calculated numbers.
- S10. Backup: run `POST api/backups` with a folder SQL Server can write to. Expect a `.bak` file and a `BackupRecords` row with `VerifiedAt`.
