# GHOST 2.0 Frontend Foundation

This folder contains the Next.js frontend for the GHOST Staff and Admin experiences. Admin UI work is integrated into this application; do not create a second production frontend.

## Local development

1. Install Node.js 22 and pnpm 12.
2. Copy `.env.example` to `.env.local`.
3. Set `NEXT_PUBLIC_GHOST_API_BASE_URL` to the running GHOST API URL.
4. Start the .NET API from `backend/src/GHOST.API` with a valid, explicitly configured database connection.
5. From this folder run `pnpm install`, then `pnpm dev`.
6. Open the local URL printed by Next.js and visit `/admin`.

## Admin authentication and API integration

- `/admin` is gated by the API login flow: `POST /api/auth/login`.
- Accounts marked `MustChangePassword` are routed through `POST /api/auth/change-password` before the Admin dashboard is shown.
- Only the short-lived access token is kept in browser `sessionStorage`. Passwords and refresh tokens are not persisted. There is no refresh-token flow yet; after expiry the Admin must sign in again.
- The frontend sends the Bearer access token with protected Admin API calls. The API remains the authority for authentication and authorization; hiding a page or button is not a security boundary.
- The API fallback authorization policy currently requires the Admin role. Non-Admin roles remain blocked until their explicit endpoint policies are implemented and tested.
- Connected Admin device operations:
  - `GET /api/devices` loads active devices.
  - `POST /api/devices/seed-defaults` adds the default device set idempotently.
  - `PUT /api/devices/{id}/rates` saves Single and/or Multi hourly rates.
- Device records are created without invented prices. The UI requires a real saved rate before a play type can be used by the session workflow.

## Admin sections and current backend coverage

- Overview: truthful dashboard skeleton. Financial KPIs, charts, occupancy, shift balances, sales and product analytics remain empty until read/reporting endpoints exist.
- Devices and pricing: connected to the API above.
- Shifts and cash: read/listing and reconciliation endpoints are still required before showing balances or enabling admin actions.
- Staff and permissions: login exists; user administration and role-management endpoints are not yet available.
- Sales and inventory: product, stock, invoice and payment endpoints are not yet available.
- Reports: requires tested aggregate queries tied to persisted financial/session records.
- Settings: only settings backed by explicit server contracts should become editable.

## Safety boundaries

- No sample revenue, session counts, cash balances, percentages, or chart bars are presented as live data.
- Admin does not open/close staff shifts, start/end sessions, or perform cashier/POS checkout. Staff operations remain in the separate Staff experience.
- Do not add branches or multi-location features: GHOST is currently a single physical venue.
- No database connection or migration is performed by frontend development. Do not point development at production or the reserved integration-test database.
- The separate `Admin-Dashboard` repository is a visual reference. Its hardcoded demonstration figures and inert navigation must not be copied as production data or behavior. Production pages belong under this `frontend/` application.
