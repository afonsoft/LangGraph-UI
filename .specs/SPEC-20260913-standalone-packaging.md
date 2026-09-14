# SPEC-20260913-standalone-packaging

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `standalone-packaging` |
| Type | `Infra` |
| Stack | `.NET 10` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `#7` |
| Status | `Done` |

## 1. User Story

**As a** end user
**I want** a single self-contained executable that serves the SPA, REST API and MCP server on one port
**So that** I can run the platform on Linux/macOS/Windows without installing the .NET runtime.

**Problem context:** Distribution must be a single file; the Blazor WASM assets and the SQLite database must work from that layout.

## 2. Scope

**In scope:**
- `KnowledgeHub.Server.csproj` publish profile: `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, per-RID `RuntimeIdentifier` via `dotnet publish -r`.
- SQLite native lib (`e_sqlite3`) self-extraction verified.
- Database file location beside the executable (not inside the extraction dir): `AppContext.BaseDirectory` + `knowledgehub.db`, configurable.
- `global.json` pinning SDK `10.0.x` with `rollForward: latestFeature`.
- `README` publish instructions (`dotnet publish -c Release -r linux-x64|win-x64|osx-arm64`).

**Out of scope:**
- MSI/installer packaging, code signing, auto-update.
- Cross-platform single-CI build matrix (manual publish per RID).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/KnowledgeHub.Server.csproj`, `global.json`, `README.md`, connection-string resolution in `Program.cs`.

**Files to create or modify:**
```text
src/KnowledgeHub.Server/KnowledgeHub.Server.csproj
global.json
README.md
src/KnowledgeHub.Server/Program.cs (DB path resolution)
```

## 4. Requirements

### RF-001: Single-file publish
- **Description:** `dotnet publish src/KnowledgeHub.Server -c Release -r linux-x64` produces one executable containing the runtime, the server and the compiled Blazor WASM static assets.
- **Input → Output:** publish command → `publish/KnowledgeHub.Server` binary.

### RF-002: Portable data directory
- **Description:** The SQLite file defaults to `{AppContext.BaseDirectory}/knowledgehub.db`; overridable via `KnowledgeHub:DatabasePath` in config/env. Directory auto-created.

### RF-003: Port configuration
- **Description:** Default URLs `http://localhost:5000`; overridable via `ASPNETCORE_URLS`. `/mcp/sse`, `/api/*` and the SPA all served from the same port.

## 5. API Contract

N/A — packaging feature.

## 6. Acceptance Criteria

- [ ] **Given** `dotnet publish -r linux-x64` **when** the binary runs on a machine check **then** `http://localhost:5000` serves the SPA.
- [ ] **Given** the published binary **when** started **then** `knowledgehub.db` is created next to the executable (not in a temp extraction dir).
- [ ] **Given** the published binary **when** an MCP client hits `/mcp/sse` **then** the SSE handshake works identically to `dotnet run`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Read-only exe dir | binary in protected folder | clear startup error telling the user to set `KnowledgeHub:DatabasePath` |

## 7. Task Plan

- [ ] **T1 — Implementation:** csproj publish properties + global.json + DB path resolution + README section.
- [ ] **T2 — Verification:** `dotnet publish -r linux-x64`, run the artifact, hit `/`, `/api/sources`, `/mcp/sse`.
- [ ] **T3 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** published binary carries no secrets; config via env vars only.

## 9. Definition of Done

- [ ] Single-file publish produces a working binary.
- [ ] Acceptance criteria verified on linux-x64.
- [ ] `dotnet build` + `dotnet test` green.

## Open Questions / Pending Ambiguity

- None.
