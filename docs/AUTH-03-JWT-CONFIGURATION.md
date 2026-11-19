# AUTH-03 — JWT configuration

The API requires explicit JWT settings. There are no committed signing secrets, weak development defaults, or silent fallbacks.

## Required configuration

Set these environment variables for the API process:

| Environment variable | Purpose | Validation |
|---|---|---|
| `Authentication__Jwt__Issuer` | Token issuer | Required, non-empty |
| `Authentication__Jwt__Audience` | Token audience | Required, non-empty |
| `Authentication__Jwt__SigningKey` | HMAC signing key | Required, at least 32 UTF-8 bytes |
| `Authentication__Jwt__AccessTokenLifetimeMinutes` | Access-token lifetime | Required, 1–30 minutes |

The configuration section is `Authentication:Jwt`. .NET maps the double underscore in environment variables to configuration separators.

## Local PowerShell example

Generate a fresh key rather than copying a key from documentation or source control:

```powershell
$bytes = [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
$key = [Convert]::ToBase64String($bytes)

$env:Authentication__Jwt__Issuer = "GHOST"
$env:Authentication__Jwt__Audience = "GHOST.StaffAdmin"
$env:Authentication__Jwt__SigningKey = $key
$env:Authentication__Jwt__AccessTokenLifetimeMinutes = "10"
```

These values apply only to the current PowerShell process and child processes. Generate and store a separate secret in the approved secret manager for every deployed environment. Never commit the key, paste it into issues/PRs, or reuse a production key locally.

## Security behavior

- Options are validated at application startup; missing or invalid values stop startup.
- HMAC-SHA256 is the only accepted signing algorithm.
- Issuer, audience, signature, and lifetime are validated.
- Expiration is required; clock skew is limited to 30 seconds.
- Raw bearer tokens are not saved by the authentication handler and detailed token errors are not exposed to clients.
- This slice configures token validation only. It does not yet issue tokens, implement login/refresh, or authorize individual endpoints. Keep the API on trusted local development until the remaining AUTH-03 endpoint-protection work is complete.
