# GHOST Test Strategy and Evidence

Actual results (this branch): backend unit 267 passed, 0 failed. SQL Server project 18 passed, 13 skipped (need a real SQL Server). Frontend: tsc clean, eslint 0 errors, next build OK, 5 node tests pass.

| ID | Scenario | Expected | Evidence |
|---|---|---|---|
| SC-01 | Apply all migrations on scratch DB | succeeds, no pending | NOT YET RUN |
| SC-02 | UPDATE/DELETE on AuditLogs, CashTransactions, StockMovements | blocked by trigger | NOT YET RUN |
| SC-03 | Two sales of last unit | one succeeds, stock 0 | NOT YET RUN |
| SC-04 | 20 sales, stock 5 | 5 succeed | NOT YET RUN |
| SC-05 | Same key x8 | one invoice | NOT YET RUN |
| SC-06 | Same cart, different keys | two invoices | NOT YET RUN (unit passes) |
| SC-07 | Same key, different cart | rejected | NOT YET RUN (unit passes) |
| SC-08 | Double refund | one succeeds | NOT YET RUN |
| SC-09 | Power-cut flow | review then approve once | NOT YET RUN (unit passes) |
| BK-01..04 | Backup, verify, restore, external drive | per script | NOT YET RUN |

Groups A to I (identity, sessions, POS, inventory, cash, audit, backup, deployment, end to end): unit coverage exists for A, B, C, D, E, F logic; G, H, I are manual on the venue PC. Each cell above that says NOT YET RUN counts as FALSE for readiness. Run: scripts/scratch-db/Run-ScratchSuite.ps1 per docs/deployment/SCRATCH-DB-RUNBOOK.md. Safety: tests only accept DB names matching GHOST_SCRATCH_* or GHOST_IT_*.
