# SPEC — Eventos de progresso de ingestão no feed em tempo real

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-job-progress-feed` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `.NET 10`, `SignalR`/activity feed, Blazor WASM |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-implementation-signalr-job-progress |
| Origem | gap-analysis 2026-09-25 — desvio de SPEC-20260924-async-ingestion-queue RF-002 ("progresso via SignalR") |

## 1. User Story

**As a** operador acompanhando um sync grande
**I want** progresso em tempo real (push) em vez de polling a cada 2s/5s
**So that** a UI reflete o andamento imediatamente e sem requests extras.

## 2. Contexto

A SPEC de fila pedia eventos `ingestion.progress` no hub existente; a implementação entregou flush periódico no DB (`Ingestion:ProgressFlushSeconds`) + polling do client — porque não existe hub genérico de eventos de domínio (só `McpActivityBroadcastService` para atividade MCP). O resultado funciona, mas é polling — a melhoria pendente é empurrar eventos de progresso pelo canal de broadcast existente.

## 3. Requisitos Funcionais

### RF-001 — Eventos de progresso
- `IMcpActivityFeed`/`McpActivityBroadcastService` (ou canal equivalente já usado pelo Monitor) ganha evento de progresso de ingestão — se o modelo atual não comportar, criar `IIngestionProgressFeed` com broadcast SignalR no mesmo hub (`/hubs/mcp` ou novo endpoint conforme arquitetura atual).
- Payload: `{jobId, sourceId, processed, skipped, failed, chunksCreated}` — throttled (máx 1/s por job).

### RF-002 — Cliente
- `Sources.razor`/`SyncNow` e futuras telas de job escutam o feed quando conectado; polling vira fallback (SSE/WS indisponível → comportamento atual).
- Badge de progresso na linha da fonte enquanto job running (opcional, barato: reusa dados do evento).

## 4. Requisitos Não-Funcionais

- Nenhum conteúdo de chunk/doc nos eventos — só contadores/ids.
- Throttle server-side para não inundar clients (1 evento/s/job).

## 5. Fora de escopo

- Notificações cross-browser/tostas globais para todos os usuários logados.
- Progresso dentro de `write_knowledge` (single-doc, instantâneo).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Canal de evento de progresso (extensão do feed existente ou `IIngestionProgressFeed`) |
| T2 | Worker publica eventos throttled |
| T3 | Cliente consome + fallback a polling |

## 7. Critérios de aceite

- [ ] Job em andamento emite eventos visíveis para o client sem polling.
- [ ] Sem evento novo quando feed indisponível — polling atual segue funcionando.
- [ ] Nenhum dado sensível (texto de chunk/doc) nos payloads.

## 8. Riscos

- Acoplar feed MCP a eventos de ingestão polui o monitor → separar por kind/filtro já existente no feed.
