# SPEC-20260922-context7-mcp-proxy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `context7-mcp-proxy` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` (official MCP C# SDK client) + Blazor WASM (BootstrapBlazor) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260922-context7-mcp-proxy` |
| Ticket | [#146](https://github.com/afonsoft/LangGraph-UI/issues/146) |
| Status | `Done` |
| Depends on | `SPEC-20260916-tavily-mcp-proxy` (Done) — hybrid provider pattern, `ResetProviderAsync`, contract pins |

## 1. User Story

**As a** Knowledge MCP Hub platform user
**I want** the Context7 key (stored via Settings or `Context7__ApiKey` env) to power the official Context7 tools through the internal MCP server — testable in the Playground with ready-made examples
**So that** agent sessions can resolve library IDs and pull up-to-date, version-specific documentation (`resolve-library-id`, `query-docs`) alongside Firecrawl, Tavily and DeepWiki.

**Problem context:** the bypass architecture is proven — lazy `McpClient` upstream + `IToolProvider` catalog merge + encrypted `IntegrationSecret` store + `/settings` UI. Context7 is the fourth upstream proxy; unlike Tavily, its provider slug is **not** yet registered in `IntegrationProviders.All`, so this feature also wires the secret-store/Settings surface.

## 2. Scope

**In scope:**
- `Context7Options` (Server, `Mcp/Upstream/`) — `Enabled`, `Endpoint`, `ApiKey`, `TimeoutSeconds`, `ToolsCacheSeconds`.
- `Context7UpstreamClient` — same shape as `TavilyUpstreamClient`: lazy singleton `McpClient`, `HttpClientTransport` `AutoDetect`, **Bearer auth**, one reconnect, per-call timeout, `ResetAsync`, key resolution store → env (`Context7__ApiKey`).
- `Context7ToolsProvider : IToolProvider` — **hybrid registration**, same as Tavily:
  - Static core always listed when `Enabled`, with `examples` (Playground convention): `resolve-library-id`, `query-docs` — **exact upstream names, verbatim, including hyphens**.
  - Dynamic merge via upstream `tools/list` when an effective key exists — names and `inputSchema` verbatim; dynamic wins on collisions.
  - Upstream annotations → `CatalogTool.ReadOnly`; both upstream tools are `readOnlyHint:true` — fallback default `ReadOnly=true` (no mutating prefixes; Context7 exposes no write tools).
  - No key → friendly `isError` pointing at `/settings`.
- `IntegrationProviders.Context7 = "context7"` added to `All` — `GET`/`PUT`/`DELETE /api/settings/integrations/context7` and the per-API-key integration endpoints (`/api/api-keys/{id}/settings/integrations/context7`) pick it up automatically.
- `SettingsEndpoints`: `ctx7sk-` prefix validation on PUT, `ctx7sk-••••last4` mask, `DisplayName = "Context7"`, note text, `ConfigSection → "Context7"`, `case context7` in `ResetProviderAsync` (reset client + provider cache).
- `Settings.razor`: `ctx7sk-…` placeholder for the provider card.
- Per-API-key parity: `SettingsToolsProvider` (`set_api_key_settings` enum + handler branch) and `ApiKeyChatSettingsService` (`DescribeAsync` `IntegrationKeys` + `RemoveAsync` cleanup) gain `context7`.
- `ConfigurationValidator.ValidateContext7` + `appsettings.json` + `docker-compose.yml` + `install.sh` env parity (`Context7__*` / `CONTEXT7_*`) + `.env.example` commented block.
- `McpContractTests.PinnedSchemas` — pin the 2 static schemas.
- Local `.env` (gitignored): `CONTEXT7_APIKEY` already configured — done during spec phase.

**Out of scope:**
- OAuth flow (`/mcp/oauth`) — unnecessary; we already hold the Bearer key.
- Keyless upstream passthrough — Context7 accepts anonymous calls at a lower rate limit, but the proxy keeps the established friendly-error pattern (decided in spec round).
- `CONTEXT7_API_KEY` header variant — Bearer only; both are officially supported and Bearer matches the existing clients.
- Renaming/prefixing tools (e.g. `context7_*`) — verbatim names per proxy convention; slug-prefixing is available via the generic `McpProxy` source type if ever needed.
- Resources/prompts upstream (Context7 exposes none today).
- Wiring per-API-key integration overrides into upstream `ResolveApiKeyAsync` — `GetIntegrationSecretAsync` exists but is unused by firecrawl/tavily clients too (pre-existing gap, not introduced here).

## 3. Technical Context

### 3.1 Facts verified upstream (live `tools/list` + public docs + source)

- **Repo:** `upstash/context7` (MCP shim MIT; backend proprietary, Upstash).
- **Remote endpoint:** `https://mcp.context7.com/mcp` — Streamable HTTP (SDK `AutoDetect` covers SSE fallback).
- **Auth:** `Authorization: Bearer ctx7sk-*` **or** `CONTEXT7_API_KEY` header — both officially supported; OAuth via `/mcp/oauth`; anonymous allowed with lower rate limits.
- **Tools** (verbatim from the live server, `readOnlyHint:true`, `idempotentHint:true`):

| Tool | Required params | Purpose |
| --- | --- | --- |
| `resolve-library-id` | `query`, `libraryName` | Resolves a package/product name to a Context7 library ID (`/org/project`[/version]); returns candidates ranked by name match, reputation, snippet count, benchmark score. Must be called before `query-docs` unless the caller already has a `/org/project` ID. |
| `query-docs` | `libraryId`, `query` | Retrieves up-to-date documentation and code examples for the resolved library ID; query scoped to a single concept. |

- **Key prefix:** `ctx7sk-` (enforced upstream — 401 message: "API keys should start with 'ctx7sk'").
- **Naming note:** hyphenated names are valid MCP tool names (`[a-zA-Z0-9_-]{1,64}`). They are generic — if a `McpProxy` source ever exposes the same names, `DynamicToolCatalog` merges last-registered-provider-wins; accepted risk, documented.
- Older Context7 versions exposed `get-library-docs`; the current server exposes `query-docs`. The dynamic merge absorbs future upstream renames automatically — only the static core is pinned.

### 3.2 Existing platform pieces

| Piece | Path |
| --- | --- |
| Provider registry | `src/KnowledgeHub.Server/Settings/IIntegrationSecretStore.cs` (`IntegrationProviders.All` — **context7 not yet registered**) |
| Secret store | `src/KnowledgeHub.Server/Settings/IntegrationSecretStore.cs` |
| Settings API | `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs` — prefix validation, `MaskHint`, `DescribeAsync`, `ResetProviderAsync` |
| Per-key settings | `src/KnowledgeHub.Server/Api/ApiKeySettingsEndpoints.cs` (auto via `All`), `Settings/ApiKeyChatSettingsService.cs` (hardcoded lists), `Mcp/ToolProviders/SettingsToolsProvider.cs` (enum + branch) |
| Settings UI | `src/KnowledgeHub.Client/Pages/Settings.razor` — card renders automatically; placeholder needs `ctx7sk-…` |
| Client seam pattern | `TavilyUpstreamClient` / `FirecrawlUpstreamClient` (`TransportFactory`, `CreateTransportOptions`, `ResetAsync`, `_connectedKey`) |
| Provider seam pattern | `TavilyToolsProvider` (`DynamicToolsSource`, `InvalidateToolsCache`, `DynamicToolExamples`) |
| DI | `KnowledgeHubServiceCollectionExtensions` (options + client + provider + `IToolProvider` forward) |
| Config validation | `Configuration/ConfigurationValidator.cs` (`ValidateTavily` template) |
| Contract tests | `tests/KnowledgeHub.Tests.Integration/McpContractTests.cs` (`PinnedSchemas`) |

### 3.3 Key handling

The dev key `ctx7sk-…a88c` is configured in the local `.env` (gitignored) as `CONTEXT7_APIKEY`. Production/persistent storage goes through `PUT /api/settings/integrations/context7` → encrypted `IntegrationSecrets` row — same store → env resolution order as the other providers. The key is **never** committed to `appsettings.json` or git.

## 4. Requirements

### Functional

- **RF-001** `Context7Options` bound from `Context7:` config section with documented defaults (`Endpoint=https://mcp.context7.com/mcp`, `Enabled=true`, `TimeoutSeconds=60`, `ToolsCacheSeconds=300`).
- **RF-002** `Context7UpstreamClient` mirrors `TavilyUpstreamClient` (lazy `McpClient`, AutoDetect, Bearer, reconnect-once, `ListToolsAsync`, `ResetAsync`, `CreateTransportOptions`/`TransportFactory` seams; `ResolveApiKeyAsync` → `IntegrationProviders.Context7` store → `Context7Options.ApiKey`).
- **RF-003** `Context7ToolsProvider` hybrid: 2 static tools with `examples` + dynamic merge + cache + last-known-good fallback.
- **RF-004** Exact upstream names — `resolve-library-id`, `query-docs` + whatever the remote `tools/list` returns. No aliases, no renames.
- **RF-005** No key → listed tools return friendly `isError` (`"Context7 API key not configured — open Settings (/settings) or set Context7__ApiKey."`).
- **RF-006** `PUT`/`DELETE /api/settings/integrations/context7` works (slug in `All`, `ctx7sk-` prefix check, masked hint) and resets the Context7 client + provider cache (`case` in `ResetProviderAsync`).
- **RF-007** `ValidateContext7` in `ConfigurationValidator` (endpoint URI, positive `TimeoutSeconds`/`ToolsCacheSeconds`; skipped when `Enabled=false`).
- **RF-008** Per-API-key parity: `set_api_key_settings` accepts `provider:"context7"`; `ApiKeyChatSettingsService.DescribeAsync` reports the `context7` integration key and `RemoveAsync` cleans it up.

### Non-functional

- **RNF-001** Key never logged/returned/serialized; Bearer only inside `HttpClientTransportOptions`.
- **RNF-002** Zero regression: Firecrawl, Tavily, DeepWiki, McpProxy sources and local tools untouched.
- **RNF-003** Structured logs on upstream failure — no key material.
- **RNF-004** No new dependencies; no `.github/workflows/` changes.

## 5. API Contract

No new endpoints. `GET /api/settings/integrations` gains a `context7` item; `PUT`/`DELETE` accept the slug (with `ctx7sk-` validation on PUT). Per-key `PUT`/`DELETE /api/api-keys/{id}/settings/integrations/context7` accept it via `IntegrationProviders.All`. `set_api_key_settings` `provider` enum gains `"context7"`. Internal surface:

```csharp
public sealed class Context7Options
{
    public const string SectionName = "Context7";
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://mcp.context7.com/mcp";
    public string ApiKey { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 60;   // docs lookups are short
    public int ToolsCacheSeconds { get; set; } = 300;
}
```

`appsettings.json` gains a `Context7` block; `docker-compose.yml` and `install.sh --docker` gain `Context7__Enabled/Endpoint/ApiKey/TimeoutSeconds` passthrough (`CONTEXT7_*` env); `.env.example` gains a commented `CONTEXT7_APIKEY=` block.

## 6. Acceptance Criteria

- [ ] **Given** the stored/env `ctx7sk-…` key, **when** `tools/list` runs, **then** `resolve-library-id` and `query-docs` register under exact upstream names (dynamic merge).
- [ ] **Given** no key, **when** either tool is called, **then** `isError` points to Settings.
- [ ] **Given** a real call, **when** `resolve-library-id` executes with the configured key, **then** live library candidates return through the internal MCP.
- [ ] **Given** `Context7:Enabled=false`, **then** no Context7 tool is listed.
- [ ] **Given** upstream `tools/list` fails with a key configured, **then** the static core is still served.
- [ ] **Given** the Playground, **when** `resolve-library-id`/`query-docs` are selected, **then** "Preencher exemplo" produces a runnable payload.
- [ ] **Given** `PUT /api/settings/integrations/context7` with a non-`ctx7sk-` key, **then** `400` with a prefix error.
- [ ] **Given** the pinned contract test, **then** it contains the 2 static schemas and the suite stays green.

## 7. Task Plan

| # | Task | Validation |
|---|------|-----------|
| T1 | `Context7Options` + appsettings/compose/install.sh/.env.example + `ValidateContext7` | `ConfigurationValidatorTests` |
| T2 | `Context7UpstreamClient` (copy of Tavily client shape) | `UpstreamCredentialTests` |
| T3 | `Context7ToolsProvider` hybrid + static schemas/examples | `Context7ToolsProviderTests` |
| T4 | `IntegrationProviders.Context7` + `SettingsEndpoints` (prefix, mask, note, reset case) + `Settings.razor` placeholder | `SettingsApiTests` |
| T5 | Per-key parity: `SettingsToolsProvider` enum/branch + `ApiKeyChatSettingsService` lists | `SettingsApiTests` / unit |
| T6 | `McpContractTests` pin 2 schemas | integration suite |
| T7 | Live evidence: tools/list + one real `resolve-library-id` | section 10 |
| T8 | SPEC → results + `Done` | — |

## 8. Organization Guardrails

- Branch `feature/Devin-20260922-context7-mcp-proxy` — never `main`/`master`/`develop`.
- `.github/workflows/` untouched.
- No aliases/renames; no secret exposure; no new dependencies.
- Reuse `IIntegrationSecretStore` — no parallel secret mechanism.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs evidenced.
- [ ] `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green.
- [ ] tools/list capture showing `resolve-library-id`/`query-docs`; one live `resolve-library-id` execution.
- [ ] SPEC updated with implementation result → `Status = Done` → PR opened.

## 10. Implementation Result

Implementado na branch `feature/Devin-20260922-context7-mcp-proxy`:

- `Context7Options` / `Context7UpstreamClient` / `Context7ToolsProvider` em `src/KnowledgeHub.Server/Mcp/Upstream/` — mesma forma do Tavily (lazy `McpClient`, AutoDetect, Bearer, reconnect-once, cache de `tools/list` com last-known-good).
- `IntegrationProviders.Context7 = "context7"` em `All`; `SettingsEndpoints` com validação `ctx7sk-`, mask `ctx7sk-••••last4`, `DisplayName`/`Note`/`ConfigSection`, `case context7` no `ResetProviderAsync`; placeholder `ctx7sk-…` em `Settings.razor`.
- Per-key parity: enum `set_api_key_settings` + branch no `SettingsToolsProvider`; `ApiKeyChatSettingsService` (`IntegrationKeys` + `RemoveAsync`); `ApiKeySettingsEndpoints` cobre o slug via `All`.
- `ValidateContext7` no `ConfigurationValidator`; bloco `Context7` em `appsettings.json`; env `Context7__*`/`CONTEXT7_*` em `docker-compose.yml`, `install.sh`, `.env.example`; README (MCP tools + env table).
- `McpContractTests`: 2 schemas estáticos pinados (`resolve-library-id`, `query-docs`) + enum `set_api_key_settings` atualizado.
- Testes novos: `Context7ToolsProviderTests` (11) + casos Context7 em `UpstreamCredentialTests` (3) e `ConfigurationValidatorTests` (4).

**Gates:** `dotnet build` ✓ · unit **358/358** ✓ · integration **164/164** ✓ · `dotnet format --verify-no-changes` ✓

**Evidência live (instância local :5005, key via `Context7__ApiKey` → `ctx7sk-••••a88c`):**
- `tools/list` interno expôs `resolve-library-id` e `query-docs` (21 tools no total).
- `tools/call resolve-library-id {libraryName:"Next.js", query:"app router routing"}` → `isError:false` com candidatos reais (`/vercel/next.js`, 4522 snippets, Benchmark 86.02).
- `tools/call query-docs {libraryId:"/vercel/next.js", query:"middleware authentication"}` → `isError:false` com doc oficial de middleware/JWT.
- `GET /api/settings/integrations` → `context7` com `hasKey:true`, `source:env`, hint mascarado.
- `PUT /api/settings/integrations/context7` com `bad-key` → `400` "Context7 API keys start with 'ctx7sk-'".

## Open Questions / Pending Ambiguity

- Ticket: [#146](https://github.com/afonsoft/LangGraph-UI/issues/146).
- Context7 rate limits are enforced upstream per key/IP — the proxy passes upstream `isError`/429 through unchanged (same posture as Tavily 432 handling).
- Per-API-key integration overrides (`apikey-context7-{id}`) are stored and listed for parity but not yet consumed by upstream `ResolveApiKeyAsync` — same pre-existing gap as firecrawl/tavily.
</content>
