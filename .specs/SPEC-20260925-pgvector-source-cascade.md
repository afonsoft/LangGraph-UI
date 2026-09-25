# SPEC — pgvector: cascade por source + manutenção (bug de órfãos)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-pgvector-source-cascade` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `pgvector`, `Npgsql`, `MaintenanceBackgroundService` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Origem | análise de cache/vector store 2026-09-25 |

## 1. User Story

**As a** operador que deleta fontes e reindexa documentos
**I want** vetores órfãos removidos do `kh_embeddings` e estatísticas do planner
atualizadas após bulk upserts
**So that** o tamanho da tabela reflita o corpus real e o planner escolha bons planos.

## 2. Contexto — bug confirmado

`KnowledgeSourceService.DeleteAsync` remove `Sources` e o cascade do SQLite
apaga documents/chunks — mas `PostgresVectorStore` só expõe
`DeleteByDocumentAsync`, chamado apenas no fluxo de sync por documento.
Deletar uma fonte deixa TODAS as linhas de `kh_embeddings` dela órfãs (cresce
para sempre; não vaza resultado pois `source_id` filtra… mas `source_id` órfãos
ainda custam storage e diluem o índice HNSW).

Além disso, bulk `UpsertBatchAsync` não roda `ANALYZE` — o planner fica com
estatísticas velhas logo após a maior mudança da tabela.

## 3. Requisitos Funcionais

- **RF-001** `IVectorStore.DeleteBySourceAsync(Guid sourceId)` — impl pgvector:
  `DELETE FROM kh_embeddings WHERE source_id = $1`; impls SQLite análogas.
- **RF-002** `KnowledgeSourceService.DeleteAsync` chama `DeleteBySourceAsync`
  (fail-soft: log + continuar — a fonte some do catálogo de qualquer forma).
- **RF-003** `ANALYZE kh_embeddings` após `UpsertBatchAsync` acima de
  `Options.AnalyzeThresholdRows` (default 500) e `VACUUM ANALYZE` agendado
  semanal via `MaintenanceBackgroundService` (somente provider pgvector;
  `VACUUM` não-concurrent — a tabela é exclusiva da app).
- **RF-004** Endpoint `GET /api/diagnostics/vectorstore` (admin): provider,
  row count, dimensões, HNSW presente?, `pg_size_pretty`, último ANALYZE —
  alimenta painel de diagnóstico.

## 4. Requisitos Não-Funcionais

- `VACUUM` fora do request path — só no maintenance loop.
- `DeleteBySourceAsync` ≤ 1 round-trip.

## 5. Fora de Escopo

- halfvec → `SPEC-20260925-pgvector-halfvec`.
- Iterative scan → `SPEC-20260925-pgvector-iterative-filtered-scan`.

## 6. Plano de Tarefas

1. Estender `IVectorStore` + 3 impls (sqlite in-memory, sqlite-vec, postgres).
2. Wire no DeleteAsync.
3. ANALYZE pós-batch + job semanal.
4. Diagnostics endpoint + testes.

## 7. Acceptance Criteria

- [ ] Deletar fonte com 100 docs → `SELECT count(*) WHERE source_id=...` = 0.
- [ ] Bulk upsert grande dispara ANALYZE.
- [ ] Endpoint de diagnóstico responde provider+contagem+índice.

## 8. Riscos

- `VACUUM` bloqueia writers brevemente — rodar em horário morto, timeout curto.
