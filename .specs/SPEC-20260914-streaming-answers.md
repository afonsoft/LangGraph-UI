# SPEC — Streaming de respostas (SSE + SignalR) para ask/agent

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-streaming-answers` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `SSE`, `SignalR`, `IAsyncEnumerable` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-streaming-answers` |
| DependsOn | `SPEC-20260914-llm-answer-synthesis`, `SPEC-20260914-agent-chat-loop` |
| Status | `Approved` |

## 1. User Story

**As a** usuário do Playground/chat
**I want** ver a resposta do LLM e os passos do agente aparecendo progressivamente
**So that** chamadas longas (síntese, loop multi-iteração) tenham feedback imediato em vez de esperar o resultado completo — análogo ao event streaming do LangGraph/DeepAgents.

## 2. Contexto

Já existe infra de push: hub SignalR `/hubs/mcp` com `McpActivityEvent` (monitor) — hoje só para atividade MCP de clientes externos. O `IChatClient` do `Microsoft.Extensions.AI` expõe `GetStreamingResponseAsync` (`IAsyncEnumerable<ChatResponseUpdate>`).

Duas superfícies de streaming:
- **REST**: endpoint SSE (`text/event-stream`) — padrão da indústria para tokens; nginx já configurado sem buffering.
- **UI**: reusar SignalR ou consumir SSE via `HttpClient` streaming no WASM (WASM suporta `HttpCompletionOption.ResponseHeadersRead` + `ReadAsStreamAsync`).

## 3. Requisitos Funcionais

### RF-001 — SSE endpoints
- `POST /api/ask/stream` e `POST /api/agent/stream` → `text/event-stream` com eventos tipados:
  - `token {delta}` — tokens do LLM
  - `tool_start {tool, args}` / `tool_end {tool, isError, elapsedMs}` — passos do agente
  - `done {answer, citations/steps, latencyMs}` — payload final idêntico ao endpoint não-stream
  - `error {message}` — falha encerra o stream com evento de erro, nunca conexão morta silenciosa
- Heartbeat `:keep-alive` a cada 15 s para proxies.

### RF-002 — Cliente Blazor
- Playground: quando a tool suporta streaming (ou via `/api/agent/stream`), resposta renderiza incrementalmente; steps do agente aparecem na timeline em tempo real.
- Fallback automático para chamada única quando streaming falha na conexão.

### RF-003 — MCP (escopo limitado)
- `agent_chat`/`ask_knowledge` via MCP seguem síncronos (MCP streamable HTTP já suporta, mas notificações de progresso ficam para fase 2 — documentado como fora de escopo mínimo).

## 4. Requisitos Não-Funcionais

- Cancelamento do client (disconnect) cancela o loop/LLM via `CancellationToken`.
- SSE através do nginx/Cloudflare: verificado com `X-Accel-Buffering: no` no response + doc do vhost já existente (`proxy_buffering off` no README).
- Buffering do Kestrel desligado no endpoint (`HttpContext.Features`/`IHttpResponseBodyFeature` flush por evento).

## 5. Fora de escopo

- MCP progress notifications / resumable streams.
- WebSocket próprio (SignalR já cobre o feed de monitor; SSE cobre respostas).
- Streaming de voz/multimodal.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `StreamingEndpoints` SSE (writer + event serializer + heartbeat + `X-Accel-Buffering`) |
| T2 | `AnswerService`/`AgentService` ganham variantes `StreamAsync` (`IAsyncEnumerable<AgentEvent>`) |
| T3 | Playground consome stream (HttpClient streaming) com fallback |
| T4 | Testes: eventos ordenados, `done` idêntico ao sync, cancelamento; verificação manual via nginx |

## 7. Critérios de aceite

- [ ] `POST /api/agent/stream` emite `tool_start`/`token`/`done` em ordem via `curl -N`.
- [ ] Funciona através de `https://rag.afonsoft.dev` (nginx sem buffering — verificado de ponta a ponta).
- [ ] Playground mostra texto incremental + timeline ao vivo.
- [ ] Disconnect do cliente aborta o trabalho no servidor.
- [ ] Suite verde.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Proxy bufferiza eventos | header `X-Accel-Buffering: no` + teste E2E via domínio |
| WASM streaming inconsistente | fallback para resposta única |
| Eventos fora de ordem em tool calls paralelas | sequence number por evento; UI reordena |
