# GHOST Authentication & Account Security Decisions

Status: Approved by product owner on 2026-11-12
Target branch: `feature/products-inventory-billing`

## Scope

Implement authentication and account lifecycle for the real GHOST PlayStation operation. This is not a demo login. Backend authorization is authoritative; frontend route guards are UX only.

## Approved decisions

1. Roles are `Admin` and `Staff`; no public self-registration.
2. The first Admin is provisioned by a one-time secure command executed on the server. It must not be an HTTP endpoint, must not use a default/hardcoded password, and must refuse to create another bootstrap Admin after an Admin already exists.
3. Admin creates Staff accounts and can disable/reactivate them.
4. Admin can reset a Staff member's password. The system generates a cryptographically random temporary password, displays it once to the Admin, stores only the password hash, marks the account as requiring a password change, and forces the Staff member to set a new password at next login. Admin cannot view the existing password.
5. Admin changes their own password by supplying the current password and a new password. Do not allow an Admin to change their own password through the Staff reset operation.
6. If the only Admin forgets their password, recovery is through a separate server-side recovery command with explicit local/server access and audit output. Never expose a public password-recovery endpoint without a verified recovery channel.
7. Every password change/reset, account disable/reactivation, successful login, failed login/lockout, refresh-token rotation/revocation, and bootstrap action must create an audit event without logging passwords, raw tokens, or temporary credentials.
8. Disabled accounts cannot authenticate or refresh tokens. Existing sessions must be rejected promptly after disable; short JWT lifetime alone is not considered sufficient revocation.
9. Passwords are stored using ASP.NET Core Identity's password hasher and its password policy/lockout mechanisms. No plaintext password storage or reversible encryption.
10. Refresh tokens are random, high-entropy, stored only as hashes, rotated on use, and revocable. Reuse of a rotated/revoked token is treated as a security event and revokes the affected token family/session.
11. Access tokens are short-lived and validated for issuer, audience, signing key, lifetime, and expected claims. Signing secrets are supplied through secure server configuration/environment, never committed to Git.
12. All API authorization is enforced server-side using policies and resource ownership checks. The acting user ID comes from validated claims through `ICurrentUser`, never from request-body `StaffUserId` or similar caller-controlled identity fields.

## Password lifecycle API intent

- Admin creates a Staff account with a username and display name; the account receives a one-time temporary password and must change it at first login.
- Admin resets Staff password; same one-time temporary-password flow.
- Authenticated user changes own password using current + new password. Admin uses this flow for their own account too.
- Admin may disable/reactivate Staff. Prevent disabling the final active Admin or removing the final viable Admin access path.
- A password reset revokes all refresh sessions for that Staff account. A password change also revokes other refresh sessions; the current session may be explicitly retained only if the security policy is implemented and tested.

## Implementation order (AUTH)

- AUTH-01: persisted user and role model using Identity EF Core, aligned to existing Guid user IDs and SQL Server persistence.
- AUTH-02: one-time Admin bootstrap command and safe recovery command.
- AUTH-03: login, short-lived JWT, hashed rotating refresh tokens, logout/revocation, lockout.
- AUTH-04: force password change, own-password change, Admin Staff reset, create/disable/reactivate Staff.
- AUTH-05: claims-backed `ICurrentUser`, role policies, API middleware, and removal of caller-supplied actor identity.
- AUTH-06: bind shifts/sessions to the authenticated user and enforce resource ownership.
- AUTH-07: SQL Server integration tests for migrations, bootstrap uniqueness, login/lockout, disabled users, password reset/change, refresh rotation/replay, and Admin/Staff authorization.

## Security acceptance tests

- No credentials, default password, or signing key exist in source control.
- Bootstrap refuses to create a second Admin and does not print/store a plaintext password in logs.
- Staff cannot access Admin endpoints even by calling the API directly.
- Admin can reset Staff password but cannot retrieve the previous password.
- Temporary password works once for initial authentication and forces a new password before any business operation.
- Own-password change requires the correct current password.
- Password reset/change invalidates the required refresh sessions.
- Disabled Staff cannot log in or refresh; existing access is rejected according to the implemented revocation check.
- Rotated refresh-token replay is rejected and audited.
- Passwords and tokens never appear in logs or API error bodies.
- Cross-Staff shift/session access is denied.

## Important implementation note

Do not change the existing EF Core context/migrations until the Identity table mapping and migration strategy have been reviewed against the current SQL Server schema. The first code slice must preserve existing business tables and Guid foreign-key compatibility. Generate and inspect migrations locally; do not hand-edit generated snapshots or apply schema changes to production without a backup and restore plan.
