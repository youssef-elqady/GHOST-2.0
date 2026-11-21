# GHOST Failure and Recovery

| Failure | Behavior | Evidence |
|---|---|---|
| Browser closes or reloads | Session and cart state live on the server (sessions); refresh cookie restores login within 7 days sliding | U; browser not tested |
| API restarts | Windows service restarts automatically (delayed auto-start, restart on failure). Frontend waits for /health/ready | scripts written, NOT installed or tested |
| SQL Server down | /health/ready returns 503, API refuses work, no partial writes (single transactions) | smoke run with fake DB: 503 confirmed |
| Power cut | On next start, heartbeat gap over 120 s marks Active sessions NeedsReview. We never claim the exact outage moment: no UPS signal exists, so the last heartbeat (30 s interval) is the last confirmed checkpoint. Outage is not billed. Admin sets end time and approves. | U (11 tests); SC-09 not run |
| Concurrent conflict | RowVersion loser gets a conflict error and may retry; retry with the same idempotency key is safe | U; SC not run |
| Backup fails | Shift close still saves; failure audited and alert raised; external copy counted only after verification | U |
| External drive missing, wrong or read-only | Marker file and id checked before copy; failure logged, no success claimed | U; real drive not tested |
| Refresh token reuse | Whole family revoked | U |
| Migration fails | Apply only on scratch first; real DB only with owner approval and a verified backup | not run |

Manual recovery steps live in GHOST-BACKUP-DISASTER-RECOVERY.md and docs/deployment/SCRATCH-DB-RUNBOOK.md.
