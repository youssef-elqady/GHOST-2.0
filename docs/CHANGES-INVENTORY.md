# What this branch changes in the repo

Base: `origin/feature/admin-frontend-complete` (8a5508e). Branch: `feature/auth03-refresh-tokens`. Frontend: unchanged so far.
All migrations are generated and **not applied** anywhere. Per this repo's rules nothing touches the `GHOST2` database.

## Domain (`backend/src/GHOST.Domain`)

| New or changed | Purpose |
|---|---|
| `RefreshToken` | Hashed refresh tokens with family, rotation, replay detection |
| `CashTransaction`, `CashTransactionType`, `CashDirection` | Append-only cash ledger |
| `Shift.RecordCash` (replaces `UpdateExpectedCash`) | Only way to move expected cash |
| `ShiftHandover` | Incoming staff confirms cash (FR-12) |
| `Session.CancelOrVoid`, `IsVoided` | Cancel window and Admin void (BD-03) |
| `Product`, `ProductStatus`, `StockMovement`, `StockMovementType`, `ProductPriceHistory` | Catalog, stock, weighted average cost |
| `StockReceipt`, `StockReceiptItem`, `StockReceiptStatus` | Receipts with Pending/Approved/Rejected |
| `Sale`, `SaleItem`, `Payment`, `Refund`, `SaleStatus` | Invoices, cash payment, refund |
| `Stocktake`, `StocktakeItem` | Blind stocktake |
| `Ean13` | Internal barcode numbers |

## Application (`GHOST.Application`)

`ShiftCashHandler`, `AcknowledgeHandoverHandler`, `CancelSessionHandler`, `SessionPolicy`, `IAuditTrail`, `InventoryHandler`, `StockReceiptHandler`, `StocktakeHandler`, `SaleHandler`, `IReportQueries`, and repository interfaces for shifts, products, sales, stock receipts and stocktakes.

## Infrastructure (`GHOST.Infrastructure`)

EF configurations for every new entity, repositories, `AuditTrail`, `ReportQueries`, SQL sequences `InvoiceNumbers` and `InternalBarcodes`, and these migrations in order: `AddRefreshTokens`, `AddCashTransactionsLedger`, `AddSessionVoidFlag`, `AddShiftHandovers`, `AddProductsAndStockMovements`, `AddStockReceipts`, `AddSalesAndInvoices`, `AddStocktakesAndBarcodes`.
Database-level protection: append-only triggers on `CashTransactions`, `StockMovements`, `ProductPriceHistory`; check constraints; filtered unique indexes (barcode, SKU, idempotency keys, one live invoice per session).

## API (`GHOST.API`)

- Policies `AdminOnly` and `StaffOrAdmin` (`Security/`), `HttpContextCurrentUser`, `ShiftAccess` (staff only act on their own open shift).
- Every controller now has an explicit policy. A test fails if a new endpoint has none.
- Refresh and logout in `AuthController`; the access token carries a session id (`sid`) so revoked sessions stop working at once.

Endpoints added:

| Route | Who |
|---|---|
| `POST api/auth/refresh`, `POST api/auth/logout` | cookie |
| `GET api/shifts/{id}/cash`, `POST .../expenses`, `POST .../withdrawals`, `POST .../handover/acknowledge` | Staff (own shift), Admin |
| `POST api/sessions/{id}/cancel` | Staff in window, Admin always |
| `GET/POST api/products`, `GET by-barcode/{code}`, `PUT {id}/price` (Admin), `POST {id}/enable` and `/disable` (Admin), `GET/POST {id}/movements`, `POST {id}/barcode` | see policy per route |
| `POST/GET api/stock-receipts`, `POST {id}/approve` and `/reject` (Admin) | |
| `POST api/sales/checkout`, `GET api/sales`, `GET api/sales/{id}`, `POST {id}/print`, `POST {id}/refund` (Admin) | |
| `POST/GET api/stocktakes`, `PUT {id}/counts`, `POST {id}/submit`, `POST {id}/approve` and `/reject` (Admin) | |
| `GET api/reports/dashboard`, `daily`, `shifts`, `employees`, `devices` (`format=csv` available) | Admin |

## Tests

169 unit tests (was 78) across auth policies, refresh tokens, cash, handover, cancel/void, products, receipts, sales, stocktake and CSV export.

## Docs

`STATE.md` (updated), `STATUS-2026-11-21.md`, `NEXT-STEPS-AND-THINKING.md`, `FRONTEND-WIRING-PLAN.md`, this file.
