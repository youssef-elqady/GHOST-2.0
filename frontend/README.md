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
- Not yet connected: opening/closing shifts, starting/resuming/completing sessions, products/inventory, payments, persistent staff messages, activity history, and authentication.
- Session start is intentionally disabled in the UI until the API can identify the authenticated staff member and return the active shift. Do not use mock dashboard values as live financial data.
- The separate `Admin-Dashboard` repository was empty at the time of integration, so an admin interface has not been copied from it.
- This is a frontend foundation, not a production-ready installer. Desktop packaging, API security, recovery behavior, and end-to-end tests remain required.

## Configuration

Set `NEXT_PUBLIC_GHOST_API_BASE_URL` to the local API URL. The default is `http://localhost:5153`.
