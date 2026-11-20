# PROPOSED GHOST Admin Read API Contracts

These are **PROPOSED** contracts for frontend sections that currently have no verified read endpoint. They are design notes only: the frontend does not call them, and no backend code is changed by this document. The existing supported routes remain the only API operations used by this frontend.

## Conventions

- Every route below is **PROPOSED** and should require `Authorization: Bearer <access-token>` plus an Admin authorization policy. The server must enforce authorization; hiding frontend controls is not access control.
- Use UTC ISO-8601 timestamps in responses and accept UTC `from` / `to` filters. The UI may render them in `Africa/Cairo`.
- Collection responses should return `{ items, page, pageSize, totalCount }`; clamp `pageSize` to a documented server limit and validate sort keys against an allowlist.
- Use stable opaque IDs, decimal JSON numbers for amounts, explicit enum strings, and consistent validation/error responses. Do not expose secrets or password hashes.
- A missing or unauthorized record should use the server's normal `404` / `403` behavior; invalid filters should return `400` with field-level validation details.

## Dashboard metrics — PROPOSED

`GET /api/admin/dashboard?from={utc}&to={utc}&timezone={iana-zone}`

Return `{ fromUtc, toUtc, currency: "EGP", deviceCounts: { total, active, inactive }, revenue: { gaming, products, refunds, collected }, sessionCount, completedSessionCount, activeSessionCount, occupiedMinutes, availableMinutes, utilizationPercent, lowStockProductCount, openShiftCount, cashVariance }`. Metrics must be computed from authoritative records with an explicit period and documented inclusion rules. Do not infer collected cash from device rates alone.

## Sessions — PROPOSED

- `GET /api/admin/sessions?page=&pageSize=&from=&to=&status=&deviceId=&employeeId=&sort=&direction=`
- `GET /api/admin/sessions/{sessionId}`

The collection item should contain `{ id, status, startedAtUtc, endedAtUtc, durationMinutes, customerLabel?, device: { id, name, category }, playType, appliedHourlyRate, amount, currency, startedBy?, endedBy? }`. Detail may add `segments: [{ deviceId, deviceName, startedAtUtc, endedAtUtc, playType, appliedHourlyRate, amount }]`. List filtering must not mutate a session; read-only Admin endpoints must not start, pause, transfer, or end sessions.

## Shifts — PROPOSED

- `GET /api/admin/shifts?page=&pageSize=&from=&to=&status=&employeeId=&sort=&direction=`
- `GET /api/admin/shifts/{shiftId}`

Return `{ id, status, openedAtUtc, closedAtUtc, employee: { id, displayName }, openingCash, expectedCash, countedCash?, variance?, currency }`; detail may include read-only reconciliation references. Do not expose opening/closing actions on these read routes.

## Cash reconciliation — PROPOSED

- `GET /api/admin/cash-reconciliations?page=&pageSize=&from=&to=&status=&shiftId=&sort=&direction=`
- `GET /api/admin/cash-reconciliations/{reconciliationId}`

Return `{ id, shiftId, status, expectedAmount, countedAmount, variance, currency, reconciledAtUtc?, reconciledBy? }`, with a detail breakdown by source such as sessions, product invoices, refunds, and recorded expenses. Reading must never create or settle a reconciliation.

## Products — PROPOSED

- `GET /api/admin/products?page=&pageSize=&search=&categoryId=&status=&sort=&direction=`
- `GET /api/admin/products/{productId}`

Return `{ id, name, sku, category, unit, salePrice, costPrice?, currency, isActive, stockQuantity?, lowStockThreshold? }`. Omit cost data for callers without an explicit permission; calculate margin only from authorized, complete cost data.

## Inventory movements — PROPOSED

`GET /api/admin/inventory/movements?page=&pageSize=&from=&to=&productId=&type=&performedBy=&sort=&direction=`

Return `{ id, productId, productName, type, quantityDelta, quantityAfter, unitCost?, referenceType?, referenceId?, note?, occurredAtUtc, performedBy }`. The read endpoint must report persisted movements only; it must not adjust stock. Product stock totals should be derived consistently from the inventory ledger or a documented snapshot.

## Sales and invoices — PROPOSED

- `GET /api/admin/sales?page=&pageSize=&from=&to=&status=&paymentMethod=&sort=&direction=`
- `GET /api/admin/invoices/{invoiceId}`

Return `{ id, number, status, issuedAtUtc, subtotal, discount, tax, total, refundedAmount, currency, paymentMethod, cashier: { id, displayName }, lineCount }`. Invoice detail should include immutable line snapshots `{ description, quantity, unitPrice, lineTotal, sourceType, sourceId? }`. These endpoints are for reporting only; they must not create checkout or payment operations.

## Employees — PROPOSED

- `GET /api/admin/employees?page=&pageSize=&search=&status=&role=&sort=&direction=`
- `GET /api/admin/employees/{employeeId}`

Return `{ id, displayName, username?, roleNames, status, createdAtUtc, lastSeenAtUtc? }`. Omit personally identifying fields not required by Admin reporting and enforce the corresponding permission on the server.

## Users and roles — PROPOSED

- `GET /api/admin/users?page=&pageSize=&search=&roleId=&status=&sort=&direction=`
- `GET /api/admin/roles`
- `GET /api/admin/permissions`

Return user summaries `{ id, username, displayName, status, roles: [{ id, name }] }`, role summaries `{ id, name, description, permissions: [{ key, name }] }`, and a permission catalog `{ items: [{ key, name, description }] }`. These are read-only proposals; do not add user/role mutations without a separate reviewed contract and server-side authorization policy.

## Audit logs — PROPOSED

`GET /api/admin/audit-logs?page=&pageSize=&from=&to=&actorId=&action=&entityType=&entityId=&sort=occurredAtUtc&direction=desc`

Return `{ id, occurredAtUtc, actor: { id, displayName }, action, entityType, entityId, outcome, requestId, summary, changes?: [{ field, oldValue?, newValue? }] }`. Redact secrets, credentials, tokens, and sensitive personal data. The endpoint must be append-only from the application's perspective and must not permit mutation or deletion.

## Analytics and reports — PROPOSED

- `GET /api/admin/analytics/summary?from=&to=&groupBy=day|week|month`
- `GET /api/admin/analytics/revenue?from=&to=&groupBy=day|week|month`
- `GET /api/admin/analytics/utilization?from=&to=&groupBy=day|week|month&deviceId=&category=`
- `GET /api/admin/analytics/products?from=&to=&groupBy=product&page=&pageSize=`
- `GET /api/admin/reports/definitions`
- `GET /api/admin/reports/{reportKey}?from=&to=&page=&pageSize=`

Summary should return `{ period, currency, gamingRevenue, productRevenue, refunds, collectedRevenue, sessionCount, completedSessions, durationMinutes, utilizationPercent }`. Revenue points should return `{ bucketStartUtc, gaming, products, refunds, collected }`; utilization points `{ bucketStartUtc, occupiedMinutes, availableMinutes, utilizationPercent }`; product rows `{ productId, name, quantitySold, revenue, cost?, margin? }`. Each series must state its timezone/bucketing and refund/cancellation policy. Report responses should identify the definition and filters used. Exports must be generated from the same authorized result set as the visible report.

## Existing API boundary

The currently verified Admin APIs are `POST /api/auth/login`, `POST /api/auth/change-password`, `GET /api/devices`, `POST /api/devices`, and `PUT /api/devices/{id}/rates`. The frontend uses these only where implemented. There are no verified read endpoints for sessions, shifts, reconciliation, products, inventory, sales, employees, users/roles, audit logs, analytics, or reports; those screens remain explicitly demo-only or unavailable until corresponding APIs are implemented.
