# AUTH-03 A2 — Login and access-token issuance

Status: implementation in review
Tracking: [Issue #14 — GHOST execution plan](https://github.com/youssef-elqady/GHOST-2.0/issues/14)

## Endpoint

`POST /api/auth/login`

Request:
- `username`
- `password`

Successful response:
- `accessToken`: short-lived signed JWT
- `tokenType`: `Bearer`
- `expiresAt`: UTC expiry
- `mustChangePassword`
- `fullName`
- `roles`

Invalid credentials, disabled accounts, and locked accounts receive the same generic 401 response. The API does not reveal whether an account exists, is disabled, or is locked.

## Security behavior

- Uses ASP.NET Core Identity password hashing and verification.
- Failed password attempts are recorded through Identity lockout counters; configured policy is 5 failures / 15 minutes.
- Successful verification resets the failed-access counter.
- Disabled and locked accounts cannot receive tokens.
- Normal accounts must have exactly one approved role: `Admin` or `Staff`.
- Every login success, failure, inactive-account denial, lockout, and invalid role assignment writes an AuditLog record. Passwords and raw tokens are never written to audit data.
- Access tokens are signed with the explicitly configured HMAC-SHA256 key and include subject, name, role, token ID, issue time, and the password-change requirement.
- A `MustChangePassword` token has no business role and is accepted only by the dedicated `PasswordChangeOnly` policy.
- Normal API policies additionally require the `ghost:business_access=true` claim. A2 deliberately does not issue that claim. This keeps all operational API routes closed until A5 role/resource/shift authorization is complete and reviewed.
- JWT role/name claim mapping is explicit because inbound claim remapping is disabled.

## Explicitly not included

- Refresh tokens, rotation, replay detection, token-family revocation (A3).
- Password-change endpoint and temporary-password completion flow (A4).
- Claims-backed current-user service, role/resource/shift authorization, endpoint-by-endpoint protection (A5).
- Frontend login integration.

## Acceptance checks

- Valid password produces a verifiable signed token with expected identity and role claims.
- Invalid credentials update Identity failure counters and do not issue tokens.
- Disabled and locked accounts do not issue tokens.
- Temporary-password tokens carry no business role and are blocked by normal API policies.
- No token can access business endpoints until the explicit business-access claim is added after A5.
- Login audit entries contain no password or raw token.
- Release build and tests pass; API login integration tests are required before this slice is marked VERIFIED.
