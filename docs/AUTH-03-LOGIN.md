# AUTH-03 — Login and access-token issuance

Status: implementation in review

## Endpoints

### POST /api/auth/login

- Accepts username and password.
- Unknown and inactive users receive the same generic 401 response.
- Five failed password attempts trigger a 15-minute Identity lockout; locked users receive 423.
- Existing accounts are upgraded to lockout-enabled on first successful-path login before password verification.
- A normal active account receives a short-lived access token and role summary.
- An account with `MustChangePassword=true` receives only a five-minute password-change token. It receives no normal access token.

### POST /api/auth/change-password

- Requires a valid password-change token with `token_use=password_change`.
- Requires the current password and a new password that passes the configured Identity password policy.
- Rejects inactive users, users no longer requiring a password change, and locked accounts.
- On success, clears `MustChangePassword` in the same Identity user update as the password hash change and returns a normal access token.
- Password-change tokens cannot access normal API endpoints.

## Token contract

Normal access tokens contain:
- `sub`: user GUID.
- `unique_name`: username.
- `role`: one claim per Identity role.
- `full_name`: display name.
- `token_use=access`.
- `jti`, issuer, audience, issued-at and expiration.

Password-change tokens contain the user identity and `token_use=password_change`, have a five-minute lifetime, and contain no role claims.

The API fallback authorization policy requires both an authenticated principal and `token_use=access`. The `PasswordChangeOnly` policy requires `token_use=password_change`. Public access is explicitly limited to the login action.

## Current limitations

- Refresh tokens, rotation, replay detection and revocation are not implemented in this slice.
- Security-stamp revocation for already-issued JWTs is not implemented yet.
- Claims-backed current-user abstraction and resource/shift ownership policies are subsequent AUTH-03 work.
- Frontend login/session integration is not implemented yet.
- Until refresh-token/session integration is complete, access tokens expire and the user must sign in again.

## Test coverage

- Active user receives a signed access token with role claims.
- Must-change-password user receives a restricted token without roles.
- Restricted flow changes password and returns a normal access token.
- Inactive account is denied.
- Five invalid passwords trigger lockout.
- Startup policy tests assert normal endpoints require access-purpose tokens and password-change policy accepts only its dedicated token purpose.

All tests use EF Core InMemory and do not connect to SQL Server.