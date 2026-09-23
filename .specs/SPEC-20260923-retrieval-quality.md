# SPEC-20260923-retrieval-quality

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `retrieval-quality` |
| Type | `Feature` (retrieval pipeline: query rewriting + reranking + metadata filters + contract) |
| Stack | `.NET 10 / C# 14` + `Microsoft.Extensions.AI` + EF Core SQLite/pgvector |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-retrieval-quality` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |
| Origin | `gap-analysis-20260923` — GAP-requirements-reranker + GAP-requirements-query-rewriting + GAP-requirements-metadata-filters + GAP-requirements-result-contract-metadata |

## 1. User Story

**As a** user/agent querying the knowledge base
**I want** the query to be optionally rewritten for retrieval, candidates re-ranked after RRF fusion, results filterable by metadata, and every result carrying stable identifiers and provenance
**So that** recall and precision improve measurably and downstream consumers (LLM, UI, citations) can trace results to exact chunks.

**Problem context:**
`SearchService.ExecuteAsync` embeds the raw user query and fuses vector+lexical rankings with RRF — the ranking stops there (`SearchService.cs:51-92`). `SearchResultItem` (`SearchDtos.cs:23-36`) exposes no `ChunkId`, `DocumentId`, `Metadata` or `IndexedAt`, and tools only accept `source`/`mode` filters. SPEC-20260914-hybrid-retrieval §5 explicitly deferred reranking to a future SPEC — this is that SPEC.

## 2. Scope

**In scope:**
- Optional query-rewriting stage before embedding (opt-in via config).
- Pluggable post-RRF rerank stage (`IReranker`) with a default `NoOpReranker` and an `LlmReranker` (uses existing `IChatClient`, scored batch prompt).
- Metadata filters on `search_knowledge`/`ask_knowledge`/REST search: `sourceType`, `pathPrefix`, `indexedAfter`, `language`.
- `SearchResultItem` gains `ChunkId`, `DocumentId`, `Metadata` (`IReadOnlyDictionary<string,string>`), `IndexedAt`.

**Out of scope:**
- Cross-encoder ONNX reranker model (interface allows it later).
- Graph-based retrieval (SPEC-20260923-graphrag).
- Changes to the FTS5/RRF fusion itself.
- Code-aware chunking (SPEC-20260923-code-aware-chunking).

## 3. Technical Context

Query pipeline lives in `src/KnowledgeHub.Server/Services/SearchService.cs`; fusion in `Search/RrfFuser.cs`; hydration joins `Chunks`→`Documents`→`Sources` (:133-163). Tool surface in `Mcp/ToolProviders/KnowledgeToolsProvider.cs`; REST in `Api/SearchEndpoints.cs`/`AskEndpoints.cs`. Answer synthesis consumes `SearchResultItem` in `Services/AnswerService.cs`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Services/SearchService.cs`
- `src/KnowledgeHub.Server/Services/AnswerService.cs`
- `src/KnowledgeHub.Shared/Contracts/SearchDtos.cs`
- `src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs`
- `src/KnowledgeHub.Server/Api/SearchEndpoints.cs`, `AskEndpoints.cs`
- `tests/KnowledgeHub.Tests.Integration/` (MCP contract tests)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Search/IQueryRewriter.cs          (new)
src/KnowledgeHub.Server/Search/LlmQueryRewriter.cs        (new)
src/KnowledgeHub.Server/Search/IReranker.cs               (new)
src/KnowledgeHub.Server/Search/NoOpReranker.cs            (new)
src/KnowledgeHub.Server/Search/LlmReranker.cs             (new)
src/KnowledgeHub.Server/Search/SearchFilter.cs            (new — typed filter model)
src/KnowledgeHub.Server/Services/SearchService.cs         (modify — pipeline stages)
src/KnowledgeHub.Shared/Contracts/SearchDtos.cs           (modify — new fields, backward-compatible)
src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs (modify — new args)
src/KnowledgeHub.Server/Api/SearchEndpoints.cs            (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modify — DI)
tests/KnowledgeHub.Tests.Unit/Search/*                    (new)
tests/KnowledgeHub.Tests.Integration/*                    (modify contract tests)
```

## 4. Requirements

### RF-001 — Query rewriting (opt-in)
- **Description:** When `Search:QueryRewrite:Enabled=true` and a chat provider is configured, the raw query is rewritten by the LLM into a retrieval-oriented form (expand acronyms, normalize terms, strip conversational filler) before embedding/lexical search.
- **Rules:** rewrite result is cached in `IDistributedCache` keyed `rewrite:{model}:{sha256(query)}` (TTL 24h); on provider failure the original query is used (fail-open, logged); rewriting never runs in `mode=lexical` unless configured.
- **Input → Output:** raw query string → rewritten query string.

### RF-002 — Pluggable reranker
- **Description:** After RRF fusion produces `topK` results from a `topK×4` window, an `IReranker` may re-order the window before truncation. `LlmReranker` scores each candidate 0–10 against the query in one batched prompt and re-sorts by `(rerankScore, fusedScore)`.
- **Rules:** enabled via `Search:Rerank:Enabled` (default `false`); rerank input capped (`Search:Rerank:MaxCandidates`, default 20); on failure/timeout the fused order is preserved; rerank scores recorded in `ScoreBreakdown.Rerank`.
- **Input → Output:** `(query, candidateWindow)` → reordered `topK`.

### RF-003 — Metadata filters
- **Description:** Search/tools accept optional filters: `sourceType` (enum), `pathPrefix` (prefix match on `UriReference`), `indexedAfter` (ISO-8601), `language` (from document metadata when present).
- **Rules:** filters apply in the hydration SQL (not post-filter) so `topK` reflects filtered candidates; invalid enum/date → 400/`isError` with a friendly message; filters compose with existing `source`/`mode`.
- **Input → Output:** filter object → filtered ranked list.

### RF-004 — Enriched result contract
- **Description:** `SearchResultItem` gains `ChunkId`, `DocumentId`, `Metadata`, `IndexedAt` — populated from `DocumentChunks`/`KnowledgeDocuments` join.
- **Rules:** all new fields optional/nullable-safe for cached payload compatibility (same approach as `SourceType` in SPEC-20260922 RF-003); no existing field renamed or removed.

### RF-005 — Cache-key versioning
- **Description:** Search-result cache keys include a filter-hash and pipeline version (`search:v2:{mode}:{topK}:{srcHash}:{filterHash}:{qHash}:{indexVersion}`) so cached v1 payloads never collide.

**Business rules / invariants:**
- `mode=semantic` behavior unchanged when rewrite+rerank disabled (byte-identical results to today).
- Every new tool argument is optional — existing clients keep working (contract tests pin this).

## 5. API Contract

**Tools (schema additions only):**
- `search_knowledge(query, topK?, source?, mode?, sourceType?, pathPrefix?, indexedAfter?, language?)`
- `ask_knowledge(question, topK?, source?, mode?, sourceType?, pathPrefix?, indexedAfter?, language?)`

**REST:** `POST /api/search` and `POST /api/ask` accept an optional `filters` object:
```json
{ "query": "...", "topK": 10, "filters": { "sourceType": "ObsidianVault", "pathPrefix": "docs/", "indexedAfter": "2026-01-01", "language": "pt-BR" } }
```
**Expected errors:** `400` invalid filter value · `401` unauthenticated.

## 6. Acceptance Criteria

- [ ] **Given** `Search:Rerank:Enabled=false` **when** searching **then** results equal current fused order (regression test).
- [ ] **Given** a mocked `IReranker` that inverts ranks **when** enabled **then** output order follows rerank scores and `ScoreBreakdown.Rerank` is populated.
- [ ] **Given** `sourceType=WebPage` **when** searching a mixed corpus **then** only WebPage chunks hydrate.
- [ ] **Given** a rewritten query cache hit **when** the same query repeats **then** no second LLM rewrite call occurs.
- [ ] **Given** reranker throws/times out **when** searching **then** fused order is returned (fail-open, warning logged).
- [ ] **Given** old cached `SearchResultItem` JSON **when** deserialized **then** new fields default safely.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Empty filter object | `filters: {}` | Same as no filters |
| Invalid `sourceType` | `"bogus"` | 400 / tool `isError` friendly |
| `indexedAfter` future date | `2999-01-01` | Empty result set (not error) |
| Rewrite disabled + no chat provider | `Enabled=false` | Skipped entirely |

## 7. Task Plan

- [ ] **T1 — Discovery:** read files in §3; confirm DTO backward-compat approach.
- [ ] **T2 — Contract:** extend `SearchResultItem` + `SearchScoreBreakdown.Rerank` + `SearchFilter` model; unit-test deserialization of legacy payloads.
- [ ] **T3 — Filters:** apply `SearchFilter` in `HydrateAsync` SQL + plumb through tools/REST.
- [ ] **T4 — Rewrite:** `IQueryRewriter`+`LlmQueryRewriter`+cache; unit tests with fake `IChatClient`.
- [ ] **T5 — Rerank:** `IReranker`+`NoOpReranker`+`LlmReranker`; pipeline integration; unit tests.
- [ ] **T6 — Verification:** `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; update pinned MCP contract tests.
- [ ] **T7 — Done + PR.**

## 8. Organization Guardrails

- Never commit to `main`/`master`/`develop` — `feature/{AgentLLM}-{YYYYMMDD}-{slug}`.
- Zero breaking change to existing tool schemas (additive args only).
- New LLM calls (rewrite/rerank) are opt-in and off by default — no surprise spend/latency.
- Secrets/PII never in logs; rewrite/rerank prompts log only lengths, not content, at `Information`.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered by tests.
- [ ] `dotnet build` 0 warnings; full `dotnet test` green; `dotnet format` clean.
- [ ] MCP contract tests updated and green.
- [ ] Cache v1→v2 coexistence proven by test.

## Open Questions / Pending Ambiguity

- Reranker batch prompt format (listwise scoring vs. pointwise) — decide at implementation; default pointwise JSON scores for determinism.
