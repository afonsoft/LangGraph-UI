# SPEC — Fila de ingestão assíncrona + versioning de estratégia de chunking

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-async-ingestion-queue` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `System.Threading.Channels`, BackgroundService, SignalR (`McpMonitorHub`), EF Core |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-async-ingestion` |
| Status | `Done` |
| Ticket | — |
| Origem | beerandcode (ingestion não roda no request — fila + job; custo de re-embed em mudança de chunker → versionar estratégia); rag-data-platform (pipeline upload→extract→embed→index desacoplado); milvus (bulk ingestion) |

## 1. User Story

**As a** operador sincronizando uma fonte grande (vault com milhares de notas, site com centenas de páginas)
**I want** que o sync rode em background com progresso em tempo real, retry por documento e reindex seletivo
**So that** a API não fica presa em requests longos, falhas parciais não perdem o trabalho todo, e mudança de estratégia de chunking não força re-embed do corpus inteiro.

## 2. Contexto

`POST /api/sources/{id}/sync` executa `IngestionService.SyncAsync` inline — request HTTP longo que expira em proxies, sem retry por documento, sem progresso granular, e qualquer mudança de chunker/contextual-enrichment obriga resync completo com re-embed total (custo × corpus).

Dois eixos:
- **Fila:** `Channel<IngestionJob>` + `IngestionWorker` (BackgroundService) — endpoint enfileira e retorna `202 + jobId`; worker processa com progresso via SignalR; falha por documento é registrada e não aborta o job.
- **Versioning:** `Documents` ganha `ChunkerVersion` (int) + `ChunkerConfigHash`; reindex compara com a versão corrente — só re-chunka/re-embeda docs cuja estratégia mudou ou conteúdo mudou (content-hash já existente).

## 3. Requisitos Funcionais

### RF-001 — Fila e job tracking
- Entidade `IngestionJob` (Id, SourceId, Status queued|running|done|failed|cancelled, counters docs/chunks, Error, StartedAt/FinishedAt) — EF migration.
- `POST /api/sources/{id}/sync` → `202 Accepted` + `{ jobId }` (compat: `?wait=true` mantém modo síncrono para clients legados).
- `GET /api/ingestion/jobs/{id}` + `GET /api/ingestion/jobs?sourceId=` (lista) — auth `Operational`.
- Dedup: sync já `queued|running` para a mesma fonte → retorna o job existente (`409`→`200` com job corrente — decidir: `200` com `existing:true`).

### RF-002 — Worker
- `IngestionWorker` consome o canal sequencialmente (default) com `Ingestion:MaxParallelJobs` (default 1 — SQLite write lock).
- Por documento: try/catch isolado — doc falho incrementa `Job.FailedDocs` com erro; job continua. Falha fatal (fonte inacessível) → job `failed` com erro.
- Progresso: eventos SignalR no hub existente (`ingestion.progress` com jobId, processed/total) + atualização periódica do job no DB (a cada N docs, `Ingestion:ProgressFlushEvery`, default 25).
- Cancelamento: `POST /api/ingestion/jobs/{id}/cancel` → CancellationToken por job.

### RF-003 — Chunker versioning + reindex seletivo
- `ChunkerSelector` expõe `CurrentVersion` (bump manual ao mudar qualquer chunker) + `ConfigHash` (maxTokens/overlap/enrichment).
- Doc com `(ChunkerVersion, ConfigHash, ContentHash)` iguais ao corrente → skip total (nem re-chunk). ContentHash igual mas version diferente → re-chunk + re-embed só daquele doc.
- `POST /api/sources/{id}/reindex` (novo) enfileira job `kind=reindex` que força re-chunk independente de content-hash.

### RF-004 — UI
- Página Sources: botão Sync vira "enfileirar"; badge de job em curso com progresso; toast ao concluir (SignalR já existe).

## 4. Requisitos Não-Funcionais

- Restart do processo: jobs `running` órfãos → marcados `failed` no startup com erro "interrupted by restart" (recuperação honesta, não retoma — doc-level idempotente permite re-sync seguro).
- Jobs são auditáveis (persistidos) — não só em memória.
- Backpressure: canal bounded (`Ingestion:QueueSize`, default 100) → `429`/`503` com ProblemDetails quando cheio.

## 5. Fora de escopo

- Fila distribuída (Redis/RabbitMQ) — single-process é o deployment atual; a abstração `IIngestionQueue` permite evoluir.
- Resumo automático do job via LLM.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Entidade `IngestionJob` + migration + endpoints (enqueue/get/list/cancel) |
| T2 | `Channel` + `IngestionWorker` + isolamento por doc + cancel |
| T3 | SignalR progress + persistência periódica |
| T4 | `ChunkerVersion`/`ConfigHash`/`ContentHash` no caminho de sync + reindex seletivo |
| T5 | UI: estado de job + progresso |
| T6 | Startup sweep de jobs órfãos |
| T7 | Testes: enqueue retorna 202; falha em 1 doc não aborta job; cancel interrompe; reindex seletivo pula docs inalterados; órfão marcado failed |

## 7. Critérios de aceite

- [ ] Sync de fonte grande retorna `202` em <1s e completa em background com progresso visível.
- [ ] Doc corrompido falha isolado; job termina `done` com `FailedDocs=1` e erro registrado.
- [ ] Segunda sync sem mudança de conteúdo/versão processa 0 embeddings.
- [ ] Mudança de `maxTokens` re-chunka só docs existentes (re-embed seletivo), não re-fetch.
- [ ] Restart no meio do job → job marcado failed na subida seguinte.
- [ ] Suite verde; `?wait=true` preserva contrato atual para integrações.

## 8. Riscos

- Clients existentes dependem do sync síncrono → `?wait=true` + deprecação documentada.
- Dois workers futuros no SQLite → manter `MaxParallelJobs=1` até Postgres ser o deployment; fila já serializa.
