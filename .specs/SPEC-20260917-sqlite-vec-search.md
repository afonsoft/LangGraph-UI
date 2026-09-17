# SPEC-20260917-sqlite-vec-search

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `sqlite-vec-search` |
| Type | `Feature` (performance) |
| Stack | `.NET 10 / EF Core SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260917-sqlite-vec-search` |
| Ticket | — |
| Status | `Draft` |

## 1. User Story

**As a** operador com base de conhecimento crescendo
**I want** busca vetorial nativa via extensão `sqlite-vec` (tabela `vec0`)
**So that** o ranking de chunks escale sem varrer todos os BLOBs em processo.

**Problem context:**
`SqliteVectorStore` hoje faz cosine similarity in-process sobre `DocumentChunks.Embedding` (BLOB) — O(N) por query, carregando todos os vetores do model na memória. Backlog #2 (SPEC-20260913-knowledge-sources §Out of scope): "in-process cosine suffices at MVP scale". Quando a base crescer, a extensão `sqlite-vec` (vec0 virtual table, KNN nativo) é o caminho zero-infra — mesma SQLite, sem pgvector.

## 2. Scope

**In scope:**
- Carregar a extensão `sqlite-vec` na conexão SQLite (bundle `SQLitePCLRaw` ou lib nativa ao lado do executável).
- Virtual table `vec_chunks` (chunk_id PK + `embedding float[N]`) criada por migration/sidecar init — N separado por dimensão configurada.
- `SqliteVecVectorStore : IVectorStore` — `Upsert(Async|Batch)`/`DeleteByDocumentAsync`/`SearchAsync` via KNN nativo, preservando filtro por `EmbeddingModel` e `sourceIds`.
- Seleção: `VectorStore:Provider = "sqlite-vec"` (novo valor; `sqlite` continua default e in-process).
- Fallback/migração: documentar re-embed ou backfill de `DocumentChunks` → `vec_chunks`.

**Out of scope:**
- Trocar o default — `sqlite` (in-process) segue o default; sqlite-vec é opt-in.
- HNSW tuning avançado, quantização, índices auxiliares.
- pgvector changes.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/VectorStore/` + bootstrap da conexão EF Core (carregar extensão via `SqliteConnection.LoadExtension` ou `Microsoft.Data.Sqlite` bundle).

**Files to read before implementing:**
- `VectorStore/IVectorStore.cs`, `SqliteVectorStore.cs`, `PostgresVectorStore.cs` (contrato + filtros)
- `Embeddings/EmbeddingVectorCodec.cs` (serialização float[]↔bytes)
- `Data/KnowledgeHubDbContext.cs` + `DatabaseMigrator` (init da extensão, migration)
- `SPEC-20260913-knowledge-sources.md` RF-006 (contrato do store)

**Files to create/modify:**
- `VectorStore/SqliteVecVectorStore.cs` (novo), DI registration switch, `Program.cs`/factory de conexão (LoadExtension), migration ou DDL de init, `VectorStoreOptions`, docs.

**Risks:** `sqlite-vec` é nativo — RID-specific no publish single-file (mitigação: carregar .so/.dll por RID, falha explícita se ausente); EF Core não gerencia vec0 (mitigação: DDL manual idempotente `CREATE VIRTUAL TABLE IF NOT EXISTS`); vec0 exige dimensão fixa por tabela (mitigação: tabela por `EmbeddingModel`/dimensão ou validação no write path).

## 4. Requirements

### RF-001: Extensão carregável
- **Description:** na abertura da conexão (ou no `SqliteVecVectorStore` init), `vec0` disponível; ausência da lib → erro claro no startup quando `Provider=sqlite-vec`.
- **Input → Output:** `SELECT vec_version()` retorna versão.

### RF-002: Store KNN
- **Description:** `SearchAsync` usa `vec_chunks` KNN (`k = topK`) respeitando `EmbeddingModel` match e `sourceIds` filter; `Upsert(Batch)` escreve na vec table + mantém `DocumentChunks.Embedding` consistente (ou nulo — decidir: single source vec0).
- **Rules:** deletar documento remove os vetores; resultados ordenados por distância.
- **Input → Output:** query vector + filtros → hits ordenados, sem varredura em memória.

### RF-003: Seleção e paridade
- **Description:** `VectorStore:Provider=sqlite-vec` ativa o novo store; mesma semântica de resposta do `sqlite` (paridade de contrato `IVectorStore`).
- **Input → Output:** mesmos testes de contrato passam para ambos os providers.

## 6. Acceptance Criteria

- **CA-001:** Given `Provider=sqlite-vec` com extensão presente, when ingest+search, then KNN retorna resultados equivalentes ao in-process (mesmo top-K, ordem).
- **CA-002:** Given extensão ausente, when startup com `sqlite-vec`, then falha explícita (não silenciosa).
- **CA-003:** Contract tests existentes rodam contra `sqlite-vec` (fixture parametrizada).
- **CA-004:** Docs: provider documentado + nota de native dependency por RID.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Load extension + `vec_version()` probe | integração: probe OK |
| T2 | `SqliteVecVectorStore` + DDL idempotente | unit/integration verde |
| T3 | Provider switch + contract test parametrizado | mesma suíte, 2 providers |
| T4 | Backfill/migração doc + README | doc atualizado |

## 8. Organization Guardrails

- Opt-in apenas — não mudar o default nem quebrar `sqlite`/`postgres`.
- Binário da extensão não commitado (download/RID no publish ou instrução doc).

## 9. Definition of Done

- [ ] `sqlite-vec` provider funcional e com paridade de contrato.
- [ ] Falha explícita sem a extensão.
- [ ] Docs + testes verdes.
