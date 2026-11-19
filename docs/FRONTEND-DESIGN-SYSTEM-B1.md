# GHOST Frontend Design System — B1

Status: implementation in review
Tracking: [Issue #14 — GHOST execution plan](https://github.com/youssef-elqady/GHOST-2.0/issues/14)

## Design intent

Premium, calm and operationally clear. Preserve GHOST's charcoal/warm-neutral surfaces and restrained gold identity. Light and Dark are both first-class modes. Arabic RTL is the default.

## Theme behavior

- One shared `ThemeProvider` owns theme state for Staff and Admin.
- Theme preference is stored under `ghost-theme` with values `light` or `dark`.
- First visit follows the system preference; a saved user preference wins.
- A root `beforeInteractive` bootstrap applies the class before hydration to avoid a visible theme flash.
- The same preference is shared across Staff and Admin routes.
- Theme storage failure must not prevent the current session's theme from working.

## Core tokens

| Token | Light | Dark | Use |
|---|---|---|---|
| Background | `#f5f5f3` | `#0e0e0d` | App canvas |
| Foreground | `#161616` | `#f7f5ef` | Main text |
| Card | `#ffffff` | `#171715` | Cards/panels |
| Gold | `#b48a36` | `#d2a858` | Brand/primary actions |
| Green | `#159447` | `#36c36b` | Success/available |
| Amber | `#b47608` | `#e1a83a` | Warning/active |
| Red | `#c63c3c` | `#ef6969` | Errors/expenses |
| Blue | `#3477c8` | `#70a8eb` | Gaming revenue/info |
| Violet | `#7656b5` | `#b39be8` | Duration/time analytics |

## Analytics color contract

- Gaming revenue: blue.
- Product revenue: green.
- Total collected revenue: gold.
- Expenses/refunds/withdrawals: red, separately identified.
- Gaming duration and device usage: violet in a separate time-axis visualization.
- EGP and minutes/hours must never share one chart axis.
- These are semantic tokens only; no financial chart or sample figures are introduced in this phase.

## Component primitives

- `Button`: primary, secondary, ghost, danger; small/medium/large; disabled state; icon slots.
- `Card`: surface container with header/title/description/content/footer.
- `Badge`: neutral, gold, success, warning, danger, info, violet; optional status dot.
- `PageHeader`: eyebrow, title, description, actions.
- `ThemeToggle`: shared accessible control used by Staff and Admin.

## Accessibility and responsive rules

- Visible keyboard focus uses the semantic focus-ring token.
- Buttons have explicit type defaults and disabled states.
- Reduced-motion preference is respected.
- Existing responsive Staff/Admin layouts remain intact; this phase does not redesign navigation or page information architecture.
- Preserve readable contrast in both themes and do not use color as the only status indicator.

## Scope and non-goals for B1

Included:
- Shared theme state and early theme bootstrap.
- Semantic theme/chart tokens.
- Reusable component primitives.
- Apply shared theme toggle and button/badge primitives to existing Admin device management.

Not included:
- Login/authenticated route protection in the frontend.
- Dashboard KPI/revenue charts.
- Fake/demo financial data.
- A charting dependency.
- Full Admin/Staff shell redesign.

## Acceptance criteria

1. Staff and Admin use the same theme state and preference.
2. First render applies the saved/system theme before hydration.
3. Both themes preserve the existing device manager and Staff operations styling.
4. Reusable primitives compile and expose semantic variants.
5. Keyboard focus and reduced motion are supported.
6. Frontend CI `pnpm build` passes.
