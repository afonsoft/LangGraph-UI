# SPEC-20260923-pgvector-metadata-upsert — Write metadata jsonb on upsert

| Field | Value |
|-------|-------|
| Date | `2026-09-23` |
| Author | `Devin` |
| Type | `Feature` (small — follow-up from SPEC-20260923-pgvector-hnsw-scale) |
| Stack | `.NET 10` + Npgsql/pgvector |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-pgvector-metadata` |
| Status | `In implementation` — approved by owner 2026-09-23 ("Implementar deferrals") |
| Origin | `SPEC-20260923-pgvector-hnsw-scale` RF-003 — `metadata jsonb` column exists but upsert never writes it ("extended `VectorUpsert` or a follow-up `UpdateMetadataAsync`"). |

## 1. User Story

**As a** platform operator on the pgvector path
**I want** upserts to persist `{"sourceType","indexedAt"}` into `metadata`
**so that** the column is real data — enabling future server-side filter
push-down instead of today's dead `'{}'` default.

## 2. Context

`PostgresVectorStore.EnsureInitializedAsync` creates `metadata jsonb NOT NULL
DEFAULT '{}'` but `VectorUpsert` carries only ids + vector, so every row
keeps `'{}'`. Retrieval-quality filters work today via `SearchService`
post-rank (store-agnostic) — this SPEC only makes the pgvector column
useful, unchanged externally.

## 3. Functional Requirements

- **RF-001** `VectorUpsert` gains optional `Metadata`
  (`IReadOnlyDictionary<string,string>?`, default null).
- **RF-002** `PostgresVectorStore.UpsertBatchAsync` serializes the map to
  the `metadata` jsonb column (empty map/`null` → `'{}'`). Other stores
  ignore the field (SQLite has real columns).
- **RF-003** `IngestionService` passes
  `{"sourceType": source.SourceType, "indexedAt": chunk.IndexedAt:o}` on
  upsert.
- **RF-004** `SearchAsync` still ignores metadata (no behavior change —
  push-down is a future SPEC).

## 4. Acceptance Criteria

- [ ] Unit: serialization helper produces valid jsonb object; null → `{}`.
- [ ] Gated integration (pgvector present): upserted rows carry the map.
- [ ] SQLite paths unaffected; build 0 warnings; suites green; format clean.
