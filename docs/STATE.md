# GHOST STATE

Last updated: 2026-11-10
Current sprint: 00 - Specification (SRS + ERD v0.4 drafted, awaiting review of GHOST-2.0 code)

## Done
- SRS + ERD v0.4 written (Claude Doc "GHOST - SRS + ERD v0.2", sections 1-11).
- Business decisions BD-01 to BD-14 fixed (see ai/ghost-ps-system/references/01-business-rules.md).
- Skill + master prompt created (ai/ghost-ps-system, ai/GHOST-master-prompt.md).

## In progress
- Nothing in code yet. Waiting for GHOST-2.0 Entities structure to start Sprint 01 (compare code vs ERD, produce keep/modify list).

## Next step
1. User sends the GHOST-2.0 Entities folder (list of classes with their properties).
2. Run Sprint 01: diff against docs/GHOST-SRS-ERD.md, output keep/modify/add list.
3. Then Sprint 02: Auth + Roles + Business Day + Shifts + Handover + CashTransaction + staff scoping.

## Decisions made (short)
- Time in seconds, server UTC, no time rounding, no free minutes.
- Gaming amount: ceiling to whole EGP once on session total (8.33 -> 9).
- Multi = fixed device price; rates per device x mode (10 devices x 2).
- Cancel window 300 s for Staff; after that Admin void only; Transfer Session exists.
- Product sale prices are whole pounds. Cost may have decimals.
- Staff can take cash from drawer without pre-approval but must give category + reason; routed to StockReceipt / Withdrawal / Expense; Admin sees all with alerts.
- One drawer, one open shift at a time; documented handover.
- Cameras: separate NVR system, no integration v1; Tailscale VPN for remote; read-only web dashboard later (P2).

## Open questions
- Percent discount rounding (proposal: round discount down to whole pound).
- Withdrawal alert threshold amount.
- Advance linked to salary deduction (P2)?
- Number of cameras (proposal 8-channel NVR), thermal printer model.
- GHOST-2.0 entities structure (needed now).

## Known risks
- No code tested yet; all rules are specification only.
- Arabic RTL PDF fonts must be tested early (Sprint 07, but try a sample sooner).
