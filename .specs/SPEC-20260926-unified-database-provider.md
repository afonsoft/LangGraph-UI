# SPEC — Provider de banco unificado: catálogo EF + vector store no mesmo backend

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-unified-database-provider` |
| Data | 2026-09-26 |
| Autor | Devin |
| Tipo | `Infra` |
| Stack | `.NET 10`, `EF Core 10`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Sqlite` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` — entregue via PR #242 |
| Ticket | `#240` — https://github.com/afonsoft/LangGraph-UI/issues/240 |
| Origem | pedido do usuário — "quero que seja o postgre para tudo; se indisponível ou não configurado, sqlite; os dois Providers unificados" |

## 1. User Story

**As a** operador do KnowledgeHub
**I want** um único backend de dados — Postgres quando configurado, SQLite quando não — servindo catálogo EF e vector store
**So that** não existe estado dividido (catálogo em SQLite + vetores em Postgres) e o deployment usa um banco só.

## 2. Contexto

AS-IS: split arquitetural — catálogo EF Core (sources, documents, chunks, users, jobs…) em SQLite (`UseSqlite` + `Database:Path`, ~24 migrations, FTS5 via `chunks_fts`), vector store separado em Postgres/pgvector via `POSTGRES_*` + `kh_embeddings`. A UI mostra `Provider: Microsoft.EntityFrameworkCore.Sqlite` para o catálogo e `postgres` para o vector store — dois bancos para uma só plataforma.

TO-BE: `Database:Provider` (`postgres`|`sqlite`, default `auto`) governa ambos. Com Postgres configurado e alcançável → catálogo EF via Npgsql + vector store pgvector na mesma base. Sem Postgres (não configurado ou inalcançável no startup) → SQLite catalog + sqlite(-vec) vector store, com warning visível.

## 3. Requisitos Funcionais

### RF-001 — Seleção de provider
- `Database:Provider`: `postgres` | `sqlite` | `auto` (default `auto`).
- `auto` → postgres quando `Database:ConnectionString` **ou** `POSTGRES_*` (mesmas envs do vector store hoje) configurados **e** o banco responde a um probe de conexão no startup; senão sqlite.
- `postgres` explícito sem connstring válida → falha de configuração no startup (não cai em sqlite silenciosamente — só `auto` faz fallback).
- Provider efetivo exposto em `/api/settings/database` (`provider` já mostra o EF provider; campo `host`/`database` do PR #238 já reporta `conn.DataSource`/`conn.Database`) e no log de startup.

### RF-002 — Catálogo EF Core em Postgres
- `Npgsql.EntityFrameworkCore.PostgreSQL` no `KnowledgeHub.Server`.
- `KnowledgeHubDbContext` passa a configurar colunas provider-conditional: `Embedding` → `bytea` em Npgsql (hoje `BLOB`), demais tipos portáveis.
- **Dual migrations**: `Migrations/Sqlite` (existentes) + `Migrations/Postgres` (novo set gerado via `dotnet ef -- --provider postgres`). `DatabaseMigrator` escolhe o set pelo provider ativo.
- Probe + fallback: falha de conexão no `auto` → `Error` no log + provider efetivo `sqlite`.

### RF-003 — Full-text search por provider
- SQLite: `chunks_fts` FTS5 atual (inalterado).
- Postgres: `tsvector`/`tsquery` — coluna gerada `search_vector` + índice GIN, `plainto_tsquery`/`websearch_to_tsquery` na lexical search; `LexicalSearchService` recebe caminho provider-conditional (mantém RRF igual — só muda a fonte do score lexical).
- Ranking: `ts_rank` normalizado para a perna lexical do RRF existente.

### RF-004 — Vector store unificado
- `Database:Provider` efetivo `postgres` → vector store `PostgresVectorStore` apontando para o mesmo connstring/db (`kh_embeddings` já é o schema).
- `sqlite` → `sqlite`/`sqlite-vec` atuais.
- `VectorStore:Provider` explícito continua aceito como override, mas **warn** quando diverge do provider efetivo (modo misto é configuração avançada, não default).
- `POSTGRES_*`/`Database:ConnectionString` alimentam ambos.

### RF-005 — Migração de dados one-shot
- Na primeira inicialização em postgres com catálogo sqlite existente e não-vazio: copiar sources, documents, chunks (incl. `Embedding`), users, api keys, jobs, settings — preservando IDs — para Postgres; marcar sqlite como migrado (flag em tabela de meta ou arquivo `.migrated` ao lado do .db).
- `kh_embeddings` já contém os vetores migrados (2.136) — não duplicar; se o chunk já tem vetor no Postgres, skip.
- Falha na migração → log Error + fallback sqlite (não deixa metade migrado — transação por entidade).

### RF-006 — sqlite-vec / sqlite inalterados como fallback
- O caminho sqlite inteiro permanece funcional — é o default de dev/CI/testes e o fallback de produção.

## 4. Requisitos Não-Funcionais

- Zero downtime do path sqlite: `dotnet test` completo continua rodando em sqlite.
- Probe de Postgres no startup < 5s timeout (não segurar boot).
- Dual migrations geradas, não escritas à mão.
- Sem secrets em logs/connstrings exibidas.

## 5. Restrições / Não-fazer

- Não remover o caminho sqlite — é fallback e ambiente de teste.
- Não migrar `kh_embeddings` de volta para sqlite (Postgres só).
- Não mexer em `SqliteVecVectorStore`.
- Sem runtime switching — provider é fixo por boot.

## 6. Critérios de Aceite

- `Database:Provider=auto` + Postgres up → EF catalog em `rag_db`, `Provider` na UI = `Npgsql…PostgreSQL`, vector store `postgres` na mesma base, dados do sqlite migrados.
- `auto` + Postgres down/ausente → sqlite, warning no log, app funcional.
- `postgres` explícito sem connstring → erro de configuração no startup.
- `dotnet test` verde (sqlite path); live pgvector tests verdes (docker).
- Migrations Postgres aplicam limpo num banco vazio.

## 7. Definição de Pronto

- [ ] Dual migrations geradas + aplicadas em PG18 real
- [ ] tsvector/GIN lexical path no postgres
- [ ] One-shot migration sqlite→postgres verificada (846 docs / 2136 chunks)
- [ ] Provider efetivo visível na UI/diagnostics
- [ ] Fallback auto→sqlite testado (postgres derrubado)
- [ ] docs EN+PT + ADR + compose `.env` atualizados
