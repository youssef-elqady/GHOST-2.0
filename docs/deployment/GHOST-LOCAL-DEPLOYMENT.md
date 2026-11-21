# GHOST Local Deployment (single PC, localhost only)

Summary: API on http://localhost:5153 (loopback enforced at startup), frontend via startup task, SQL Server local, Chrome or Edge only (Secure cookie on localhost).
Scripts in deploy/windows/: Install-GhostServices.ps1, Start-GhostFrontend.ps1, Check-GhostHealth.ps1, New-BackupDriveMarker.ps1, Uninstall-GhostServices.ps1 (never touches a DB). Scratch DB steps: docs/deployment/SCRATCH-DB-RUNBOOK.md.
Order on the venue PC: 1) scratch DB + suite + restore test, 2) fix findings, 3) owner approves migration on the real DB after a verified backup, 4) install services, 5) reboot and power-pull test, 6) pilot.
Health: /health/live, /health/ready. Logs: rolling files, 30 days.
Status: scripts written, NOT yet run on Windows. Nothing here has touched any real database.
