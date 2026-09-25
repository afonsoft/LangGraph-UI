# SPEC — Auto-sync registrado como job na fila de ingestão

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-autosync-through-queue` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `.NET 10`, `VaultWatcherService`, `IngestionQueue` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-operation-autosync-no-job-record |
| Origem | gap-analysis 2026-09-25 — desvio de SPEC-20260924-async-ingestion-queue RF-001/RF-002 |

## 1. User Story

**As a** operador auditando syncs automáticos
**I want** que o sync agendado do VaultWatcher passe pela fila e registre um `IngestionJob`
**So that** `/api/ingestion/jobs` mostra TODO sync (manual ou automático) com contadores e erro — hoje auto-sync é invisível.

## 2. Contexto

`VaultWatcherService` chama `ingestion.SyncAsync(source.Id, ct)` inline (sem job). A SPEC-20260924-async-ingestion-queue RF-001 define `IngestionJob` como o registro auditável de syncs — mas syncs automáticos não geram job, então falhas/contadores de auto-sync não aparecem em `/api/ingestion/jobs` nem no popup de status da fonte. A fila já existe e é o ponto único correto; o watcher é background, então enfileirar não muda UX.

## 3. Requisitos Funcionais

### RF-001 — Enqueue no watcher
- `VaultWatcherService` injeta `IIngestionQueue` e chama `EnqueueAsync(sourceId, "sync")` em vez de `SyncAsync` direto.
- Dedup da fila já cobre "já existe queued/running" — o throttle `_lastFullSync` pode permanecer como defesa extra.
- `QueueFullException` → log warning e segue (não falha o loop do watcher).

### RF-002 — Trigger por mudança de arquivo (se existir no watcher)
- Qualquer outro caminho de sync automático no watcher segue o mesmo padrão.

## 4. Requisitos Não-Funcionais

- Worker já serializa jobs (MaxParallelJobs=1) — auto-sync não pode competir com manual além de enfileirar.
- Job `Kind` pode ganhar prefixo (`"autosync"`) para distinguir origem — opcional, auditar antes.

## 5. Fora de escopo

- Prioridade de fila (manual vs auto).
- SignalR progress do watcher (coberto por SPEC-20260925-job-progress-feed).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Trocar chamada inline por `EnqueueAsync` |
| T2 | Teste: fonte com AutoSync gera IngestionJob done com contadores |

## 7. Critérios de aceite

- [ ] Auto-sync aparece em `GET /api/ingestion/jobs?sourceId=` com status/counters.
- [ ] Dois ticks consecutivos com sync em andamento não duplicam job (dedup existente).
- [ ] Fila cheia não derruba o watcher (log + segue).

## 8. Riscos

- Auto-sync muito frequente enche a fila → dedup por fonte já mitiga; `Ingestion:QueueSize` boundado.
