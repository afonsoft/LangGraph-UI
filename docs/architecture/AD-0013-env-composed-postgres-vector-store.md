# AD-0013 — Env-composed external Postgres vector store

## Context

`VectorStore:Provider=postgres` existed since the pgvector backend landed (AD-0002), but pointing it at a real server required hand-writing a full Npgsql connection string in `appsettings`/env — error-prone, all-or-nothing, and unfriendly to the Docker topology where Postgres typically runs on the **host** (aaPanel/systemd) while the app runs in a container. Compose also ignored `.env` files entirely, so operators had to duplicate vars.

## Decision

Adopt the proxyLLM-AI pattern: `docker-compose.yml` loads `.env` via `env_file` (`required: false`, so clean checkouts still deploy) and composes `VectorStore__ConnectionString` from discrete `POSTGRES_HOST`/`PORT`/`DB`/`USER`/`PASSWORD` vars; an explicit `VECTORSTORE_CONNECTIONSTRING` overrides the composition when set. `host.docker.internal` (compose `extra_hosts` → host-gateway) reaches host Postgres — whose `pg_hba.conf` must allow the **container subnet** for the target db/user, and whose `vector` extension must exist (`PostgresVectorStore` issues `CREATE EXTENSION IF NOT EXISTS vector`, a no-op when pre-created by a superuser or runnable when the app user holds `CREATE` on the database). `install.sh` mirrors the same composition for the `docker run` path. The catalog/documents stay in SQLite; only embeddings live in `rag_db`.

## Consequences

- Positive: operators set five obvious vars instead of a connection string; secrets stay in the gitignored `.env`; host Postgres is reachable without publishing it on LAN; upgrade path is `CREATE EXTENSION` + flip `VECTORSTORE_PROVIDER`.
- Trade-off: pgvector must be provisioned on the host server (package matching the server major, or source build); `pg_hba.conf` needs a per-subnet host rule per compose network; the SQLite→pgvector embedding migration is an operational step (done via TSV load, `chunk_id` as key).

## Related SPEC

- [.specs/SPEC-20260923-pgvector-hnsw-scale.md](../../.specs/SPEC-20260923-pgvector-hnsw-scale.md)
- [.specs/SPEC-20260924-pgvector-rag-performance.md](../../.specs/SPEC-20260924-pgvector-rag-performance.md)
- [.specs/SPEC-20260925-pgvector-halfvec.md](../../.specs/SPEC-20260925-pgvector-halfvec.md)
- Implementation: PR #227 (`env_file` + `POSTGRES_*` composition + host pgvector provisioning)
