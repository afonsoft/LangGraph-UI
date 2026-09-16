# SPEC-20260916-tavily-mcp-proxy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `tavily-mcp-proxy` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` (official MCP C# SDK client) + Blazor WASM (BootstrapBlazor) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-tavily-mcp-proxy` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |
| Depends on | `SPEC-20260916-firecrawl-mcp-proxy` (Done) — secret store, Settings UI, upstream-client pattern |

## 1. User Story

**As a** KnowledgeHub platform user
**I want** the Tavily key (already stored via Settings) to power all official Tavily tools through the internal MCP server — testable in the Playground with ready-made examples
**So that** I can consume web search, extraction, mapping, crawling and research capabilities alongside Firecrawl and DeepWiki.

**Problem context:** `SPEC-20260916-firecrawl-mcp-proxy` delivered the bypass architecture (lazy `McpClient` upstream + `IToolProvider` catalog merge), the encrypted `IntegrationSecret` store and the `/settings` UI — the `tavily` provider slug already exists in `IntegrationProviders.All` and its key is already persisted in the DB. What's missing is the actual proxy: options, upstream client, tool provider, registration and validation.

## 2. Scope

**In scope:**
- `TavilyOptions` (Server, `Mcp/Upstream/`) — `Enabled`, `Endpoint`, `ApiKey`, `TimeoutSeconds`, `ToolsCacheSeconds`.
- `TavilyUpstreamClient` — same shape as `FirecrawlUpstreamClient`: lazy singleton `McpClient`, `HttpClientTransport` `AutoDetect`, **Bearer auth**, one reconnect, per-call timeout, `ResetAsync`, key resolution store → env (`Tavily__ApiKey`).
- `TavilyToolsProvider : IToolProvider` — **hybrid registration**, same as Firecrawl:
  - Static core always listed when `Enabled`, with `examples` (Playground convention): `tavily_search`, `tavily_extract`, `tavily_map`, `tavily_crawl`, `tavily_research`.
  - Dynamic merge via upstream `tools/list` when an effective key exists — names and `inputSchema` verbatim; dynamic wins on collisions.
  - Upstream annotations → `CatalogTool.ReadOnly` (mutating fallback: `tavily_crawl`, `tavily_research` are billable/long-running → `ReadOnly=false` when upstream omits the hint).
  - No key → friendly `isError` pointing at `/settings`.
- `ConfigurationValidator.ValidateTavily` + `appsettings.json` + `docker-compose.yml` env parity (`Tavily__*`).
- `SettingsEndpoints`: add `tavily` to `ResetProviderAsync` (reset `TavilyUpstreamClient` + provider cache); update the placeholder note text.
- `McpContractTests.PinnedSchemas` — pin the 5 static schemas.

**Out of scope:**
- OAuth flow (Tavily remote supports it — unnecessary; we already hold the key).
- `DEFAULT_PARAMETERS` header support (optional upstream feature; can be added later if needed).
- Tavily local-file features — none exist upstream; passthrough only.
- Keyless mode upstream behavior — keyless server-side mode exists, but our provider surfaces the friendly no-key error instead of relying on it.

## 3. Technical Context

### 3.1 Facts verified upstream

- **Repo:** `tavily-ai/tavily-mcp` (TS, `src/index.ts`) — tools defined server-side: `tavily_search`, `tavily_extract`, `tavily_map`, `tavily_crawl`, `tavily_research` (exact names confirmed in source).
- **Remote endpoint:** `https://mcp.tavily.com/mcp` — Streamable HTTP.
- **Auth:** `?tavilyApiKey=<key>` query param **or** `Authorization: Bearer <key>` — both officially supported.
- **Decision:** Bearer header, not the query param — keys in URLs leak into access logs, browser history and upstream proxies. Same transport shape as Firecrawl/DeepWiki clients.
- `DEFAULT_PARAMETERS` header (JSON object) can set request defaults upstream — out of scope.

### 3.2 Existing platform pieces (all from the Firecrawl SPEC)

| Piece | Path |
| --- | --- |
| Provider registry | `src/KnowledgeHub.Server/Settings/IIntegrationSecretStore.cs` (`IntegrationProviders.Tavily` already registered) |
| Secret store | `src/KnowledgeHub.Server/Settings/IntegrationSecretStore.cs` |
| Settings API | `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs` — `tvly-` prefix validation + `tvly-••••last4` mask already implemented |
| Settings UI | `src/KnowledgeHub.Client/Pages/Settings.razor` — Tavily card renders automatically |
| Client seam pattern | `FirecrawlUpstreamClient` / `DeepWikiUpstreamClient` (`TransportFactory`, `CreateTransportOptions`, `ResetAsync`) |
| Provider seam pattern | `FirecrawlToolsProvider` (`DynamicToolsSource`, `MutatingPrefixes`, `InvalidateToolsCache`) |
| DI | `KnowledgeHubServiceCollectionExtensions` (options + client + provider + `IToolProvider` forward) |

### 3.3 Key already stored

`tvly-dev-…XUsO` is persisted in `IntegrationSecrets` (`provider='tavily'`) in the production DB — encrypted with the same Data Protection key ring. `GetAsync`/`GetInfoAsync`/`SetAsync`/`RemoveAsync` already work for this provider; the Settings UI card is live. Implementation therefore needs **zero changes** to the store or the DTOs — only the proxy machinery + the reset hook.

## 4. Requirements

### Functional

- **RF-001** `TavilyOptions` bound from `Tavily:` config section with documented defaults.
- **RF-002** `TavilyUpstreamClient` mirrors `FirecrawlUpstreamClient` (lazy `McpClient`, AutoDetect, Bearer, reconnect-once, `ListToolsAsync`, `ResetAsync`, `CreateTransportOptions`/`TransportFactory` seams).
- **RF-003** `TavilyToolsProvider` hybrid: 5 static tools with `examples` + dynamic merge + cache + last-known-good fallback.
- **RF-004** Exact upstream names — `tavily_search`, `tavily_extract`, `tavily_map`, `tavily_crawl`, `tavily_research` + whatever the remote `tools/list` returns. No aliases, no renames.
- **RF-005** No key → listed tools return friendly `isError` (`"Tavily API key not configured — open Settings (/settings) or set Tavily__ApiKey."`).
- **RF-006** `PUT`/`DELETE /api/settings/integrations/tavily` resets the Tavily client + provider cache (add `case` to `ResetProviderAsync`).
- **RF-007** `ValidateTavily` in `ConfigurationValidator` (endpoint URI, positive `TimeoutSeconds`/`ToolsCacheSeconds`; skipped when `Enabled=false`).

### Non-functional

- **RNF-001** Key never logged/returned/serialized; Bearer only inside `HttpClientTransportOptions`.
- **RNF-002** Zero regression: Firecrawl, DeepWiki and local tools untouched.
- **RNF-003** Structured logs on upstream failure — no key material.

## 5. API Contract

No new endpoints. `GET /api/settings/integrations` already returns the `tavily` provider; `PUT`/`DELETE` already accept it. Internal surface changes only:

```csharp
public sealed class TavilyOptions
{
    public const string SectionName = "Tavily";
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://mcp.tavily.com/mcp";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 120;   // research/crawl são longos
    public int ToolsCacheSeconds { get; set; } = 300;
}
```

`appsettings.json` gains a `Tavily` block; `docker-compose.yml` gains `Tavily__Enabled/Endpoint/ApiKey/TimeoutSeconds` passthrough.

## 6. Acceptance Criteria

- [ ] **Given** the stored `tvly-…` key, **when** `tools/list` runs, **then** all upstream Tavily tools register under exact original names (dynamic merge).
- [ ] **Given** no key, **when** `tavily_search` is called, **then** `isError` points to Settings.
- [ ] **Given** a real call, **when** `tavily_search` executes with the stored key, **then** live results return through the internal MCP.
- [ ] **Given** `Tavily:Enabled=false`, **then** no `tavily_*` tool is listed.
- [ ] **Given** upstream `tools/list` fails with a key configured, **then** the static core is still served.
- [ ] **Given** the Playground, **when** `tavily_search` is selected, **then** "Preencher exemplo" produces a runnable payload; `tavily_crawl` keeps the write-confirm gate.
- [ ] **Given** the pinned contract test, **then** it contains the 5 static schemas and the suite stays green.

## 7. Task Plan

| # | Task | Validation |
|---|------|-----------|
| T1 | `TavilyOptions` + appsettings/compose/env + `ValidateTavily` | `ConfigurationValidatorTests` |
| T2 | `TavilyUpstreamClient` (copy of Firecrawl client shape) | `UpstreamCredentialTests` |
| T3 | `TavilyToolsProvider` hybrid + static schemas/examples + mutating prefixes | `TavilyToolsProviderTests` |
| T4 | DI wiring + `ResetProviderAsync` tavily case + Settings note text | `SettingsApiTests` (existing PUT/DELETE cover it) |
| T5 | `McpContractTests` pin 5 schemas | integration suite |
| T6 | Live evidence: tools/list + one real `tavily_search` | section 10 |
| T7 | SPEC → results + `Done` | — |

## 8. Organization Guardrails

- Branch `feature/Devin-20260916-tavily-mcp-proxy` — never `main`/`master`/`develop`.
- `.github/workflows/` untouched.
- No aliases/renames; no secret exposure; no new dependencies.
- Reuse `IIntegrationSecretStore` — no parallel secret mechanism.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs evidenced.
- [ ] `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green.
- [ ] tools/list capture showing `tavily_*`; one live `tavily_search` execution.
- [ ] SPEC updated with implementation result → `Status = Done` → PR opened.

## 10. Implementation Result

Implementado na branch `feature/Devin-20260916-tavily-mcp-proxy`:

- `TavilyOptions` / `TavilyUpstreamClient` / `TavilyToolsProvider` em `src/KnowledgeHub.Server/Mcp/Upstream/` — mesma forma do Firecrawl (lazy `McpClient`, AutoDetect, Bearer, reconnect-once, cache de `tools/list` com last-known-good).
- `ValidateTavily` no `ConfigurationValidator`; bloco `Tavily` em `appsettings.json`; env `Tavily__*` no `docker-compose.yml`.
- `ResetProviderAsync` com `case tavily` (reset client + invalida cache); nota do card atualizada — a key `tvly-…XUsO` já estava persistida e passou a alimentar o proxy sem nenhuma mudança no store.
- `McpContractTests`: 5 schemas estáticos pinados; `tavily_crawl`/`tavily_research` no conjunto de write-tools.
- Testes novos: `TavilyToolsProviderTests` (9) + casos Tavily em `UpstreamCredentialTests` (3) e `ConfigurationValidatorTests` (4).

**Gates:** `dotnet build` ✓ · unit **192/192** ✓ · integration **140/140** ✓ · `dotnet format --verify-no-changes` ✓

**Evidência live (instância local, key persistida via `PUT /api/settings/integrations/tavily` → `tvly-••••XUsO`):**
- `tools/list` interno expôs `tavily_search`, `tavily_extract`, `tavily_map`, `tavily_crawl`, `tavily_research` (18 tools no total).
- `tools/call tavily_search` alcançou o upstream com a key: resposta `isError:false` contendo payload upstream `status:432` ("exceeds your plan's set usage limit") — a dev key está no teto do plano; o passthrough repassou o erro intacto (confirmando auth Bearer aceita — 401 teria indicado rejeição).

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Ticket/issue number.
- `tavily_research` may be plan-gated upstream — the proxy passes the upstream `isError` through unchanged (observado com `tavily_search` + status 432).
- `DEFAULT_PARAMETERS` header support deferred — adds a config knob users haven't asked for.
