# SPEC-20260915-mcp-monitor-activity

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-monitor-activity` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `SignalR`, `MCP (ModelContextProtocol)` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-rename-mcp-monitor` |
| Ticket | `—` |
| Status | `Approved` |

## 1. User Story

**As a** platform user watching `/mcp-monitor`
**I want** the page to show MCP sessions and call activity — including calls that happened *before* I opened the page
**So that** I can actually observe the traffic my MCP clients generate, instead of an always-empty monitor.

**Problem context:**
Reported 2026-09-15: MCP calls were made through a real client (API key usage updated on `/api-keys` — requests reached the server and authenticated), yet `/mcp-monitor` showed zero sessions and zero activity.

**Root causes in code:**
1. `McpMonitorHub.OnConnectedAsync` pushes `feed.Snapshot()` on the `"Snapshot"` channel, and `McpMonitorClient` exposes a `Snapshot` event — **but `McpMonitor.razor` never subscribes to it**. Only live events render; the ring-buffer history (capacity 500) is fetched and silently discarded.
2. **Two wire shapes for the same concept.** Live `"Activity"` broadcasts send an anonymous `{timestamp, sessionId, method, detail, durationMs, succeeded}` while `"Snapshot"` sends raw `McpActivityEvent` (`{timestamp, kind, sessionId, transport, method, toolName, durationMs, succeeded, error}`) — no shared DTO, so even a subscribed client cannot render the snapshot with the same code path.
3. Sessions card cannot be reconstructed: snapshot items carry `Kind` server-side, but no mapped DTO exists client-side to replay open/close events into the sessions list.

## 2. Scope

**In scope:**
- Shared monitor contract in `KnowledgeHub.Shared/Contracts`: a `McpMonitorEventDto` carrying `Kind` + the display fields, used for **both** the `"Snapshot"` payload and the live `"Activity"` broadcast (single mapping point server-side).
- `McpActivityBroadcastService` / `McpMonitorHub`: map `McpActivityEvent` → `McpMonitorEventDto` once; snapshot sends the mapped list.
- `McpMonitorClient`: typed `Snapshot`/`Activity` payloads over the shared DTO.
- `McpMonitor.razor`: subscribe to `Snapshot` and replay it — `SessionOpened` → add session (timestamp as `connectedAt`), `SessionClosed` → remove session, other kinds → append to the activity table — then keep applying live events through the same path.
- Unit tests for the replay logic and the DTO wire contract.

**Out of scope:**
- New monitor features (filters, counters, "limpar" button, per-session stats) — fix only.
- Changing what the feed records (`McpActivityFilters`, `McpSessionMiddleware`) — recording path already proven by the API-key audit and existing tests.
- Persisting the feed beyond the in-memory ring buffer.
- `Approvals.razor` behavior (already consumes `Activity`; must keep working).
- Hub auth (`AuthPolicies.Operational`) and the degraded/reconnect UX from SPEC-20260914-signalr-hub-resilience — unchanged.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Shared/Contracts/` — new `McpMonitorDtos.cs` (shared wire contract; `KnowledgeHub.Shared` is already referenced by Client and transitively by Server).
- `src/KnowledgeHub.Server/Hubs/McpActivityBroadcastService.cs` — single `McpActivityEvent → McpMonitorEventDto` mapping used for live events.
- `src/KnowledgeHub.Server/Hubs/McpMonitorHub.cs` — snapshot sends mapped DTOs.
- `src/KnowledgeHub.Client/Services/McpMonitorClient.cs` — typed `On<McpMonitorEventDto>("Activity")` / `On<IReadOnlyList<McpMonitorEventDto>>("Snapshot")`; `SessionOpened`/`SessionClosed` channels unchanged.
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor` — subscribe `Snapshot`, replay into `_sessions`/`_activity`, cap at `MaxActivity`.

**Files to read before implementing:**
- `src/KnowledgeHub.McpEngine/Activity/McpActivityEvent.cs`, `IMcpActivityFeed.cs`, `McpActivityFeed.cs`
- `src/KnowledgeHub.Server/Hubs/McpMonitorHub.cs`, `McpActivityBroadcastService.cs`
- `src/KnowledgeHub.Client/Services/McpMonitorClient.cs`, `Pages/McpMonitor.razor`
- `tests/KnowledgeHub.Tests.Unit/McpEngine/ActivityEventContractTests.cs`, `McpActivityFeedTests.cs`
- `src/KnowledgeHub.Shared/Contracts/` (DTO conventions)

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/McpMonitorDtos.cs        (create)
src/KnowledgeHub.Server/Hubs/McpMonitorHub.cs              (modify)
src/KnowledgeHub.Server/Hubs/McpActivityBroadcastService.cs (modify)
src/KnowledgeHub.Client/Services/McpMonitorClient.cs        (modify)
src/KnowledgeHub.Client/Pages/McpMonitor.razor              (modify)
tests/KnowledgeHub.Tests.Unit/**                            (add: replay + contract tests)
```

## 4. Requirements

### RF-001: Shared monitor event DTO
- **Description:** `McpMonitorEventDto` in `KnowledgeHub.Shared/Contracts` — `{ Timestamp, Kind, SessionId, Method, Detail, DurationMs, Succeeded }` where `Kind` is serialized so the client can distinguish `SessionOpened`/`SessionClosed` from activity rows, `Method` carries the formatted `tools/call:{tool}` value and `Detail` carries `Error ?? Transport` (same semantics as today's anonymous payload).
- **Rules:** one DTO for snapshot and live broadcast; `Kind` may travel as the enum name string (web/camelCase JSON, case-insensitive read) or the existing enum — pick the option consistent with Shared conventions.
- **Input → Output:** `McpActivityEvent` → identical `McpMonitorEventDto` on both channels.

### RF-002: Snapshot payload aligned
- **Description:** `McpMonitorHub.OnConnectedAsync` sends `feed.Snapshot()` **mapped** to `IReadOnlyList<McpMonitorEventDto>` — same projection as `BroadcastAsync` (extract a shared `Map` so the shapes cannot diverge again).
- **Input → Output:** hub connect → `"Snapshot"` with up to `ActivityFeedCapacity` mapped events, oldest→newest.

### RF-003: Page subscribes and replays
- **Description:** `McpMonitor.razor` subscribes `Monitor.Snapshot` before `StartAsync`; on snapshot, replay in order — `SessionOpened` adds `_sessions[SessionId] = Timestamp`, `SessionClosed` removes it, other kinds append to `_activity` (cap `MaxActivity`). Live events continue through the same replay routine so snapshot and live paths share one code path.
- **Rules:** replay is idempotent (dictionary semantics); events with `SessionId == null` skip session bookkeeping; activity rows render exactly as today (`Method`, `Detail`, `DurationMs`, `Succeeded`).

### RF-004: Live channels keep working
- **Description:** `SessionOpened`/`SessionClosed`/`Activity` live broadcasts keep their current semantics; `Approvals.razor` (`Monitor.Activity` → reload) unaffected. The `Activity` payload switches to `McpMonitorEventDto` (same fields it consumes today).

### RF-005: Extracted replay logic is testable
- **Description:** the snapshot-replay mapping lives in a pure, unit-testable place (e.g., static helper in `McpMonitorClient` or a small `McpMonitorState` type under `Services/`), not inline-only in the `.razor` file.

**Business rules / invariants:**
- Ring buffer (`McpActivityFeed`) unchanged — snapshot only reads it.
- Sessions opened before buffer eviction won't be listed (inherent ring-buffer limit — acceptable).
- No change to `RequireAuthorization(AuthPolicies.Operational)` on the hub or the degraded-state UI.

## 5. API Contract

SignalR hub `/hubs/mcp` messages (auth: `AuthPolicies.Operational` — cookie or `aft_*` key):

| Message | Payload |
| --- | --- |
| `Snapshot` (connect only) | `McpMonitorEventDto[]` — `{ timestamp, kind, sessionId, method, detail, durationMs, succeeded }` |
| `Activity` | `McpMonitorEventDto` (same shape) |
| `SessionOpened` | `{ sessionId, connectedAt }` — unchanged |
| `SessionClosed` | `{ sessionId }` — unchanged |

## 6. Acceptance Criteria

- [ ] **Given** MCP calls completed before opening the page **when** `/mcp-monitor` connects **then** the activity table lists them (method, ms, ok/erro) and sessions opened-but-not-closed appear in "Sessões ativas".
- [ ] **Given** the page open **when** a client calls `tools/call` **then** a live row appears (current behavior preserved).
- [ ] **Given** a snapshot containing open+close for the same session **when** replayed **then** the session does not appear as active.
- [ ] **Given** an empty feed **when** the page connects **then** "Aguardando chamadas MCP…" / "Nenhuma sessão MCP conectada." still show.
- [ ] **Given** `dotnet test` **then** suite green including new replay/contract tests.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Snapshot > MaxActivity | 500 buffered events | page keeps newest 200, drops oldest |
| `SessionId` null | in-process tool calls (agent loop) | activity row only, no session bookkeeping |
| Reconnect | hub drops and re-subscribes | snapshot replays again; state rebuilt (dedupe/clear-on-replay acceptable if documented) |
| Disconnected hub | transport failure | existing degraded banner; no crash |

## 7. Task Plan

- [ ] **T1 — Contract:** `McpMonitorDtos.cs` in Shared + server-side `Map(McpActivityEvent)` used by hub snapshot and broadcast.
- [ ] **T2 — Client:** typed `Snapshot`/`Activity` handlers over the DTO; extract replay helper (RF-005).
- [ ] **T3 — Page:** subscribe `Snapshot`, replay into `_sessions`/`_activity`, verify live path unchanged.
- [ ] **T4 — Tests:** unit tests for replay ordering/caps/null-session + wire-shape contract test for `McpMonitorEventDto`.
- [ ] **T5 — Verify:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`; manual smoke — make an MCP call, *then* open `/mcp-monitor`, confirm history renders.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-rename-mcp-monitor`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Scope:** monitor render pipeline only — feed recording, hub auth and resilience UX unchanged.
- **Bugfix rule:** reproduction/regression test first, then fix.
- **Security:** snapshot contains session ids/method names only — no secrets; same data already broadcast live today.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] Acceptance criteria covered by tests/manual smoke evidence.
- [ ] `dotnet build`, `dotnet format`, `dotnet test` green.
- [ ] `Approvals.razor` and degraded-state UX unregressed.
- [ ] One shared wire shape for snapshot and live activity (no anonymous divergence).

## Open Questions / Pending Ambiguity

- None — scope (fix only, no new monitor features) and root-cause hypothesis (missing `Snapshot` subscription + dual payload shape) confirmed with the requester.
