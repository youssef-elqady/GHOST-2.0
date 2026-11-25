# GHOST 2.0 implementation state (live)

Branch feature/auth03-refresh-tokens, base commit 338bc87. Local clone only: NOT committed, NOT pushed.
Status words: CODE (written) / BUILT (dotnet build passed) / TESTED (run on SQL Server) / WORKED (verified in real run).
No SQL Server or API/Frontend run has been done by this work. Nothing is TESTED or WORKED yet.

## Phase 1 - B1 test fixture (tests only, no product code)
- [x] ScratchDb.cs: one-at-a-time Gate, every shift/business day opened by a scenario is closed (never deleted) at the end,
      clear error if the scratch DB already has an open shift/day not opened by the suite. CODE + BUILT.
- [x] ScratchSuiteTests.cs: IAsyncLifetime enter/leave per scenario. SC08 rewritten (same Admin = cashier, 2 concurrent refunds of one invoice). CODE + BUILT.
- [ ] SC03 to SC09 on a fresh GHOST_SCRATCH_ DB: NOT RUN (needs his approval for that DB step).
- SC04 still uses InRange(ok,1,5): not tightened because losers may fail on RowVersion conflict; a real run decides.

## Phase 2 - critical fixes (waiting order: B3, B4/B6, B7/B10, B5)
- [x] B3 stock receipt idempotency (StockIntake has no create path: it only corrects; correction is limited by the already-corrected sum, concurrent corrections rely on Product RowVersion, NOT TESTED)
      CODE + BUILT + unit-tested with fakes (7 tests, 303/303 unit tests pass; mutation check: removing the key lookup makes 4 of them fail).
      Domain: StockReceipt.IdempotencyKey + RequestFingerprint. Handler: same key returns the original receipt, different content is rejected, race loser returns the winner.
      DB: migration 20261125121337_StockReceiptIdempotency (adds 2 columns, unique filtered index (CreatedById, IdempotencyKey), drops IX_StockReceipts_CreatedById). NOT APPLIED to any database.
      API: key now required on POST /api/stock-receipts (400 without). Frontend staff-intake.tsx sends one key per delivery (tsc clean).
      SQL Server test SC10 (6 concurrent identical receipts) written, NOT RUN.
- [ ] B4 pause/resume/transfer ownership: ON HOLD, his decision needed (sessions span handover)
- [ ] B6 complete another staff's session
- [x] B10 fingerprint now includes the discount reason (trimmed). Invoices stored before this (hash without reason, or null hash) are still matched. CODE + BUILT + 2 unit tests.
- [x] B7 discount policy. New Admin settings Sales:MaxDiscountPercent (1-100, default 100 = no extra cap, so no behaviour change until he sets it) and Sales:AllowZeroTotalInvoice (0/1, default 0).
      A discount is checked as effective percent of subtotal (fixed amounts too). Zero-total invoice is refused unless the setting is 1 (this DOES change today's behaviour: 100% discount is now refused by default).
      No schema change (settings are rows). CODE + BUILT + 4 unit tests (mutation-checked). Cap VALUE is his decision; default left permissive on purpose.
- [ ] B5 persistent alert for completed sessions with no invoice

## Waiting on decisions (do not guess)
Admin refund shift/source (B2), pause/resume/transfer across handover, re-invoice after refund (B8),
discount cap value, expense/withdrawal/advance approval, hour-adjustment approval.

## Phase 3 - auth tab isolation: design write-up first, no auth change.
## Phase 4 - new modules: not started.
