# GHOST Backup and Disaster Recovery

## Policy (owner decisions)
Daily backup, backup on every shift close, copy to external drive, keep 7 or more, a real restore test.
## Implementation
- `BackupHandler` writes a full backup with CHECKSUM and records BackupRecords. Runs in the background after shift close; failure never blocks close.
- `ExternalDriveGuard`: checks the marker file (Backup:ExternalDriveMarker) and id (Backup:ExternalDriveId), presence and writability, never creates the drive root. Drive letter alone is never trusted. Create the marker with deploy/windows/New-BackupDriveMarker.ps1. Open question for owner: marker file vs Windows volume serial (marker chosen).
- Failures: audited as System, alert types BackupFailed, ExternalBackupFailed. External copy is marked successful only after verification.
## Restore test
scripts/scratch-db/Test-BackupRestore.ps1: prints edition and recovery model, BACKUP WITH CHECKSUM, RESTORE VERIFYONLY, RESTORE into a NEW scratch DB (no REPLACE), compares row counts. Status: script written, NOT run. BACKUP_RESTORE_OK = UNKNOWN (counts as FALSE).
## SQL edition and log backups
Edition is not yet known. SQL Server Express has no SQL Agent: use Windows Task Scheduler for the daily backup. Log backups are needed only if the recovery model is FULL; if it is SIMPLE (typical for Express default on new DBs, check output of the restore script) they are not possible or needed. Recommendation after the owner runs the script: SIMPLE plus frequent full backups loses at most the time since last backup; FULL plus log backups every 15 minutes limits loss to 15 minutes. Decision pending the edition and recovery model output.
