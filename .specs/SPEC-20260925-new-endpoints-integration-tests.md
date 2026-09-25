# SPEC — Cobertura de integração para endpoints novos (jobs, reindex, baselines)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-new-endpoints-integration-tests` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `xUnit`, `WebApplicationFactory`, SQLite in-memory |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | GAP-tests-ingestion-eval-endpoints |
| Origem | gap-analysis 2026-09-25 — endpoints sem cobertura de integração |

## 1. User Story

**As a** mantenedor evoluindo a API
**I want** os endpoints novos (ingestion jobs, reindex, eval baselines, sync async) cobertos por testes de integração
**So that** regressões de contrato (o que já aconteceu com o sync async) são pegas no CI, não em produção.

## 2. Contexto

A falha de CI no PR #184 — 25 testes quebrando porque `/sync` virou assíncrono — só foi pega depois do push porque os endpoints novos nasceram sem cobertura: nenhum teste toca `GET /api/ingestion/jobs*`, `POST /cancel`, `POST /reindex`, `GET/POST /api/eval/baselines` nem o contrato `202+jobId` vs `?wait=true`. Unit tests cobrem `IngestionQueue`/`EvalRunner.EvaluateGate`, mas não o pipeline HTTP end-to-end.

## 3. Requisitos Funcionais

### RF-001 — Ingestion jobs
- `POST /api/sources/{id}/sync` (sem wait) → `202` + `jobId`; `GET /api/ingestion/jobs/{id}` retorna status terminal com contadores.
- `POST /api/ingestion/jobs/{id}/cancel` num job queued → `cancelled`; em terminal → `409`.
- `GET /api/ingestion/jobs?sourceId=` filtra por fonte.
- Dedup: dois syncs seguidos → segundo retorna `existing:true` com mesmo jobId.
- `POST /api/sources/{id}/reindex` → job `kind=reindex`.

### RF-002 — Eval baselines
- `POST /api/eval/baselines` promove run; `GET` lista; promover run inexistente → 404; run com `baseline` nomeado auto-compara (`Delta` preenchido).
- Gate: run com `gate` → `gateResult` persistido e retornado em `GET /api/eval/runs/{id}`.

### RF-003 — Sync contract
- `?wait=true` → `SyncResultDto` síncrono (já coberto indiretamente — adicionar assert explícito de shape `DocumentsProcessed` presente).

## 4. Requisitos Não-Funcionais

- Segue o padrão dos testes existentes (`WebApplicationFactory`, SQLite in-memory, `TestAuth`).
- Sem mock do worker — `IngestionWorker` roda no host de teste; asserts toleram `queued|running` até terminal com timeout de poll (~10s).

## 5. Fora de escopo

- Teste de `EvalScheduleService` (timer real — cobertura unitária basta).
- Cancel de job running (corrida instável — só queued cancel é determinístico).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `IngestionJobsApiTests` — enqueue/get/list/cancel/dedup/reindex |
| T2 | `EvalBaselinesTests` — promote/list/404/gate |
| T3 | Assert de contrato em sync `?wait=true` vs default |

## 7. Critérios de aceite

- [ ] CI falharia se `/sync` deixasse de retornar jobId ou `?wait=true` quebrasse.
- [ ] Jobs endpoint coberto nos 3 verbos.
- [ ] Baseline promote + gate persistência verificados via HTTP.

## 8. Riscos

- Job running pode não terminar no tempo do teste → poll com timeout + assert em estado terminal OU verificação de counters intermediários.
