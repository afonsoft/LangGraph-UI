# AD-0004 — Dual authentication: cookie sessions + hashed API keys

## Context
The browser UI needs interactive auth; MCP clients (Cursor, scripts, agents)
are non-browser and need bearer credentials.

## Decision
Cookie session (`HttpOnly`, `SameSite=Lax`, `Secure`, 12 h sliding, forced
password change on first login, 5-strike lockout) for humans; `aft_<32-hex>`
API keys for machines — only the SHA-256 hash + display prefix are stored, the
secret is shown once. Keys carry optional scopes: allowed sources, allowed
tools, per-key chat/integration overrides, and per-key rate limits — all
administered only via cookie-session endpoints (keys cannot self-escalate).

## Consequences
- Positive: one authorization model everywhere (`CookieSession` vs bearer
  policies); revocation is instant; secrets never persist in plaintext.
- Trade-off: key-based callers can't rotate their own keys — deliberate.

## Related SPEC
- SPEC-20260914-auth-login, SPEC-20260916-api-key-settings,
  SPEC-20260923-source-authorization, SPEC-20260923-per-key-rate-limits (`.specs/`)
