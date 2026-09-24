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
| `POST /api/sources/{id}/sync` · `/activate` · `/deactivate` | sync + ativação |
| `GET /api/sources/{id}/documents` · `/usage` | documentos + estatísticas |

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

## Segurança e eval

| Rota | Propósito |
|---|---|
| `GET /api/security/events` | auditoria de flags de prompt-injection (só metadados — nunca conteúdo bruto) |
| `POST /api/eval/run` · `GET /api/eval/runs` · `GET /api/eval/runs/{id}` | harness de eval de retrieval (Recall@K/P@K/MRR/faithfulness) |

## Outros

| Rota | Propósito |
|---|---|
| `/mcp` (+ `/mcp/sse`, `/mcp/message`) | transportes MCP — ver README |
| `/hubs/mcp` | feed de atividade SignalR |
| `/metrics` | endpoint de scrape Prometheus (`Telemetry:Metrics:Prometheus=true`) |
| `/framework-assets/{stem}/{ext}` | espelho de assets `_framework` seguro para proxy |
