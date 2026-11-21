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

## Staff sessions wired (Nov 21)
- staff-dashboard.tsx: loads own open shift and active sessions (GET api/sessions?status=Active) next to devices; start-session button now calls POST api/sessions with the open shift id (disabled with a hint when no shift is open); busy states block double taps.
- components/staff-session-controls.tsx: pause/resume, complete (end shift = current shift), transfer to an available device with play type. Playing devices are now clickable. All rules stay on the server; its Arabic error is shown.
- Opening/closing a shift refreshes the dashboard.
- tsc clean, eslint 0 errors, next build ok. Not browser-tested against a live API; no component test framework.

## CORS from configuration (Nov 21, owner decision: single PC, localhost only)
- Program.cs: CORS origins now come from Authentication:RefreshToken:AllowedOrigins (the same list the refresh/logout guard uses), validated at startup by Authentication/LocalOriginPolicy.cs. Wildcards, LAN, public hosts and malformed origins stop the API from starting. Tests: Security/LocalOriginPolicyTests (13 cases). Backend 222 unit tests pass.
- Note: .NET config binding of arrays can keep the built-in localhost:3000 defaults next to configured values. Both are loopback, so the policy still holds.
- Secure refresh cookie stays on. Chrome/Edge treat http://localhost as a secure context and keep Secure cookies there; Safari does not. Venue PC should use Chrome or Edge. Not tested in a real browser yet.

## Staff POS wired (Nov 21)
- components/staff-pos.tsx: product catalog (GET api/products?status=Active), search by name/barcode, cart with stock caps, cash checkout with an idempotency key that is created once per cart content and reused on retry (a retry after a timeout cannot double-charge); new key when the cart changes or after success. Total shown is a preview; the server recomputes. Needs an open shift.
- lib/ghost-api.ts: getProducts, ApiProduct, ApiSale typed result for checkout.
- Not wired: discounts, linking a sale to a session, invoice list/print, refunds UI. tsc clean, eslint 0 errors, next build ok, no browser test.

## Backup: shift close + external drive (Nov 21, owner decision)
- BackupType.ShiftClose (=3). Migration 12 `AllowShiftCloseBackupType` widens CK_BackupRecords_Type to (1,2,3). Not applied to any SQL Server yet.
- Config (appsettings Backup:*): Enabled, Directory (SQL Server writes here), RetentionCount (keep newest N, default 7, applies locally and on the external drive), ExternalDirectory (for example E:\GHOST-Backups), BackupOnShiftClose (default true), DailyAt.
- After a successful verified backup the file is copied to ExternalDirectory (written as .partial, size-checked, renamed, older GHOST_*.bak beyond N removed). A failed or unverified backup is never copied. A failed copy keeps the local backup, is returned in the API result (ExternalCopy) and logged; manual runs by an Admin also write an audit row Backup.ExternalCopyFailed.
- Shift close triggers a background backup AFTER the close is saved; a backup problem never fails the close (ShiftCloseBackupTrigger, logged).
- Requirement: the API must be able to read Backup:Directory (same PC as SQL Server, folder readable by the API account) or the external copy reports an error.
- Tests: 3 handler tests + 3 real file-system tests (copy, keep newest 2, partial cleanup, missing source, unusable destination). Backend 228 unit tests pass. Real BACKUP/RESTORE VERIFYONLY on SQL Server: NOT tested yet.
