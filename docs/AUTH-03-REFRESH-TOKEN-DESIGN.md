# AUTH-03 A3 — Refresh-token and session lifecycle design

Status: design proposed; implementation not started
Base: merged AUTH-03 login/password-change flow in PR #29 and audit hardening in PR #31.

## Goals

- Keep users signed in without long-lived bearer JWTs.
- Rotate refresh credentials on every use.
- Detect replay of a consumed/revoked refresh token and revoke its entire session family.
- Support logout of one session and later account-wide revocation on password reset/disable.
- Make session revocation visible to access-token validation, not only to future refresh requests.
- Keep raw refresh tokens out of the database, logs, audit rows, and browser-readable storage.

## Refresh-token format and persistence

- Generate 32 cryptographically random bytes per token and encode as Base64URL.
- Persist only SHA-256 hash (64-character hex) of the token; never persist the raw value.
- Add a dedicated `RefreshTokens` table with:
  - `Id` (GUID primary key)
  - `UserId` (FK to Users)
  - `FamilyId` (GUID identifying one browser/device session family)
  - `TokenHash` (unique, fixed-length 64)
  - `CreatedAt`, `ExpiresAt`, `FamilyExpiresAt`
  - `ConsumedAt`, `RevokedAt`
  - `ReplacedByTokenId`
  - `RevocationReason`
  - SQL Server `rowversion` concurrency token
- Index `TokenHash` uniquely, `FamilyId`, and `UserId`.
- Use restrictive user FK behavior; do not cascade-delete refresh history.

## Lifetime policy

- Sliding token lifetime: **7 days** from issuance.
- Absolute session-family lifetime: **30 days** from initial login.
- Each rotation expires at the earlier of `now + 7 days` or the family's absolute expiry.
- Expired/near-expiry families require a fresh login after 30 days.
- These are initial V1 defaults and should be configurable later without weakening the absolute cap.

## Rotation transaction and replay behavior

1. Require the refresh cookie, trusted Origin, and a custom `X-GHOST-Request: 1` header.
2. Hash the supplied cookie and begin a SQL transaction using Serializable isolation.
3. Load the token row by hash and inspect its family and user.
4. Reject unknown, expired, revoked, inactive-user, or password-change-required sessions.
5. If a token is already consumed/revoked, treat the request as replay:
   - revoke every still-active token in that `FamilyId`
   - record a security audit event
   - commit the revocation
   - return generic 401 and clear the refresh cookie
6. On first valid use:
   - mark the presented token consumed
   - create a new token hash in the same family
   - link old `ReplacedByTokenId` to the new row
   - issue a new access JWT carrying `sid=FamilyId`
   - commit before returning the access token and replacement cookie
7. Use both transaction isolation and rowversion/concurrency handling; two simultaneous refreshes must never produce two valid descendants.

## Access-token revocation

- Normal access JWTs carry a `sid` claim for their refresh-token family.
- JWT validation checks that the family remains active and that the user remains active. This makes logout, replay revocation, and account disable effective for already-issued access tokens rather than waiting only for the 10-minute JWT expiry.
- Password-change-purpose tokens remain separate, short-lived, and do not carry a normal session family.
- The database check is the authoritative V1 behavior. Any caching optimization must preserve prompt revocation and have a bounded, documented TTL.

## Browser transport and CSRF

- The raw refresh token is delivered only in an **HttpOnly cookie**; it is never returned in JSON.
- Cookie attributes: `HttpOnly`, `SameSite=Strict`, `Path=/api/auth`, no Domain attribute, `Secure=true` outside Development. Development may use `Secure=false` only on loopback HTTP.
- Frontend requests use `credentials: 'include'`; access JWT remains in memory only. Never put access or refresh tokens in localStorage/sessionStorage.
- CORS remains an exact allowlist (current local origins) and adds credentials; never combine credentials with wildcard origins.
- Refresh/logout require an exact allowed Origin plus `X-GHOST-Request: 1`. The custom header forces preflight and prevents ambient-cookie cross-origin requests from being accepted.
- Keep frontend/API hostnames consistent in local development (`localhost` with `localhost`, or `127.0.0.1` with `127.0.0.1`) so SameSite behavior is predictable.
- Cookie name: `ghost_refresh`; production cookie must always be Secure and HTTPS-only.

## Endpoints

- Login: existing `POST /api/auth/login` creates a new family and sets the refresh cookie for normal active accounts. Accounts requiring a password change receive only the restricted password-change token and no refresh cookie.
- Refresh: `POST /api/auth/refresh` rotates the cookie and returns a new access JWT.
- Logout: `POST /api/auth/logout` revokes the current family and clears the cookie; idempotent when the cookie is absent/invalid.
- Successful forced password change creates a new session family only after the password update succeeds; no old family is retained.
- Account-wide revocation hooks are required for later Admin password reset, account disable/reactivation, and own-password changes.

## Audit events

- `Authentication.RefreshSucceeded`
- `Authentication.RefreshRejected`
- `Authentication.RefreshReplayDetected`
- `Authentication.RefreshFamilyRevoked`
- `Authentication.Logout`
- `Authentication.SessionsRevoked`

Audit records may include user/family IDs and reason codes, but never raw refresh tokens, token hashes, access JWTs, cookies, passwords, or secrets.

## Required tests

### Unit/controller
- Random token generation uses 256 bits of entropy and stores only a hash.
- Rotation returns a different raw token and persists only the replacement hash.
- Expired/revoked/inactive/password-change-required sessions are rejected.
- Reusing a consumed token revokes the whole family and records replay audit.
- Logout revokes one family and clears the cookie.
- Password change creates a new family and invalidates previous families.
- Cookie flags, Origin allowlist, and required custom header are enforced.
- Access JWT with revoked `sid` or inactive user is rejected.

### SQL Server integration (guarded)
- Unique token-hash index.
- Transaction rollback/commit semantics.
- Two concurrent refreshes of one token yield at most one replacement; the losing replay revokes the family.
- Family revocation affects all descendants.
- Tests are restricted to exact `GHOST_AUTH02_IT`, with backup/restore gates and no production connection.

## Migration and rollout

- Add model/configuration and generate the EF migration; inspect generated Up/Down SQL and snapshot diff.
- Do not apply any migration in this phase.
- Existing users receive refresh sessions only after successful login; no token backfill.
- Keep the API operational access gate restrictive until A4/A5 are complete.
- Frontend storage/session integration is deferred until cookie, CORS, CSRF, and refresh endpoints are tested.

## Exit criteria

- Design approved in code review.
- Entity/configuration/migration generated and reviewed.
- Refresh, replay, logout, concurrent rotation, and session revocation tests pass.
- Access-token validation rejects revoked families and inactive users.
- No database migration is applied until exact target, backup, restore rehearsal, and explicit approval are confirmed.
