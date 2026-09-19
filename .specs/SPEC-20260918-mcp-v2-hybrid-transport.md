# SPEC-20260918-mcp-v2-hybrid-transport

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-v2-hybrid-transport` |
| Type | `Feature` |
| Stack | `.NET 10 / ModelContextProtocol SDK 2.2.0` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-mcp-v2-hybrid` |
| Ticket | `#126` |
| Status | `Approved` |

## 1. User Story

**As a** KnowledgeHub operator and MCP client user
**I want** the MCP server to serve the `2026-07-28` protocol revision natively while keeping legacy `initialize`/SSE clients fully functional
**So that** new-generation clients (v2 SDK, `server/discover`, `MCP-Protocol-Version` header) are not forced to downgrade, and nothing breaks for existing clients.

**Problem context:**
The MCP C# SDK packages are already at **2.2.0** (latest per `dotnet list package --outdated`), but the server still runs the v1 transport shape:

- `McpServiceCollectionExtensions.cs:36` sets `transport.Stateless = false` (= `SessionMode = Stateful`). Per the v2 docs, a `Stateful` endpoint **refuses** native `2026-07-28` requests with `-32022 UnsupportedProtocolVersion`, forcing every v2 client to downgrade to the legacy `initialize` handshake.
- `EnableLegacySse = true` (MCP9004 suppressed) is still required for SSE-only clients (older Cursor/Claude Desktop) and for the `?access_token=` SSE path verified in production (PR #120).
- Session-dependent infrastructure exists and must keep working for session clients: `McpSessionMiddleware` (activity feed), `McpSessionRegistry` + `ToolCatalogChangeNotifier` (push `notifications/tools/list_changed`), `SessionCallGate` (max 8 concurrent calls/session — and `GateFor(null)` returns `null`, so sessionless calls would be **unbounded**).
- Not affected: elicitation/sampling/roots (unused → MCP9005 N/A), experimental Tasks (unused → SEP-2663 break N/A), stateful-only knobs `IdleTimeout`/`MaxIdleSessionCount`/`EventStreamStore`/`SessionMigrationHandler`/`PerSessionExecutionContext` (unused → MCP9006 N/A).
- Upstream clients (`DeepWikiUpstreamClient`, `FirecrawlUpstreamClient`, `TavilyUpstreamClient`, `McpProxySession`) use `McpClient.CreateAsync` + `HttpClientTransport` `AutoDetect` — the v2 client auto-probes `2026-07-28` and falls back to `initialize`; no change needed.

Sources: https://devblogs.microsoft.com/dotnet/announcing-v20-of-the-official-mcp-csharp-sdk/ · https://csharp.sdk.modelcontextprotocol.io/v2/concepts/stateless/stateless.html

## 2. Scope

**In scope:**
- Switch the HTTP transport to `SessionMode = HttpServerSessionMode.StatefulForInitializeClients` (hybrid), behind a `Mcp:SessionMode` config knob (default `StatefulForInitializeClients`).
- Keep `EnableLegacySse = true` for SSE-only clients.
- `SessionCallGate`: bounded concurrency for sessionless calls via a shared bucket.
- Activity feed/monitor: tolerate and display sessionless requests.
- Tests covering both protocol paths; docs sync (CLAUDE.md/README/memory).

**Out of scope:**
- MRTR adoption (`InputRequiredException`, elicitation over MCP for HITL approvals) — backlog.
- `Mcp-Param-*` header promotion — N/A for the dynamic (non-attribute) catalog.
- `ModelContextProtocol.Extensions.Tasks` / `.Extensions.Apps` — not used.
- Package version bump — already on 2.2.0 (latest).
- Removing legacy SSE — still required by deployed clients.

## 3. Technical Context

**Where the change happens:**
`src/KnowledgeHub.McpEngine` — transport options in `McpServiceCollectionExtensions`, session gate in `Activity/SessionCallGate`, middleware/filters tolerate null session. Endpoint mapping (`MapKnowledgeHubMcp` → `app.MapMcp("/mcp")`) unchanged. Auth (`RequireAuthorization(AuthPolicies.Operational)` + `?access_token=` on SSE) unchanged.

Hybrid semantics (per SDK docs): `initialize`-handshake clients get full stateful sessions (session id, GET/DELETE streams, notifications, legacy SSE); `2026-07-28` requests are served per-request with no session, `GET`/`DELETE` → 405, services resolve from `HttpContext.RequestServices` with request scoping disabled.

**Files to read before implementing:**
- `src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs`, `McpEndpointExtensions.cs`
- `src/KnowledgeHub.McpEngine/Activity/{McpSessionMiddleware,McpSessionRegistry,McpActivityFilters,SessionCallGate}.cs`
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` (handlers block ~L218-256)
- `src/KnowledgeHub.Server/Mcp/ToolCatalogChangeNotifier.cs`
- `tests/KnowledgeHub.Tests.Integration/{McpTransportTests,TestMcp}.cs`, MCP contract tests
- `.specs/SPEC-20260913-mcp-sse-engine.md`, `.specs/SPEC-20260917-sse-e2e-prod.md`

**Files to create or modify:**
```text
src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs
src/KnowledgeHub.McpEngine/Activity/SessionCallGate.cs
src/KnowledgeHub.McpEngine/Activity/McpSessionMiddleware.cs   (only if needed)
tests/KnowledgeHub.Tests.Integration/McpTransportTests.cs
tests/KnowledgeHub.Tests.Integration/TestMcp.cs               (helper for stateless calls)
tests/KnowledgeHub.Tests.Unit/McpEngine/*                     (gate bucket tests)
appsettings*.json / .env.example                               (Mcp:SessionMode doc)
CLAUDE.md, README.md, .claude/memory/*                         (docs sync)
```

## 4. Requirements

### RF-001: Hybrid SessionMode with config knob
- **Description:** `WithHttpTransport` must set `transport.SessionMode = HttpServerSessionMode.StatefulForInitializeClients` by default, overridable via `Mcp:SessionMode` (`Stateless | Stateful | StatefulForInitializeClients`), parsed with a clear startup error on invalid value. Keep `EnableLegacySse = true` with the existing MCP9004 pragma and an updated comment explaining the hybrid rationale.
- **Rules:** the `Stateless` bool shorthand is not used (it cannot express hybrid); `Mcp:SessionMode=Stateless` must be honored for future full-stateless deployments (legacy SSE will not map — accepted, documented; SDK refuses `EnableLegacySse` under Stateless, so SSE is only enabled for session-capable modes).
- **Input → Output:** config value → effective `SessionMode` on the transport.

### RF-002: Native 2026-07-28 path works
- **Description:** A `POST /mcp` carrying `MCP-Protocol-Version: 2026-07-28` and a `server/discover`/`tools/list`/`tools/call` JSON-RPC body must be served **without** any `initialize` handshake: HTTP 200, JSON-RPC result, no `Mcp-Session-Id` minted or echoed. `GET`/`DELETE /mcp` for such clients return 405 (SDK behavior — verify).
- **Rules:** auth still enforced (`RequireAuthorization` unchanged); a `2026-07-28` request carrying a stray `Mcp-Session-Id` ignores it (SDK behavior — verify).
- **Input → Output:** v2-native client request → correct result, sessionless.

### RF-003: Legacy session path unchanged
- **Description:** `initialize`-handshake clients keep: issued `Mcp-Session-Id`, `GET` stream availability, `notifications/tools/list_changed` push on catalog changes, and legacy SSE on `GET /mcp/sse` + `POST /mcp/message` including `?access_token=` auth (PR #120 path).
- **Input → Output:** down-level client → identical behavior to today.

### RF-004: Bounded concurrency for sessionless calls
- **Description:** `SessionCallGate` must bound concurrent `tools/call` for requests with no session via a single shared bucket (key `"stateless"`, same `MaxConcurrentCallsPerSession` limit), instead of bypassing the gate.
- **Rules:** session calls keep per-session keys; the shared bucket key must be collision-proof vs real session ids (prefix or constant outside the id space); metric/event still records `SessionId = null`.
- **Input → Output:** N concurrent sessionless `tools/call` → at most `Mcp:MaxConcurrentCallsPerSession` in flight.

### RF-005: list_changed degradation documented
- **Description:** `tools/list_changed` push continues to reach stateful sessions only (registry only holds session servers — no code change); document that sessionless clients re-run `tools/list` on demand (catalog `Version` still bumps; `DynamicToolCatalog` cache still invalidated for all callers).
- **Input → Output:** catalog change → session clients notified; sessionless clients see fresh catalog on next list.

### RF-006: Monitor tolerates sessionless traffic
- **Description:** Activity feed events for sessionless requests record `SessionId = null` (already supported by DTO/UI); `server/discover` appears as a request method in the feed; `McpSessionMiddleware` must not emit session open/close for requests without the header (verify current tolerance), and the Monitor UI must render sessionless events without errors.
- **Input → Output:** stateless traffic → request/tool-call events with null session, no session lifecycle noise.

### RF-007: Upstream clients unchanged and verified
- **Description:** No code change to `McpClient` consumers; add/keep tests proving `HttpClientTransport AutoDetect` still connects (fallback path exercised today). `McpClientOptions.ProtocolVersion` stays unpinned.
- **Input → Output:** DeepWiki/Firecrawl/Tavily/McpProxy providers → same behavior, existing tests green.

### RF-008: Docs sync
- **Description:** Update `CLAUDE.md`/`README.md` MCP section (hybrid transport, sessionless semantics, `Mcp:SessionMode` knob), `.env.example` comment if the knob is added there, and harness memory (`orchestrator_stats.md`/`memory.md`) post-delivery.
- **Input → Output:** stale docs → documented hybrid behavior.

**Business rules / invariants:**
- No behavior change for REST API, agent loop, or internal catalog consumers (they bypass MCP transport).
- Deprecation warnings remain suppressed only where justified (MCP9004 pragma stays; no new blanket suppressions).
- `dotnet build` must stay 0-warning.

## 5. API Contract (if applicable)

Wire-level (MCP over HTTP):

| Client | Request | Expected |
| --- | --- | --- |
| `2026-07-28` | `POST /mcp` + headers `MCP-Protocol-Version: 2026-07-28` + `Mcp-Method: <method>` (+ `Mcp-Name: <tool>` for `tools/call`) + body `params._meta["io.modelcontextprotocol/protocolVersion"]` + `params._meta["io.modelcontextprotocol/clientCapabilities"]` | 200, JSON-RPC result, **no** `Mcp-Session-Id` |
| `2025-11-25` | `POST /mcp` + `initialize` | 200 + `Mcp-Session-Id`; session features on |
| SSE-only | `GET /mcp/sse` (+`?access_token=`) | endpoint event; `POST /mcp/message` works |
| `2026-07-28` | `GET/DELETE /mcp` (version header, no session) | 405 |

**Expected errors:** `-32022` must NOT be returned to `2026-07-28` requests anymore; a `2026-07-28` request missing any of the required headers/`_meta` keys fails fast with `-32602`/`-32020` naming the missing piece; `ping` does not exist on `2026-07-28` (-32601); invalid `Mcp:SessionMode` config → startup `InvalidOperationException` naming the key and valid values.

## 6. Acceptance Criteria

- [ ] **Given** hybrid mode **when** a client posts `tools/list` with `MCP-Protocol-Version: 2026-07-28` **then** 200 + full tool list + no `Mcp-Session-Id` header.
- [ ] **Given** hybrid mode **when** a client posts `initialize` **then** 200 + `Mcp-Session-Id` issued and subsequent session requests work.
- [ ] **Given** an SSE-only client **when** `GET /mcp/sse?access_token=<key>` **then** endpoint event + `POST /mcp/message` tools/call works.
- [ ] **Given** a catalog change **when** sessions exist **then** session clients receive `tools/list_changed`; sessionless clients see the new catalog on next `tools/list`.
- [ ] **Given** >8 concurrent sessionless `tools/call` **when** the shared bucket is full **then** excess calls wait (bounded), then complete.
- [ ] **Given** `Mcp:SessionMode=Stateless` **when** app starts **then** server runs stateless (documented SSE caveat); invalid value → clear startup error.
- [ ] **Given** existing suite **when** `dotnet test` runs **then** all tests green including new stateless-path tests; `dotnet format` exit 0.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| v2-native request w/ stray `Mcp-Session-Id` | header present | ignored, no session minted |
| `GET /mcp` from 2026-07-28 client | GET | 405 |
| Concurrent sessionless burst | 20 parallel calls | ≤8 in flight, rest queued |
| `Mcp:SessionMode=bogus` | config | startup fails with clear message |
| SSE client mid-upgrade | legacy SSE | unaffected (session path) |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read section-3 files; confirm SDK 2.2.0 API surface (`HttpServerTransportOptions.SessionMode`, `HttpServerSessionMode`) via installed package; map every session-id touchpoint.
- [ ] **T2 — Transport:** implement RF-001 (SessionMode + `Mcp:SessionMode` knob + updated comments/pragma).
- [ ] **T3 — Gate:** implement RF-004 shared bucket in `SessionCallGate` (+ unit tests).
- [ ] **T4 — Middleware/feed:** verify/adjust RF-006 tolerance; `server/discover` recorded correctly.
- [ ] **T5 — Tests:** integration tests for stateless path (discover, tools/list, tools/call, no session header, 405 GET), session path regression, SSE path regression; gate unit tests.
- [ ] **T6 — Docs:** RF-008 updates.
- [ ] **T7 — Validation:** build 0 warnings, `dotnet test` green, `dotnet format` exit 0; local live smoke of both paths (`curl` initialize + `2026-07-28` probe + SSE).
- [ ] **T8 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

.NET: unit tests for the gate + option parsing; integration tests for both
wire paths (existing `TestMcp` helper extended with a sessionless mode);
live `curl` evidence on localhost and prod smoke post-deploy.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`, `master` or `develop`. Use `feature/Devin-20260918-mcp-v2-hybrid`. *(Nota: SPEC commitada em main por instrução explícita do owner.)*
- **Workflows:** `.github/workflows/` protected — no changes needed anyway.
- **Security:** no secrets in commit; `?access_token=` behavior unchanged; no logging of session ids beyond current debug level.
- **Scope:** transport/gate/tests/docs only — no MRTR, Tasks, or header promotion.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] All acceptance criteria (section 6) covered by passing tests or live evidence.
- [ ] Edge cases handled.
- [ ] `dotnet build` 0 warnings, `dotnet test` green, `dotnet format` exit 0.
- [ ] Legacy SSE + `?access_token=` verified working post-change.
- [ ] Guardrails respected; docs synced.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-mcp-v2-hybrid`.

## Open Questions / Pending Ambiguity

- Whether `HttpServerSessionMode` lives in `ModelContextProtocol.Server` or `Microsoft.Extensions.*` namespace in 2.2.0 — resolve at T1 from the installed package (docs show `options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients`).
- Whether `EnableLegacySse` composes with hybrid mode on the same endpoint (expected: yes — SSE clients are initialize-era) — verify at T5/T7.
