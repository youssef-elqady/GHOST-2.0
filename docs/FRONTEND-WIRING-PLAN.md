# Frontend wiring plan (Next.js Staff + Admin to the real API)

Scope: `frontend/` (Next.js 16, React 19, Arabic RTL). Today it calls only `/api/auth/login`, `/api/auth/change-password`, and the device endpoints (`lib/ghost-api.ts`). Everything else on the Admin screens is demo data (`lib/demo/admin-data.ts`). The staff dashboard has no real data either.
This document is a plan. No frontend code was changed in this session.

## 1. Rules

- The server decides money, stock, permissions. The UI shows results and never sends a user id, price or total.
- One API client (`lib/ghost-api.ts`) for all calls. Components never call `fetch`.
- The access token lives in memory only (as `AdminAuthProvider` does now). The refresh token lives in an HttpOnly cookie the browser cannot read.
- Arabic error messages come from one map keyed by the API `error` string, as the current `request()` does.

## 2. Login that never makes staff re-login

The API gives a short-lived access JWT (`AccessTokenLifetimeMinutes`, 1 to 30, set in config) and a refresh cookie `ghost_refresh` (path `/api/auth`, HttpOnly, SameSite=Strict, 7 days sliding, 30 days hard cap).

### Required client changes

1. Send cookies: every call to `/api/auth/*` uses `credentials: 'include'`. The API already allows credentials for `http://localhost:3000` and `http://127.0.0.1:3000` (`Program.cs`, CORS policy `GhostFrontend`). Add the production origin there and in `Authentication:RefreshToken:AllowedOrigins`.
2. Send the CSRF header on refresh and logout: `X-GHOST-Request: 1`. The API also checks the `Origin` header against the allow-list.
3. Same site in production: the cookie is SameSite=Strict, so the UI and API must share a site (for example `app.shop.local` and `api.shop.local`, or one reverse proxy path). Ports on `localhost` are fine in development.
4. Refresh on 401, once, with a shared promise so parallel calls do not refresh twice:

```ts
let refreshing: Promise<AuthResponse | null> | null = null

async function refreshAccessToken() {
  refreshing ??= fetch(`${API}/api/auth/refresh`, {
    method: 'POST', credentials: 'include',
    headers: { 'X-GHOST-Request': '1' },
  }).then(async (r) => (r.ok ? ((await r.json()) as AuthResponse) : null))
    .finally(() => { refreshing = null })
  return refreshing
}

async function authed<T>(path: string, init?: RequestInit): Promise<T> {
  let res = await send(path, init, getToken())
  if (res.status === 401) {
    const next = await refreshAccessToken()
    if (!next) { window.dispatchEvent(new Event('ghost-admin-session-expired')); throw new Error('انتهت الجلسة') }
    setSession(next.accessToken, next.user, next.expiresAtUtc)
    res = await send(path, init, next.accessToken)
  }
  return parse<T>(res)
}
```

5. Refresh early, not only on 401: in `AdminAuthProvider`, replace the timer that logs out at expiry with a timer that calls refresh about 60 seconds before `expiresAtUtc`. Only if refresh fails does the session end.
6. On page load (a reload loses the in-memory token), call refresh once before showing the login form. If it succeeds the user is back in without typing a password.
7. Logout calls `POST /api/auth/logout` with `credentials: 'include'`, then clears memory.
8. Replay detection: if the API revokes a family (token reuse), refresh returns 401 and the UI shows the login page. Do not retry.

What the user will see: staff log in once per day at most (after 7 idle days, 30 days total, logout, password change, or an Admin disabling the user). A running game session is a server record and is unaffected by token refresh.

## 3. Missing read endpoints (backend work that comes first)

The screens need list and detail reads that do not exist yet. These are small, read-only and Admin or own-shift only:

| Need | Proposed endpoint |
|---|---|
| Current open shift of the logged-in staff | `GET api/shifts/current` |
| Active and recent sessions with device and amount | `GET api/sessions?status=&from=&to=` and `GET api/sessions/{id}` |
| Shifts list and detail | `GET api/shifts?from=&to=`, `GET api/shifts/{id}` (cash summary exists) |
| Audit log | `GET api/audit-logs?from=&to=&action=&userId=` (Admin) |
| Users and roles | `GET/POST api/users`, `POST api/users/{id}/disable`, `POST api/users/{id}/reset-password` (Admin) |
| Session timeline (FR-28) | `GET api/sessions/{id}/events` |
| Alerts and settings | `GET api/alerts`, `GET/PUT api/settings` (Admin) |

`frontend/docs/proposed-admin-read-api-contracts.md` already sketches shapes for some of these. Reconcile it with the real DTOs when each endpoint is built.

## 4. Screen by screen

### Staff app (new route group `/staff`, same Next.js app)

| Screen | Calls |
|---|---|
| Login | `POST /api/auth/login`, then `POST /api/auth/change-password` if required |
| Open shift, shift status | `GET api/shifts/current`, `POST api/business-days/{day}/shifts` |
| Device board | `GET api/devices`, `GET api/sessions?status=Active` (poll every 10 s) |
| Start, pause, resume, transfer, complete | `POST api/sessions` and `/pause` `/resume` `/transfer` `/complete` |
| Cancel session | `POST api/sessions/{id}/cancel` (button hidden after the window for Staff; the server enforces it) |
| POS | `GET api/products?search=`, `GET api/products/by-barcode/{code}`, `POST api/sales/checkout` with an idempotency key generated per cart |
| Print | `POST api/sales/{id}/print`, label "نسخة" when `isCopy` |
| Cash drawer | `GET api/shifts/{id}/cash`, `POST .../expenses`, `POST .../withdrawals` |
| Handover | `POST api/shifts/{to}/cash/handover/acknowledge` |
| Receive goods | `POST api/stock-receipts` |
| Quick-add product | `POST api/products` (becomes PendingPricing) |
| Stocktake | `POST api/stocktakes`, `PUT .../counts`, `POST .../submit` (no system quantities shown while counting) |
| Close shift | `POST api/business-days/{day}/shifts/{shift}/close` |

### Admin app (`/admin/*`, replace demo data)

| Screen | Calls |
|---|---|
| Overview / dashboard | `GET api/reports/dashboard`, `daily` |
| Devices | already wired |
| Sessions | `GET api/sessions`, void via `POST .../cancel` |
| Shifts, cash reconciliation | `GET api/reports/shifts`, `GET api/shifts/{id}/cash` |
| Employees | `GET api/reports/employees` |
| Products | `GET/POST api/products`, `PUT .../price`, enable/disable, barcode |
| Inventory | `GET api/products`, `GET .../movements`, receipts approve/reject, stocktakes approve/reject |
| Sales | `GET api/sales`, refund |
| Reports | the report endpoints with `format=csv` download |
| Audit logs | `GET api/audit-logs` |
| Users and roles | `api/users` endpoints |
| Settings | `api/settings` |

## 5. Order of work (about 5 to 8 days)

1. Auth client: refresh, early refresh, reload recovery, logout (0.5 day). Test with a 1-minute access lifetime.
2. Read endpoints and the typed API client for all existing routes (1.5 days).
3. Staff app: shift, device board, sessions (1.5 days).
4. Staff app: POS, print, cash, receipts, stocktake (2 days).
5. Admin screens on real data: dashboard, products, inventory, sales, shifts, reports (2 days).
6. Users, audit, settings, alerts (1 day).
7. Browser testing against a real SQL Server with seeded data (1 day).

## 6. Testing the wiring

- Set `Authentication__Jwt__AccessTokenLifetimeMinutes=1`. Stay on a screen for 3 minutes: no login prompt appears and calls keep working.
- Clear the cookie in dev tools: the next refresh fails and the login page appears.
- Replay a copied refresh cookie from a second browser: the first session must also be rejected.
- Disable a user from Admin: that user's next call fails within one request.
- Open two tabs and refresh at once: only one `POST /api/auth/refresh` should be in flight (the shared promise).
