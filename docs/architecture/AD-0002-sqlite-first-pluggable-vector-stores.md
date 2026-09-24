# AD-0002 — SQLite-first persistence with pluggable vector stores

## Context
The product must work with zero external dependencies, yet scale when a user
opts into Postgres. Retrieval needs both lexical (FTS5) and vector search.

## Decision
SQLite (`knowledgehub.db` beside the executable, EF Core migrations at startup)
owns the catalog, documents, chunks (FTS5), settings, API keys, security
events, eval runs and the knowledge graph. Vectors go through `IVectorStore`:
`sqlite` (chunk columns, in-process cosine) · `sqlite-vec` (native KNN) ·
`postgres`/pgvector (HNSW index past `HnswThreshold`, batched multi-row
upserts, `metadata jsonb` provenance). Embeddings are stamped with
`{provider}:{model}` so a model switch never mixes stale vectors.

## Consequences
- Positive: backup = one file + uploads; per-RID self-contained binary works.
- Trade-off: SQLite write concurrency is bounded (single writer) — acceptable
  for a single-tenant admin tool; pgvector path exists when throughput demands
  it.

## Related SPEC
- [.specs/SPEC-20260923-pgvector-hnsw-scale.md](../../.specs/SPEC-20260923-pgvector-hnsw-scale.md)
- [.specs/SPEC-20260923-pgvector-metadata-upsert.md](../../.specs/SPEC-20260923-pgvector-metadata-upsert.md)
