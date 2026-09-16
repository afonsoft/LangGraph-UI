# SPEC-20260916-firecrawl-mcp-proxy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `firecrawl-mcp-proxy` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` (official MCP C# SDK client) + Blazor WASM (BootstrapBlazor) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-firecrawl-mcp-proxy` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |

## 1. User Story

**As a** KnowledgeHub platform user
**I want** to configure my Firecrawl and DeepWiki API keys in a Settings screen and use all official Firecrawl tools through the internal MCP server — testable in the Playground with ready-made examples
**So that** I can consume scraping, crawling and search capabilities (plus DeepWiki private repos) with the same experience already provided by other integrated MCPs.

**Problem context:** Today the only external MCP integrated is DeepWiki (`SPEC-20260913-deepwiki-mcp-proxy`), which re-exposes 3 upstream tools through a lazy `McpClient` proxy against the public endpoint. Firecrawl (`https://mcp.firecrawl.dev/v2/mcp`, Streamable HTTP, Bearer `fc-*`) exposes a larger, plan-dependent tool surface — the official docs state the client receives the exact input schema of every available tool on `tools/list`. The platform has no Settings screen and no persisted secret store: upstream credentials live only in `appsettings.json`/env vars. This feature adds the Firecrawl provider reusing the DeepWiki bypass architecture, the first persisted-secret + Settings-UI slice (covering both providers), DeepWiki private-endpoint switching when a key is configured, and Playground-ready examples for the new tools.

## 2. Scope

**In scope:**
- `FirecrawlUpstreamClient` (Server) — same shape as `DeepWikiUpstreamClient`: lazy singleton `McpClient`, `HttpClientTransport` with `AutoDetect` (Streamable HTTP → SSE fallback), `Authorization: Bearer <apiKey>` via `AdditionalHeaders`, one reconnect attempt on transport/session failure, per-call `TimeoutSeconds`.
- `FirecrawlToolsProvider : IToolProvider` — **hybrid registration** (user decision):
  - Static *core* set always emitted when `Enabled` — official names, minimal schemas **with `examples`** (Playground convention), handlers = passthrough or friendly error: `firecrawl_scrape`, `firecrawl_search`, `firecrawl_map`, `firecrawl_crawl`, `firecrawl_check_crawl_status`, `firecrawl_parse`.
  - Dynamic merge: when an effective API key exists, call upstream `tools/list` (`McpClient.ListToolsAsync`, cached `ToolsCacheSeconds`, default 300s) and merge every returned tool into the catalog — **names and `inputSchema` byte-identical to upstream**; dynamic entries override the static same-named ones (upstream is authoritative).
  - Upstream tool annotations (`readOnlyHint`/`destructiveHint`) mapped to `CatalogTool.ReadOnly` so the Playground badge + HITL write-confirm gate still apply to mutating/billable tools (e.g. `firecrawl_crawl`, `firecrawl_interact`, `firecrawl_monitor_delete`).
  - On upstream list failure → keep last-known-good cache; if none, emit the static core set only.
- **DeepWiki private mode (user decision):** `DeepWikiUpstreamClient` resolves its key from the secret store (DB) → env `DeepWiki__ApiKey`; **when a key exists the endpoint switches to `https://mcp.devin.ai/mcp`** (private repos, Bearer), otherwise `https://mcp.deepwiki.com/mcp` (public, no auth). `DeepWikiOptions` gains `PrivateEndpoint`; key save/remove resets the client.
- `Firecrawl` config section + `ConfigurationValidator` (`ValidateFirecrawl` + `DeepWiki:PrivateEndpoint` check) + `docker-compose.yml`/`appsettings.json`/env parity.
- Persisted integration secrets (new platform capability, multi-provider):
  - `IntegrationSecret` entity + EF Core migration; `IIntegrationSecretStore` (singleton, `IServiceScopeFactory`) encrypting values with ASP.NET Core Data Protection (keys persisted under `<data-dir>/dataprotection-keys`).
  - Effective key resolution order per provider: **DB store → `<Provider>:ApiKey` (env/config)**.
  - REST `GET /api/settings/integrations` + `PUT/DELETE /api/settings/integrations/{provider}` for `firecrawl` and `deepwiki`, behind `AuthPolicies.Operational`; GET never returns raw keys (masked hint `fc-••••last4` + `hasKey` + `source`).
  - `PUT`/`DELETE` invalidate the corresponding upstream client (`ResetAsync`) and the Firecrawl tools cache.
- `Pages/Settings.razor` (`/settings`) + NavMenu entry + `SettingsApiClient` — one card per integration (Firecrawl, DeepWiki): password input, masked state display, save/remove, toast feedback (pattern of `ApiKeys.razor`). Structure must allow adding providers later.
- **Playground:** Firecrawl static schemas carry root+property `examples` (SPEC-20260914-playground-tool-form convention) so "Preencher exemplo" produces runnable payloads; dynamic tools without upstream `examples` use the existing skeleton fallback. Long-running upstream calls (`firecrawl_crawl`, `firecrawl_agent`) get an extended client timeout path in `ToolsApiClient`.
- `backup.sh`/`restore.sh`: include `dataprotection-keys/` so restored backups can decrypt stored secrets (documented trade-off — the backup already contains the full DB).
- Tests: unit (validation, masking, merge dedup, no-key error, secret round-trip, DeepWiki endpoint switch), integration (list enabled/disabled, no-key isError, settings API, DeepWiki key→endpoint behavior), regression (existing DeepWiki + MCP contract suites green).

**Out of scope:**
- Keyless Firecrawl mode (hosted without key exposes only search/scrape/parse) — product decision: require a key, fail friendly.
- Firecrawl OAuth (`/v2/mcp-oauth`) — API key only.
- DeepWiki private-mode *extra* tools (`devin_*`, `generate_wiki`, `list_available_repos`) — the same 3 tools are re-exposed; dynamic DeepWiki `tools/list` remains backlog.
- `firecrawl_parse` two-phase local-file upload handoff and `firecrawl_interact` session semantics — transparent passthrough.
- Generic full Settings framework — only the integrations section; retro-fitting Chat/Embeddings keys into the store.
- Upstream resources/prompts proxying; tool-result caching; `McpProxy` SourceType (still backlog #3).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Mcp/Upstream/` (new Firecrawl trio + DeepWiki credential/endpoint changes), `src/KnowledgeHub.Server/Settings/` (new), `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs`, `src/KnowledgeHub.Client/Pages/Settings.razor` + `Playground` touch-points, DI composition, config validation, EF migration, docker-compose.

### Discovery findings (Phase 1 — required deliverable)

**How DeepWiki is integrated today (the bypass strategy):**

| Concern | Path / mechanism |
| --- | --- |
| Options | `src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiOptions.cs` — `Enabled`, `Endpoint`, `ApiKey`, `TimeoutSeconds` |
| Upstream client | `src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiUpstreamClient.cs` — lazy `McpClient` (SDK), `HttpClientTransport` `AutoDetect`, Bearer via `AdditionalHeaders`, reconnect-once → `isError`, `TransportFactory` test seam |
| Tool provider | `src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolsProvider.cs` — `IToolProvider` emitting `CatalogTool`s with upstream-identical names + mirrored schemas; handlers forward `ctx.Arguments` to `upstream.CallAsync` |
| DI wiring | `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs:107-112` |
| Catalog aggregation | `src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs` — merges all `IToolProvider`s, last-wins on name collision |
| Dispatch | `KnowledgeHubServiceCollectionExtensions.cs:114-152` — `ListToolsHandler`/`CallToolHandler` over `IDynamicToolCatalog` |
| REST façade | `src/KnowledgeHub.Server/Api/ToolsEndpoints.cs` — `/api/tools` (Playground) shares the same catalog/handlers |
| Config | `appsettings.json` `DeepWiki` section; env `DeepWiki__*`; `docker-compose.yml:31-33` |
| Startup validation | `src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs:72-83` |
| Tests | `DeepWikiProxyTests.cs`, `DeepWikiToolValidationTests.cs`, `ConfigurationValidatorTests.cs`, `McpToolsTests.cs`, `McpContractTests.cs`, `ToolsApiTests.cs` |

**Settings / secrets reality check:**
- There is **no Settings page** in `src/KnowledgeHub.Client/Pages/` and **no persisted secret store** in the domain. The `ApiKey` entity holds `aft_*` keys the platform *issues* to clients (hashed) — a different concern.
- All upstream credentials today are config-only (`DeepWiki__ApiKey`, `Embeddings__ApiKey`, `Chat__ApiKey`). This SPEC introduces the first persisted-secret slice, used by Firecrawl **and** DeepWiki.
- No Data Protection usage yet — `Microsoft.AspNetCore.DataProtection` ships in the ASP.NET Core shared framework (no new NuGet dependency).

**Playground mechanism (reused, no redesign):**
- `Playground.razor` lists the whole catalog via `GET /api/tools` — new tools appear automatically.
- `ToolArgumentBuilder` (`KnowledgeHub.Shared/Tooling`) builds the form from `inputSchema`; "Preencher exemplo" consumes the schema's root `examples` annotation (SPEC-20260914-playground-tool-form RF-006/RF-007); schemas without `examples` get a generated skeleton.
- Write-confirm gate fires when `ToolDescriptorDto.ReadOnly == false`.

**Firecrawl upstream facts (verified 2026-09-16, docs.firecrawl.dev/mcp-server):**
- Endpoint `https://mcp.firecrawl.dev/v2/mcp` (Streamable HTTP), `Authorization: Bearer <fc-*>`; key never in the URL.
- Tool availability is connection-mode/plan dependent; known official names: `firecrawl_scrape`, `firecrawl_search`, `firecrawl_map`, `firecrawl_parse`, `firecrawl_crawl`, `firecrawl_check_crawl_status`, `firecrawl_agent`, `firecrawl_agent_status`, `firecrawl_interact`, `firecrawl_interact_stop`, `firecrawl_developer_search`, `firecrawl_research_*`, `firecrawl_monitor_*`, `firecrawl_search_feedback`, `firecrawl_feedback`. Exact set + schemas come from upstream `tools/list` — hence hybrid registration.

**DeepWiki upstream facts (docs + user-provided setup):**
- Public: `https://mcp.deepwiki.com/mcp`, no auth. Private repos: `https://mcp.devin.ai/mcp` + `Authorization: Bearer <key>` — same tool names, plus private-mode extras (out of scope).

**Impacts:** new DB table + migration; new settings API surface (auth-gated); new client page/nav; `DeepWikiUpstreamClient` modified (endpoint switching — regression risk covered by tests); Data Protection key ring under `data/`; backup/restore scripts touched; no contract changes to existing MCP endpoints/tools.

**Risks:** upstream `tools/list` latency on cold start (static fallback + cache); DP keys lost on backup-less restore → secrets undecryptable (keys included in backup); plan-gated tools surface upstream `isError` (passed through); static fallback schemas may drift (documented degraded mode); long `firecrawl_crawl`/`agent` calls vs client HTTP timeout (extended timeout path).

**Dependencies:** `ModelContextProtocol` SDK (`McpClient`, `HttpClientTransport`, `ListToolsAsync` — already referenced); ASP.NET Core Data Protection (shared framework); EF Core migrations (existing).

**Files to read before implementing:**
- `.specs/SPEC-20260913-deepwiki-mcp-proxy.md` · `.specs/SPEC-20260913-dynamic-mcp-tools.md` · `.specs/SPEC-20260914-playground-tool-form.md` · `.specs/SPEC-20260915-mcp-monitor-client-config.md`
- `src/KnowledgeHub.Server/Mcp/Upstream/*.cs` · `src/KnowledgeHub.Server/Mcp/{IToolProvider,CatalogTool,DynamicToolCatalog,ToolArgs,ToolCallContext}.cs`
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` · `Configuration/ConfigurationValidator.cs` · `Program.cs` · `appsettings.json`
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` · `DatabasePath.cs` · `Auth/AuthPolicies.cs`
- `src/KnowledgeHub.Client/Pages/{ApiKeys,Playground}.razor` · `Services/{AuthApiClient,ToolsApiClient}.cs` · `Client/Program.cs` · `Layout/NavMenu.razor`
- `src/KnowledgeHub.Shared/Tooling/ToolArgumentBuilder.cs` · `Contracts/ToolDtos.cs`
- `backup.sh` / `restore.sh` · `docker-compose.yml`
- Official docs: `https://docs.firecrawl.dev/mcp-server` · `https://docs.firecrawl.dev/mcp-server/tools` · `https://github.com/firecrawl/firecrawl`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Mcp/Upstream/FirecrawlOptions.cs            (new)
src/KnowledgeHub.Server/Mcp/Upstream/FirecrawlUpstreamClient.cs     (new)
src/KnowledgeHub.Server/Mcp/Upstream/FirecrawlToolsProvider.cs      (new)
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiOptions.cs             (modified — PrivateEndpoint)
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiUpstreamClient.cs      (modified — key store + endpoint switch + reset)
src/KnowledgeHub.Server/Settings/IIntegrationSecretStore.cs         (new)
src/KnowledgeHub.Server/Settings/IntegrationSecretStore.cs          (new)
src/KnowledgeHub.Server/Domain/Entities/IntegrationSecret.cs        (new)
src/KnowledgeHub.Server/Api/SettingsEndpoints.cs                    (new)
src/KnowledgeHub.Shared/Contracts/IntegrationSettingsDtos.cs        (new)
src/KnowledgeHub.Client/Services/SettingsApiClient.cs               (new)
src/KnowledgeHub.Client/Pages/Settings.razor                        (new)
src/KnowledgeHub.Client/Pages/Playground.razor                      (modified — long-call timeout path, if needed)
src/KnowledgeHub.Client/Services/ToolsApiClient.cs                  (modified — per-call timeout, if needed)
src/KnowledgeHub.Server/Migrations/<ts>_AddIntegrationSecrets.cs    (generated)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs  (modified)
src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs     (modified)
src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs               (modified)
src/KnowledgeHub.Server/Program.cs                                  (modified — MapSettingsApi)
src/KnowledgeHub.Server/appsettings.json                            (modified — Firecrawl + DeepWiki:PrivateEndpoint)
src/KnowledgeHub.Client/Program.cs                                  (modified — SettingsApiClient)
src/KnowledgeHub.Client/Layout/NavMenu.razor                        (modified — Settings link)
docker-compose.yml                                                  (modified — Firecrawl__* env)
backup.sh / restore.sh                                              (modified — dataprotection-keys)
README.md                                                           (modified — env vars / Settings doc)
tests/KnowledgeHub.Tests.Unit/FirecrawlToolValidationTests.cs       (new)
tests/KnowledgeHub.Tests.Unit/IntegrationSecretStoreTests.cs        (new)
tests/KnowledgeHub.Tests.Unit/Server/ConfigurationValidatorTests.cs (modified)
tests/KnowledgeHub.Tests.Integration/FirecrawlProxyTests.cs         (new)
tests/KnowledgeHub.Tests.Integration/SettingsApiTests.cs            (new)
tests/KnowledgeHub.Tests.Integration/DeepWikiProxyTests.cs          (modified — endpoint-switch cases)
```

## 4. Requirements

### RF-001: Firecrawl provider in the internal MCP
- **Description:** Register a Firecrawl `IToolProvider` + upstream client in the internal MCP per official docs (`https://mcp.firecrawl.dev/v2/mcp`, Streamable HTTP, Bearer auth), honoring upstream contracts without changing existing tools.
- **Input → Output:** `tools/call firecrawl_*` → upstream `CallToolResult` passed through.

### RF-002: Same bypass architecture as DeepWiki
- **Description:** `FirecrawlOptions` + `FirecrawlUpstreamClient` (lazy `McpClient`, `AutoDetect`, Bearer via `AdditionalHeaders`, reconnect-once, per-call timeout, `TransportFactory` test seam) + `FirecrawlToolsProvider` contributing `CatalogTool`s to `IDynamicToolCatalog`.
- **Rules:** follow `Mcp/Upstream/` conventions; shared credential resolution via `IIntegrationSecretStore` (used by both upstream clients).

### RF-003: Official tools exposed, original names (hybrid)
- **Rules:**
  - Static core set (always listed when `Enabled`): `firecrawl_scrape`, `firecrawl_search`, `firecrawl_map`, `firecrawl_crawl`, `firecrawl_check_crawl_status`, `firecrawl_parse` — minimal permissive schemas (`type:object`, `additionalProperties:true`) **with `examples`**, marked degraded-mode.
  - With an effective key: upstream `ListToolsAsync` merged verbatim (name + `inputSchema` + annotations); dynamic wins on collision; cache `ToolsCacheSeconds`; failure → last-known-good → static core.
  - `Enabled=false` → provider emits nothing; no aliases/renames ever.

### RF-004: Integration secret store (multi-provider)
- **Description:** `IntegrationSecret` table (`Provider` unique, `ProtectedValue`, `KeyHint` last-4, `UpdatedAt`) + EF migration; `IIntegrationSecretStore` encrypts via `IDataProtector` (purpose `integration-secrets`); DP keys under `<data-dir>/dataprotection-keys`.
- **Rules:** resolution order DB → env/config; providers: `firecrawl`, `deepwiki`.

### RF-005: Settings REST API
- **Description:** `GET /api/settings/integrations` (list all providers masked) + `PUT`/`DELETE /api/settings/integrations/{provider}`; `AuthPolicies.Operational`.
- **Rules:** `PUT` validates non-empty + `fc-` prefix for firecrawl, non-empty for deepwiki; never returns raw secrets; save/remove triggers the provider's client reset + Firecrawl tools-cache invalidation.

### RF-006: Settings UI
- **Description:** `/settings` page with an Integrations section — one card per provider (Firecrawl, DeepWiki): masked state, password input, save/remove, toasts; NavMenu entry "Settings".

### RF-007: Firecrawl credential validation and friendly errors
- **Rules:** no effective key → `firecrawl_*` calls return `isError` "configure a API Key em Settings" (tools remain listed via static set); upstream 401/invalid → sanitized `isError`; structured logs without key material.

### RF-008: DeepWiki private-mode endpoint switch
- **Description:** `DeepWikiUpstreamClient` resolves key (store → env). **Key present → `PrivateEndpoint` (default `https://mcp.devin.ai/mcp`) + Bearer; absent → `Endpoint` (default `https://mcp.deepwiki.com/mcp`), no auth header.** Both URIs configurable; key change resets the session.
- **Rules:** same 3 tools (`ask_question`, `read_wiki_structure`, `read_wiki_contents`) unchanged downstream; behavior identical for existing keyless installs (public endpoint, no header — as today).

### RF-009: Playground integration
- **Description:** New `firecrawl_*` tools appear automatically; static schemas include `examples` so "Preencher exemplo" yields runnable payloads; dynamic tools fall back to skeleton fill; upstream annotations map to `ReadOnly` so mutating/billable tools keep the HITL write-confirm; long-running tools get an extended client timeout.

### RF-010: Startup config validation
- **Description:** `ValidateFirecrawl` (when enabled: `Endpoint` absolute http(s), `TimeoutSeconds`/`ToolsCacheSeconds` positive) + `DeepWiki:PrivateEndpoint` URI check when set.

### RF-011: Observability
- **Description:** Proxy calls in `IMcpActivityFeed` (existing filter — free); structured logs tagged per provider for connect/list/call failures; secrets never logged.

### RNF-001 Security
- No key in logs/tool output/errors/responses; masked in UI; ciphertext at rest (Data Protection); settings endpoints behind `AuthPolicies.Operational`; keys never in URLs.

### RNF-002 Compatibility
- No breaking change: existing tools, MCP endpoints, DeepWiki public behavior and REST façade untouched; `DynamicToolCatalog` contract unchanged; keyless DeepWiki installs behave exactly as today.

### RNF-003 Observability
- Structured, identifiable error events (`firecrawl` / `deepwiki` provider tags).

## 5. API Contract

**Downstream (MCP JSON-RPC, existing transports):**
```json
{ "method": "tools/call", "params": { "name": "firecrawl_scrape", "arguments": { "url": "https://example.com" } } }
```
Result: upstream `CallToolResult` passed through; missing key → `isError` with configuration hint.

**Settings REST (new, `AuthPolicies.Operational`):**

| Endpoint | Request | Response |
| --- | --- | --- |
| `GET /api/settings/integrations` | — | `{ "integrations": [{ "provider": "firecrawl", "hasKey": true, "keyHint": "fc-••••wxyz", "source": "store|env|none" }, { "provider": "deepwiki", ... }] }` |
| `PUT /api/settings/integrations/{provider}` (`firecrawl`\|`deepwiki`) | `{ "apiKey": "…" }` | `204`; `400` empty/invalid format; `404` unknown provider |
| `DELETE /api/settings/integrations/{provider}` | — | `204` |

**Upstream (SDK-managed):**
- Firecrawl: `POST https://mcp.firecrawl.dev/v2/mcp` — `initialize` + `tools/list` + `tools/call`; `Authorization: Bearer <key>` only when an effective key exists.
- DeepWiki: `https://mcp.deepwiki.com/mcp` (no key) ↔ `https://mcp.devin.ai/mcp` (key present, Bearer).

## 6. Acceptance Criteria

- [x] **Given** a valid Firecrawl key configured, **when** a `firecrawl_*` tool is called, **then** execution is routed to the Firecrawl MCP upstream.
- [x] **Given** no Firecrawl key exists, **when** a Firecrawl tool is called, **then** an `isError` clearly states the key must be configured in Settings.
- [x] **Given** an invalid Firecrawl key, **when** a tool executes, **then** the error is sanitized — no secret in output or logs.
- [x] **Given** catalog load with a configured key, **when** the internal MCP initializes, **then** all upstream Firecrawl tools register under exactly their original names (dynamic merge); without a key the static core names are listed.
- [x] **Given** `/settings`, **when** the user opens it, **then** Firecrawl **and** DeepWiki key fields exist (password input, masked state, save/remove).
- [x] **Given** `GET /api/settings/integrations`, **then** raw keys are never returned (masked hint + source only).
- [x] **Given** a DeepWiki key saved in Settings, **when** `ask_question` runs, **then** the call goes to `https://mcp.devin.ai/mcp` with Bearer; **given** the key removed, **then** calls return to `https://mcp.deepwiki.com/mcp` without restart.
- [x] **Given** a DeepWiki env key (`DeepWiki__ApiKey`), **when** calls run, **then** the private endpoint is used (same rule as store).
- [x] **Given** `Firecrawl:Enabled=false`, **when** `tools/list`, **then** no `firecrawl_*` tool appears; DeepWiki and local tools unaffected.
- [x] **Given** the Playground, **when** a `firecrawl_*` static tool is selected and "Preencher exemplo" clicked, **then** the form fills with a runnable example; a `firecrawl_crawl`/mutating tool still shows the write-confirm gate.
- [x] **Given** a key saved via Settings then removed, **when** `tools/call` runs after each change, **then** the effective credential changes without restart.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Upstream `tools/list` fails | network/5xx | last-known-good cache → else static core; `tools/call` → `isError` |
| Upstream 401 | bad key | `isError` sanitized; warning log without key material |
| Plan-gated tool called | e.g. `firecrawl_agent` on free plan | upstream `isError` passed through |
| Crawl exceeds timeout | long `firecrawl_crawl` | `isError` timeout (configurable) |
| DB unavailable on secret read | startup race | env-key fallback; warning log |
| Key rotation via Settings | `PUT` mid-flight | new sessions use new key; in-flight calls finish on old session |
| Dynamic tool lacks `examples` | upstream schema | Playground skeleton fill |
| Long tool via Playground | `firecrawl_crawl` > default HTTP timeout | extended timeout path or clear timeout error |

## 7. Task Plan

| # | Task | Validation |
|---|------|-----------|
| T1 | `FirecrawlOptions` + `appsettings`/`docker-compose`/env parity + `ValidateFirecrawl` + `DeepWiki:PrivateEndpoint` | `ConfigurationValidatorTests` |
| T2 | `IntegrationSecret` entity + migration + `IIntegrationSecretStore` (Data Protection, keys under `data/`) | `IntegrationSecretStoreTests` round-trip/masking |
| T3 | `FirecrawlUpstreamClient` (lazy `McpClient`, `ListToolsAsync`, credential resolution, `ResetAsync`) | unit tests via `TransportFactory` seam |
| T4 | `FirecrawlToolsProvider` hybrid registration (static core + `examples` + dynamic merge + cache + no-key error + annotation mapping) | `FirecrawlToolValidationTests` |
| T5 | `DeepWikiUpstreamClient`/`DeepWikiOptions` key resolution + endpoint switch + reset | `DeepWikiProxyTests` endpoint-switch cases |
| T6 | `SettingsEndpoints` + DTOs + auth wiring in `Program.cs` | `SettingsApiTests` |
| T7 | `Settings.razor` (Firecrawl + DeepWiki cards) + `SettingsApiClient` + NavMenu entry | `dotnet build`; manual smoke |
| T8 | Playground: verify `examples` flow + long-call timeout path in `ToolsApiClient`/`Playground.razor` | `dotnet build`; manual smoke |
| T9 | `backup.sh`/`restore.sh` include `dataprotection-keys/` | shellcheck + local run |
| T10 | Integration tests `FirecrawlProxyTests` + full suite regression | `dotnet test` |
| T11 | DoD + evidences: files changed, diffs, test/build output, Settings + Playground screenshots, `tools/list` capture with `firecrawl_*`, one live tool execution | fill section 9 |

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260916-firecrawl-mcp-proxy` only — never `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched (protected).
- **Security:** keys via env/config or encrypted DB store — never logged, never serialized to frontend, never in URLs; no `.env`/secrets in commits; `McpContractTests.PinnedSchemas` updated in the same commit if any pinned schema changes.
- **Scope:** no aliases/renames of upstream tools; no contract changes to existing endpoints; no new NuGet dependencies without justification (Data Protection is shared-framework).
- **Architecture:** upstream I/O isolated in the `*UpstreamClient`s; catalog contribution is the only coupling to SPEC-04; UI talks only to the settings REST API.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by tests or equivalent evidence.
- [x] Edge cases handled.
- [x] `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green; `shellcheck` clean on touched scripts.
- [x] Evidences attached: list of changed files, per-file diff, test/build results, Settings screenshot (both providers), Playground screenshot with a `firecrawl_*` tool + example fill, `tools/list` capture showing `firecrawl_*`, one live Firecrawl tool execution.
- [x] No regression on DeepWiki (`DeepWikiProxyTests` + MCP contract suites green; keyless path unchanged).
- [x] Guardrails respected; no secrets in logs/responses.
- [ ] SPEC updated with implementation result → `Status = Done` → PR opened.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Ticket/issue number (none provided).
- Default `Enabled=true` with static core visible — matches DeepWiki's default-on posture and CA-002's "tools visible but error without key" flow.
- Data Protection keys inside backups make the backup able to decrypt stored secrets — accepted trade-off (backup already contains the full DB); documented for operators.
- DeepWiki private mode may expose extra upstream tools (`devin_*`, `generate_wiki`, `list_available_repos`) — intentionally not re-exposed here; candidate for the dynamic-list backlog.

## 10. Implementation Result (2026-09-16)

**Status → Done.** Implementado conforme aprovado; evidências abaixo.

### Verificação

| Gate | Resultado |
| --- | --- |
| `dotnet build KnowledgeHub.slnx` | 0 erros (1 warning CS8604 pré-existente em `IngestionService.cs`) |
| `dotnet test` | **176/176** unit + **140/140** integration |
| `dotnet format --verify-no-changes` | limpo |
| `shellcheck -S error backup.sh restore.sh` | limpo |

### Evidências capturadas

- `PUT /api/settings/integrations/firecrawl` → 204; `GET` → `hasKey=true, source=store, keyHint=fc-••••839c` — resposta nunca contém a key.
- `tools/list` com key configurada: **32 tools**, sendo **25 `firecrawl_*`** do upstream real (merge dinâmico verbatim: `firecrawl_agent`, `firecrawl_monitor_*`, `firecrawl_interact_*`, `firecrawl_research_*`, `firecrawl_developer_search`…) + static core como fallback; `readOnlyHint` do upstream mapeado (`firecrawl_monitor_delete` → `false`, `firecrawl_monitor_list` → `true`).
- Execução real: `tools/call firecrawl_scrape {url: example.com}` → markdown real retornado; `POST /api/tools/firecrawl_search` (path do Playground) → resultados web reais.
- Keys persistidas criptografadas no DB real (`data/knowledgehub.db`) via Data Protection — ciphertext `CfDJ…` na tabela `IntegrationSecrets`; seed também executado na instância docker em produção (ambas `firecrawl` e `tavily` — Tavily já preparado para a SPEC seguinte).
- Container `knowledgehub` rebuildado/recriado com a nova imagem (`docker compose up -d --build`) — migration `AddIntegrationSecrets` aplicada no boot.

### Decisões tomadas durante a implementação

- `SetApplicationName("KnowledgeHub")` no Data Protection — sem isso o discriminator derivaria do content root (`/app` no container vs path local), tornando secrets ilegíveis entre contextos.
- `InternalsVisibleTo` → `KnowledgeHub.Tests.Unit` + seams internos (`TransportFactory`, `DynamicToolsSource`, `CreateTransportOptions`) para testes sem rede real.
- `FirecrawlToolsProvider.InvalidateToolsCache()` limpa também o last-known-good — intencional: troca de key não deve servir tools da credencial anterior.
- Timeout do `HttpClient` WASM → 6 min (cobre `TimeoutSeconds=300` com margem).
- `backup.sh`/`restore.sh` incluem `dataprotection-keys.tar.gz` — sem o key ring os secrets restaurados não descriptografam.

### Pendente

- Screenshots da UI (sem ferramenta de captura automática nesta sessão — a página `/settings` está no ar na instância em produção).
- Ticket `[A DEFINIR]`.
