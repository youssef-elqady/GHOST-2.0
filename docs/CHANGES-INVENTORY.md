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

## Added later on 2026-11-21

- Domain: `SystemSetting`, `BackupRecord` (`BackupType`, `BackupStatus`).
- Application: `SettingsCatalog`, `SettingsHandler`, `AlertsHandler`, `IAlertQueries`, `BackupHandler`, `IDatabaseBackupGateway`, repository interfaces `ISettingsRepository`, `IBackupRepository`.
- Infrastructure: `AlertQueries`, `SqlServerBackupGateway`, repositories, configurations; row versions on `Sale` and `StockReceipt`; indexes `IX_Sales_PaidAt`, `IX_CashTransactions_Type_CreatedAt`, `IX_Shifts_ClosedAt`, `IX_Sessions_CancelledAt`; migrations `AddSettingsBackupsAndReportIndexes` and `AddSaleAndReceiptRowVersions`.
- API: `GET/PUT api/settings` (Admin), `GET api/alerts` (Admin), `GET/POST api/backups` (Admin), hosted `ScheduledBackupService`, `Backup:*` configuration (disabled by default).
- Fixes: checkout idempotency race (R1), double-refund and approve/reject races (R2, R3), split queries (R4).
- Tests: 191 unit tests (was 169).


## Read APIs (Nov 21)
- Application/Reads: ReadModels (DTOs, IOperationsReadQueries), OperationsReadHandler (authorization + paging clamp, max 100).
- Infrastructure/Persistence/OperationsReadQueries.cs: projections only, one count + one page query per list, user names joined in-query (no N+1). Session detail uses AsSplitQuery.
- API/Controllers/ReadApisControllers.cs: ShiftsController, SessionReadsController (same api/sessions route), AuditLogsController, UsersController.
- Visibility: Admin all. Staff: own shifts only (others look like 404); sessions that are Active or started/ended in own shifts. Lists of shifts, audit logs and users are Admin only. User DTO excludes hashes and stamps.
- Migration 11: AddAuditLogAtIndex (IX_AuditLogs_At).
- Tests: Security/ReadApiAuthorizationTests.cs (15 tests).


## Frontend: refresh-on-401 (Nov 21)
- lib/ghost-api.ts: all calls send credentials; on 401 (not login/change-password/refresh/logout) one shared refresh, then one retry with the new token; failed refresh ends the session. Added refreshAccessToken, logoutRequest, configureApiAuth.
- lib/admin-auth.tsx: restores the session on page load from the refresh cookie (no password), refreshes 60 s before expiry, real logout call, `restoring` flag.
- components/admin-shell.tsx, admin-login.tsx: wait for restore before redirecting; logged-in visitors skip the login page.
- tests/ghost-api-refresh.test.mts (4 tests, `pnpm test`): result 4 pass, 0 fail. `tsc --noEmit` clean, eslint 0 errors (9 old warnings), `next build` succeeds.
- Not verified: real browser against a running API (needs SQL Server), cookie behaviour on a LAN HTTP origin (see LOCAL-DEPLOYMENT-REVIEW).

## Authorization matrix fix (Nov 21)
- BUG: ASP.NET requires ALL [Authorize] attributes (class + action). DevicesController and BusinessDaysController had class-level AdminOnly plus action-level StaffOrAdmin, so Staff got 403 on GET api/devices, open shift and close shift. Fixed: class StaffOrAdmin, AdminOnly on the admin actions (create/seed/rates device, open business day).
- Test: Security/EndpointRoleMatrixTests.cs runs every controller action through the real policies for Admin, Staff and no role against a reviewed table. With the old attributes it fails (3 actions listed); with the fix it passes.
- Reviewed table flags for owner decision: Staff can Create product, AdjustStock, GenerateBarcode and create stock receipts/stocktakes (approvals are Admin only).

## Staff wiring groundwork (Nov 21)
- Backend: GET api/business-days/current (Staff/Admin). Gap found: opening a shift needs a businessDayId and nothing let Staff discover it. Test added; role matrix updated (209 unit tests pass).
- Frontend lib/ghost-api.ts: typed clients for current business day, current shift, open/close shift, sessions list/start/pause/resume/transfer/complete, checkout with client-generated idempotency key. 5 node tests pass.
- Gap found: the Staff dashboard (route /) has no login and calls getDevices() without a token. With the Staff role now correctly allowed, Staff needs its own login screen and an auth provider shared with Admin. This is the next step before any Staff screen can use real data.

## Staff login gate (Nov 21)
- Route / now requires sign-in: components/staff-gate.tsx (restore from refresh cookie, else AdminLogin with audience=staff), app/page.tsx wraps AdminAuthProvider. StaffDashboard receives the token, user name and a real logout. getDevices now sends the token (it was anonymous, which the API would reject).
- tsc clean, eslint 0 errors, next build ok. Not run in a browser against a live API.

## Staff shift screen wired (Nov 21)
- components/staff-shift.tsx replaces the placeholder ShiftView: reads current business day + own open shift, opens a shift with opening cash (own user id from the token), closes it with counted cash and optional reason. Expected cash and difference are decided by the server; the UI does not reveal expected cash before the count.
- tsc clean, eslint 0 errors, next build ok. No component tests (no framework); not run in a browser against a live API.
