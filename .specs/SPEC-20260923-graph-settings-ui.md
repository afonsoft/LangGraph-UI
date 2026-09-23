# SPEC-20260923-graph-settings-ui — Runtime Graph settings via /settings

| Field | Value |
|-------|-------|
| Date | `2026-09-23` |
| Author | `Devin` |
| Type | `Feature` (small — runtime-config surface over SPEC-20260923-graphrag) |
| Stack | `.NET 10 / C# 14` + EF Core SQLite + Blazor WASM |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260923-graph-settings-ui` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |
| Origin | Owner request: "na tela de settings ter a opção de configurar o Graph, ativar ele e configurar as demais coisas dele". |

## 1. User Story

**As an** admin
**I want** to enable GraphRAG and tune its knobs (extraction budget, chunk
chars, result cap) from `/settings`
**so that** I can turn the graph on/off and tune it without redeploying.

## 2. Context

SPEC-20260923-graphrag reads four `IConfiguration` keys at call time:
`Graph:Enabled` (master switch — tools catalog + ingestion gate),
`Graph:MaxChunksPerSync` (extraction budget), `Graph:MaxChunkChars`
(extractor truncation), `Graph:MaxResults` (traversal cap). Today they only
change via env/appsettings + restart. The repo already has the
`ChatSettings` precedent: single-row EF entity + singleton service with
cached snapshot + `Invalidate()` + `/api/settings/*` + Settings.razor card.

## 3. Functional Requirements

- **RF-001** `GraphSettings` entity (single-row `Id=1`): `Enabled`,
  `MaxChunksPerSync`, `MaxChunkChars`, `MaxResults`, `UpdatedAt` + EF migration.
- **RF-002** `IGraphSettingsService` singleton: `GetEffective()` snapshot
  (store row → `Graph:*` config fallback), `DescribeAsync`, `SaveAsync`,
  `ClearAsync`, `Invalidate()`.
- **RF-003** Consumers swapped from `IConfiguration` to the service:
  `GraphToolsProvider` (`Enabled`, `MaxResults`), `EntityExtractor`
  (`MaxChunkChars`), `IngestionService` (`Enabled`, `MaxChunksPerSync`).
- **RF-004** Endpoints under the existing `Operational`-authorized group:
  `GET /api/settings/graph` → `GraphSettingsDto`; `PUT` validates
  `MaxChunksPerSync` 1–10_000, `MaxChunkChars` 200–50_000,
  `MaxResults` 10–10_000 → 204; `DELETE` clears the row → 204.
- **RF-005** Save/clear calls `IToolCatalogChangeNotifier` — toggling
  `Enabled` changes the tools catalog live.
- **RF-006** `Settings.razor` card "Knowledge Graph (GraphRAG)": enable
  checkbox, three numeric fields, source badge (store|env), Save +
  "Restaurar ambiente"; help text notes the per-source `"graph": true` flag.

## 4. Non-Functional

- Zero-config behavior unchanged: no row → `Graph:*` config → built-in defaults.
- Operational policy covers the new endpoints (same group).
- No secrets involved — plain scalar settings, no masking needed.

## 5. Acceptance Criteria

- [ ] `PUT /api/settings/graph {"enabled":true,...}` → `GET` shows
  `enabled:true, source:"store"`; `DELETE` → `source:"env"`.
- [ ] Enabling via PUT makes `find_dependencies` appear in `GET /api/tools`
  without restart (E2E through the catalog notifier).
- [ ] Invalid bounds → 400 with `error`.
- [ ] Unit: `GraphSettingsService` store-overrides-env, `Invalidate` reloads.
- [ ] `dotnet build` 0 warnings; unit + integration green; format clean.
