# API REST

Todos os endpoints `/api/*` exigem autenticação (sessão por cookie ou `Authorization: Bearer aft_...`), exceto `POST /api/auth/login` e `/health*`. Rate limits ativos (policies `llm`/`sync`/`general`; 429 + `Retry-After` quando excedido).

## Auth

| Rota | Propósito |
|---|---|
| `POST /api/auth/login` | `{username, password}` → cookie de sessão |
| `GET /api/auth/me` | usuário atual + `mustChangePassword` |
| `POST /api/auth/logout` | limpa a sessão |
| `POST /api/auth/change-password` | `{currentPassword, newPassword}` → 204 |

## API keys (somente sessão por cookie)

| Rota | Propósito |
|---|---|
| `GET/POST /api/apikeys` · `DELETE /api/apikeys/{id}` | gerencia keys (secret exibido uma vez) |
| `PUT/DELETE /api/api-keys/{id}/rate-limit` | overrides de rate limit LLM/sync por key (campo `null` = herda global) |
| `PUT/DELETE /api/api-keys/{id}/scopes` | restringe a key a sources/tools permitidos |
| `GET/PUT/DELETE /api/api-keys/{id}/settings/chat` | endpoint/modelo/key de chat por key |
| `PUT/DELETE /api/api-keys/{id}/settings/integrations/{provider}` | secrets de integração por key |

## Sources e ingestão

| Rota | Propósito |
|---|---|
| `GET/POST /api/sources` · `GET/PUT/DELETE /api/sources/{id}` | CRUD de sources (configs redigidos) |
| `POST /api/sources/{id}/sync` | **assíncrono** — `202 {jobId,status,existing}`; `?wait=true` mantém o `SyncResultDto` síncrono legado |
| `POST /api/sources/{id}/reindex` | `202` — força re-chunk/re-embed mesmo sem alteração de conteúdo |
| `POST /api/sources/{id}/activate` · `/deactivate` | ativação |
| `GET /api/sources/{id}/documents` · `/usage` | documentos + estatísticas |

### Jobs de ingestão

| Rota | Propósito |
|---|---|
| `GET /api/ingestion/jobs?sourceId=&status=&limit=` | lista jobs (filtro por fonte + status) |
| `GET /api/ingestion/jobs/{id}` | um job — status, contadores por doc, erro |
| `POST /api/ingestion/jobs/{id}/cancel` | cancela job em fila (`409` se já em execução/terminal) |

## Busca, respostas, agente

| Rota | Propósito |
|---|---|
| `GET /api/search` | busca híbrida (`mode`, `topK`, filtros `sourceType`/`pathPrefix`/`indexedAfter`/`language`) |
| `POST /api/ask` · `GET /api/ask/stream` | resposta com citações; stream SSE |
| `POST /api/agent` · `GET /api/agent/stream` | loop de agente com tools; SSE (`token`/`tool_start`/`tool_end`/`awaiting_approval`/`done`/`error`, heartbeat 15 s) |
| `GET/POST /api/threads` · `GET /api/threads/{id}/messages` | threads de conversação |

## Aprovações e tools

| Rota | Propósito |
|---|---|
| `GET /api/approvals` · `POST /api/approvals/{id}/approve|deny` | fila de aprovação HITL |
| `GET /api/tools` · `POST /api/tools/{name}` | fachada REST sobre o catálogo MCP vivo (mesmas tools de `tools/list`/`tools/call`) |

## Settings (policy Operational)

| Rota | Propósito |
|---|---|
| `GET/PUT/DELETE /api/settings/chat` · `POST /api/settings/chat/test` | config de chat persistida + teste de conectividade |
| `GET/PUT/DELETE /api/settings/graph` | GraphRAG `enabled`, `maxChunksPerSync`, `maxChunkChars`, `maxResults` — aplica sem restart |
| `GET/PUT/DELETE /api/settings/integrations/{provider}` | chaves de integração mascaradas (firecrawl, deepwiki, tavily, context7) |
| `GET /api/settings/cache` · `POST /api/settings/cache/clear` | stats de cache (keys rastreadas do processo + overlay do servidor Redis via SCAN/INFO — `serverReported`/`partial`) + limpeza de todas as regiões |
| `GET/PUT /api/settings/log-level` | nível de log em runtime (`LoggingLevelSwitch`); `PUT {level, autoResetMinutes}` — `autoResetMinutes` 0–120 agenda reset automático ao nível configurado |

## Segurança e eval

| Rota | Propósito |
|---|---|
| `GET /api/security/events` | auditoria de flags de prompt-injection (só metadados — nunca conteúdo bruto) |
| `POST /api/eval/run` · `GET /api/eval/runs` · `GET /api/eval/runs/{id}` | harness de eval de retrieval (Recall@K/P@K/MRR/faithfulness, latências p50/p95/p99) — request aceita `baseline` (nome) e `gate` (regras `[{metric,direction,threshold}]`), report traz `gateResult` + `topRegressions` |
| `GET/POST /api/eval/baselines` | lista baselines nomeados; `POST {name, runId}` promove um run |

### Args das tools search/ask

`search_knowledge` e `ask_knowledge` (tools MCP e fachada REST) aceitam, além de
`mode`/`topK`/filtros de metadados:

| Arg | Valores |
|---|---|
| `expand` | `off` (default) · `multi` (N rewrites fundidos via RRF) · `hyde` (doc hipotético no braço vetorial) · `both` |
| `contextExpand` | `none` · `window` (chunks vizinhos) · `section` (seção-pai) — anexa `context` a cada hit sem mudar o ranking |
| `useGraph` | bool — ativa o braço de knowledge graph (entity linking + evidência de 1 hop) |

## Outros

| Rota | Propósito |
|---|---|
| `GET /api/diagnostics/vectorstore` | diagnóstico do vector store — provider, dimensão, contagem de chunks, tipo de storage (`vector`\|`halfvec`), estado do índice |
| `/mcp` (+ `/mcp/sse`, `/mcp/message`) | transportes MCP — ver README |
| `/hubs/mcp` | feed de atividade SignalR |
| `/metrics` | endpoint de scrape Prometheus (`Telemetry:Metrics:Prometheus=true`) |
| `/framework-assets/{stem}/{ext}` | espelho de assets `_framework` seguro para proxy |
