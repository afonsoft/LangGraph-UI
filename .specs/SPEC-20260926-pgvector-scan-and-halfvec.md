# SPEC — pgvector: GUC correto do iterative scan + ordem de migração halfvec + halfvec em banco novo

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-pgvector-scan-and-halfvec` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (PostgresVectorStore, PostgresOptions) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Ticket | devin-ai-integration review — PR #190 (🔍 SPEC-time), #197 (🔴), #198 (2×🔴) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — cluster E (E1–E3) |

## 1. User Story

**As a** operador com pgvector que habilita iterative filtered scan ou halfvec
**I want** que as opções opt-in realmente ativem
**So that** buscas filtradas por fonte tenham recall completo e a economia de storage funcione em bases novas e existentes.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| E1 | `SET LOCAL pgvector.iterative_scan = relaxed_order` — o GUC do pgvector ≥0.8 é **`hnsw.iterative_scan`**. PostgreSQL aceita o nome desconhecido sem erro → a feature opt-in nunca ativa (o `PostgresException` catch nem é exercido). O review já propôs a linha correta | `PostgresVectorStore.cs:213` (e o vizinho `hnsw.max_scan_tuples` usa o prefixo certo — inconsistência interna) |
| E2 | Migração vector→halfvec faz `ALTER COLUMN TYPE` **antes** de `DROP INDEX` do HNSW — o índice existente usa `vector_cosine_ops`, o ALTER falha tentando reconstruí-lo para o novo tipo → migração aborta e busca/ingestão param | `PostgresVectorStore.cs:400-415` (ALTER → depois DROP) |
| E3 | Em banco novo, `ResolveStorageTypeAsync` roda **antes** de `CREATE EXTENSION IF NOT EXISTS vector` — `extversion` não existe → resolve `vector` mesmo com `StorageType=halfvec`; sem `AllowStorageMigration`, fica vector para sempre (opt-in silenciosamente ignorado) | `PostgresVectorStore.cs:299-330` (ordem: resolve storage → DDL com CREATE EXTENSION) |

## 3. Requisitos Funcionais

### RF-001 — GUC correto do iterative scan

- `SET LOCAL hnsw.iterative_scan = relaxed_order` (não `pgvector.iterative_scan`).
- Manter o `PostgresException` swallow para pgvector <0.8 — mas agora também validar: em pgvector ≥0.8 o comando deve ser aceito de fato (teste de integração ou validação por `SHOW` após SET quando possível).

### RF-002 — Migração halfvec com índice derrubado antes

- Ordem correta: `DROP INDEX IF EXISTS kh_embeddings_hnsw` → `ALTER COLUMN embedding TYPE halfvec` → recria HNSW com `halfvec_cosine_ops` pelo threshold logic existente.
- `_hnswIndexCreated=false` setado antes do ALTER para garantir recriação.

### RF-003 — halfvec honrado em banco novo

- `CREATE EXTENSION IF NOT EXISTS vector` executa **antes** de `ResolveStorageTypeAsync` — bases novas com `StorageType=halfvec` + pgvector ≥0.7 + dims ≤2000 nascem com coluna halfvec.
- Bases já criadas em `vector` sem `AllowStorageMigration` permanecem vector (mismatch tolerado — comportamento atual preservado e logado uma vez).

## 4. RNFs

- Opt-in preservado: sem `IterativeScan`/`StorageType=halfvec`/`AllowStorageMigration`, zero mudança de comportamento.
- Sem migração EF nova (DDL gerenciado pelo store, como hoje).

## 5. Fora de Escopo

- Suporte a `hnsw.ef_search`/outros GUCs novos.
- Conversão halfvec→vector (caminho reverso) — fora desta SPEC.

## 6. Plano de Tarefas

1. Trocar GUC para `hnsw.iterative_scan` (+ teste unit do command text / integração com pg real se disponível).
2. Reordenar migração: DROP INDEX → ALTER → flag de recriação.
3. `CREATE EXTENSION` antecipado antes de `ResolveStorageTypeAsync`.
4. Testes: comando emitido com o nome certo; migração com índice existente completa; fresh DB nasce halfvec quando opt-in.
5. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] `IterativeScan=true` emite `SET LOCAL hnsw.iterative_scan = relaxed_order` (verificável no comando gerado).
- [ ] Migração vector→halfvec com HNSW existente conclui: índice dropado, coluna alterada, índice recriado com ops correto.
- [ ] Banco novo com `StorageType=halfvec` (+pgvector≥0.7, dims≤2000) cria coluna `halfvec` na primeira inicialização.
