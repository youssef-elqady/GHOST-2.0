# GHOST 2.0: Status Report (working draft, 2026-11-21)
Branch `feature/auth03-refresh-tokens`. Main is untouched. This report records evidence per item. UNKNOWN counts as FALSE. Unit-test success is NOT migration evidence, and no stage below is proof of the next one.

## Staged readiness
| Stage | Status |
|---|---|
| S1 Build successful (compiles, unit tests) | YES: backend unit 275 passed, 0 failed; frontend tsc clean, next build OK |
| S2 Package assembled | PARTIAL: pieces built individually (API, Bootstrap, migration bundle, frontend standalone, node.exe), zip about 245 MB, checksums written. build-release.sh not run end to end |
| S3 Installer executed on Windows | NOT RUN |
| S4 Clean Windows installation passed | NOT RUN (checklist rows 1-21 all NOT RUN) |
| S5 SQL Server migrations 13-16 applied and verified on a scratch server | NOT RUN |
Production Ready: NO.

## The eight statuses
**Implemented:** refresh tokens and session design; outage detection (120 s threshold, 30 s heartbeat) with NeedsReview and Admin approval that now requires a reason; admin outage review panel; audit for session, shift, refund, stock and rejected attempts (IRejectionAudit); refund requires an open shift before any change; stock intake references and append-only StockMovements trigger; intake correction endpoint and Admin intake report endpoint; Staff intake and correction screen; external drive identity = marker id + volume serial with letter-change follow; installer scripts, uninstaller, evidence-collection script; engineering and deployment docs.
**Build Verified:** API, Bootstrap, migration bundle, frontend standalone build (Linux sandbox). PowerShell scripts parse with the PowerShell 7.4 parser (syntax only).
**Automated Tests Passed:** backend unit tests 275 passed, 0 failed (includes drive guard serial tests, rejection audit, refund cross-shift and closed-shift tests, intake correction tests). SQL project: 18 passed, 13 skipped (need real SQL Server).
**SQL Server Integration Tested:** NONE. Scratch suite SC-01..SC-10 and migrations 13-16 have not run on SQL Server.
**Manually Tested:** NONE. No browser walk-through, no Windows run.
**Not Tested:** Windows installer and service behavior, offline and restart behavior, power-pull test, backup/restore into a new DB, uninstall keeps DB and backups, drive swap with real volume serial (Windows API and CIM read untested), frontend screens in a browser, concurrent intake on SQL Server.
**Blocked:** all of the above "Not Tested" items need a real Windows machine and a scratch SQL Server; the Linux sandbox cannot provide either.
**Requires Owner Decision:** (1) approval of the revised financial design (docs/engineering/FINANCIAL-OWNER-DECISIONS-WALKTHROUGH.md, 4 open confirmations); (2) whether intake needs Admin approval before it counts; (3) release of the parked Rebill migration (local branch hold/rebill-migration17); (4) controller-side tightening of refund shift (currently client-supplied CashShiftId); (5) scratch SQL Server access and a Windows test machine.

## Financial module
DESIGN ONLY. No financial code and no migration 17+ exist on the branch. Paid-now = 0 default, Admin-only finance and the rest are specified in the walkthrough doc and wait for his approval.

## Known gaps
Rejected-attempt audit is not wired for receipt approve/reject and product price. Invoice list and refund screens, Admin intake report screen, CancelWindowSeconds setting, and demo-screen wiring are not done.
