# SPEC-20260914-auth-login

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `auth-login` |
| Type | `Feature` |
| Stack | `.NET 10`, `Blazor WASM`, `EF Core`, `ASP.NET Core Auth` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-auth-login` |
| Ticket | `—` |
| Status | `Done` |

## 1. User Story

**As a** platform owner exposing KnowledgeHub publicly (rag.afonsoft.dev)
**I want** a login screen (default `admin` / `123qwe`, seeded in the database, forced password change on first access) and per-client API keys (`aft_GUID`) to authenticate the MCP endpoint, the REST API and the SignalR hub
**So that** the admin UI, management API and tool surface are no longer open to anyone on the internet.

**Problem context:**
Today there is **no authentication anywhere** — `/api/*`, `/hubs/mcp` and `/mcp` are all anonymous. The deployment is public, meaning `write_knowledge`, `write_note`, `agent_chat` and source management are reachable by anyone. There is no `Users` table, no `AddAuthentication`, no login UI.

## 2. Scope

**In scope:**
- `Users` + `ApiKeys` EF Core entities (SQLite, migration `AddAuth`), seeded `admin` user on first startup.
- Cookie session auth (HttpOnly, SameSite=Lax, Secure, 12h sliding) for the browser: SPA + `/api/*` + `/hubs/mcp`.
- API key auth (`Authorization: Bearer aft_*`, custom `ApiKey` authentication scheme, SHA-256 hash lookup) accepted on `/mcp`, `/api/*` and `/hubs/mcp` — for non-browser clients (Cursor, Claude Desktop, scripts).
- Auth REST surface: `login`, `logout`, `me`, `change-password`, `api-keys` CRUD.
- SPA screens: `/login`, `/change-password` (forced flow), `/api-keys` (list/create/revoke; full key shown **once** on creation).
- Login lockout: 5 consecutive failures → 5 min lockout.
- First-access gate: while `MustChangePassword`, the only authenticated action is `POST /api/auth/change-password`; the SPA redirects to `/change-password`.
- Password policy: ≥8 chars, ≠ current password.

**Out of scope:**
- Multi-user management UI (roles, CRUD de usuários) — `Users` table is generic but only `admin` is seeded.
- OAuth/OIDC/external providers, MFA, password reset, refresh tokens.
- JWT — API key covers the Bearer use case; cookie covers the browser.
- MCP authorization *inside* the protocol (per-tool ACL) — auth happens at the HTTP endpoint.
- Anonymous read-only access tier.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/Domain/Entities/` — new `AppUser`, `ApiKey`.
- `src/KnowledgeHub.Server/Data/` — `KnowledgeHubDbContext` (DbSets + config) + new EF migration.
- `src/KnowledgeHub.Server/Auth/` — new: `AuthEndpoints`, `ApiKeyEndpoints`, `PasswordService` (hash/verify), `ApiKeyAuthenticationHandler`, `AuthSeeder`, `AuthOptions`.
- `src/KnowledgeHub.Server/Program.cs` — `AddAuthentication`/`AddAuthorization`, `UseAuthentication`/`UseAuthorization`, `RequireAuthorization` on every `Map*Api`, `MapHub`, `MapKnowledgeHubMcp`; seed call after `MigrateAsync`.
- `src/KnowledgeHub.Client/` — `Pages/{Login,ChangePassword,ApiKeys}.razor`, `Services/{AuthApiClient,KhAuthenticationStateProvider,AuthRedirectHandler}.cs`, `App.razor` → `AuthorizeRouteView` + `RedirectToLogin`, `NavMenu` (+API Keys entry, logout), `Program.cs` DI.
- `src/KnowledgeHub.Shared/Contracts/AuthDtos.cs` — login/me/change-password/api-key DTOs.
- `docker-compose.yml` — `AUTH_ADMIN_INITIAL_PASSWORD` env passthrough.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Program.cs`, `Data/{KnowledgeHubDbContext,DatabaseMigrator}.cs`
- `src/KnowledgeHub.Server/Domain/Entities/ToolApproval.cs` — entity conventions
- `src/KnowledgeHub.Server/Api/ToolsEndpoints.cs` — Minimal API + ProblemDetails conventions
- `src/KnowledgeHub.McpEngine/McpEndpointExtensions.cs` — `MapKnowledgeHubMcp` returns `IEndpointConventionBuilder`
- `src/KnowledgeHub.Client/{Program.cs,App.razor,Layout/NavMenu.razor,Services/SourceApiClient.cs}` — client conventions
- `tests/KnowledgeHub.Tests.Integration/McpContractTests.cs` — `WebApplicationFactory` fixture pattern (tests must now authenticate)
- `src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs` — config validation hook for `Auth:*`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Domain/Entities/AppUser.cs              (new)
src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs               (new)
src/KnowledgeHub.Server/Auth/AuthOptions.cs                     (new)
src/KnowledgeHub.Server/Auth/PasswordService.cs                 (new)
src/KnowledgeHub.Server/Auth/AuthSeeder.cs                      (new)
src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs     (new)
src/KnowledgeHub.Server/Auth/AuthEndpoints.cs                   (new)
src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs                 (new)
src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs           (DbSets + config)
src/KnowledgeHub.Server/Migrations/*_AddAuth.cs                 (new, dotnet ef)
src/KnowledgeHub.Server/Program.cs                              (auth pipeline + seed)
src/KnowledgeHub.Shared/Contracts/AuthDtos.cs                   (new)
src/KnowledgeHub.Client/Services/AuthApiClient.cs               (new)
src/KnowledgeHub.Client/Services/KhAuthenticationStateProvider.cs (new)
src/KnowledgeHub.Client/Services/AuthRedirectHandler.cs         (new)
src/KnowledgeHub.Client/Pages/Login.razor                       (new)
src/KnowledgeHub.Client/Pages/ChangePassword.razor              (new)
src/KnowledgeHub.Client/Pages/ApiKeys.razor                     (new)
src/KnowledgeHub.Client/App.razor                               (AuthorizeRouteView)
src/KnowledgeHub.Client/Layout/NavMenu.razor                    (+API Keys, logout)
src/KnowledgeHub.Client/Program.cs                              (DI)
docker-compose.yml                                              (env passthrough)
tests/KnowledgeHub.Tests.Unit/Server/AuthTests.cs               (new)
tests/KnowledgeHub.Tests.Integration/AuthFlowTests.cs           (new)
tests/KnowledgeHub.Tests.Integration/*                          (fixtures autenticados)
```

## 4. Requirements

### RF-001: `AppUser` entity + seed
- **Description:** `AppUser { Id:Guid, Username(≤64, unique), PasswordHash, MustChangePassword:bool, FailedAttempts:int, LockoutUntil:DateTimeOffset?, CreatedAt }`. On startup (after `MigrateAsync`), if `Users` is empty → insert `admin` with hash of `Auth:AdminInitialPassword` (default `"123qwe"`), `MustChangePassword=true`. Idempotent; logs "seeded admin user" without the password.
- **Input → Output:** empty DB → `admin` row; existing users → no-op.

### RF-002: Password hashing
- **Description:** `PasswordService.Hash/Verify` via `Microsoft.AspNetCore.Identity.PasswordHasher` (PBKDF2, part of the ASP.NET Core shared framework — no new package; fallback `Rfc2898DeriveBytes.Pbkdf2` SHA-256, 100k iters, if unavailable). Hash is salted, never logged; passwords never appear in logs/ProblemDetails.

### RF-003: Cookie authentication scheme
- **Description:** `AddAuthentication().AddCookie(...)`: HttpOnly, `SameSite=Lax`, `Secure=Always`, sliding 12h, `LoginPath`/`AccessDeniedPath` unset (API returns 401/403 JSON, never redirects — `CookieAuthenticationEvents.OnRedirectToLogin → 401`). Browser `fetch`/XHR and the WASM `HttpClient` send the cookie automatically (same-origin); WebSockets to `/hubs/mcp` carry it too.

### RF-004: `ApiKey` entity + `aft_GUID` generation
- **Description:** `ApiKey { Id:Guid, Name(≤100), KeyHash(SHA-256 hex), Prefix (first 12 chars, e.g. `aft_ab12cd34`), UserId→AppUser, CreatedAt, LastUsedAt?, RevokedAt? }`. Generated key = `aft_` + `Guid.NewGuid().ToString("N")` (32 hex — header-safe, no special chars). Full key is returned **once** at creation; only `Prefix` is stored/displayed. `LastUsedAt` updated on successful auth (throttled: write at most once/minute per key).

### RF-005: `ApiKey` authentication scheme
- **Description:** `AuthenticationHandler` for scheme `"ApiKey"`: reads `Authorization: Bearer <token>`; if it starts with `aft_` → SHA-256 → lookup non-revoked key → principal with `ClaimTypes.NameIdentifier` = user, `auth_method=apikey`. Non-`aft_` tokens → `NoResult` (other schemes may apply). Also honors `?access_token=` on `/hubs/mcp` for non-browser SignalR clients (standard SignalR pattern).

### RF-006: Endpoint protection
- **Description:** `UseAuthentication()` + `UseAuthorization()`; `RequireAuthorization()` (accepting `Cookies` **or** `ApiKey` schemes — authorization policy `AuthenticatedByAnyScheme`) on `MapSourcesApi`, `MapSearchApi`, `MapAskApi`, `MapAgentApi`, `MapApprovalsApi`, `MapThreadsApi`, `MapStreamingApi`, `MapToolsApi`, `MapHub<McpMonitorHub>`, `MapKnowledgeHubMcp`. **Public:** `/api/auth/login`, `/health/live`, `/health/ready`, `MapStaticAssets`, `MapFallbackToFile("index.html")` (SPA must render `/login` unauthenticated).

### RF-007: `POST /api/auth/login`
- **Description:** Body `{username, password}`. Locked (`LockoutUntil > now`) → `423` + retry hint. Wrong credentials → increment `FailedAttempts`; at 5 → `LockoutUntil = now+5min`, reset counter; `401` generic "credenciais inválidas" (no user-existence leak). Success → reset counters, `SignIn` cookie, `200 {username, mustChangePassword}`.

### RF-008: `GET /api/auth/me` + `POST /api/auth/logout`
- **Description:** `me` → `401` anonymous, else `200 {username, mustChangePassword}` — consumed by the WASM `AuthenticationStateProvider`. `logout` → `SignOut`, `204`.

### RF-009: `POST /api/auth/change-password` + first-access gate
- **Description:** Body `{currentPassword, newPassword}`; validates current, policy (`≥8`, `≠` current) → `400` with rule violated. Success → new hash, `MustChangePassword=false`, re-issue cookie. **Gate:** while `MustChangePassword`, every authenticated endpoint except `/api/auth/me`, `/change-password`, `/logout` returns `403 {error:"password_change_required"}` — enforced by authorization policy on a `pwd_changed` claim (claim stamped at sign-in; re-stamped after change).

### RF-010: API key management endpoints
- **Description:** `GET /api/apikeys` → `[{id,name,prefix,createdAt,lastUsedAt,revokedAt}]` (never the secret). `POST /api/apikeys {name}` → `201 {id, name, prefix, key:"aft_…"}` — `key` returned only here. `DELETE /api/apikeys/{id}` → revoke (`RevokedAt=now`), `204`; revoked keys fail auth `401`. Cookie session only — API keys **cannot** manage API keys (no self-service bootstrap from a leaked key).

### RF-011: SPA auth flow
- **Description:** `KhAuthenticationStateProvider : AuthenticationStateProvider` calls `/api/auth/me` (cached); `App.razor` uses `AuthorizeRouteView` + `NotAuthorized` → `RedirectToLogin`. `/login`: form → `POST login` → `mustChangePassword ? /change-password : /`. `/change-password` enforces RF-009 then → `/`. `AuthRedirectHandler : DelegatingHandler` on the shared `HttpClient`: `401` (non-`/api/auth/*`) → invalidate state + `NavigationManager → /login`; `403 password_change_required` → `/change-password`. `NavMenu` adds "API Keys" + "Sair".

### RF-012: `/api-keys` screen
- **Description:** BootstrapBlazor table (name, prefix, created, last used, revoked badge) + "Nova chave" (name input) → modal shows the full `aft_…` key once with copy button + warning "não será exibida de novo"; revoke via `PopConfirmButton`. Same UI conventions as `Sources.razor`.

### RF-013: Tests
- **Description:** Unit: `PasswordService` hash/verify/wrong-password; lockout transitions; `aft_` generation/lookup/revoked rejection. Integration: unauthenticated `/api/sources` → `401`; login `admin`/`123qwe` → `mustChangePassword:true` + cookie; any endpoint while flag set → `403`; change-password → endpoints pass; `/mcp` `tools/list` with `Bearer aft_*` → `200`, without → `401`, revoked → `401`. Existing fixtures updated to authenticate (helper `TestAuth.LoginAsync(factory)`).

## 5. API Contract

```text
POST /api/auth/login        { "username": "admin", "password": "123qwe" }
→ 200 { "username": "admin", "mustChangePassword": true }   + Set-Cookie (HttpOnly)
→ 401 { "error": "credenciais inválidas" }
→ 423 { "error": "conta bloqueada", "retryAfterSeconds": 300 }

GET  /api/auth/me           → 200 { "username": "admin", "mustChangePassword": false } | 401
POST /api/auth/logout       → 204
POST /api/auth/change-password { "currentPassword": "...", "newPassword": "..." }
→ 204 | 400 { "error": "senha deve ter ≥8 caracteres" | "nova senha igual à atual" } | 401

GET    /api/apikeys         → 200 [ { "id": "…", "name": "cursor", "prefix": "aft_ab12cd34",
                                      "createdAt": "…", "lastUsedAt": "…", "revokedAt": null } ]
POST   /api/apikeys         { "name": "cursor" }
→ 201 { "id": "…", "name": "cursor", "prefix": "aft_ab12cd34", "key": "aft_<32hex>" }
DELETE /api/apikeys/{id}    → 204

Non-browser clients (MCP / API / hub):
  Authorization: Bearer aft_<32hex>
  SignalR (non-browser): /hubs/mcp?access_token=aft_<32hex>
```

**Expected errors:** `401` unauthenticated/invalid-or-revoked key · `403 password_change_required` · `423` lockout · `400` password policy — generic ProblemDetails/JSON, no PII.

## 6. Acceptance Criteria

- [x] **Given** fresh DB **when** server starts **then** `admin` exists (hash of `123qwe` or `Auth:AdminInitialPassword`), `MustChangePassword=true`, and `/api/sources` anonymous → `401`. *(covered: `AuthFlowTests` + `AuthSeeder`; verified live on localhost:5009)*
- [x] **Given** `admin`/`123qwe` **when** `POST /api/auth/login` **then** cookie set + `mustChangePassword:true`; SPA lands on `/change-password`. *(covered: `AuthFlowTests.Login_*`; SPA redirect via `mustChangePassword` in `Login.razor`)*
- [x] **Given** `mustChangePassword` **when** calling any other endpoint **then** `403 password_change_required`; after `change-password` → all endpoints pass and SPA reaches `/`. *(covered: `AuthFlowTests.PasswordGate_*` + `PasswordChangedHandler`/`PasswordGateResultHandler`; verified live: 403 → 204 → 200)*
- [x] **Given** 5 failed logins **when** the 6th arrives within lockout **then** `423`; after 5 min or a success → counters reset. *(covered: `AuthFlowTests.Lockout_*` + `AuthTests` lockout transitions)*
- [x] **Given** an API key created in `/api-keys` **when** `tools/list` on `/mcp` with `Bearer aft_*` **then** `200`; revoked → `401`; the full key is never returned by `GET /api/apikeys`. *(covered: `AuthFlowTests.ApiKey_*` incl. `/mcp` initialize with `Accept: application/json, text/event-stream`; verified live: `aft_*` → 200, revoked → 401)*
- [x] **Given** `dotnet test` **then** new auth tests + full suite pass (existing fixtures authenticate via helper). *(113 unit + 91 integration, 0 failures; `TestAuth` rotates the fixture password once then reuses it)*

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| User enumeration | login `nobody`/x | same generic `401` + same timing path (hash a fixed dummy on miss) |
| `Authorization` non-`aft_` | `Bearer abc` | `401` — scheme returns NoResult, not a crash |
| Key used after revoke | revoked `aft_*` | `401` |
| `mustChangePassword` + API key | Bearer `aft_*` | API keys bypass the password gate (they're post-login artifacts) |
| Session expiry mid-use | cookie >12h | `401` → SPA redirects `/login` |
| `admin` deleted/renamed | — | out of scope (single admin); seed recreates only when `Users` is empty |

## 7. Task Plan

- [x] **T1 — Domain + persistence:** entities, DbContext config, `dotnet ef migrations add AddAuth` (`20260915001428_AddAuth`), `PasswordService`, `AuthOptions`, `AuthSeeder` (+ unit tests for hash/lockout — 12/12).
- [x] **T2 — Auth pipeline:** cookie + `ApiKey` schemes, policies (`Authenticated`, `Operational` w/ `pwd_changed`, `CookieSession`), `Program.cs` wiring + `RequireAuthorization` on all mapped surfaces incl. per-endpoint on `MapStreamingApi`.
- [x] **T3 — Endpoints:** `AuthEndpoints` (login/logout/me/change-password — dummy-hash timing equalization, lockout `423`) + `ApiKeyEndpoints` (SQLite-safe in-memory `DateTimeOffset` sort); `ConfigurationValidator.ValidateAuth`.
- [x] **T4 — Client:** `AuthDtos`, `AuthApiClient`, `KhAuthenticationStateProvider`, `AuthRedirectHandler`, pages (`Login`, `ChangePassword`, `ApiKeys`), `App.razor`/`NavMenu`/`Program.cs`, `[Authorize]` on all protected pages.
- [x] **T5 — Tests + verify:** integration suite (auth flow + `/mcp` Bearer — 8/8 `AuthFlowTests`), all existing fixtures authenticate via `TestAuth` (91/91), `dotnet build` ✓, `dotnet format --verify-no-changes` ✓, `dotnet test` ✓ (113 unit + 91 integration); live smoke on `localhost:5009` (login → forced change → `aft_*` key → MCP `initialize` 200 → revoke → 401).

**7.1 Validation:** .NET → unit tests for auth rules + integration tests for endpoint/auth flows; full suite green.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-auth-login`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Secrets:** never log passwords/keys/cookies; API keys stored hashed only; `Auth:AdminInitialPassword` via env, never committed; `123qwe` exists solely as documented seed default behind the forced-change gate.
- **Scope:** single admin user; no roles/OIDC/JWT in this SPEC.
- **Backward compat:** external MCP clients break until configured with `Bearer aft_*` — call this out in the PR; README gains a "Autenticação" section.

## 9. Definition of Done

- [x] All requirements (section 4) implemented; migration `AddAuth` applied cleanly (integration suite runs against it).
- [x] All acceptance criteria (section 6) covered by passing tests or verified manually.
- [x] Edge cases handled (dummy-hash timing equalization, non-`aft_` → `NoResult`, revoked → `401`, API keys bypass the password gate, `SameAsRequest` Secure keeps plain-http dev working).
- [x] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` green.
- [x] Guardrails respected — no secrets in code/logs; hashed-at-rest keys; `.github/workflows/` untouched.
- [ ] Manual smoke at `https://rag.afonsoft.dev`: anonymous → `/login`; `admin`/`123qwe` → forced change; API key created → real `/mcp` `tools/list` via Bearer. *(requires deploy; full flow verified locally on `localhost:5009`)*

## Open Questions / Pending Ambiguity

- Key format detail: `aft_` + GUID format `"N"` (32 hex, no hyphens) assumed — trivially adjustable at implementation.
- Whether `/api/agent/resume` and HITL approval flow need any per-user scoping — out of scope; all authenticated users share the same surface.
