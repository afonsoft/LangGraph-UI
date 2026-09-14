# SPEC-20260913-deepwiki-mcp-proxy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `deepwiki-mcp-proxy` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` (official MCP C# SDK client) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `#8` |
| Status | `Done` |

## 1. User Story

**As a** connected MCP agent
**I want** the KnowledgeHub MCP server to re-expose DeepWiki tools (`ask_question`, `read_wiki_structure`, `read_wiki_contents`) as proxy tools
**So that** a single MCP endpoint gives me local knowledge-hub search AND AI-powered GitHub repository documentation.

**Problem context:** Agents today must register `https://mcp.deepwiki.com/mcp` as a second MCP server. A built-in proxy turns KnowledgeHub into a single aggregation point. **Decision (revised after SDK analysis):** the upstream client uses the official SDK `McpClient` + `HttpClientTransport` (`AutoDetect` → Streamable HTTP, SSE fallback) instead of a hand-rolled SSE parser — the SDK manages handshake, `Mcp-Session-Id`, framing and reconnects.

## 2. Scope

**In scope:**
- `DeepWikiUpstreamClient` (Server) — singleton owning an `McpClient`:
  - `McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions { Endpoint, TransportMode = AutoDetect, AdditionalHeaders = { Authorization: Bearer <apiKey>? } }))`, lazily connected on first call.
  - `AutoDetect` tries Streamable HTTP then falls back to SSE — covers the upstream deprecation transparently.
  - Lazy reconnect: `HttpRequestException`/session errors → dispose + reconnect once, then surface error.
  - `TimeoutSeconds` per call (default 60 — `ask_question` is LLM-backed).
- Three downstream proxy tools contributed to `IDynamicToolCatalog` (SPEC-04) when `DeepWiki:Enabled` — **names are byte-identical to upstream** (transparent bypass, user decision):
  - `ask_question(repoName: string | string[], question: string)`
  - `read_wiki_structure(repoName: string)`
  - `read_wiki_contents(repoName: string)`
  - `inputSchema` mirrors upstream exactly (`anyOf` for `repoName` on ask); `readOnlyHint: true`.
  - Name-collision guard: these 3 names are reserved; local tools win on collision (never happens with current local set: `search_knowledge_hub`, `query_*`, `*_obsidian_*`).
  - Dispatch: `tools/call` on the proxy names → `client.CallToolAsync(name, args)` → pass through `CallToolResult` content blocks.
- Config `DeepWiki`: `Enabled` (default `true`), `Endpoint` (default `https://mcp.deepwiki.com/mcp`), `ApiKey` (default empty → private-mode `devin_*` not exposed), `TimeoutSeconds` (default `60`).
- Validation before upstream call: `repoName` matches `owner/repo` per element; array ≤ 10; `question` non-empty → `McpErrorCode.InvalidParams` / `isError`.
- Activity feed: proxy calls recorded in `IMcpActivityFeed` like any `tools/call` (via SPEC-01 filter — free).

**Verified upstream facts (2026-09-13):**
- `GET https://mcp.deepwiki.com/sse` → `410 Gone`; `POST https://mcp.deepwiki.com/mcp` `initialize` → `200`, `serverInfo: DeepWiki 2.14.3`, SSE-framed `event: message` response.
- Public tools: `read_wiki_structure`, `read_wiki_contents`, `ask_question`. Private mode (`devin_*`, `generate_wiki`, `list_available_repos`) requires API key — not re-exposed in this SPEC (backlog #4).

**Out of scope:**
- Upstream `tools/list` passthrough / `devin_*` re-exposure (backlog — needs ApiKey + dynamic upstream discovery).
- `McpProxy` SourceType — catalog-driven upstream registration via `/api/sources` (backlog #3).
- Response caching, upstream resources/prompts proxying.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Mcp/Upstream/` + contribution to `IDynamicToolCatalog`. Package `ModelContextProtocol` (client half of the SDK — `ModelContextProtocol.AspNetCore` already referenced transitively brings `ModelContextProtocol.Core` client APIs; verify at implementation).

**Files to read before implementing:**
- `.specs/SPEC-20260913-dynamic-mcp-tools.md` · `.specs/SPEC-20260913-mcp-sse-engine.md`
- SDK transports doc: `HttpClientTransport`, `HttpTransportMode.AutoDetect`, `AdditionalHeaders`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiOptions.cs
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiUpstreamClient.cs
src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolContribution.cs
src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs (consume contribution)
src/KnowledgeHub.Server/appsettings.json (DeepWiki section)
src/KnowledgeHub.Server/Program.cs (wire-up)
tests/KnowledgeHub.Tests.Unit/Mcp/DeepWikiUpstreamClientTests.cs
tests/KnowledgeHub.Tests.Unit/Mcp/DeepWikiToolValidationTests.cs
```

## 4. Requirements

### RF-001: SDK upstream client
- **Description:** Lazy `McpClient` creation on first call; `AutoDetect` transport; `ApiKey` → `Authorization: Bearer` additional header; per-call `CancellationToken` with `TimeoutSeconds`.
- **Rules:** transport/session failure → one reconnect attempt → `isError` result; client is a singleton (upstream session reused across calls).
- **Input → Output:** `(toolName, arguments)` → upstream `CallToolResult` content.

### RF-002: Proxy tool registration
- **Description:** When enabled, `tools/list` includes the 3 tools after knowledge-hub tools; schemas mirror upstream; listed even if upstream is momentarily down (call-time failure, not list-time).

### RF-003: Dispatch + error mapping
- **Description:** Forward tool name unchanged → `CallToolAsync` upstream → return content as-is. Upstream `isError`/JSON-RPC error/timeout → `CallToolResult { IsError = true }` with sanitized upstream message (no headers/secrets).

### RF-004: Client-side validation
- **Description:** `owner/repo` format per element; array ≤ 10; `question` non-empty → `isError`/InvalidParams without upstream call.

### RF-005: Configuration
- **Description:** `Enabled=false` removes tools entirely; endpoint/timeout/key overridable via `appsettings`/env (`DeepWiki__ApiKey`).

## 5. API Contract

Downstream (MCP JSON-RPC via SPEC-01 transports):

```json
{ "method": "tools/call", "params": { "name": "ask_question", "arguments": { "repoName": "langchain-ai/langgraph", "question": "How does checkpointing work?" } } }
```
Result: upstream `CallToolResult` content passed through.

Upstream (SDK-managed): `POST https://mcp.deepwiki.com/mcp` Streamable HTTP — `initialize` + `notifications/initialized` + `tools/call` handled by `McpClient`.

## 6. Acceptance Criteria

- [ ] **Given** `Enabled=true` **when** `tools/list` **then** the 3 upstream-named tools with upstream-faithful schemas.
- [ ] **Given** tools listed **when** `tools/call read_wiki_structure {repoName:"langchain-ai/langgraph"}` **then** upstream topic list returned.
- [ ] **Given** invalid `repoName` (`"nope"`) or 11-item array **when** `tools/call` **then** `isError`, no upstream call.
- [ ] **Given** `Enabled=false` **when** `tools/list` **then** none of the 3 proxied tools.
- [ ] **Given** upstream timeout/failure **when** `tools/call` **then** `isError: true` with sanitized message; downstream session unaffected.
- [ ] **Given** consecutive calls **when** second call runs **then** the same `McpClient` session is reused.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Upstream session expiry | stale `Mcp-Session-Id` | SDK reconnect/retry once, then `isError` |
| DNS/network failure | no connectivity | `isError` + connectivity hint |
| Concurrent calls | 2 parallel `tools/call` | serialized on shared client (SDK-safe) or per-call lock |
| Empty `question` | `""` | `isError` client-side |

## 7. Task Plan

- [ ] **T1 — Discovery:** pin `ModelContextProtocol` version; confirm `HttpClientTransport`/`AutoDetect`/`AdditionalHeaders` API.
- [ ] **T2 — Implementation:** options → upstream client → tool contribution → catalog wiring → appsettings.
- [ ] **T3 — Verification:** unit tests with stub transport/handler (validation, error mapping, reconnect); live smoke vs real upstream.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; `tools/call read_wiki_structure` via `/mcp/message` and `/mcp`.
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** `DeepWiki:ApiKey` via env/config only — never logged or serialized into tool output/errors.
- **Scope:** no local persistence of upstream responses.
- **Architecture:** upstream I/O behind `DeepWikiUpstreamClient` (SDK `McpClient`); catalog contribution is the only coupling to SPEC-04.

## 9. Definition of Done

- [ ] All requirements implemented.
- [ ] Acceptance criteria covered by tests + live smoke check.
- [ ] Edge cases handled.
- [ ] `dotnet build` + `dotnet test` green.
- [ ] Guardrails respected; no secrets in logs.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Enable by default? Recommended: `Enabled=true` — public endpoint, zero config value.
- `[A DEFINIR]` Resource proxying (`deepwiki://` URIs)? Recommended: skip — tools cover the use case.
