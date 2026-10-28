# GHOST 2.0 Frontend Foundation

This folder contains the v0-generated Staff Dashboard UI imported from `youssef-elqady/ghost-staff`.

## Local development

1. Install Node.js LTS and pnpm.
2. Copy `.env.example` to `.env.local`.
3. Start the .NET API from `backend/src/GHOST.API`.
4. From this folder run `pnpm install`, then `pnpm dev`.
5. Open the local URL printed by Next.js.

## API integration status

- Connected: `GET /api/devices` loads active devices from the existing API.
- Device setup: `POST /api/devices/seed-defaults` adds PS2–PS6, VIP 1–3, and Room 1–2 idempotently; devices are created without invented prices.
- Admin pricing UI: `PUT /api/devices/{id}/rates` saves Single and/or Multi hourly rates to the database. Session start still requires a configured rate for the selected play type.
- Not yet connected: opening/closing shifts, starting/resuming/completing sessions, products/inventory, payments, persistent staff messages, activity history, and authentication.
- `/admin` now includes device seeding and rate editing. It is still a development-only UI: there is no authentication or server-side role authorization, so the device-seed and rate-write endpoints are not yet genuinely admin-only. Do not expose this API to an untrusted network or use it as a production system until authentication/authorization is implemented.
- Session start is intentionally disabled in the UI until the API can identify the authenticated staff member and return the active shift. Do not use mock dashboard values as live financial data.
- The separate `Admin-Dashboard` repository was empty at the time of integration. The `/admin` shell was created using the same visual language as the staff UI, not copied from that repository.
- This is a frontend foundation, not a production-ready installer. Desktop packaging, API security, recovery behavior, and end-to-end tests remain required.

## Configuration

Set `NEXT_PUBLIC_GHOST_API_BASE_URL` to the local API URL. The default is `http://localhost:5153`.
