# Clean Windows machine test checklist (all items NOT YET RUN)

Fill in Result and Evidence (screenshot or log line). Any FAIL means NOT READY. Use a fresh Windows 10/11 PC or VM with NO internet, NO Node, NO .NET, NO dev tools.

Run sheet: copy the release folder to the clean PC (USB). After EACH row below run `installer\Collect-TestEvidence.ps1` (right click, Run with PowerShell, as administrator) and keep the evidence folders. Zip them and send back with the filled table. The script collects no passwords, connection strings or database content. Rows 15 and 21 must use a scratch database name, never the live GHOST database. Stages are separate: a passing row never counts as proof for a later stage (build, package, installer run, clean install, SQL migrations).

| # | Test | Expected | Result | Evidence |
|---|------|----------|--------|----------|
| 1 | Run installer without admin rights | Stops with a clear message | | |
| 2 | Install with SQL Express in prereq | Installs, DB GHOST created after typing the phrase, API READY | | |
| 3 | Run installer again | No data loss, same login keeps working, nothing duplicated | | |
| 4 | DB named GHOST already exists | Installer stops, DB untouched | | |
| 5 | Check-GHOST.ps1 | all OK, listening on loopback only | | |
| 6 | Another PC on the network opens http://<this-pc>:3000 | Cannot connect | | |
| 7 | Unplug network, reboot | GHOST opens from the Desktop icon, full POS flow works offline | | |
| 8 | Reboot with SQL Server slow | Screen waits for READY, no error page | | |
| 9 | Kill GHOST.API.exe in Task Manager | Service restarts within about 5 s | | |
| 10 | Kill node.exe | Task restarts the screen within about 1 min | | |
| 11 | Close shift | Local backup file appears, audit row exists | | |
| 12 | Unplug external drive, close shift | Close succeeds, alert shown, no false success | | |
| 13 | Plug in WRONG drive (other marker) | Copy refused, alert | | |
| 14 | Plug right drive, close shift | Copy verified | | |
| 15 | Restore latest backup into a NEW database name, compare counts | Counts match | | |
| 16 | Start a session, pull the power plug, start PC | Session NeedsReview, outage not billed, Admin sets end time and approves once | | |
| 17 | Normal shutdown during a session, start | Session still Active, no outage flag (gap under threshold) or flagged per threshold | | |
| 18 | Log in, reload browser, restart API | Session persists per refresh design | | |
| 19 | Uninstall | Services and icons gone, DB and backups still there | | |
| 20 | Uninstall -RemoveBackups, answer wrong phrase | Backups kept | | |
| 21 | Scratch suite (SC-01..SC-10) on this SQL Server | all pass | | |

## Build-machine evidence (2026-11-21, Linux sandbox, steps run individually, same commands as build-release.sh)
- API win-x64 self-contained publish: OK (401 files). Bootstrap publish: OK. EF migration bundle (ghost-migrate.exe, dotnet-ef 11.0.0 with EF 10 project): built OK, NOT executed (needs Windows and a scratch DB).
- Frontend `pnpm build` (standalone): OK. The standalone `node server.js` started on Linux and answered HTTP 200 on / and /admin/shifts. Not browser-tested.
- node.exe 22.11.0 downloaded; package assembled, SHA256SUMS written (2358 files), zip about 245 MB. The zip was built from the pieces, the script itself was not executed end to end.
- PowerShell: all installer and deploy scripts parse with the PowerShell 7.4 parser (syntax only). They have NOT been run.
- Nothing above proves the installer works on Windows. Rows 1-21 stay NOT RUN.
