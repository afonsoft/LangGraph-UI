# SPEC-20260913-blazor-admin-ui

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `blazor-admin-ui` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly + BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `#6` |
| Status | `Done` |

## 1. User Story

**As a** platform administrator
**I want** a web UI to manage sources, watch live MCP traffic and test retrieval in a playground
**So that** I can operate the knowledge hub without touching the API directly.

**Problem context:** The catalog and MCP runtime are headless. Operations need a visual catalog, live visibility into agent calls, and a fast way to validate search quality before plugging external clients.

## 2. Scope

**In scope:**
- `/sources` page: BootstrapBlazor `Table` of sources (name, type badge, active toggle, last sync, document count); actions — create/edit via modal with per-type dynamic form fields, delete (confirm), manual sync button with result toast.
- `/mcp-monitor` page: live view via SignalR (`/hubs/mcp`) — active SSE sessions (id, connected-at) + scrolling activity log (method, tool name, latency ms, ok/error).
- `/playground` page: chat-style tester — input query + topK, calls `GET /api/search`, renders ranked results with source/title/score; optional raw JSON view.
- `NavMenu` updated to the 3 pages; template demo pages (Counter/Weather) removed.
- `SourceApiClient` + `SearchApiClient` typed `HttpClient` wrappers in the Client project; `McpMonitorClient` wrapping `HubConnection`.
- Server: SignalR hub `McpMonitorHub` broadcasting `IMcpActivityFeed` events; WASM hosting wired (`UseBlazorFrameworkFiles`, `UseStaticFiles`, `MapFallbackToFile("index.html")`).

**Out of scope:**
- Authentication/login UI.
- Streaming LLM chat (playground shows search results, not a generated answer).
- Mobile-specific layouts beyond Bootstrap's responsive grid.
- i18n (UI copy in pt-BR, hardcoded).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Client` (Pages/Services/Layout) + server-side hub in `src/KnowledgeHub.Server/Hubs/` + `Program.cs` wiring. DTOs come from `KnowledgeHub.Shared/Contracts`.

**Files to read before implementing:**
- `.specs/SPEC-20260913-knowledge-sources.md` (API contract) · `.specs/SPEC-20260913-mcp-sse-engine.md` (activity feed)
- `src/KnowledgeHub.Client/Program.cs` · `_Imports.razor` · `Layout/*` · `wwwroot/index.html`

**Files to create or modify:**
```text
src/KnowledgeHub.Client/Services/SourceApiClient.cs
src/KnowledgeHub.Client/Services/SearchApiClient.cs
src/KnowledgeHub.Client/Services/McpMonitorClient.cs
src/KnowledgeHub.Client/Pages/Sources.razor
src/KnowledgeHub.Client/Pages/SourceEditDialog.razor
src/KnowledgeHub.Client/Pages/McpMonitor.razor
src/KnowledgeHub.Client/Pages/Playground.razor
src/KnowledgeHub.Client/Layout/NavMenu.razor (update)
src/KnowledgeHub.Client/Program.cs (DI + BootstrapBlazor)
src/KnowledgeHub.Client/wwwroot/index.html (BootstrapBlazor assets)
src/KnowledgeHub.Server/Hubs/McpMonitorHub.cs
src/KnowledgeHub.Server/Program.cs (SignalR + WASM hosting)
```

## 4. Requirements

### RF-001: Sources catalog page
- **Description:** Grid listing all sources with columns Name, Type, Active, LastSyncAt, Documents; toolbar "Nova fonte"; row actions: edit, activate/deactivate, sync now, delete (with confirm).
- **Input → Output:** `GET /api/sources` → rendered rows; actions hit the corresponding endpoints then refresh.

### RF-002: Dynamic create/edit form
- **Description:** Modal with Name, Description, Type selector, IsActive, AutoSyncEnabled, SyncIntervalMinutes, and type-specific fields: `path` (ObsidianVault/DocumentFile), `url` (WebPage), `endpoint`+`headers` (RestApi), `connectionString`+`query` (SqlDatabase). Client-side required-field validation mirrors server rules.
- **Input → Output:** form → `POST/PUT /api/sources`; server `400/409` surfaced as inline error.

### RF-003: MCP monitor page
- **Description:** Connects to `/hubs/mcp` on page load; shows active session cards and a chronological activity table (timestamp, method, detail, duration, status); disconnects on page leave (`IAsyncDisposable`); auto-reconnect with backoff.
- **Input → Output:** SignalR events → live UI.

### RF-004: Playground page
- **Description:** Query input + topK slider → `GET /api/search` → result cards ordered by score showing chunk text, document title, source name, score.
- **Rules:** empty result → friendly empty state; errors → toast.

### RF-005: WASM hosting
- **Description:** Server serves the compiled client: `UseBlazorFrameworkFiles()` + `UseStaticFiles()` + `MapFallbackToFile("index.html")`; deep links (e.g. `/sources` refresh) work.

## 5. API Contract

Consumes SPEC-02 endpoints plus:

**Hub:** `ws://host/hubs/mcp` (SignalR, JSON)
- Server→Client: `SessionOpened { sessionId, connectedAt }`, `SessionClosed { sessionId }`, `Activity { timestamp, sessionId, method, detail, durationMs, succeeded }`.
- Client→Server: none.

## 6. Acceptance Criteria

- [ ] **Given** the app running **when** navigating to `/sources` **then** existing sources render in the grid.
- [ ] **Given** "Nova fonte" → type `ObsidianVault` **when** the type is selected **then** the `path` field appears and is required.
- [ ] **Given** an MCP client connected to `/mcp/sse` **when** it calls `tools/list` **then** the monitor shows the session and the activity row in ≤1s.
- [ ] **Given** indexed content **when** a query runs in `/playground` **then** ranked results with scores display.
- [ ] **Given** a deep link **when** refreshing `/playground` **then** the SPA loads correctly (fallback works).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| API down / error | 500 on `/api/sources` | error toast, no crash |
| Duplicate name | POST → 409 | inline error in modal |
| Hub disconnect | network drop | auto-reconnect indicator |

## 7. Task Plan

- [ ] **T1 — Discovery:** read template client structure + SPEC-02 contracts.
- [ ] **T2 — Implementation:** BootstrapBlazor registration → API clients → pages → hub + monitor client → nav cleanup.
- [ ] **T3 — Verification:** `dotnet build`; manual UI walkthrough; integration smoke test that `/` serves `index.html`.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; browser preview on `http://localhost:5000`.
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **UI rule (scaffold-mvp golden rule):** no hand-rolled base components — BootstrapBlazor components only.
- **Security:** `connectionString`/`headers` fields render as password/textarea inputs and are never logged client-side.
- **Architecture:** pages call typed clients only; no `HttpClient` inline in `.razor` files.

## 9. Definition of Done

- [ ] All requirements implemented.
- [ ] Acceptance criteria verified (manual + smoke tests).
- [ ] `dotnet build` green; no console errors in browser.
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- None blocking.
