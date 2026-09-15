# SPEC-20260915-sources-edit-dialog

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `sources-edit-dialog` |
| Type | `Feature` |
| Stack | `.NET 10`, `Blazor WASM`, `MCP (ModelContextProtocol)` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-sources-edit-dialog` |
| Ticket | `—` |
| Status | `Done` |

## 1. User Story

**As a** platform admin editing a knowledge source
**I want** the edit dialog to (a) carry a per-source **read-only** flag enforced by the backend, (b) show a **summary of what's indexed** (documents, chunks, last sync), and (c) surface save failures as a **friendly summary** instead of a raw error string
**So that** I can understand and control each source — including protecting a vault from MCP writes — without guessing what went wrong.

**Problem context:**
The current `SourceEditDialog` is form-only: no visibility into what the source has indexed, and `_editError` dumps the raw server string (`"Configuration key 'path' is required"`, `"A source named 'x' already exists"`, `"HTTP 400"`). Separately, `write_note`/`write_knowledge` MCP tools write into Obsidian vaults with **no per-source guard** — there is no `readOnly` concept anywhere. The documents endpoint (`GET /api/sources/{id}/documents`) and the client method `SourceApiClient.DocumentsAsync` already exist but are never used.

## 2. Scope

**In scope:**
- `configuration.readOnly` (bool, default `false`) on the source, edited via a **"Somente leitura"** checkbox shown for `ObsidianVault` sources; backend enforcement on the vault write tools.
- Edit-dialog summary block (existing sources only): document count, total chunks, latest index time, last sync status/timestamp/error, collapsible top-20 document list.
- Friendly error surface: title + summarized message + collapsible raw detail, replacing the raw string dump.
- "Sincronizar agora" action inside the dialog (edit mode): runs `POST /api/sources/{id}/sync`, renders the `SyncResultDto` inline, refreshes the summary block.

**Out of scope:**
- Live "test connection / validate path" probing.
- Per-document delete/reindex from the popup.
- `readOnly` enforcement for non-Obsidian types (no write tools target them today); the checkbox is hidden for other types.
- Vault mount semantics (`:ro`/`:rw` — infra concern, orthogonal).
- Pagination of the document list beyond a top-20 preview.

## 3. Technical Context

**Where the change happens:**
- Dialog: `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor` — new fields (`ReadOnly`), summary block, error UI, sync action.
- Host page: `src/KnowledgeHub.Client/Pages/Sources.razor` — passes the source dto, wires document loading + sync.
- Client API: `src/KnowledgeHub.Client/Services/SourceApiClient.cs` — `DocumentsAsync`/`SyncAsync` already exist.
- Enforcement: `src/KnowledgeHub.Server/Mcp/ToolProviders/ObsidianToolsProvider.cs` (`write_note`), `KnowledgeToolsProvider.cs` (`write_knowledge`), shared helper in `src/KnowledgeHub.Server/Mcp/ObsidianNoteWriter.cs` (e.g. `IsReadOnly(KnowledgeSource)` reading `ConfigurationJson`).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor`, `Pages/Sources.razor`, `Services/SourceApiClient.cs`
- `src/KnowledgeHub.Server/Mcp/ObsidianNoteWriter.cs`, `Mcp/ToolProviders/ObsidianToolsProvider.cs`, `Mcp/ToolProviders/KnowledgeToolsProvider.cs`
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs` (config validation + secret redaction rules)
- `src/KnowledgeHub.Shared/Contracts/KnowledgeSourceDtos.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Client/Pages/SourceEditDialog.razor   (modify — major)
src/KnowledgeHub.Client/Pages/Sources.razor            (modify — summary load, sync wiring)
src/KnowledgeHub.Server/Mcp/ObsidianNoteWriter.cs      (modify — IsReadOnly helper)
src/KnowledgeHub.Server/Mcp/ToolProviders/ObsidianToolsProvider.cs  (modify — write_note guard)
src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs (modify — write_knowledge guard)
tests/KnowledgeHub.Tests.Unit/**                        (add — readOnly enforcement)
tests/KnowledgeHub.Tests.Integration/**                 (add — write tool rejected on read-only vault)
```

## 4. Requirements

### RF-001: Read-only flag on the source
- **Description:** `configuration.readOnly` (bool, default `false`). The dialog shows a `Switch`/checkbox **"Somente leitura"** only when `Type == ObsidianVault`; `BuildConfiguration()` persists `readOnly` and `FromDto` restores it. Unknown/missing key = `false`.
- **Input → Output:** checkbox on → saved source carries `"readOnly": true` in `Configuration`.

### RF-002: Backend enforcement
- **Description:** `write_note` and `write_knowledge` refuse to run against a source whose configuration has `readOnly == true`, returning a clear error (e.g. `vault 'X' is read-only`). Enforcement lives server-side in a single shared helper (`ObsidianNoteWriter.IsReadOnly`), called by each write tool — `read_document` is unaffected.
- **Input → Output:** `write_note(path, content)` on read-only vault → `isError`/`InvalidParams` result; no file written, no reindex.

### RF-003: Indexed-content summary
- **Description:** When editing an existing source (`Id != null`), the dialog loads `GET /api/sources/{id}/documents` asynchronously and shows: **document count**, **total chunks** (sum of `ChunkCount`), **latest `IndexedAt`**, plus a collapsible list of the first 20 documents (title + chunks). It also renders the source's own `LastSyncAt`/`LastSyncStatus`/`LastError` summary. Load failure is non-fatal — muted "resumo indisponível".
- **Rules:** hidden entirely in create mode; spinner/"carregando…" while fetching; newest documents first.

### RF-004: Friendly error surface
- **Description:** `_editError` renders as a titled alert — "Não foi possível salvar" + the message — with raw technical detail (HTTP status/server body) inside a collapsible `<details>` element. Client-side `Validate()` messages keep working unchanged.
- **Input → Output:** failed save → friendly summary + expandable detail, never a bare dump.

### RF-005: Inline "Sincronizar agora"
- **Description:** In edit mode the dialog offers **"Sincronizar agora"**: calls `SyncAsync(id)`, shows the `SyncResultDto` inline (status, docs processed/skipped/removed, chunks created, duration), then refreshes the RF-003 summary. `status == "skipped"` renders as info, not error.
- **Input → Output:** click → button busy state → inline result + refreshed summary.

**Business rules / invariants:**
- `readOnly` never affects ingestion, search or reads — only MCP writes.
- Configuration round-trips: keys not managed by the dialog are preserved on save (verify `BuildConfiguration`/`FromDto` don't drop them — extend to keep unknown keys).
- No new API endpoints; reuse `/api/sources/{id}/documents` and `/api/sources/{id}/sync`.

## 5. API Contract

No new endpoints. Existing reuse:

| Endpoint | Uso |
| --- | --- |
| `GET /api/sources/{id}/documents` | RF-003 summary (`KnowledgeDocumentDto[]`) |
| `POST /api/sources/{id}/sync` | RF-005 inline sync (`SyncResultDto`) |
| `PUT /api/sources/{id}` | persists `configuration.readOnly` |

MCP behavior change (RF-002): `write_note`/`write_knowledge` return error result for read-only vaults — no schema change.

## 6. Acceptance Criteria

- [ ] **Given** an existing ObsidianVault source with indexed docs **when** opening Editar **then** the summary shows docs count, total chunks, latest index time, last sync info and a collapsible document list (≤20 rows).
- [ ] **Given** "Somente leitura" checked and saved **when** reloading the dialog **then** the flag persists in `configuration.readOnly`.
- [ ] **Given** a read-only vault **when** `write_note` or `write_knowledge` is invoked **then** it fails with a clear read-only error and writes nothing.
- [ ] **Given** a failed save (validation/409/HTTP) **when** the dialog re-renders **then** the error shows title + summary + collapsible detail.
- [ ] **Given** "Sincronizar agora" **when** clicked **then** the result renders inline and the summary refreshes; `skipped` shows as info.
- [ ] **Given** create mode **then** no summary block and no sync button; readOnly checkbox still appears for `ObsidianVault`.
- [ ] **Given** `dotnet test` **then** suite green.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Documents endpoint fails | edit dialog opens | muted "resumo indisponível"; form still works |
| Sync already running | "Sincronizar agora" | `skipped` result shown as info |
| `readOnly` absent in config | legacy source | treated as `false`; checkbox off |
| Non-Obsidian type selected | type switch | readOnly checkbox hidden; flag not written |
| Unknown config keys | source with extra keys | preserved on save (no silent drop) |

## 7. Task Plan

- [x] **T1 — Model/UI:** `SourceEditModel.ReadOnly`, checkbox (ObsidianVault only), `BuildConfiguration`/`FromDto` round-trip incl. unknown keys.
- [x] **T2 — Enforcement:** `ObsidianNoteWriter.IsReadOnly` + guards in `write_note`/`write_knowledge`.
- [x] **T3 — Summary block:** async `DocumentsAsync` load in edit mode + stats + collapsible doc list + last-sync info.
- [x] **T4 — Error surface:** friendly alert + `<details>` raw detail (`ApiResult<T>.Detail`).
- [x] **T5 — Inline sync:** button + busy state + inline `SyncResultDto` + summary refresh.
- [x] **T6 — Tests:** unit (`ObsidianNoteWriterTests.IsReadOnly`) + integration (`ToolsCall_ReadOnlyVault_WritesRejected`); `dotnet build`/`format`/`test` green.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-sources-edit-dialog`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Scope:** dialog UX + per-source readOnly — no new endpoints, no schema/migration changes (flag lives in `ConfigurationJson`).
- **Security:** write guard is server-side; summary shows titles/URIs only (same data the API already returns).

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] Acceptance criteria covered by tests/manual evidence. *(readOnly enforcement pinado em integração; render do resumo/erro/sync inline é verificação manual de UI)*
- [x] `dotnet build`, `dotnet format`, `dotnet test` green. *(146 unit + 128 integration)*
- [x] readOnly enforced server-side for both write tools.
- [x] Unknown configuration keys preserved on save.

## Open Questions / Pending Ambiguity

- None — read-only semantics (backend-enforced `configuration.readOnly`), summary content, friendly error surface, inline sync and single-SPEC scope confirmed with the requester.
