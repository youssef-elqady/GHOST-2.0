# Scratch database runbook (run on the venue PC or your SQL Server machine)

Purpose: turn every "built, not yet tested on SQL Server" item into evidence. Nothing here touches GHOST2.
Rules enforced by the scripts and the test code: scratch names only (`GHOST_SCRATCH_*`), a new database is created and never an existing one, no REPLACE, no DROP, no TRUNCATE, no secrets printed.

Prerequisites: .NET 10 SDK, `dotnet tool install --global dotnet-ef`, PowerShell 5.1+, SQL Server reachable. Check the edition first (step 3 prints it).

## Steps (in order, about 20 minutes)

1. Create a new scratch DB and apply all migrations (this is test M-01: migrations succeed on a clean database):
   `.\scripts\scratch-db\New-ScratchDb.ps1 -Server localhost -Name GHOST_SCRATCH_01`
   Expect: "All migrations applied". Any error: keep the output, send it, stop.
2. Run the SQL Server suite:
   `.\scripts\scratch-db\Run-ScratchSuite.ps1 -Server localhost -Name GHOST_SCRATCH_01`
   Expect: 0 failed. Results file: `scripts\scratch-db\scratch-results.trx`.
3. Backup, verify and restore proof (also prints SQL Server edition and recovery model):
   `.\scripts\scratch-db\Test-BackupRestore.ps1 -Server localhost -Source GHOST_SCRATCH_01 -BackupDir D:\GhostScratchBackups`
   Expect: BK-01 to BK-04 all OK.
4. Send me the console output of the three steps (no passwords in it). I will fill the evidence table in docs/engineering/GHOST-TEST-STRATEGY.md and fix whatever failed.

If you use a SQL login instead of Windows authentication add `-User name -Password (Read-Host -AsSecureString)` to each command.

## What the suite proves (IDs)

| ID | Scenario | Pass means |
|----|----------|-----------|
| SC-01 | all migrations applied | no pending migrations |
| SC-02 | append-only triggers (cash ledger, audit log) | UPDATE and DELETE are rejected |
| SC-03 | two simultaneous sales of the last unit | exactly one succeeds, stock 0, one invoice |
| SC-04 | 20 simultaneous sales, 5 in stock | sold equals stock removed, never negative |
| SC-05 | same checkout sent 8 times at once | one invoice, one stock deduction |
| SC-06 | two purchases with identical carts, different keys | two invoices, stock reduced twice |
| SC-07 | same key, different cart | second request rejected, nothing extra sold |
| SC-08 | two Admins refund the same invoice at once | one refund, stock returned once |
| SC-09 | power cut: heartbeat gap 3 h, restart, Admin review | session flagged, outage not billed, approved once, audit rows present |

Not covered by scripts (manual on the PC, see GHOST-LOCAL-DEPLOYMENT): browser run, Windows service restart test, external drive marker, real power pull.
