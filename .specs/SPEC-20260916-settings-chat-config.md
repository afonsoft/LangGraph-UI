# SPEC-20260916-settings-chat-config

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `settings-chat-config` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + Blazor WASM (BootstrapBlazor) + EF Core SQLite |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-settings-chat-config` |
| Ticket | PR #82 |
| Status | `Done` — mergeado em `main` via PR #82 (`c036bc2`); migration `20260916192029_AddChatSettings`, `ChatSettingsService`, endpoints `/api/settings/chat` e card UI entregues |

## 1. User Story

**As a** KnowledgeHub platform administrator
**I want** to configure the OpenAI-compatible chat provider (endpoint, model, API key) from the Settings screen and test the connection before saving
**So that** I can enable server-side answer synthesis and the agent loop without redeploying or editing `.env`/`appsettings.json`.

**Problem context:** Chat configuration (`ChatProviderOptions`: `Provider`, `Endpoint`, `Model`, `ApiKey`, `Temperature`, `MaxTokens`, `TimeoutSeconds`) is bind-time only — `IChatClient` is a singleton registered at startup **only when `Chat:Provider != none`** (`KnowledgeHubServiceCollectionExtensions.cs:52-58`), so changing the provider requires editing config + container recreate. The platform already has the infrastructure this feature plugs into: the `/settings` page with integration cards, the encrypted `IIntegrationSecretStore` (Data Protection → SQLite, masked last-4 hints), the `AuthPolicies.Operational`-gated `/api/settings` group, and the reset/invalidate pattern used by upstream clients. Provider is fixed to **OpenAI-compatible** (`openai`) — the same client already proven against the OmniRoute gateway (`OpenAiChatClient` POSTs `{Endpoint}/v1/chat/completions`); `none`/`ollama` remain env-only.

## 2. Scope

**In scope:**
- `ChatSettings` EF entity + migration — single-row override for `Endpoint` + `Model` (`Id`, `Endpoint`, `Model`, `UpdatedAt`); provider implied `openai` when a row exists.
- Chat API key via the **existing** `IIntegrationSecretStore` under slug `chat` (`IntegrationProviders.Chat`) — encrypted at rest, masked hint, 60 s read cache. `chat` is **not** added to `IntegrationProviders.All`: it never appears in the integrations grid and the generic `PUT/DELETE /api/settings/integrations/{provider}` keep returning 404 for it.
- Effective config resolution (singleton `ChatSettingsService`, `IServiceScopeFactory` for EF access — same shape as `IntegrationSecretStore`):
  - Endpoint/Model: stored `ChatSettings` row → env `Chat:Endpoint`/`Chat:Model` (used only when env `Provider` is also effective) → unset.
  - ApiKey: secret store `chat` → env `Chat:ApiKey`.
  - `Temperature`/`MaxTokens`/`TimeoutSeconds`: always env/config — not editable in this slice.
  - Effective provider: `openai` when a stored row exists; otherwise env `Chat:Provider`; otherwise `none`.
- Runtime application **without restart**: `ChatSettingsService` owns a lazily-built, cached `IChatClient` + `Invalidate()`. DI registration changes from conditional singleton to a scoped factory returning the resolver's current client (nullable) — `AnswerService`/`AgentService` keep their `GetService<IChatClient>` null-tolerant consumption.
- REST endpoints inside the existing `/api/settings` group (already `AuthPolicies.Operational` in `Program.cs:189`):
  - `GET /api/settings/chat` — effective state DTO (never the secret).
  - `PUT /api/settings/chat` — save `{endpoint, model, apiKey?}`; blank `apiKey` keeps the stored key; invalidate.
  - `DELETE /api/settings/chat/apikey` — remove stored `chat` key only (env key falls back); invalidate.
  - `DELETE /api/settings/chat` — remove the `ChatSettings` row **and** the stored key → full env fallback; invalidate.
  - `POST /api/settings/chat/test` — probe **form-supplied** values; blank fields fall back to stored → env; nothing persisted.
- `Settings.razor` — new "Chat (LLM)" card above the integrations grid: endpoint + model inputs, password API-key input ("deixe em branco para manter" when a key exists), `Salvar`, `Testar conexão` (inline result: latency + whether the model was listed, or sanitized error), `Remover` (key only, when `source=store`), `Restaurar ambiente` (full reset, when a stored row exists).
- `SettingsApiClient` extensions + new `ChatSettingsDtos.cs` in `KnowledgeHub.Shared/Contracts`.
- Tests: unit (precedence, PUT validation, masking, invalidation) + integration (settings-chat endpoints; `/test` against a stubbed HTTP probe — the service takes a `Func<HttpClient>` seam like `DeepWikiUpstreamClient.TransportFactory`).

**Out of scope:**
- Provider selection in the UI — fixed `openai`; `ollama`/`none` stay env-only.
- Editing `Temperature`, `MaxTokens`, `TimeoutSeconds`, agent-loop limits — env/config only.
- Embeddings or VectorStore settings; generic "any `Section__Key`" editor.
- Chat provider/model discovery UX (autocomplete from `/v1/models`) — the test button reports model presence but the field stays free-text.
- Per-thread/per-request model override.
- Changes to `agent_chat`/`ask_knowledge` tool contracts or MCP surface.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Settings/` (new chat settings service), `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs` (chat endpoints in the existing group), `src/KnowledgeHub.Server/Domain/Entities/` + `Data/KnowledgeHubDbContext.cs` (new entity/migration), `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` (DI rewiring of `IChatClient`), `src/KnowledgeHub.Shared/Contracts/` (new DTOs), `src/KnowledgeHub.Client/Pages/Settings.razor` + `Services/SettingsApiClient.cs`.

### Discovery findings (verified 2026-09-16)

| Concern | Fact |
| --- | --- |
| Chat options | `Chat/ChatProviderOptions.cs` — `Provider` (`none`\|`ollama`\|`openai`), `Endpoint`, `ApiKey`, `Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds` |
| Client construction | `Chat/ChatClientFactory.cs` — `ollama`→`OllamaChatClient`, `openai`→`OpenAiChatClient`, else null; `OpenAiChatClient` POSTs `{Endpoint}/v1/chat/completions` — **endpoint must not include `/v1`** |
| DI wiring | `KnowledgeHubServiceCollectionExtensions.cs:48-62` — `IChatClient` singleton **only when** `Chat:Provider != none`; `AnswerService`/`AgentService` consume via `GetService` (nullable) |
| Validation rules | `ConfigurationValidator.ValidateChat` (lines 157-180): non-`none` provider → absolute http(s) `Endpoint` + non-empty `Model`; numeric checks for timeout/temperature/maxTokens — the PUT endpoint reuses the same rules |
| Secret store | `Settings/IIntegrationSecretStore.cs` + `IntegrationSecretStore.cs` — `IDataProtector` purpose `integration-secrets`, `KeyHint` last-4, 60 s `IMemoryCache`, `GetAsync`/`GetInfoAsync`/`SetAsync`/`RemoveAsync`; accepts arbitrary provider slugs |
| Settings API | `Api/SettingsEndpoints.cs` — `GET/PUT/DELETE /api/settings/integrations[/{provider}]`; group mounted with `RequireAuthorization(AuthPolicies.Operational)` (`Program.cs:189`); save/remove calls `ResetProviderAsync` + `IToolCatalogChangeNotifier` — the invalidate pattern this feature mirrors |
| Settings UI | `Client/Pages/Settings.razor` — card-per-provider grid, `FormState` dict, password input + `IsAsync` save `Button`, `PopConfirmButton` remove, `ToastService` feedback |
| EF entity pattern | `Domain/Entities/IntegrationSecret.cs` (`Guid Id`, required strings, `UpdatedAt`); `KnowledgeHubDbContext.cs:17` `DbSet<IntegrationSecret>` + `OnModelCreating` config at line 113 |
| Client API layer | `Client/Services/SettingsApiClient.cs` — typed `HttpClient` methods returning `ApiResult<object>` with `{error}` body parsing |
| Live env | `Chat__*` wired via `docker-compose.override.yml` `environment:` + `.env` (OmniRoute gateway, model `DeepSeek`) — the UI config supersedes it once saved |

**Impacts:** new DB table + migration; `IChatClient` DI lifetime change (regression surface: `AnswerService`, `AgentService`, `StreamingEndpoints`, `AskEndpoints`, `AgentEndpoints`); `IntegrationProviders` gains a slug constant; no changes to MCP tool contracts.

**Risks:** stale cached client after env↔store precedence flips (mitigated by `Invalidate()` on every mutation); stored config silently shadowing env (`GET` exposes `source` so the UI shows where config comes from); test probe leaking the key (server-side only, sanitized errors, never logged).

**Files to read before implementing:**
- `.specs/SPEC-20260914-llm-answer-synthesis.md` · `.specs/SPEC-20260916-firecrawl-mcp-proxy.md` · `.specs/SPEC-20260914-agent-chat-loop.md`
- `src/KnowledgeHub.Server/Chat/{ChatProviderOptions,ChatClientFactory,OpenAiChatClient,OllamaChatClient,HttpChatClient}.cs`
- `src/KnowledgeHub.Server/Settings/{IIntegrationSecretStore,IntegrationSecretStore}.cs`
- `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs` · `Configuration/ConfigurationValidator.cs` · `KnowledgeHubServiceCollectionExtensions.cs` · `Program.cs`
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` · `Domain/Entities/IntegrationSecret.cs` · `Auth/AuthPolicies.cs`
- `src/KnowledgeHub.Server/Services/{IAnswerService,AnswerService,AgentService}.cs`
- `src/KnowledgeHub.Client/Pages/Settings.razor` · `Services/SettingsApiClient.cs`
- `src/KnowledgeHub.Shared/Contracts/IntegrationSettingsDtos.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Domain/Entities/ChatSettings.cs            (new)
src/KnowledgeHub.Server/Settings/IChatSettingsService.cs           (new)
src/KnowledgeHub.Server/Settings/ChatSettingsService.cs            (new)
src/KnowledgeHub.Shared/Contracts/ChatSettingsDtos.cs              (new)
src/KnowledgeHub.Server/Settings/IIntegrationSecretStore.cs        (modified — IntegrationProviders.Chat)
src/KnowledgeHub.Server/Api/SettingsEndpoints.cs                   (modified — chat endpoints)
src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs              (modified — DbSet + config)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modified — IChatClient via resolver)
src/KnowledgeHub.Server/Migrations/<ts>_AddChatSettings.cs         (generated)
src/KnowledgeHub.Client/Pages/Settings.razor                       (modified — Chat card)
src/KnowledgeHub.Client/Services/SettingsApiClient.cs              (modified — chat methods)
README.md                                                         (modified — Settings chat section)
tests/KnowledgeHub.Tests.Unit/ChatSettingsServiceTests.cs          (new)
tests/KnowledgeHub.Tests.Unit/ChatSettingsValidationTests.cs       (new)
tests/KnowledgeHub.Tests.Integration/SettingsChatApiTests.cs       (new)
```

## 4. Requirements

### RF-001: Persisted chat override
- **Description:** `ChatSettings` entity (`Id` int single-row, `Endpoint` required, `Model` required, `UpdatedAt`) + EF migration; upsert on save. API key stored via `IIntegrationSecretStore.SetAsync("chat", key)` — same DP encryption, `KeyHint`, and read cache as other providers.
- **Rules:** `IntegrationProviders.Chat = "chat"` added but kept **out of** `All`; the row and the key are independent (a row may exist with env-sourced key, and vice-versa).
- **Input → Output:** `PUT {endpoint, model, apiKey?}` → persisted row (+ protected key when `apiKey` non-blank) → `204`.

### RF-002: Effective configuration resolution
- **Description:** `ChatSettingsService` (singleton) resolves effective `ChatProviderOptions`: provider `openai` + stored `Endpoint`/`Model` + store→env key when a `ChatSettings` row exists; otherwise the env-bound options (`IOptions<ChatProviderOptions>` semantics — env `Provider` untouched, so env `ollama`/`none` keep working); `Temperature`/`MaxTokens`/`TimeoutSeconds` always from env/config.
- **Rules:** endpoint stored verbatim; the client layer already normalizes trailing slash + appends `v1/chat/completions`.

### RF-003: Runtime application without restart
- **Description:** `ChatSettingsService` caches the `ChatClientFactory`-built `IChatClient` for the effective options; `Invalidate()` clears it (build is lazy on next `GetClient()`). DI: `services.AddScoped<IChatClient>(sp => sp.GetRequiredService<ChatSettingsService>().GetClient())` — **unconditional** registration replacing the `Provider != none` startup gate; `GetClient()` may return null and consumers keep working (existing `GetService` null-tolerance).
- **Rules:** every mutation endpoint calls `Invalidate()` before returning; thread-safe (lock/`Lazy` swap — a slow rebuild must not block concurrent reads of the old client).

### RF-004: Settings REST contract
- **Description:** endpoints under the existing `/api/settings` group (`AuthPolicies.Operational` — inherited from `Program.cs:189`):
  - `GET /api/settings/chat` → `ChatSettingsDto { provider, endpoint, model, hasApiKey, apiKeyHint, source, envConfigured, updatedAt }` — `source`: `store` | `env` | `none`; never returns the key; when `source=env` the DTO carries the env endpoint/model so the UI can display them read-only.
  - `PUT /api/settings/chat` → `SaveChatSettingsRequest { endpoint, model, apiKey? }` → `204`; validation: `endpoint` absolute http(s) URI, `model` non-empty (mirrors `ValidateChat`); blank/omitted `apiKey` keeps the stored key.
  - `DELETE /api/settings/chat/apikey` → `204`; removes the stored `chat` secret only.
  - `DELETE /api/settings/chat` → `204`; removes the `ChatSettings` row **and** the stored key.
  - `POST /api/settings/chat/test` → `TestChatConnectionRequest { endpoint?, model?, apiKey? }` → `TestChatConnectionResponse`.
- **Expected errors:** `400` `{error}` on validation failure; `404` never (the resource is a singleton); auth handled by the policy.

### RF-005: Connection test probe
- **Description:** `POST /api/settings/chat/test` issues `GET {endpoint}/v1/models` server-side with `Authorization: Bearer <key>` when a key is resolvable (form → store → env), `HttpClient` timeout 10 s, then returns `{ ok, latencyMs, modelListed, detail }`.
- **Rules:** `modelListed` = whether the resolved `model` appears in the response's `data[].id` (null when the body isn't a model list); `ok=false` → `detail` is a sanitized reason (`HTTP 401`, `timeout`, `connection failed`) — never the response body, never the key; the probe uses an injectable `Func<HttpClient>` seam for tests; **nothing is persisted or logged beyond status/latency**.

### RF-006: Settings UI — Chat card
- **Description:** `/settings` gains a "Chat (LLM)" card above the integrations grid: state badge (`configurada` store / `ambiente` env / `não configurada`), endpoint + model text inputs prefilled with the effective values, API-key password input (placeholder `deixe em branco para manter` when `hasApiKey`, hint `••••last4` shown), and actions: `Salvar`, `Testar conexão` (uses **form values**, blank key → stored), `Remover` key (only when a store-sourced key exists — `PopConfirmButton`), `Restaurar ambiente` (only when a stored row exists — `PopConfirmButton`).
- **Rules:** test result renders inline (success: latency + model found/missing; failure: `detail`) plus a `ToastService` notification — same conventions as the integrations cards (`IsAsync`, `IsDisabled` while busy).

### RNF-001 Security
- API key never in responses/logs/errors/toasts; masked hint only (`••••last4`); ciphertext at rest via the existing store; all endpoints behind `AuthPolicies.Operational`; probe never includes the key in the URL or `detail`.

### RNF-002 Compatibility
- Env-only deployments behave exactly as today (provider `none` → null client → raw-context answers; env `ollama`/`openai` → unchanged behavior). A stored row intentionally shadows env endpoint/model — surfaced via `source` in `GET`. MCP tool surface unchanged.

### RNF-003 Observability
- Structured logs for save/remove/test (provider `chat` tag, latency, outcome) — no key material, no endpoint query strings with credentials.

## 5. API Contract

**`GET /api/settings/chat`** — `AuthPolicies.Operational` (cookie or `aft_*` key)

Response:
```json
{
  "provider": "openai",
  "endpoint": "https://omniroute.afonsoft.dev",
  "model": "DeepSeek",
  "hasApiKey": true,
  "apiKeyHint": "••••315f",
  "apiKeySource": "store",
  "source": "store",
  "envConfigured": true,
  "updatedAt": "2026-09-16T18:00:00Z"
}
```

**`PUT /api/settings/chat`**

Request:
```json
{ "endpoint": "https://omniroute.afonsoft.dev", "model": "DeepSeek", "apiKey": "sk-…" }
```
Response: `204 No Content`. Errors: `400 { "error": "endpoint must be an absolute http(s) URI" }` | `{ "error": "model is required" }`.

**`DELETE /api/settings/chat/apikey`** · **`DELETE /api/settings/chat`** → `204 No Content`.

**`POST /api/settings/chat/test`**

Request (fields may be omitted — resolved form → store → env):
```json
{ "endpoint": "https://omniroute.afonsoft.dev", "model": "DeepSeek", "apiKey": "" }
```
Response:
```json
{ "ok": true, "latencyMs": 342, "modelListed": true, "detail": null }
```
Failure:
```json
{ "ok": false, "latencyMs": 10003, "modelListed": null, "detail": "timeout" }
```

## 6. Acceptance Criteria

- [ ] **Given** env-only config (`Chat:Provider=none`) **when** `GET /api/settings/chat` **then** returns `source=none`, `hasApiKey=false`, `provider=none`.
- [ ] **Given** a valid form **when** `PUT /api/settings/chat` with endpoint+model+apiKey **then** `204`, a `ChatSettings` row exists, the key is stored encrypted, and a subsequent `GET` reports `source=store` + `apiKeyHint`.
- [ ] **Given** a stored key **when** `PUT` with `apiKey` blank **then** the stored key is preserved (hint unchanged).
- [ ] **Given** a saved config **when** `/api/ask` or `agent_chat` runs **then** requests go to the stored endpoint/model with the stored Bearer key — no restart.
- [ ] **Given** a saved config **when** `DELETE /api/settings/chat` **then** the row+key are removed, the resolver invalidates, and env config takes over on the next call.
- [ ] **Given** a reachable endpoint listing `DeepSeek` **when** `POST /test` with those form values **then** `{ ok:true, modelListed:true }` and latency > 0.
- [ ] **Given** a wrong key **when** `POST /test` **then** `{ ok:false, detail:"HTTP 401" }` and nothing is persisted.
- [ ] **Given** an unroutable endpoint **when** `POST /test` **then** `{ ok:false }` within ~10 s with a generic `detail`.
- [ ] **Given** the UI **when** opening `/settings` **then** the Chat card shows the effective state and the key field never displays the secret.
- [ ] **Given** a form with edited values **when** clicking `Testar conexão` before `Salvar` **then** the probe uses the typed values (blank key falls back to stored) and no persistence occurs.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Relative/invalid endpoint | `PUT {endpoint:"omniroute"}` | `400 {error}` — absolute http(s) required |
| Empty model | `PUT {model:""}` | `400 {error}` |
| Endpoint with `/v1` suffix | `PUT {endpoint:"https://x/v1"}` | accepted verbatim; `OpenAiChatClient` would produce `/v1/v1/…` — the UI hint + README warn to omit `/v1` (documented, not silently rewritten) |
| Test with no resolvable key | `POST /test`, no key anywhere | probe goes unauthenticated; 401 → `ok:false` |
| `PUT` while agent loop in-flight | concurrent `agent_chat` | old client finishes the scope; next scope picks the new client |
| Stored row + env `ollama` | row exists, `Chat:Provider=ollama` | store wins → provider `openai` |
| Remove key when source=env | `DELETE /apikey`, no stored key | `204` no-op; env key still effective |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read section-3 files; confirm `IntegrationSecret` EF config shape and `AnswerService`/`AgentService` ctor usage of `IChatClient`.
- [ ] **T2 — Persistence:** `ChatSettings` entity + `DbSet` + `OnModelCreating` + `dotnet ef migrations add AddChatSettings -p src/KnowledgeHub.Server`; `IntegrationProviders.Chat`.
- [ ] **T3 — Service:** `IChatSettingsService`/`ChatSettingsService` (effective options, cached client + `Invalidate`, describe/save/delete/removeKey/test with `Func<HttpClient>` seam); DI rewiring in `KnowledgeHubServiceCollectionExtensions`.
- [ ] **T4 — API:** chat endpoints in `SettingsEndpoints`; DTOs in `KnowledgeHub.Shared/Contracts/ChatSettingsDtos.cs`.
- [ ] **T5 — UI:** Chat card in `Settings.razor` + `SettingsApiClient` methods.
- [ ] **T6 — Tests:** unit (resolution precedence, PUT validation, blank-key keep, invalidation, masking) + integration (`GET/PUT/DELETE`, `/test` with stubbed probe, auth gate); regression on existing `AnswerService`/`AgentService` tests.
- [ ] **T7 — Validation:** `dotnet build KnowledgeHub.slnx`, `dotnet test`, `dotnet format --verify-no-changes`; fill DoD (section 9).
- [ ] **T8 — Done + PR:** DoD complete → `Status = Done` → PR on `feature/Devin-20260916-settings-chat-config`.

**7.1 Validation strategy by type/stack**

.NET: unit tests for business rules; integration tests for API/data flows; minimum **80%** coverage on new code paths. Existing suites must stay green (chat consumers are a regression surface).

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`, `master` or `develop` — `feature/Devin-20260916-settings-chat-config`.
- **Workflows:** do not modify `.github/workflows/` (protected).
- **Security:** never commit `.env`, `*.key`, `*.pem`; API key only via the encrypted store; no key material in logs, errors, toasts or DTOs.
- **Scope:** only the Chat card on Settings — no generic settings framework, no embeddings/vector-store UI.
- **Architecture:** no business logic in Razor markup; persistence via EF + the existing secret store; config consumers go through `ChatSettingsService`, never read the table directly.
- **Specs:** this SPEC is the source of truth; keep `Status`/`Ticket` synced.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) covered by passing tests or equivalent evidence (section 7.1).
- [ ] Edge cases handled.
- [ ] `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass; ≥80% coverage on new paths.
- [ ] Guardrails in section 8 respected.
- [ ] Logs contain no secrets; errors use the generic `{error}` format.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on `feature/Devin-20260916-settings-chat-config`.

## Open Questions / Pending Ambiguity

- Ticket/Issue number — `[A DEFINIR]` (no open issue exists; create one via `create-issues` after approval if desired).
