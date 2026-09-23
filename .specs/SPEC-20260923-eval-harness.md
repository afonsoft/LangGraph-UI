# SPEC-20260923-eval-harness

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `eval-harness` |
| Type | `Feature` (measurement infrastructure) |
| Stack | `.NET 10 / C# 14` + xUnit + existing `ISearchService`/`IAnswerService` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-eval-harness` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` — merged via PR #158 (6b5cdca) |
| Origin | `gap-analysis-20260923` — GAP-tests-eval-harness (alta). Proposal §13.3. |

## 1. User Story

**As a** maintainer of the retrieval pipeline
**I want** a versioned evaluation dataset and a runner that computes Recall@K, Precision@K, MRR and answer-faithfulness metrics
**So that** retrieval changes (rerank, rewrite, chunking, model swaps) are validated with numbers instead of vibes — the proposal's core requirement ("RAG simples, **mensurável**").

**Problem context:**
The repo has zero retrieval-quality measurement: no eval dataset, no metric computation, no regression gate. Every retrieval change so far was validated only by unit/integration correctness, never by quality metrics.

## 2. Scope

**In scope:**
- Versioned eval dataset format (JSON in `tests/eval/` — questions, expected relevant document URIs/chunk markers, expected-answer notes, negative cases).
- `EvalRunner` service + admin REST endpoint `POST /api/eval/run` (operational policy) that executes the dataset against live `ISearchService`/`IAnswerService` and emits a report.
- Metrics: Recall@K, Precision@K, MRR (retrieval); citation-coverage and keyword/LLM-judged faithfulness (answer, opt-in).
- Report persisted to `eval_runs` table + JSON artifact; diff vs. previous run exposed.
- Optional test project `tests/KnowledgeHub.Tests.Eval` runnable on demand (not in default `dotnet test` — dataset-dependent).

**Out of scope:**
- Blocking CI gate (report-only first; gate is a later decision once baselines exist).
- Online/real-user eval; A/B serving.
- LLM-judge provider abstraction beyond existing `IChatClient`.

## 3. Technical Context

`ISearchService.SearchAsync(query, topK, sourceId, mode)` returns `SearchResultItem` with `UriReference` — the eval join key is `UriReference` (+ optional chunk-text marker substring). `IAnswerService.AnswerAsync` returns `AskResponse` with `Citations`. Auth: endpoint behind `AuthPolicies.Operational` like other `/api/*`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Services/SearchService.cs`, `AnswerService.cs`
- `src/KnowledgeHub.Shared/Contracts/SearchDtos.cs`, `AskDtos.cs` (or equivalent ask DTO file)
- `src/KnowledgeHub.Server/Api/SearchEndpoints.cs`, `AskEndpoints.cs`
- `src/KnowledgeHub.Server/Auth/AuthPolicies.cs`
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` (migration for `eval_runs`)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Eval/EvalCase.cs                  (new)
src/KnowledgeHub.Server/Eval/EvalMetrics.cs               (new — Recall@K/P@K/MRR/faithfulness)
src/KnowledgeHub.Server/Eval/EvalRunner.cs                (new)
src/KnowledgeHub.Server/Eval/EvalReport.cs                (new)
src/KnowledgeHub.Server/Api/EvalEndpoints.cs              (new — POST /api/eval/run, GET /api/eval/runs)
src/KnowledgeHub.Server/Domain/Entities/EvalRun.cs        (new)
src/KnowledgeHub.Server/Migrations/*                      (new EF migration)
tests/eval/dataset.sample.json                            (new — versioned sample)
tests/KnowledgeHub.Tests.Unit/Eval/EvalMetricsTests.cs    (new)
tests/KnowledgeHub.Tests.Integration/EvalEndpointsTests.cs(new)
```

## 4. Requirements

### RF-001 — Dataset format
- **Description:** JSON array of cases: `{ id, question, expectedUris: [], expectedTextMarkers: [], mode?, topK?, expectNoAnswer?, tags: [] }`.
- **Rules:** dataset lives in-repo (`tests/eval/`); cases tagged by category (literal, semantic, ambiguous, no-answer, injection); malformed dataset → 400 with per-line errors.
- **Input → Output:** dataset JSON → parsed `EvalCase[]` or validation errors.

### RF-002 — Retrieval metrics
- **Description:** Per case and aggregate: `Recall@K` (fraction of `expectedUris` present in top-K results), `Precision@K`, `MRR` (1/rank of first expected hit). A hit = result `UriReference` equals an `expectedUris` entry **or** chunk text contains all `expectedTextMarkers`.
- **Rules:** `expectNoAnswer` cases score 1.0 when zero results or explicit no-match; skipped from MRR denominators appropriately; metrics rounded to 4 decimals.

### RF-003 — Answer faithfulness (opt-in)
- **Description:** When `Eval:Faithfulness=keyword|llm`, run `ask` per case and score: `keyword` = fraction of expected markers present in answer text; `llm` = judge prompt returns `{supported: bool, claims: n, supportedClaims: n}` → `supportedClaims/claims`.
- **Rules:** `llm` mode requires configured chat provider — otherwise skipped with reason recorded; judge prompt never receives dataset-foreign context.

### RF-004 — Runner + report
- **Description:** `POST /api/eval/run { dataset, topK?, mode?, faithfulness? }` executes all cases, aggregates metrics, stores an `EvalRun` row (id, startedAt, durationMs, datasetHash, metricsJson, perCaseJson), returns the report JSON. `GET /api/eval/runs` lists recent runs; `GET /api/eval/runs/{id}` returns one; `?compare={prevId}` adds per-metric deltas.
- **Rules:** run is sequential and cancellation-aware; each case catches its own error and records `error` instead of aborting the run; dataset hashed (SHA-256) for reproducibility.

### RF-005 — Regression diff
- **Description:** Report includes `delta` vs. a reference run when requested: per-metric absolute change and a `regressions[]` list of cases that went hit→miss.

**Business rules / invariants:**
- Eval never mutates the knowledge index (read-only path: search + ask only).
- Results are reproducible: same dataset + same index version ⇒ same retrieval metrics.

## 5. API Contract

**Endpoint:** `POST /api/eval/run` — **Auth:** Operational (cookie or API key)

**Request:**
```json
{ "dataset": "<json array or dataset name>", "topK": 10, "mode": "hybrid", "faithfulness": "none|keyword|llm", "compareTo": "<runId?>" }
```
**Response (success):**
```json
{ "runId": "...", "cases": 42, "metrics": { "recallAtK": 0.83, "precisionAtK": 0.41, "mrr": 0.72, "faithfulness": null }, "regressions": [], "durationMs": 1234 }
```
**Expected errors:** `400` malformed dataset · `401` unauthenticated · `404` compareTo run missing.

## 6. Acceptance Criteria

- [ ] **Given** a dataset with known expected URIs against a seeded test index **when** `eval/run` executes **then** reported Recall@K/MRR match hand-computed values.
- [ ] **Given** a case whose expected URI is absent from the index **when** scored **then** it counts as a miss and appears in the per-case breakdown.
- [ ] **Given** `faithfulness=llm` without a chat provider **when** running **then** faithfulness is `null` with `skippedReason`, retrieval metrics still reported.
- [ ] **Given** two runs **when** `compareTo` is set **then** deltas and hit→miss regressions are listed.
- [ ] **Given** a malformed dataset **when** posted **then** 400 with line-level errors.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Empty dataset | `[]` | 400 — at least one case required |
| Case times out | provider hang | case recorded as `error`, run continues |
| `expectNoAnswer` with hits | contradictory | scored per rule + flagged `inconsistent` |
| Duplicate expected URIs | `["a","a"]` | deduped before scoring |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; confirm DTO names and DbContext patterns.
- [ ] **T2 — Metrics:** `EvalMetrics` pure functions + unit tests (hand-computed fixtures).
- [ ] **T3 — Dataset:** `EvalCase` parsing + validation + `tests/eval/dataset.sample.json`.
- [ ] **T4 — Runner:** `EvalRunner` (search+ask orchestration, per-case error capture) + `EvalRun` entity + migration.
- [ ] **T5 — Endpoints:** `EvalEndpoints` + integration tests via `WebApplicationFactory`.
- [ ] **T6 — Verification:** build/test/format green; record a sample run against the seeded test corpus.
- [ ] **T7 — Done + PR.**

## 8. Organization Guardrails

- Never commit to `main`/`master`/`develop` — feature branch only.
- Eval endpoint is Operational-gated; dataset content may contain internal terms — never log case text at `Information`, only ids.
- `tests/KnowledgeHub.Tests.Eval` must not run in default `dotnet test` (explicit filter).

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Unit tests for every metric; integration tests for endpoints.
- [ ] Sample dataset committed; one real run recorded as baseline evidence in §10-style append.
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- Dataset initial content — seed with ~15 cases covering existing sources; owners extend per domain.
- CI gating deferred: revisit after ≥3 baseline runs establish variance.
