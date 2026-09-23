# SPEC-20260923-pgvector-hnsw-scale

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `pgvector-hnsw-scale` |
| Type | `Improvement` (vector-store scale/perf) |
| Stack | `.NET 10 / C# 14` + Npgsql + Pgvector + pgvector ≥0.5 (HNSW) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-pgvector-hnsw` |
| Ticket | `[A DEFINIR]` |
| Status | `Draft` |
| Origin | `gap-analysis-20260923` — GAP-implementation-pgvector-hnsw (média). Proposal §7.4. |

## 1. User Story

**As a** platform operator running `VectorStore:Provider=postgres`
**I want** an HNSW index on the embedding column, batched upserts and filterable metadata in `kh_embeddings`
**So that** vector search stays sub-linear as the corpus grows and ingestion does not issue one INSERT round-trip per chunk.

**Problem context:**
`PostgresVectorStore.EnsureInitializedAsync` (`PostgresVectorStore.cs:99-109`) creates only a btree on `model` — every `SearchAsync` is a sequential scan over `vector(384)` rows (exact `<=>` distance). It also does **not** override `IVectorStore.UpsertBatchAsync`, so ingestion falls back to the interface default: one `INSERT … ON CONFLICT` per chunk (N round-trips). The proposal explicitly recommends HNSW (`§7.4`) and metadata `jsonb` filtering (`§7.2`).

## 2. Scope

**In scope:**
- HNSW index creation on `kh_embeddings.embedding` (`vector_cosine_ops`), created lazily once row count crosses a configurable threshold (`VectorStore:Postgres:HnswThreshold`, default 10k) — small tables keep exact scan (better recall, no index cost).
- `UpsertBatchAsync` override: single round-trip `INSERT … SELECT … FROM unnest($1,$2,$3,$4,$5)` or batched `COPY`, `ON CONFLICT (chunk_id) DO UPDATE`.
- `metadata jsonb` column on `kh_embeddings` (source_type, doc title hash, indexed_at) so SQL-side pre-filtering works when SPEC-20260923-retrieval-quality filters land.
- Migration/self-heal: `EnsureInitializedAsync` adds column/index idempotently on existing deployments.
- Benchmark evidence: before/after `EXPLAIN ANALYZE` + latency on a ≥50k-row synthetic corpus (documented in SPEC §10-style evidence block).

**Out of scope:**
- IVFFlat alternative (documented tradeoff only).
- Switching catalog/documents to Postgres (embeddings-only store stays the design).
- Full Postgres migration of the app DB.

## 3. Technical Context

`PostgresVectorStore` owns its own `NpgsqlDataSource` and DDL. `IVectorStore` contract already supports batch via default method (`IVectorStore.cs:23-28`). Dimension guard: `EmbeddingCompatibilityCheck` + `VectorStore:Dimensions` config; HNSW index is per-dimension — index creation must use the configured dimension at init.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs`
- `src/KnowledgeHub.Server/VectorStore/IVectorStore.cs`
- `src/KnowledgeHub.Server/VectorStore/SqliteVecVectorStore.cs` (batch/txn pattern reference)
- `src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs`
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` (:122-137 provider wiring)
- `tests/KnowledgeHub.Tests.Integration/` (existing postgres tests, if any — else unit-level SQL tests)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs     (modify)
src/KnowledgeHub.Server/VectorStore/PostgresOptions.cs         (new — HnswThreshold, batch size)
src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs(modify — validate pg section)
tests/KnowledgeHub.Tests.Unit/VectorStore/PostgresVectorStoreTests.cs (new/extend)
tests/KnowledgeHub.Tests.Integration/PostgresVectorStoreTests.cs    (new — gated on env conn string)
```

## 4. Requirements

### RF-001 — Conditional HNSW index
- **Description:** `EnsureInitializedAsync` counts rows; when `count >= HnswThreshold` and no HNSW index exists, runs `CREATE INDEX CONCURRENTLY`-equivalent (regular `CREATE INDEX IF NOT EXISTS … USING hnsw (embedding vector_cosine_ops)` acceptable — document lock behavior; CONCURRENTLY cannot run in transaction).
- **Rules:** index creation is once-per-process guarded (`_initGate` pattern already exists) and logged; failure to create the index logs a warning and keeps exact search working; `m`/`ef_construction` configurable with pgvector defaults.

### RF-002 — Batched upsert
- **Description:** `UpsertBatchAsync` overrides the default: one command using `unnest` of parallel arrays (chunk_id uuid[], document_id uuid[], source_id uuid[], model text, embedding vector[]) with `ON CONFLICT (chunk_id) DO UPDATE`; chunk count > `BatchMax` (default 500) splits into multiple commands inside one connection/transaction.
- **Rules:** transactional per batch; dimension check per vector before send; fallback to per-item upsert on batch failure (same contract as `IngestionService.EmbedChunksIndividuallyAsync`).

### RF-003 — Metadata column
- **Description:** `kh_embeddings` gains `metadata jsonb NOT NULL DEFAULT '{}'`; upsert writes `{"sourceType":…,"indexedAt":…}` (passed via extended `VectorUpsert` or a follow-up `UpdateMetadataAsync`).
- **Rules:** additive — existing rows default to `{}`; `SearchAsync` ignores it until the filters SPEC consumes it (no behavior change).

### RF-004 — Evidence
- **Description:** SPEC update §10-style: `EXPLAIN (ANALYZE)` plan proof of index usage + p50/p95 latency before/after on synthetic ≥50k corpus; environment noted (docker `pgvector/pgvector:pg16`).

**Business rules / invariants:**
- Below `HnswThreshold` behavior is byte-identical to today (exact scan).
- All DDL idempotent (`IF NOT EXISTS`, guarded by gate) — safe on boot races.
- Dimension mismatch keeps the existing guard semantics — never create an index for the wrong dim.

## 5. API Contract

None (internal store). Config surface:
```json
"VectorStore": { "Provider": "postgres", "ConnectionString": "…", "Dimensions": 384,
                 "Postgres": { "HnswThreshold": 10000, "HnswM": 16, "HnswEfConstruction": 64, "BatchMax": 500 } }
```

## 6. Acceptance Criteria

- [ ] **Given** a corpus below threshold **when** searching **then** plan is a seq scan (verified via `EXPLAIN` in integration test or documented evidence).
- [ ] **Given** corpus ≥ threshold **when** init runs **then** `kh_embeddings_embedding_hnsw_idx` exists (`pg_indexes`) and queries use it.
- [ ] **Given** a batch of 1200 upserts **when** `UpsertBatchAsync` runs **then** ≤ ⌈1200/BatchMax⌉ commands execute (command log assertion).
- [ ] **Given** batch command failure **when** upserting **then** per-item fallback preserves the items (same contract as sqlite paths).
- [ ] **Given** an existing deployment DB **when** app boots **then** `metadata` column appears without data loss.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| pgvector <0.5 (no hnsw) | old extension | warning + exact scan continues |
| Two app instances racing init | concurrent DDL | `IF NOT EXISTS` + gate; one wins, other no-ops |
| Empty batch | `[]` | no-op, zero commands |
| Duplicate chunk_id in one batch | bad input | `ON CONFLICT` last-wins within batch |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3; check existing pg integration test infrastructure.
- [ ] **T2 — Options + DDL:** metadata column + threshold check + HNSW create.
- [ ] **T3 — Batch upsert:** unnest implementation + fallback.
- [ ] **T4 — Tests:** unit (SQL shape, fallback, dimension guard) + integration (docker pgvector, index existence).
- [ ] **T5 — Benchmark evidence:** 50k-row corpus before/after; append to SPEC.
- [ ] **T6 — Verification:** build/test/format green. Done + PR.

## 8. Organization Guardrails

- No change to default `sqlite` provider — postgres stays opt-in.
- Index parameters conservative defaults (m=16, ef_construction=64).
- Raw DDL only through `EnsureInitializedAsync` — no EF migration (this DB is owned by the store, not the EF context).

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Benchmark evidence appended (query plan + latency numbers).
- [ ] Integration test for index creation gated on available conn string (skip when absent).
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- `ef_search` runtime tuning — expose later if p95 demands it.
