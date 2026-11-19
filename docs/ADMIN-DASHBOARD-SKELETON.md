# Admin Dashboard Skeleton — B2/B3

Status: implementation in review
Tracking: Issue #14

## Page hierarchy
1. Shared Admin shell: sidebar, topbar, branch context, shared theme toggle.
2. Page header and data-connection status.
3. KPI strip: total collected revenue, gaming revenue, product sales, gaming hours, completed sessions.
4. Financial analytics panel with distinct legends for gaming, products, total collected and expenses/refunds.
5. Separate usage-time panel for gaming duration and utilization.
6. Financial source summary and shift/cash reconciliation.
7. Product performance table.
8. Device/room utilization table.
9. Data-backed insights and integrity notice.

## Data integrity contract
- All KPI values remain — until a verified API source exists.
- No sample revenue, counts, percentages, session durations, stock levels, or shift balances.
- Missing API data is never represented as zero.
- Financial amounts use EGP and are separated by source.
- Gaming time uses hours/minutes in a separate visualization and axis.
- Expenses/refunds are not silently netted into revenue.
- Gross profit is not shown until product cost snapshots and expenses are reliable.
- Drill-downs must eventually link each aggregate to source invoices, payments, sessions or stock movements.

## Current UI implementation
- Dashboard skeleton is rendered only on Admin Overview.
- KPI cards, chart panels, financial source rows, shift/cash summary, product/device tables, and insights placeholders are present.
- Existing Admin Device Management route remains unchanged in behavior.
- No analytics endpoint, chart library, or mock data has been added in this phase.

## Required backend contracts before real data
- Financial aggregation by business day and custom date range.
- Gaming revenue from completed/paid sessions and payment records.
- Product revenue from invoice lines and payment records.
- Expense/refund/void/discount breakdown with audit context.
- Cash/shift expected-vs-actual reconciliation.
- Session duration and room/device utilization from authoritative session intervals.
- Product quantity and cost snapshots from inventory/sales movements.

## Acceptance gates
- Frontend production build passes.
- Light/Dark and responsive visual QA passes.
- Each future KPI reconciles to source records.
- Currency and time axes remain separate.
- API error, empty, loading, and permission states are visually distinct.