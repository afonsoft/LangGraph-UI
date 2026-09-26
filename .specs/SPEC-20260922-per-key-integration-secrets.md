# SPEC-20260922-per-key-integration-secrets

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `per-key-integration-secrets` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` (official MCP C# SDK client) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260922-per-key-integration-secrets` |
| Ticket | [#148](https://github.com/afonsoft/LangGraph-UI/issues/148) |
| Status | `Done` |
| Depends on | `SPEC-20260916-api-key-settings` (Done) — `IApiKeyChatSettingsService`, `apikey-{provider}-{keyId}` secret surface |
| Origin | `gap-analysis-20260922` — GAP-implementation-per-key-upstream-secrets (CONFIRMADO, média) + GAP-implementation-perkey-deepwiki-orphan (CONFIRMADO, folded) |

## 1. User Story

**As a** Knowledge MCP Hub API-key holder
**I want** the integration key I saved for my API key (`set_api_key_settings` / `PUT /api/api-keys/{id}/settings/integrations/{provider}`) to be the credential used when *my* session calls upstream tools (Context7, Firecrawl, Tavily, DeepWiki)
**So that** per-key overrides actually take effect — today they are persisted but silently ignored at call time.

**Problem context:**
`ApiKeyChatSettingsService.GetIntegrationSecretAsync` resolves `apikey-{provider}-{keyId}` → `{provider}` global store, but has **0 callers**. Each `*UpstreamClient.ResolveApiKeyAsync` resolves only `secrets.GetAsync(provider)` → env/config, so a per-key secret never reaches the upstream session. Additionally `deepwiki` is accepted by `ApiKeySettingsEndpoints` (`IntegrationProviders.All`) but omitted from `set_api_key_settings`, `DescribeAsync.IntegrationKeys` and `RemoveAsync` — a per-key deepwiki secret is invisible and orphaned on key deletion.

**Blocking prerequisite discovered during verification:** `IHttpContextAccessor` is **not registered** anywhere (empirically proven — probe test resolving it from `WebApplicationFactory<Program>.Services` fails). Consequently `set_api_key_settings` throws `InvalidOperationException` at runtime and the per-key `IChatClient`/`AnswerService` scoping in `KnowledgeHubServiceCollectionExtensions` (:73-96) silently falls back to global. The whole per-key surface is dead until the accessor is registered — RF-000.

## 2. Scope

**In scope:**
- Call-time per-key resolution for all 4 upstream providers (context7, firecrawl, tavily, deepwiki): caller `key_id` claim → `GetIntegrationSecretAsync` → override passed to the upstream client → env fallback preserved.
- Single cached `McpClient` per upstream with transparent reconnect when the effective key changes (existing `_connectedKey` mechanism; approved option: "reconnect ao trocar de chave").
- `deepwiki` as first-class per-key provider: `set_api_key_settings` enum + branch, `IntegrationKeys` in `DescribeAsync`, cleanup in `RemoveAsync`.
- Unit + integration tests.

**Out of scope:**
- Per-caller `tools/list` dynamic merge — listing stays on global key state (approved); callers with only a per-key secret still see the static tool set, and their calls work.
- `McpProxySession`/`McpProxyToolsProvider` (arbitrary `SourceType.McpProxy` sources keep per-source credentials).
- Concurrent per-key session pooling (dictionary of clients) — single-client reconnect may thrash if two different-key callers alternate; documented limitation.
- Cookie-authenticated sessions never carry `key_id` → global resolution (unchanged).

## 3. Technical Context

**Where the change happens:**
Tool handlers receive `ToolCallContext ctx` with `ctx.Services` → `IHttpContextAccessor.HttpContext.User` carrying `ApiKeyAuthenticationHandler.AuthMethodClaim`/`KeyIdClaim` (pattern already in `SettingsToolsProvider` :42-44). Each upstream `*ToolsProvider.DispatchAsync` resolves the caller's key id, fetches the per-key effective secret via `IApiKeyChatSettingsService.GetIntegrationSecretAsync`, and passes it to `*UpstreamClient.CallAsync` as an override. `GetClientAsync` uses `apiKeyOverride ?? ResolveApiKeyAsync()` and the existing `_connectedKey` comparison reconnects transparently when the key differs.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Settings/ApiKeyChatSettingsService.cs` (GetIntegrationSecretAsync :165, IntegrationKeys :79, RemoveAsync :144)
- `src/KnowledgeHub.Server/Mcp/ToolProviders/SettingsToolsProvider.cs` (claims extraction :42-44, provider enum :18/:67)
- `src/KnowledgeHub.Server/Mcp/Upstream/{Context7,Firecrawl,Tavily,DeepWiki}{UpstreamClient,ToolsProvider}.cs`
- `src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs` (claim names)
- `tests/KnowledgeHub.Tests.Unit/Server/{UpstreamCredentialTests,Context7ToolsProviderTests}.cs`
- `tests/KnowledgeHub.Tests.Integration/SettingsApiTests.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Mcp/CallerIdentity.cs                     # new — KeyIdClaim extraction helper
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs# AddHttpContextAccessor (RF-000)
src/KnowledgeHub.Server/Mcp/Upstream/Context7UpstreamClient.cs    # CallAsync(apiKeyOverride)
src/KnowledgeHub.Server/Mcp/Upstream/FirecrawlUpstreamClient.cs   # idem
src/KnowledgeHub.Server/Mcp/Upstream/TavilyUpstreamClient.cs      # idem
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiUpstreamClient.cs    # idem
src/KnowledgeHub.Server/Mcp/Upstream/Context7ToolsProvider.cs     # DispatchAsync per-key resolution
src/KnowledgeHub.Server/Mcp/Upstream/FirecrawlToolsProvider.cs    # idem
src/KnowledgeHub.Server/Mcp/Upstream/TavilyToolsProvider.cs       # idem
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolsProvider.cs     # idem
src/KnowledgeHub.Server/Mcp/ToolProviders/SettingsToolsProvider.cs# +deepwiki
src/KnowledgeHub.Server/Settings/ApiKeyChatSettingsService.cs     # +deepwiki in IntegrationKeys/RemoveAsync
tests/KnowledgeHub.Tests.Unit/Server/PerKeyIntegrationSecretTests.cs      # new — override precedence/reconnect/deepwiki
tests/KnowledgeHub.Tests.Integration/PerKeyIntegrationApiTests.cs         # new — deepwiki round-trip + Bearer set_api_key_settings e2e
tests/KnowledgeHub.Tests.Integration/McpContractTests.cs                  # schema pin + deepwiki enum
```

## 4. Requirements

### RF-000: Register `IHttpContextAccessor`
- **Description:** `builder.Services.AddHttpContextAccessor()` in `AddKnowledgeHubServer` (composition root already consumes it at :73/:83).
- **Rules:** unblocks `set_api_key_settings`, per-key `IChatClient`/`AnswerService` scoping, and RF-001.
- **Input → Output:** DI registration → `IHttpContextAccessor` resolvable.

### RF-001: Caller key-id extraction
- **Description:** A shared helper extracts `Guid?` caller API-key id from `ToolCallContext` via `IHttpContextAccessor` + `KeyIdClaim`/`AuthMethodClaim`.
- **Rules:** returns `null` when HttpContext is absent, auth method ≠ `apikey`, or claim is not a Guid; never throws.
- **Input → Output:** `ToolCallContext` → `Guid?`

### RF-002: Per-key effective secret at call time
- **Description:** Each upstream `*ToolsProvider.DispatchAsync` resolves `callerKeyId`; when non-null it calls `IApiKeyChatSettingsService.GetIntegrationSecretAsync(keyId, provider, ct)` and passes the result as `apiKeyOverride` to `*UpstreamClient.CallAsync`. Resolution order at call time: `apikey-{provider}-{keyId}` → `{provider}` store → env/config.
- **Rules:** cookie sessions and non-apikey callers pass `null` override (global path unchanged); the friendly `isError` no-key result fires only when neither override nor global/env key exists.
- **Input → Output:** upstream tool call → `CallToolResult` executed with the caller's effective credential.

### RF-003: Upstream client key override + transparent reconnect
- **Description:** `CallAsync(toolName, arguments, apiKeyOverride, ct)` on all 4 clients; `GetClientAsync` uses `apiKeyOverride ?? ResolveApiKeyAsync()` and reconnects when the effective key differs from `_connectedKey` (existing mechanism). DeepWiki keeps `EffectiveEndpoint(key)` behavior — per-key secret → private endpoint.
- **Rules:** single cached `McpClient`; no per-key pooling; override is never logged.

### RF-004: DeepWiki as first-class per-key provider
- **Description:** `set_api_key_settings` accepts `deepwiki` (schema enum + branch + descriptions); `DescribeAsync.IntegrationKeys` includes `deepwiki`; `RemoveAsync` deletes `apikey-deepwiki-{keyId}`.
- **Rules:** no other provider list diverges from `IntegrationProviders.All`.

**Business rules / invariants:**
- Per-key secret wins over global store; global store wins over env.
- A caller without a stored per-key secret behaves exactly as today.
- Secrets never appear in logs, tool results, or DTOs beyond the existing last-4 hint.

## 5. API Contract

No new endpoints. `PUT/DELETE /api/api-keys/{id}/settings/integrations/{provider}` already accepts `deepwiki`; `GET /api/api-keys/{id}/settings/chat` response gains `integrationKeys.deepwiki` (`ApiKeyIntegrationKeyDto` — backward-compatible map entry).

## 6. Acceptance Criteria

- [x] **Given** an apikey-authenticated session with a per-key context7 secret **when** it calls `query-docs` **then** the upstream request carries `Authorization: Bearer <per-key>` (not the global/env key). — `Context7_PerKeySecret_UsedAsBearer`
- [x] **Given** an apikey-authenticated session without a per-key secret and a global store key **when** it calls an upstream tool **then** the global key is used. — `Context7_NoPerKey_FallsBackToGlobalStore`
- [x] **Given** no stored keys and only env config **when** any caller invokes an upstream tool **then** the env key is used. — `Context7_NoPerKeyNoStore_UsesEnv`
- [x] **Given** two sequential calls with different effective keys **when** the second call runs **then** the client reconnects with the new key (single `McpClient`, `_connectedKey` mismatch). — `Context7_KeyChangeBetweenCalls_Reconnects`
- [x] **Given** a cookie/session without `key_id` **when** it calls an upstream tool **then** global resolution applies and no exception is thrown. — `Context7_NonApiKeyCaller_SkipsPerKeyLookup`, `Context7_InvalidKeyIdClaim_TreatedAsGlobalCaller`
- [x] **Given** `provider=deepwiki` via `set_api_key_settings` **then** the key is saved; `GET .../settings/chat` shows `integrationKeys.deepwiki`; `DELETE /api/api-keys/{id}/settings` removes `apikey-deepwiki-{id}`. — `SetApiKeySettings_DeepWiki_BearerCaller_SavesPerKey`, `DeepWiki_PerKey_PutGetDelete_RoundTrip`, `SetApiKeySettings_DeepWiki_NullApiKey_RemovesPerKey`

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Per-key removed between calls | `RemoveIntegrationKeyAsync` then new call | next call resolves global/env; reconnect via `_connectedKey` |
| Per-key set, no global/env | only `apikey-{p}-{id}` stored | call works with per-key; `tools/list` still static-only (documented) |
| Invalid `key_id` claim | non-Guid claim value | treated as no per-key (global path) |
| Alternating keys A/B | interleaved calls | reconnect per call — accepted limitation |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read files in section 3; confirm claim names and test seams (`TransportFactory`, `DynamicToolsSource`).
- [x] **T2 — Implementation:** `CallerIdentity` helper; `apiKeyOverride` param on 4 `CallAsync`/`GetClientAsync`; provider dispatch wiring; deepwiki enum/`IntegrationKeys`/`RemoveAsync`; `AddHttpContextAccessor` (RF-000).
- [x] **T3 — Tests:** 11 unit (`PerKeyIntegrationSecretTests`) + 4 integration (`PerKeyIntegrationApiTests`) — all green.
- [x] **T4 — Validation:** `dotnet build` 0W/0E; `dotnet test` 369 unit + 168 integration passed; `dotnet format --verify-no-changes` clean.
- [x] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260922-per-key-integration-secrets` from `main`; never commit to `main`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** no secrets in logs/DTOs beyond last-4 hint; encrypted store via `IIntegrationSecretStore` only.
- **Scope:** no per-key tools/list, no client pooling, no McpProxy changes.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests.
- [x] Edge cases handled.
- [x] `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green.
- [x] Guardrails respected; no PII/tokens in logs.
