# AD-0014 — Unified database provider (catalog EF + vector store)

## Context

After AD-0013 the platform ran a split storage topology: the EF Core catalog
(sources, documents, chunks, users, jobs) in SQLite while embeddings lived in
Postgres/pgvector. The UI surfaced two different providers
(`Microsoft.EntityFrameworkCore.Sqlite` vs `postgres`), deployments paid for
two stores, and operators asked for a single backend.

## Decision

`Database:Provider` (`auto`|`postgres`|`sqlite`, default `auto`) resolves once
at DI registration into a `CatalogDatabase` singleton: `auto` picks Postgres
when a connstring resolves (`Database:ConnectionString` or the `POSTGRES_*`
env composition) **and** a 4s probe connects — otherwise SQLite with a logged
fallback reason; `postgres` without a connstring is a startup error; `sqlite`
forces local.

- **Dual migrations** — a `PostgresKnowledgeHubDbContext` subclass owns the
  `Migrations/Postgres` set (`[DbContext]`-annotated per provider, same
  assembly); the model branches on `Database.IsNpgsql()` (`Embedding` →
  `bytea` vs `BLOB`) under a `ProviderAwareModelCacheKeyFactory`.
- **FTS by provider** — SQLite keeps the `chunks_fts` FTS5 virtual table;
  Postgres migration adds a stored `search_vector` `tsvector` column over
  `COALESCE(EnrichedText, TextContent)` + GIN index, queried via
  `websearch_to_tsquery`/`ts_rank` (self-synced — no reconcile).
- **Vector store follows** — an unset/empty `VectorStore:Provider` resolves to
  the catalog provider and reuses its connstring; an explicit divergent value
  still wins but logs a mixed-mode warning.
- **One-shot data copy** — on the first Postgres boot with an empty catalog
  and an existing SQLite file, `SqliteToPostgresMigrator` copies every entity
  parents-first preserving GUID keys (so `kh_embeddings.chunk_id` joins stay
  valid) and leaves a `.migrated` sidecar marker.

## Consequences

- Positive: one database per deployment; operators upgrade by setting
  `DATABASE_PROVIDER=auto` + `POSTGRES_*`; retrieval keeps its lexical leg on
  Postgres instead of degrading; SQLite stays intact as dev/CI/fallback path.
- Trade-off: provider is fixed per boot (no runtime switching); Postgres
  schema evolution now needs a second migration per change
  (`dotnet ef --context PostgresKnowledgeHubDbContext`); `sqlite-vec` remains
  SQLite-only.

## Related SPEC

- [.specs/SPEC-20260926-unified-database-provider.md](../../.specs/SPEC-20260926-unified-database-provider.md)
- Supersedes the split topology of [AD-0013](AD-0013-env-composed-postgres-vector-store.md)
