# GHOST Install Guide (technician, one Windows PC, no programming needed)
دليل التثبيت مختصر بالعربي في آخر الصفحة.

STATUS: NOT TESTED on a real Windows PC. The package builds (see release notes) but the install, restart, power-cut and restore steps must pass the clean-machine checklist (CLEAN-MACHINE-TEST-CHECKLIST.md) before GHOST is used for real work.

## Before you start
- Windows 10 or 11, 64-bit, 5 GB free, administrator rights, Edge or Chrome installed.
- The GHOST release folder (copied to the PC; do not move files out of it).
- If the PC has no SQL Server: the Microsoft SQL Server Express installer (SQLEXPR_x64_ENU.exe), copied into the `prereq` folder of the release. No internet is needed on the PC after that.
- Optional: the external backup drive.

## Install
1. (Optional, once) Plug in the external drive. Double click `installer\Prepare-BackupDrive.cmd`, type the drive letter. Write down the printed ExternalDriveId (it looks like `<marker>@<volume serial>`). The drive is recognised by the marker plus the volume serial, not by its letter; a copied marker file on another drive is refused.
2. Right click `installer\Install-GHOST.cmd`, choose Run as administrator.
   - With a backup drive: open PowerShell as administrator in the `installer` folder and run `.\Install-GHOST.ps1 -ExternalBackupDir E:\GhostBackups -ExternalDriveId <id>`.
3. The installer checks the PC, verifies the files, installs SQL Server Express if needed (10 to 20 minutes), then asks you to type `CREATE GHOST` to make a NEW empty database. If a database named GHOST already exists it stops and changes nothing.
4. When it says the API is READY, answer `y` to create the first Admin account and follow the prompts (it asks you to type a confirmation phrase and a password).
5. Use the GHOST icon on the Desktop. GHOST also opens by itself when a user signs in to Windows.

## Check
Run `installer\Check-GHOST.ps1` in PowerShell: every line must say OK, including "Listening on this PC only".

## Daily life
- GHOST starts by itself when Windows starts. The screen opens only after the database is ready.
- If a service crashes Windows restarts it (after 5, 15, then 60 seconds).
- Backups: every day at 03:00 and every time a shift closes, into `C:\GHOST\backups` (last 7 kept) and, if the drive is plugged in, onto the external drive. A missing or wrong drive never blocks work: GHOST shows an alert.

## After a power cut
Start the PC. GHOST marks sessions that were running as needing review. The Admin opens the overview, sets the real end time for each and approves. GHOST never guesses the cut moment.

## Restore (technician only, never on the live database)
Use scripts `Test-BackupRestore.ps1` (repository) to restore into a NEW database name and compare, then decide with the owner. Never restore over the live database without the owner's written approval.

## Uninstall
Right click `installer\Uninstall-GHOST.cmd`, Run as administrator. It removes services and icons. The database and backups are kept. Backups can be deleted only by running `Uninstall-GHOST.ps1 -RemoveBackups` and typing `DELETE BACKUPS`. The database is never deleted by GHOST tools.

## Problems
Logs: `C:\GHOST\logs` (api and frontend, install-*.log). Run Check-GHOST.ps1 and send its output plus the latest log to the developer. Do not send passwords or connection strings.

## بالعربي (مختصر)
1. انسخ مجلد GHOST على الجهاز. لو مفيش SQL Server حط ملف SQL Server Express في مجلد prereq.
2. كليك يمين على Install-GHOST.cmd ثم Run as administrator.
3. اكتب CREATE GHOST لما يسألك (هيعمل قاعدة جديدة فاضية فقط، ولو فيه قاعدة بنفس الاسم هيقف من غير ما يلمسها).
4. أنشئ حساب الأدمن الأول لما يسألك.
5. افتح GHOST من الأيقونة على سطح المكتب. الإزالة لا تحذف القاعدة ولا النسخ الاحتياطية.
