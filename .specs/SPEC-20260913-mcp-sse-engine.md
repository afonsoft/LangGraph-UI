# SPEC-20260913-mcp-sse-engine

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-sse-engine` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol.AspNetCore` (official MCP C# SDK, GA 1.x) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `#2` |
| Status | `Done` |

## 1. User Story

**As a** MCP client (Cursor, Claude Desktop, LangGraph agent)
**I want** to connect to `GET /mcp/sse` and post JSON-RPC 2.0 messages to `POST /mcp/message` — and modern clients to use Streamable HTTP on `/mcp`
**So that** I can discover and invoke knowledge-hub tools through the standard MCP protocol without any external runtime.

**Problem context:** The platform must expose a native MCP server inside the same Kestrel process as the Blazor SPA and REST API. **Decision (revised after SDK analysis):** instead of hand-rolling the JSON-RPC/SSE layer, we use the **official MCP C# SDK** (`modelcontextprotocol/csharp-sdk`, maintained by Microsoft + Anthropic). The SDK's `MapMcp("/mcp")` with `EnableLegacySse` exposes the exact required endpoints (`/mcp/sse` + `/mcp/message?sessionId=`) AND Streamable HTTP at `/mcp` — satisfying the spec while remaining protocol-correct and forward-compatible.

## 2. Scope

**In scope:**
- `KnowledgeHub.McpEngine` project hosting the MCP composition: `AddKnowledgeHubMcp()` DI extension and `MapKnowledgeHubMcp()` endpoint extension wrapping the official SDK.
- SDK server config: `AddMcpServer().WithHttpTransport(o => { o.Stateless = false; o.EnableLegacySse = true; })` + `app.MapMcp("/mcp")`:
  - `POST/GET/DELETE /mcp` — Streamable HTTP (modern transport, stateful).
  - `GET /mcp/sse` — legacy SSE handshake (SDK-managed sessionId).
  - `POST /mcp/message?sessionId={id}` — legacy SSE message ingress.
- Server identity: `serverInfo: knowledge-hub/0.1.0`, capabilities `tools` + `resources` (+ `listChanged` notifications).
- `IMcpActivityFeed` — in-memory ring buffer (500 events) + pub/sub for the MCP Monitor UI:
  - Session open/close — captured by a lightweight middleware observing `/mcp/sse` connects/disconnects and `Mcp-Session-Id` issuance on `/mcp`.
  - Request/response telemetry (method, tool name, latency, outcome) — via SDK `McpRequestFilter<CallToolRequestParams, CallToolResult>` and filters for list/read methods; transport-level correlation via middleware where filters don't apply.
- Backpressure note: legacy SSE POST returns `202` before handlers run (documented SDK caveat); mitigate with a per-session concurrency gate in the tool-dispatch layer if abuse appears.

**Out of scope:**
- Hand-rolled session manager, `Channel` pipelines, and a custom JSON-RPC dispatcher — replaced by the SDK (first revision had these; removed to avoid reinventing a GA protocol stack).
- OAuth 2.1 authorization, `AllowedHosts` hardening beyond localhost defaults, stdio transport, MCP prompts/sampling.
- Authentication on `/mcp/*` (localhost-first tool).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.McpEngine` (SDK composition + activity feed + filters). `Program.cs` calls `builder.Services.AddKnowledgeHubMcp()` and `app.MapKnowledgeHubMcp()`. Tool/resource content comes from SPEC-04 providers registered in DI.

**Files to read before implementing:**
- `CLAUDE.md` · `AGENTS.md`
- `src/KnowledgeHub.Server/Program.cs`
- SDK docs: transports (`WithHttpTransport`, `EnableLegacySse`, `Stateless`), tools (`McpServerHandlers`, `McpRequestFilter`)

**Files to create or modify:**
```text
src/KnowledgeHub.McpEngine/KnowledgeHub.McpEngine.csproj (add ModelContextProtocol.AspNetCore)
src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs
src/KnowledgeHub.McpEngine/McpEndpointExtensions.cs
src/KnowledgeHub.McpEngine/Activity/IMcpActivityFeed.cs
src/KnowledgeHub.McpEngine/Activity/McpActivityFeed.cs
src/KnowledgeHub.McpEngine/Activity/McpActivityEvent.cs
src/KnowledgeHub.McpEngine/Activity/McpSessionMiddleware.cs
src/KnowledgeHub.McpEngine/Activity/ToolCallActivityFilter.cs
src/KnowledgeHub.Server/Program.cs (wire-up)
tests/KnowledgeHub.Tests.Unit/McpEngine/*Tests.cs
tests/KnowledgeHub.Tests.Integration/McpTransportTests.cs
```

## 4. Requirements

### RF-001: Dual-transport MCP endpoint
- **Description:** `app.MapKnowledgeHubMcp()` maps `MapMcp("/mcp")` with `Stateless=false` and `EnableLegacySse=true` (pragma-suppress `MCP9004`). Legacy SSE clients hit `/mcp/sse` + `/mcp/message`; Streamable HTTP clients hit `/mcp`.
- **Rules:** `Stateless=false` is required — SSE never maps under stateless mode, and stateful mode also enables server→client notifications used by SPEC-04.
- **Input → Output:** HTTP → SDK-managed MCP session + JSON-RPC handling.

### RF-002: Handshake + JSON-RPC methods
- **Description:** SDK handles `initialize` (protocolVersion negotiation, capabilities, `serverInfo knowledge-hub/0.1.0`), `ping`, `notifications/*`, `tools/list`, `tools/call`, `resources/list`, `resources/read` (content from SPEC-04 providers). Standard JSON-RPC error codes come from the SDK.
- **Input → Output:** JSON-RPC 2.0 → protocol-correct responses over both transports.

### RF-003: Activity feed
- **Description:** Record `session_opened`, `session_closed`, `request` events `{ timestamp, sessionId, method, toolName?, durationMs, succeeded, error? }` in the ring buffer; publish to subscribers (SignalR hub, SPEC-05).
- **Rules:** tool-call telemetry via `McpRequestFilter` (has latency + IsError); session lifecycle via middleware on `/mcp/sse` (connect/disconnect) and `Mcp-Session-Id` on `/mcp`; feed never throws into the pipeline.

### RF-004: Session concurrency safety
- **Description:** Legacy SSE lacks HTTP backpressure; document the risk and keep a per-session semaphore in the dispatch path ready to enable via config (`Mcp:MaxConcurrentCallsPerSession`, default 8).

## 5. API Contract

**Endpoint:** `GET /mcp/sse` → `text/event-stream`
```text
event: endpoint
data: /mcp/message?sessionId=3f2c...

event: message
data: {"jsonrpc":"2.0","id":1,"result":{...}}
```

**Endpoint:** `POST /mcp/message?sessionId={id}` → `202 Accepted` (response on SSE stream)

**Endpoint:** `POST /mcp` (Streamable HTTP) → `application/json` or `text/event-stream` framed response; issues `Mcp-Session-Id` header (stateful).

## 6. Acceptance Criteria

- [ ] **Given** a client opens `GET /mcp/sse` **when** the stream starts **then** the first frame is `event: endpoint` with a `sessionId` query param (SDK-emitted).
- [ ] **Given** an open SSE session **when** POST `initialize` to `/mcp/message` **then** the stream receives `event: message` with `protocolVersion`, `capabilities.tools`, `capabilities.resources`, `serverInfo.name=knowledge-hub`.
- [ ] **Given** an open session **when** `tools/list` **then** at minimum `search_knowledge` is present (SPEC-04 provider).
- [ ] **Given** a modern client **when** POST `initialize` to `/mcp` with `Accept: application/json, text/event-stream` **then** a valid Streamable HTTP response is returned (SSE-framed or JSON).
- [ ] **Given** an expired sessionId **when** POST `/mcp/message` **then** HTTP error per SDK behavior (400 Bad Request — SDK 2.x actual).
- [ ] **Given** a tool call **when** it completes **then** `IMcpActivityFeed` contains the event with latency and outcome.
- [ ] **Given** a client disconnects **when** SSE stream closes **then** `session_closed` is recorded.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Malformed JSON | `{invalid` POST | SDK JSON-RPC error (-32600 in SDK 2.x for unparseable bodies) |
| Unknown method | `method: "x"` | `-32601` from SDK |
| Unknown tool | `tools/call nope` | SDK/handlers return `isError` or `McpProtocolException` |
| Flood of POSTs | N parallel calls | bounded by per-session gate (RF-004) |

## 7. Task Plan

- [ ] **T1 — Discovery:** confirm pinned `ModelContextProtocol.AspNetCore` API surface (`WithHttpTransport` options, `McpRequestFilter`, `McpServerHandlers`) against installed version.
- [ ] **T2 — Implementation:** package → DI/endpoint extensions → activity feed + middleware + filter → Program wire-up.
- [ ] **T3 — Verification:** unit tests (activity feed, filter); integration `WebApplicationFactory` round-trip for both transports.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; `curl -N /mcp/sse` + `curl -X POST /mcp/message` + `curl -X POST /mcp`.
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** `AllowedHosts` restricted to loopback (`localhost`, `127.0.0.1`, `::1`) — SDK guidance against DNS rebinding; no CORS on `/mcp` unless a browser client needs it.
- **Scope:** SDK owns protocol correctness; we only compose + observe. Do NOT reimplement JSON-RPC.
- **Architecture:** `KnowledgeHub.McpEngine` stays free of EF Core; catalog-driven content resolves through SPEC-04 providers.

## 9. Definition of Done

- [ ] Both transports live and protocol-correct.
- [ ] Activity feed emits session + request events.
- [ ] `dotnet build` + `dotnet test` green; curl round-trips verified.
- [ ] Guardrails respected; `MCP9004` suppressed with a justification comment.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Also keep a dev-only stdio entry point (`dotnet run -- --stdio`)? Recommended: no — SSE+HTTP cover the use case; add if a client requires stdio.
