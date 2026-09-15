# SPEC-20260914-signalr-hub-resilience

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `signalr-hub-resilience` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `SignalR` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-signalr-hub-resilience` |
| Ticket | `—` |
| Status | `Draft` |

## 1. User Story

**As a** platform user
**I want** the MCP monitor to degrade gracefully when the SignalR hub can't be reached — never crashing the SPA — and to show *why* the connection failed
**So that** a transient or environmental connectivity failure doesn't surface as a fatal error that follows me to unrelated pages.

**Problem context:**
Reported on `https://rag.afonsoft.dev/sources`: `Unable to connect to the server with any of the available transports. (WebSockets failed: The operation was cancelled.) (ServerSentEvents failed: The transport is not supported in the browser.) (LongPolling failed: OperationCanceled)`.

**Investigation evidence (2026-09-14, via curl against production):**
- `POST /hubs/mcp/negotiate` → `200` with `connectionToken` + all transports.
- WS upgrade (HTTP/1.1) → `101 Switching Protocols` through Cloudflare; SignalR handshake layer alive.
- LongPolling poll + handshake POST → `200`.
- Conclusion: **server/proxy path is healthy — failure is client-side**.

**Root causes in code:**
1. `McpMonitor.razor` awaits `Monitor.StartAsync()` **without try/catch** — any failure becomes an unhandled exception → Blazor `#blazor-error-ui` persists across SPA navigation (why the error "appears" on `/sources`, which doesn't use SignalR). `Approvals.razor` already swallows it (`// real-time opcional`).
2. `McpMonitorClient` is `Transient` and disposed on page `DisposeAsync` — navigating away mid-connect cancels `StartAsync` → "The operation was cancelled" on every transport (they're tried sequentially under the same token).
3. The client retries the pointless `ServerSentEvents` transport (never supported in WASM) — noise in the error and one extra failed attempt.

## 2. Scope

**In scope:**
- `McpMonitorClient`: treat `OperationCanceledException` during `StartAsync` as benign cancellation (dispose/navigation) — no throw; capture per-transport failure detail for diagnostics.
- `McpMonitor.razor`: wrap `StartAsync`; on failure show `desconectado` badge + error summary + "Reconectar" button instead of letting the exception escape.
- `McpMonitorClient` transport config: `HttpTransportType.WebSockets | HttpTransportType.LongPolling` — skip SSE (unsupported in WASM) so it never appears in errors.
- UX copy: when all transports fail, hint that WS may be blocked by the user's network/proxy.

**Out of scope:**
- Server/proxy changes — verified working (evidence §1).
- SignalR on other pages; moving the connection app-wide.
- Reconnect policy tuning beyond existing `WithAutomaticReconnect`.
- `Approvals.razor` — already resilient (may adopt the shared error surface if trivial).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Client/Services/McpMonitorClient.cs` — `StartAsync` cancellation handling, transport filter, `LastError`/failure-detail surface.
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor` — try/catch, degraded state UI + reconnect.

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Services/McpMonitorClient.cs`
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor`, `Pages/Approvals.razor` (existing catch pattern)

**Files to create or modify:**
```text
src/KnowledgeHub.Client/Services/McpMonitorClient.cs   (modify)
src/KnowledgeHub.Client/Pages/McpMonitor.razor          (modify)
```

## 4. Requirements

### RF-001: Benign cancellation
- **Description:** `McpMonitorClient.StartAsync` swallows `OperationCanceledException` (connection disposed/navigated mid-connect) — returns without throwing. Real failures still throw so the page can surface them.
- **Input → Output:** dispose during connect → no exception; genuine connect failure → throws.

### RF-002: Transport filter
- **Description:** `WithUrl(..., HttpTransportType.WebSockets | HttpTransportType.LongPolling)` — SSE is never attempted in WASM (removes the expected "not supported" noise from aggregate errors).

### RF-003: Failure surfacing
- **Description:** `McpMonitorClient.LastError: string?` — captures the connect failure message (incl. per-transport detail) for the page to display.

### RF-004: Monitor page resilience
- **Description:** `McpMonitor.razor`: `try { await Monitor.StartAsync(); } catch { /* state shows desconectado + Monitor.LastError + Reconnect button */ }`. `Reconectar` button calls `StartAsync` again. Badge shows `desconectado` + error tooltip/message when `LastError` set; when all transports failed, hint "WebSocket pode estar bloqueado pela rede/proxy".

### RF-005: Dispose safety
- **Description:** disposing the client while `StartAsync` is in flight must not produce an unhandled exception in the component lifecycle (RF-001 covers the throw; verify no `StateHasChanged` on disposed component).

## 5. API Contract

N/A — no contract changes.

## 6. Acceptance Criteria

- [ ] **Given** hub unreachable/mid-connect navigation **when** leaving `/mcp-monitor` or `/approvals` **then** no unhandled exception and no persistent error bar on other pages.
- [ ] **Given** `/mcp-monitor` with failing connect **when** `StartAsync` throws a real error **then** badge shows `desconectado`, the page stays usable, error summary + "Reconectar" are visible.
- [ ] **Given** a working hub **when** `/mcp-monitor` opens **then** connects via WebSockets (or LongPolling fallback) with no SSE attempt in errors.
- [ ] **Given** `dotnet test` **then** suite stays green.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Navigate away mid-connect | open monitor → immediately go to `/sources` | silent cancel; no error bar |
| Reconnect after failure | click "Reconectar" | retries `StartAsync`; success → `conectado` |
| All transports fail | WS blocked by user proxy | degraded badge + hint, page functional |
| Double `StartAsync` | Reconnect clicked twice | guarded by `State != Disconnected` check (existing) |

## 7. Task Plan

- [ ] **T1 — Client:** `McpMonitorClient` — transport filter, OCE swallow on `StartAsync`, `LastError` capture.
- [ ] **T2 — Page:** `McpMonitor.razor` — try/catch + degraded state + "Reconectar" button.
- [ ] **T3 — Verify:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`; manual smoke — open `/mcp-monitor`, navigate away quickly, confirm no error bar on `/sources`.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-signalr-hub-resilience`; never commit to `main`/`master`/`develop`.
- **Scope:** client-side resilience only — do not touch server hub, proxy config, or transports on the server.
- **No regressions:** `Approvals.razor` behavior unchanged.

## 9. Definition of Done

- [ ] All requirements implemented; acceptance criteria verified.
- [ ] `dotnet build`, `dotnet format`, `dotnet test` green.
- [ ] Manual smoke on production: monitor connects; forced failure shows degraded state, not an app crash.

## Open Questions / Pending Ambiguity

- Whether the production failure was navigation-cancel vs. environmental WS blocking can't be fully confirmed without the user's browser console — the SPEC covers both (resilience + diagnostics + transport filter).
