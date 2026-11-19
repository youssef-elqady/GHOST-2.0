# Frontend Discovery — 2026-11-19

Tracking issue: [#14 — GHOST execution plan](https://github.com/youssef-elqady/GHOST-2.0/issues/14)

Scope: source-level discovery only. No frontend code changed in this review.

## Current production frontend foundation

- Next.js 16.4.3, React 19, Tailwind CSS 4, TypeScript 5.7, Lucide icons.
- Root route `/` renders the Staff dashboard; `/admin` renders the Admin dashboard.
- Root layout already sets Arabic language and RTL direction.
- Existing global CSS already contains light and dark semantic tokens, IBM Plex Sans Arabic typography, responsive navigation, device cards, panels, tables, and modal styles.
- The Staff dashboard fetches the active device list from `GET /api/devices`, with explicit loading/error/empty states. Session details, shift data, products, payments, handover messages, and history remain placeholders until API contracts exist.
- Admin device management currently reads devices, seeds default devices, and updates hourly rates through the API. Other Admin sections intentionally show no fake financial values.
- `ghost-api.ts` centralizes the current API base URL and fetch/error handling.

## KEEP

- Keep the existing Next.js application as the only production frontend.
- Keep the separate Staff and Admin experiences and their current route intent.
- Keep Arabic RTL and IBM Plex Sans Arabic as the default presentation.
- Keep the existing API wrapper and real device/rate workflows.
- Keep explicit loading, empty, and error states. The current decision not to fabricate financial values is correct.
- Keep the restrained charcoal/warm-neutral and gold GHOST visual direction, with semantic green/amber/red/blue states.

## MODIFY

- Consolidate duplicated Staff/Admin theme state into one shared theme provider. Persist one preference and avoid hydration flashes; support both `light` and `dark` consistently.
- Normalize the current `.dark` class tokens and the Admin reference's `data-theme` tokens into one documented design-token system.
- Replace inline styles in Admin device management with reusable design-system components as those components are introduced.
- Upgrade the Admin overview placeholder into a real analytics layout only after backend contracts and source-ledger definitions are ready.
- Keep Staff's current operational focus, but replace placeholders only when the matching server workflow and authorization rules exist.
- Review the API base URL configuration and ensure local/dev configuration cannot accidentally target the hard-coded `GHOST2` database connection.

## ADD

- Shared theme provider and design tokens: surface/background/text/border, accent, semantic status colors, spacing, radii, shadows, focus states.
- Reusable Button, IconButton, Card, KPI, Badge, Input, Select, DateRange, Table, EmptyState, ErrorState, Skeleton, Modal, and PageHeader primitives.
- Auth-aware route/layout boundary after login and token lifecycle are implemented.
- A premium Admin shell with sidebar, topbar, branch/status context, profile, theme switch, responsive navigation, and consistent page container.
- A distinct Staff shell optimized for quick device/session operations and cashier tasks.
- Admin analytics contracts and screens: financial summary, gaming/product revenue, gaming duration, shift/cash review, device/room utilization, product performance, and drill-down reports.

## DEFER

- Do not add real revenue charts until sales/payments/cash-ledger endpoints exist and totals reconcile.
- Do not add product profit charts until product cost snapshots and stock/sales movements are complete.
- Do not show utilization or active-session KPIs until reliable session/device queries exist.
- Do not copy reference-repository sample identities, Saudi Riyal amounts, historical dates, 24-device counts, or hardcoded activity rows.
- Do not introduce a chart dependency until the required chart types and accessibility needs are agreed during the Design System phase.

## Visual reference review

### Admin reference — `youssef-elqady/Admin-Dashboard`

Keep as visual inspiration:
- Premium admin information hierarchy: KPI cards, revenue panel with date-range tabs, shift summary, active sessions, device availability, sidebar and topbar.
- Light and dark theme support with warm neutral surfaces, restrained gold, and sage/semantic accents.
- Compact Arabic typography, clean spacing, responsive layout.

Do not copy:
- Hardcoded revenue such as 2,840 EGP, sample sales such as 620 EGP, 42 transactions, cash drawer values, 24 devices, or fabricated sessions/customers.
- The current chart's synthetic SVG coordinates as if they represented venue history.

### Staff reference — `youssef-elqady/ghost-staff`

Keep as visual inspiration:
- Device-first operational workspace, room grouping, device cards, session setup modal, shift checklist, handover layout, product catalog structure, and staff history table.

Do not copy:
- Static devices, sample Saudi names, Saudi Riyal values, sample dates/times, fake shift state, prefilled history, or client-only message persistence as production behavior.

## Admin analytics visual specification

The dashboard must distinguish measures clearly:

| Measure | Visual treatment | Unit |
|---|---|---|
| Gaming revenue | Blue series | EGP |
| Product revenue | Green series | EGP |
| Total collected revenue | Gold KPI/line | EGP |
| Expenses/refunds/withdrawals | Red semantic series, separately identified | EGP |
| Gaming duration / device usage | Violet series in its own chart | Hours/minutes |
| Product quantity sold | Neutral/teal series in a separate quantity chart/table | Units |

Rules:
- Never put EGP and minutes/hours on the same axis.
- Do not call gross revenue "profit". Profit requires reliable cost and expense definitions.
- Every visual states its date range, currency/unit, and data freshness.
- Date filters must be consistent across the dashboard and support custom ranges.
- Clicking a KPI/bar should drill down to the transactions or sessions that form the number.
- A missing API response is an error/unknown state, not zero.
- Use real backend aggregation; do not calculate authoritative financial totals in the browser.

## Recommended dashboard page hierarchy

1. Page heading + branch/business-day context + date-range selector.
2. KPI strip: collected revenue, gaming revenue, product revenue, gaming hours, sessions, average transaction value (only when available).
3. Revenue comparison chart: gaming vs products, with clear EGP scale and period comparison.
4. Separate usage chart: gaming hours and room/device utilization.
5. Shift/cash summary and reconciliation status.
6. Product performance table: product, quantity, sales, cost snapshot, estimated margin (only when data supports it).
7. Device/room utilization table and peak-hour analysis.
8. Recent audited activity and data-backed alerts.

## Exit criteria for discovery

- Existing frontend and references inspected at source level.
- KEEP/MODIFY/ADD/DEFER decisions documented.
- No frontend code changed in discovery.
- Next step is Phase B1: implement shared design tokens and theme provider in a focused PR, after JWT config is reviewed.
