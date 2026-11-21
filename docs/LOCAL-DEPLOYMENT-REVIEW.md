# Local-only deployment review (no code changed for this)

> الملخص: النظام نفسه مفيهوش أي اعتماد على الإنترنت في الباك إند أو الداتابيز، وكل حاجة تقدر تشتغل على جهاز واحد. فيه 3 حاجات لازم تتعدل قبل ما نقول "Local-Only جاهز": خط Google Fonts في الفرونت، كوكي الـ refresh اللي بيتعمله `Secure` في الـ Production (هيتكسر على شبكة المحل بدون HTTPS)، وقائمة CORS اللي مكتوبة ثابتة على localhost. ومفيش أي حاجة موجودة لتشغيل الخدمات تلقائيًا مع ويندوز.

Scope: review of the code and configuration on branch `feature/auth03-refresh-tokens`. Nothing was implemented from this review. Items marked "needs decision" wait for the owner.
Method: file reads and searches only. Nothing was run on a Windows machine, so every Windows-service and browser-cookie statement is design analysis, not a test result, unless it says "tested".

## Short answers to the 8 questions

| # | Question | Answer |
|---|---|---|
| 1 | All three on one PC? | Yes. Frontend: Node `next start`. API: Kestrel. DB: local SQL Server. Nothing in the repo needs a second machine. |
| 2 | Internet dependencies at runtime? | One real one: Google Fonts in the frontend CSS. Nothing else found. |
| 3 | Auth/JWT/refresh/cookies work locally? | JWT yes. Refresh cookie works on `http://localhost` only. It will be rejected on `http://<LAN IP>` in Production (it is marked `Secure`). |
| 4 | Works with internet fully disconnected? | Yes after the font fix and after builds are done once online. Clock drift is the main offline risk. |
| 5 | Sessions survive restart? | Yes. All session state and times are stored in SQL Server; no in-memory timers. Downtime still counts as played time (decision needed). |
| 6 | Auto-start and restart on crash? | Nothing exists in the repo. Needs a small code change and a install script. |
| 7 | Backup plan, no data loss? | Backup code now exists (daily, verified, keep 7). Missing: backup on shift close, copy to a second disk, a tested restore, and the SQL Server recovery model decision. |
| 8 | Current production settings suitable? | Not yet. See the table in section 8. |

## 1. Same PC

Evidence:
- API URL and port: `backend/src/GHOST.API/Properties/launchSettings.json` (`http://localhost:5153`). In Production there is no `Kestrel` section in `appsettings.json`, so the URL must come from `ASPNETCORE_URLS`.
- Frontend: `frontend/package.json` scripts `build` and `start` (`next start`, default port 3000). API address is `NEXT_PUBLIC_GHOST_API_BASE_URL` (`frontend/.env.example`, default `http://localhost:5153`). It is baked in at build time, so a LAN address must be set before `next build`.
- Database: connection string is `ConnectionStrings:DefaultConnection` (`GHOST.API/Configuration/DatabaseConnectionOptions.cs`). Any SQL Server instance works; `Server=localhost` is fine.
- Migrations are not run at startup (no `Migrate()` call anywhere). The first admin is created by the separate tool `backend/src/GHOST.Bootstrap` using env var `GHOST_CONNECTION_STRING`.

Result: works as is. Needs steps (not code): apply migrations, run Bootstrap once, set env vars.

## 2. Internet and cloud dependencies

Checked: all `.csproj` packages, all string URLs in C#, `frontend/package.json`, CSS, layout, `next.config.ts`.

| Finding | Evidence | Effect offline |
|---|---|---|
| Google Fonts import | `frontend/app/globals.css` line 1: `@import url('https://fonts.googleapis.com/...IBM+Plex+Sans+Arabic...')` | Request fails, page falls back to a system font. Arabic still renders but looks different, and the browser may wait for the failed request before painting. **Needs code change** (self-host the font file). |
| `@vercel/analytics` listed in dependencies | `frontend/package.json`. No import found in `app/`, `components/`, `lib/`. | Not used today, so nothing is sent. Remove the dependency so it cannot be switched on by accident. Small code change. |
| Next.js build telemetry | Next.js collects anonymous usage data during `next build` unless disabled. | Harmless offline. Set `NEXT_TELEMETRY_DISABLED=1` as config. |
| NuGet and pnpm downloads | `backend/**/*.csproj`, `frontend/pnpm-lock.yaml` | Build time only. Build on a developer machine, copy the published output to the PC. The PC never needs the internet to run. |
| Backend HTTP calls, licence checks, telemetry | None found: no `HttpClient`, no external URL in C# except the two localhost CORS origins. | None. |
| `.github/workflows/*` | CI only, not part of runtime. | None. |

Packages used by the API: JwtBearer, EF Core SqlServer, Identity EFCore, OpenApi. All local.

## 3. Auth, JWT, cookies on a local network

- JWT: HMAC key from env var, issuer/audience are plain strings, no discovery endpoint. Works offline. (`docs/AUTH-03-JWT-CONFIGURATION.md`; `JwtAuthenticationServiceCollectionExtensions.cs`). Clock skew is 30 s, so the PC clock matters (see section 4).
- Refresh cookie `ghost_refresh`: `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, and `Secure = !isDevelopment` (`GHOST.API/Authentication/RefreshRequestGuard.cs`, `CreateCookieOptions`).
  - Production over plain HTTP: browsers drop `Secure` cookies from HTTP pages. Chrome and Firefox make an exception for `http://localhost`, so a browser on the same PC would work. A tablet or second PC opening `http://192.168.x.x:3000` would not get the cookie. Refresh would then fail and staff would have to log in again every access-token lifetime. That breaks the decision "staff do not re-login". This is design analysis; it was not tested in a browser.
  - Fix options: (a) local HTTPS with a certificate trusted on every device, (b) a config switch for the cookie `Secure` flag, accepted only for a private LAN. Needs a decision.
- `SameSite=Strict`: UI on `:3000` and API on `:5153` of the same host count as the same site, so cookies flow. Different hostnames (for example UI by name, API by IP) would not.
- Origin allowlist for refresh and logout: `Authentication:RefreshToken:AllowedOrigins`, default `http://localhost:3000` and `http://127.0.0.1:3000` (`RefreshTokenOptions.cs`). Config only: add the LAN origin, for example env var `Authentication__RefreshToken__AllowedOrigins__0`.
- Header `X-GHOST-Request: 1`: required on refresh and logout. No domain needed. The frontend must send it (not wired yet, see `FRONTEND-WIRING-PLAN.md`).
- CORS: origins are hard-coded in `GHOST.API/Program.cs` (lines 31 to 39): only `localhost:3000` and `127.0.0.1:3000`. A tablet on the LAN would be blocked by the browser. **Needs code change** (read from config).

## 4. Offline behaviour and the clock

With the font fixed, nothing at runtime needs the internet.

Clock: the system uses the Windows clock for everything. Evidence: `DateTimeOffset.UtcNow` in `Session.cs` (lines 128, 152, 174, 187, 200), `SessionPause.cs`, `SessionSegment.cs`, `Sale.cs`, `Shift.cs`. There is no network time source, no monotonic clock, and no clock-change guard.
- Offline PCs drift. Billing is by timestamps, so drift of a few seconds a month is harmless. A manual clock change is not: moving the clock back can make a running session look shorter, and moving it forward can charge extra time.
- JWT lifetime also uses the clock (30 s skew). A large clock change logs everybody out.
- Needs decision: keep Windows time sync on against a local source, or accept manual time and add a guard that refuses a clock that moved backward more than a few minutes since the last database write.

## 5. Sessions after an app or PC restart

Evidence: sessions, segments, and pauses are database rows with `StartedAt`/`EndedAt` (`Session.cs`, `SessionSegment.cs`, `SessionPause.cs`, EF configurations). Billable time is calculated from these timestamps when read (`GetBillableDuration`). The API has no in-memory timers. The only background job is the new backup scheduler.
- Restart of the API, of Node, or of the PC: all sessions are still `Active` after the restart. No state is lost. Not tested on a real database yet.
- Downtime counts as played time: a session that was running when the power went out keeps accruing until someone ends it. The "complete" endpoint (`SessionsController`) has no field for an end time, so the operator cannot end it "at the time the power failed". Needs decision: add an Admin-only "end at" with a required reason (audited), or accept manual discount.
- A crash in the middle of a save: each business action is one `SaveChanges`, so SQL Server rolls back the half-finished one.

## 6. Auto-start and restart on crash

Exists: nothing. No Dockerfile, no compose file, no service script, no `web.config`, no scheduled-task script in the repo (searched `git ls-files`). Only GitHub CI workflows exist.

What is needed, by component:

| Component | Option | Needs |
|---|---|---|
| SQL Server | Windows service, automatic start | Nothing. Installer default. |
| .NET API | Windows Service. Publish self-contained, install with `sc.exe create ... start= auto`, and set `sc failure` to restart. | One small code change: `builder.Host.UseWindowsService()` plus the package `Microsoft.Extensions.Hosting.WindowsServices`. Without it the exe does not answer the service manager properly. Secrets (JWT key, connection string) must be stored as service environment variables or in a protected `appsettings.Production.json`. |
| Next.js | Node process under a service wrapper such as NSSM (restart on exit), or Task Scheduler "at startup" with restart on failure | A script. Optionally `output: 'standalone'` in `next.config.ts` for a smaller deployment folder. |
| Alternative to all of the above | IIS with the .NET hosting bundle for the API | Heavier. Not recommended for one PC. |
| Docker Desktop with compose and restart policies | Possible | Needs Dockerfiles that do not exist, and Docker on Windows needs WSL2. More moving parts than services. Not recommended. |

Also: Windows must not sleep or install updates and reboot in the middle of the day (power plan and active hours), and the PC needs a UPS. Those are venue settings, not code.

## 7. Local backup and restore, with no data loss

What exists (milestone 6, `docs/CHANGES-INVENTORY.md`):
- `BackupHandler`: `BACKUP DATABASE ... WITH CHECKSUM`, then `RESTORE VERIFYONLY`, keeps the newest N (default 7), records every attempt in `BackupRecords`. Manual endpoint `POST api/backups` and a daily job (`Backup:Enabled`, `Backup:Directory`, `Backup:DailyAt`, `Backup:RetentionCount`). Not yet run on a real SQL Server.
- Append-only ledger and stock tables (database triggers) protect history from edits.

What FR-81 asks for (SRS): backup at shift close, at start, and daily; keep 7; restore test.

Missing for "no sales or session data is lost":
1. Backup on shift close and at startup. Easy code change.
2. Same-disk risk: a backup on the same drive does not survive a dead disk. Needs a second location: a second internal disk or a USB drive. Needs decision.
3. Gap between backups: with a daily backup, a disk failure at 9 PM loses the day. To get close to zero loss, use the Full recovery model with transaction-log backups every 5 to 15 minutes. Needs decision (more setup, needs SQL Server Standard or Developer for scheduling by agent, or a scheduled task using `sqlcmd` on Express).
4. SQL Server edition: Express works with BACKUP but has limits (10 GB per database, no SQL Agent). Which edition is installed?
5. A documented and rehearsed restore procedure (restore to a scratch database, count the rows, compare the last invoice number). Not written yet.
6. The SQL Server service account must be able to write to the backup folder; the API does not write the file, SQL Server does.

## 8. Production settings for local use

| Setting | Current state | Local-only suitable? |
|---|---|---|
| API URL | No Kestrel config; falls back to `http://localhost:5000` unless `ASPNETCORE_URLS` is set | Config only |
| HTTPS | No `UseHttpsRedirection`, no HSTS in the API. Dev profile has an https URL with the dev certificate. | OK on HTTP. Decision on whether to use HTTPS |
| `next.config.ts` | Sends `Strict-Transport-Security: max-age=63072000` | Ignored over HTTP. If you later serve HTTPS with a private certificate, browsers will refuse HTTP for two years. Decide before enabling HTTPS |
| Cookie `Secure` | `!IsDevelopment` | Needs code or HTTPS. See section 3 |
| CORS | Hard-coded localhost | Needs code change |
| Refresh origins | Config with localhost default | Config only |
| `AllowedHosts` | `*` | Fine on a LAN |
| JWT secrets | Required env vars, no defaults | Config only. Must be set for the service |
| Connection string | `ConnectionStrings:DefaultConnection`. Trust server certificate setting is up to the string you write. | Config only |
| Logging | Console only | Needs a log file on disk for a service. Needs code or config (decision: file location and size) |
| Frontend API address | Build-time `NEXT_PUBLIC_GHOST_API_BASE_URL` | Config at build |
| OpenAPI endpoint | Mapped only in Development | Fine |

## Summary split

**Already works as is**
- Backend and database need no internet. No outbound calls.
- Session state and billing are stored in SQL Server and survive restarts.
- JWT signing is local. Refresh tokens are in the database.
- All three parts can run on one PC.

**Needs configuration only**
- `ASPNETCORE_URLS`, connection string, JWT variables, `Authentication__RefreshToken__AllowedOrigins__N`, `NEXT_PUBLIC_GHOST_API_BASE_URL` at build time, `NEXT_TELEMETRY_DISABLED=1`.
- Backup folder and `Backup:*` settings; SQL Server permission on that folder.
- Windows power plan, update hours, UPS.

**Needs code changes (small)**
1. Self-host the Arabic font and remove the Google Fonts import.
2. Remove the unused `@vercel/analytics` dependency.
3. Read CORS origins from configuration.
4. Cookie `Secure` handling for plain HTTP on a LAN (or run HTTPS locally).
5. `UseWindowsService()` for the API, and a file logger.
6. Backup on shift close and at startup.
7. Optional: clock-backward guard; Admin "end session at time" with reason.
8. Install and update scripts (services, firewall rule for the LAN port, backup folder).

**Needs decisions from the owner**
1. Will staff use the venue PC only, or also tablets and phones over the LAN? (This decides the cookie and CORS work.)
2. HTTP on the LAN, or HTTPS with a private certificate installed on each device?
3. Where do backups go besides the local disk (USB drive, second disk), and how often?
4. Is near-zero data loss required (log backups every few minutes), or is daily plus shift-close enough?
5. Which SQL Server edition is installed (Express or other)?
6. How are sessions billed during a power cut?
7. Time source: manual clock, or Windows time sync from a local source?
8. Services via Windows Service wrappers (recommended) or another option?

## What was not checked

No Windows machine, no browser test, no SQL Server test, and no load test. Cookie behaviour on HTTP LAN addresses is from browser rules and should be confirmed on the real tablets before relying on it.
