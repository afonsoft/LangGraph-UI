# SPEC-20260914-playground-tools

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `playground-tools` |
| Type | `Feature` |
| Stack | `.NET 10`, `Blazor WASM`, `MCP` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-playground-tools` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** platform user
**I want** the Playground page to list every MCP tool — including dynamic `query_{slug}` per-source tools and DeepWiki tools — with a generated input form and a call/result viewer
**So that** I can exercise the exact surface that MCP clients (Cursor, Claude Desktop) consume, without external tooling.

**Problem context:** Playground today only wraps `/api/search` (equivalent to `search_knowledge`). There is no way to test `ask_knowledge`, `write_knowledge`, `read_document`, `write_note`, `query_*`, or `ask_question`/`read_wiki_*` (DeepWiki) from the UI.

## 2. Scope

**In scope:**
- REST endpoints exposing the tool catalog and tool invocation, backed by the **same** `IDynamicToolCatalog` + `CatalogTool.Handler` used by MCP `tools/list`/`tools/call` (single source of truth).
- Small refactor: replace `RequestContext<CallToolRequestParams>` in `CatalogTool.Handler` with a neutral call context so REST can invoke handlers without fabricating an MCP session.
- Playground page redesign: tool picker + schema-driven form + result viewer (formatted text / raw JSON), latency + error surfacing.
- `ToolsApiClient` in the WASM client.
- Integration tests for the new endpoints; existing MCP contract tests must stay green unchanged.

**Out of scope:**
- MCP protocol changes — `/mcp` JSON-RPC surface untouched.
- AuthN/AuthZ on the new endpoints (same trust level as existing `/api/*`).
- Persisting playground history.
- A "playground" for the SignalR monitor feed.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/Mcp/` — `CatalogTool`, `ToolArgs`, the four providers (`KnowledgeToolsProvider`, `SourceQueryToolsProvider`, `ObsidianToolsProvider`, `DeepWikiToolsProvider`), `IDynamicToolCatalog`.
- `src/KnowledgeHub.Server/Api/` — new `ToolsEndpoints.cs`; registered in `Program.cs` next to `MapSourcesApi`/`MapSearchApi`.
- `src/KnowledgeHub.Client/` — `Services/ToolsApiClient.cs`, `Pages/Playground.razor`.
- `src/KnowledgeHub.Shared/Contracts/` — DTOs if needed (tool descriptor + result).
- `tests/KnowledgeHub.Tests.Integration/` — new endpoint tests.

**Files to read:**
- `src/KnowledgeHub.Server/Mcp/CatalogTool.cs`, `ToolArgs.cs`, `DynamicToolCatalog.cs`
- `src/KnowledgeHub.Server/Mcp/ToolProviders/*.cs`, `Mcp/Upstream/DeepWikiToolsProvider.cs`
- `src/KnowledgeHub.Server/Api/SearchEndpoints.cs` — Minimal API conventions
- `src/KnowledgeHub.Client/Pages/Playground.razor`, `Services/SearchApiClient.cs`
- `KnowledgeHubServiceCollectionExtensions.cs` — CallToolHandler adapter point

## 4. Requirements

### RF-001: Neutral tool-call context
- **Description:** `CatalogTool.Handler` signature becomes `Func<ToolCallContext, CancellationToken, ValueTask<CallToolResult>>` where `ToolCallContext` carries `IServiceProvider Services` and `IReadOnlyDictionary<string, JsonElement>? Arguments` (and optionally `SessionId`). `ToolArgs.*` helpers take `ToolCallContext` (or the arguments dictionary). The MCP `CallToolHandler` in `KnowledgeHubServiceCollectionExtensions` adapts: `new ToolCallContext(ctx.Services!, ctx.Params?.Arguments)` — MCP behavior unchanged.

### RF-002: `GET /api/tools`
- **Description:** Returns the live catalog: `[{ name, description, inputSchema (JsonObject), readOnly }]`. Reflects `query_{slug}` tools for active sources and DeepWiki tools when `DeepWiki:Enabled=true`. Response shape documented for the client.

### RF-003: `POST /api/tools/{name}` (call)
- **Description:** Body = JSON object of tool arguments (may be empty `{}`). Resolves the tool by name from the catalog (404 `unknown tool` otherwise), invokes `Handler` with a `ToolCallContext` scoped to the request's `IServiceProvider`, returns the serialized `CallToolResult` (`content[]`, `isError`, `structuredContent` when present). Handler `McpProtocolException`/`ToolArgs` errors surface as `isError:true` content — not HTTP 500 — matching MCP semantics.

### RF-004: Playground — tool catalog
- **Description:** Left panel lists all tools from `GET /api/tools` (name + readOnly badge), refresh button refetches (catalog changes with sources). Selecting a tool shows its description and a form generated from `inputSchema`: `string` → text input, `integer` → number, `boolean` → switch, `array`/`anyOf`/`object` → JSON textarea fallback. `required[]` fields marked; client-side required validation before submit. `search_knowledge` selected by default (preserves the current quick-search use case).

### RF-005: Playground — result viewer
- **Description:** On execute: disable button, show spinner + elapsed ms. Result shows each `content[]` item — `text` rendered formatted (`white-space: pre-wrap`), other types as JSON; `isError` styles the card red; toggle for raw `CallToolResult` JSON. DeepWiki long calls: HTTP client timeout ≥ 120s and a hint that upstream calls can be slow.

### RF-006: Tests
- **Description:** Integration tests: `GET /api/tools` returns the pinned core set + `query_{slug}` after source registration (reuse existing fixture pattern); `POST /api/tools/search_knowledge` executes and returns `isError:false` text content; unknown tool → 404; missing required arg → `isError:true`. MCP contract tests (`McpContractTests`) pass untouched.

## 5. API Contract

```text
GET /api/tools
→ 200 { "tools": [ { "name": "ask_question", "description": "...",
                     "inputSchema": { ...json schema... }, "readOnly": true } ] }

POST /api/tools/{name}
body: { "repoName": "owner/repo", "question": "..." }   (tool arguments)
→ 200 { "content": [ { "type": "text", "text": "..." } ], "isError": false, ... }
→ 404 { "error": "unknown tool 'x'" }
```

## 6. Acceptance Criteria

- [ ] **Given** no sources registered **when** `GET /api/tools` **then** it returns the always-on tools (`search_knowledge`, `ask_knowledge`, `write_knowledge`) plus DeepWiki tools iff enabled.
- [ ] **Given** an active ObsidianVault source `meuvault` **when** `GET /api/tools` **then** it also lists `query_meuvault`, `read_document`, `write_note` — matching `tools/list` exactly.
- [ ] **Given** the Playground **when** a tool is selected **then** its form renders from `inputSchema` and calling it shows the same result content an MCP client would get.
- [ ] **Given** `ask_question` (DeepWiki) **when** invoked from the Playground **then** the proxied answer/error renders — proving end-to-end UI → catalog → upstream.
- [ ] **Given** `dotnet test` **then** new endpoint tests + all existing tests pass.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Unknown tool | `POST /api/tools/nope` | 404, no handler executed |
| Missing required arg | `POST /api/tools/search_knowledge {}` | `isError:true` naming `query` |
| DeepWiki disabled | `DeepWiki:Enabled=false` | tools absent from list; `POST` to one → 404 |
| Tool writes (write_*) | normal call | executes for real — readOnly badge warns the user |

## 7. Task Plan

- [ ] **T1 — Context refactor:** `ToolCallContext` + `CatalogTool.Handler` signature + `ToolArgs` + providers + MCP adapter. All existing tests must pass unchanged (proves no behavior drift).
- [ ] **T2 — REST endpoints:** `ToolsEndpoints.cs` (`GET /api/tools`, `POST /api/tools/{name}`), registered in `Program.cs`.
- [ ] **T3 — Client:** `ToolsApiClient` + Playground rebuild (picker, schema form, result viewer).
- [ ] **T4 — Tests + verify:** integration tests, full suite, docker deploy + browser smoke incl. a DeepWiki call.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-playground-tools`; never commit to main.
- **Security:** write tools execute for real — surface `readOnly`/`!readOnly` clearly in the UI; no new auth surface beyond existing `/api/*` posture.
- **Contracts:** `McpContractTests` is the canary — it must pass unchanged; REST mirrors MCP semantics, not vice-versa.

## 9. Definition of Done

- [ ] Playground lists and invokes the full live tool catalog including `query_{slug}` and DeepWiki tools.
- [ ] `GET`/`POST /api/tools` integration-tested.
- [ ] Full suite green; deployed via docker compose; verified at https://rag.afonsoft.dev/playground.

## Open Questions / Pending Ambiguity

- Playground layout: tool-list sidebar vs. dropdown picker — implementer's choice (BootstrapBlazor `Select`/`Tab` patterns already in the codebase).
- Whether to keep the old free-form search box as a shortcut — resolved by defaulting the picker to `search_knowledge`.
