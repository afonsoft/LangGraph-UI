# SPEC — Spans OTel do pipeline RAG (ingestão, search, answer)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-otel-pipeline-spans` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `System.Diagnostics.ActivitySource`, `OpenTelemetry` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Origem | análise de observabilidade 2026-09-25 |

## 1. User Story

**As a** operador olhando um trace de `/api/ask` no Grafana/Jaeger
**I want** spans para rewrite → embed → FTS → vector → RRF → rerank → LLM
**So that** veja onde a latência está sem adivinhar.

## 2. Contexto

OTel está wired (AspNetCore instrumentation + OTLP opt-in) mas cobre só HTTP
e HTTP-out. O pipeline RAG inteiro é um span implícito de "POST /api/ask 4.2s" —
não se sabe se a latência veio do embedding provider, do vector search ou do LLM.
`KnowledgeHubMetrics.MeterName` já é registrado como `ActivitySource` no
`WithTracing`, então basta emitir Activities.

## 3. Requisitos Funcionais

- **RF-001** `RagActivities` (`ActivitySource` = nome do meter existente):
  `ingestion.job` (jobId, sourceId, kind), `ingestion.chunk`, `ingestion.embed`
  (batch size, provider), `search.rewrite`, `search.fts`, `search.vector`
  (provider, topK), `search.rrf`, `search.mmr`, `search.rerank`,
  `search.expand` (contextExpand), `answer.llm` (model, tokens in/out),
  `answer.grade` (corrective, attempt).
- **RF-002** Instrumentar `SearchService.SearchAsync` por braço, `AnswerService`,
  `IngestionWorker`/`IngestionService` — spans filhos sob o request/span MCP
  (Activity.Current herda automaticamente).
- **RF-003** Tags bounded: nunca incluir texto da query/documento — apenas
  contadores (chunks, hits, latências, sizes). Erros → `ActivityStatusCode.Error`.
- **RF-004** `chat.*` spans do `IChatClient`/`Microsoft.Extensions.AI` se a lib
  expõe source — registrar `AddSource` se existir (verificar nome do source em
  runtime via logging de ActivityListener debug).
- **RF-005** Sem OTLP configurado → Activities custam quase nada (listeners off).

## 4. Requisitos Não-Funcionais

- Nenhum payload de conteúdo nas tags (privacidade + cardinalidade).
- Span overhead <1ms por operação (guardado por `ActivitySource.HasListeners`).

## 5. Fora de Escopo

- Baggage propagação cross-process (single process).

## 6. Plano de Tarefas

1. `RagActivities` static + registro no `WithTracing`.
2. Spans no SearchService por braço + AnswerService + ingestion.
3. Grafana/Jaeger smoke doc.

## 7. Acceptance Criteria

- [ ] `/api/ask` produz trace com subspans search.* e answer.llm no OTLP.
- [ ] Falha no braço vector → span error + FTS segue.
- [ ] Sem `Telemetry:Otlp` → zero comportamento visível além de métricas já
  existentes.

## 8. Riscos

- Over-instrumentation polui — apenas boundaries de latência real (I/O, LLM,
  índice).
