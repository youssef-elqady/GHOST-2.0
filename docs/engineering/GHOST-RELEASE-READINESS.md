# GHOST Release Readiness

READY = all of the following TRUE. UNKNOWN counts as FALSE.
| Gate | Status | Why |
|---|---|---|
| BUILD_OK | TRUE | Release build, 0 errors (this run) |
| BUSINESS_RULES_OK | UNKNOWN | unit tests pass; gaps listed in BUSINESS-INVARIANTS |
| AUTHORIZATION_OK | UNKNOWN | role matrix unit test passes; Staff stock permissions undecided; no live check |
| TRANSACTIONS_OK | UNKNOWN | no SQL Server run |
| CONCURRENCY_OK | UNKNOWN | SC-03..08 not run |
| RECOVERY_OK | UNKNOWN | power-cut unit only; no service restart test |
| BACKUP_RESTORE_OK | UNKNOWN | restore never run |
| AUDIT_OK | UNKNOWN | triggers unapplied; rejected attempts not audited |
| END_TO_END_OK | UNKNOWN | no browser run on venue PC |

Overall: NOT READY. Remaining: run scratch suite, apply migrations on scratch, restore test, service install and restart test, pilot.
Open owner decisions: Staff stock permissions; marker vs volume serial; outage threshold 120 s; unique index per session invoice; audit rejected attempts.
