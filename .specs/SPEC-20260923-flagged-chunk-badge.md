# SPEC-20260923-flagged-chunk-badge — Suspicion badge on kept flagged chunks

| Field | Value |
|-------|-------|
| Date | `2026-09-23` |
| Author | `Devin` |
| Type | `Feature` (small — deferral from SPEC-20260923-prompt-injection-guard) |
| Stack | `.NET 10` + Blazor WASM |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-flagged-chunk-badge` |
| Status | `Done` — merged via PR |
| Origin | `SPEC-20260923-prompt-injection-guard` §Open-questions — "flagged-but-included chunks should get a UI badge in the playground — deferred to a UI follow-up". |

## 1. User Story

**As an** admin debugging retrieval
**I want** search results that survived `Security:Injection:ExcludeFlagged=false`
to show a visible suspicion badge
**so that** I can tell injected/flagged content apart from clean chunks.

## 2. Context

`SPEC-20260923-prompt-injection-guard` stamps `DocumentChunk.SuspicionFlags`
at ingest and `SearchService.HydrateAsync` drops flagged chunks post-rank
when `ExcludeFlagged=true` (default). When an operator disables the flag to
audit results, flagged chunks come back **indistinguishable** from clean
ones — `SearchResultItem` carries no suspicion signal and the playground
shows nothing.

## 3. Functional Requirements

- **RF-001** `SearchResultItem` gains `SecurityFlagged` (bool) and
  `SuspicionFlags` (string?, e.g. `"instruction-override,jailbreak"`).
- **RF-002** `SearchService` populates both when a flagged item is kept
  (only possible when `ExcludeFlagged=false`); clean items serialize
  `securityFlagged:false`, `suspicionFlags:null`.
- **RF-003** `Playground.razor` renders a warning badge (`Color.Warning`,
  icon `fa-triangle-exclamation`, title = flag list) on flagged result
  items; nothing renders for clean items.
- **RF-004** No behavior change to ranking/exclusion — pure surfacing.

## 4. Acceptance Criteria

- [ ] With `ExcludeFlagged=false`, a seeded flagged chunk returns
  `securityFlagged:true` + flags from `GET /api/search` and MCP
  `search_knowledge`.
- [ ] Default `ExcludeFlagged=true` still excludes (regression).
- [ ] Playground shows the badge on flagged items only.
- [ ] Build 0 warnings; unit + integration green; format clean.
