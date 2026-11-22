# GHOST Local Deployment (single PC, localhost only)

Summary: API on http://localhost:5153 (loopback enforced at startup), frontend via startup task, SQL Server local, Chrome or Edge only (Secure cookie on localhost).
Official install path: deploy/installer/ (this is the folder scripts/release/build-release.sh packages). Scripts: Install-GHOST.cmd (runs Install-GHOST.ps1), Start-GHOST-Frontend.ps1, Check-GHOST.ps1, Open-GHOST.ps1, Prepare-BackupDrive.cmd (runs New-BackupDriveMarker.ps1), Collect-TestEvidence.ps1, Uninstall-GHOST.cmd (runs Uninstall-GHOST.ps1). Uninstall never deletes a DB. The older deploy/windows/ folder is not the install path and is not packaged. Scratch DB steps: docs/deployment/SCRATCH-DB-RUNBOOK.md.
Order on the venue PC: 1) scratch DB + suite + restore test, 2) fix findings, 3) owner approves migration on the real DB after a verified backup, 4) install services, 5) reboot and power-pull test, 6) pilot.
Health: /health/live, /health/ready. Logs: rolling files, 30 days.
Status: scripts written, NOT yet run on Windows. Nothing here has touched any real database.
