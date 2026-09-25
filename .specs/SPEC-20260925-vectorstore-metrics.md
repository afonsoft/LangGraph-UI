# SPEC — Métricas de vector store e health de providers

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-vectorstore-metrics` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `System.Diagnostics.Metrics`, `IVectorStore`, health checks |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise de vector store 2026-09-25 |

## 1. User Story

**As a** operador observando latência de busca
**I want** métricas por provider de vector store (sqlite-vec | pgvector) —
duração de upsert/search, contagem de erros, e um health check
**So that** veja regressão de latência do índice e indisponibilidade do Postgres
antes dos usuários.

## 2. Contexto

`KnowledgeHubMetrics` cobre cache e requests MCP; o vector store é caixa-preta:
não se sabe a latência p95 de `SearchAsync`, falhas de conexão do Postgres são
invisíveis até uma query estourar, e `/health/ready` testa só SQLite.

## 3. Requisitos Funcionais

- **RF-001** Instrumentos: `vector_search_duration` (histogram, tags
  provider/filtered), `vector_upsert_duration`, `vector_errors` (counter),
  `vector_rows` (observable gauge, amostral).
- **RF-002** Decorator `InstrumentedVectorStore : IVectorStore` em volta das
  impls — métricas fora dos stores concretos.
- **RF-003** `VectorStoreHealthCheck` tag `ready`: provider pgvector →
  `SELECT 1` + `SELECT to_regclass('kh_embeddings')`; sqlite-vec → query
  trivial. `Degraded` em falha (vector down ≠ app down — search degrada para
  FTS-only já que o pipeline é híbrido).
- **RF-004** Quando pgvector falha, `SearchService` já cai para FTS? Se não,
  garantir fallback FTS com warning (fail-soft do braço vetorial).

## 4. Requisitos Não-Funcionais

- Zero overhead quando exporters ausentes (metrics são no-op).
- Health check timeout 2s.

## 5. Plano de Tarefas

1. Decorator + DI.
2. Health check.
3. Fallback FTS no SearchService quando o braço vetorial lança.
4. Testes + doc.

## 6. Acceptance Criteria

- [ ] Postgres morto → ready degraded + search continua via FTS.
- [ ] p95 de vector search visível no /metrics OTLP.

## 7. Riscos

- `vector_rows` por COUNT(*) pode ser caro — amostrar a cada 5min via
  observable gauge com cache.
