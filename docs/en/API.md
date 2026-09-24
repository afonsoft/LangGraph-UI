# REST API

All `/api/*` endpoints require authentication (cookie session or `Authorization: Bearer aft_...` API key) except `POST /api/auth/login` and `/health*`. Rate limits apply (`llm`/`sync`/`general` policies; 429 + `Retry-After` when exceeded).

## Auth

| Route | Purpose |
|---|---|
| `POST /api/auth/login` | `{username, password}` → session cookie |
| `GET /api/auth/me` | current user + `mustChangePassword` |
| `POST /api/auth/logout` | clear session |
| `POST /api/auth/change-password` | `{currentPassword, newPassword}` → 204 |

## API keys (cookie session only)

| Route | Purpose |
|---|---|
| `GET/POST /api/apikeys` · `DELETE /api/apikeys/{id}` | manage keys (secret shown once) |
| `PUT/DELETE /api/api-keys/{id}/rate-limit` | per-key LLM/sync rate-limit overrides (`null` field = inherit global) |
| `PUT/DELETE /api/api-keys/{id}/scopes` | restrict key to allowed sources/tools |
| `GET/PUT/DELETE /api/api-keys/{id}/settings/chat` | per-key chat endpoint/model/key |
| `PUT/DELETE /api/api-keys/{id}/settings/integrations/{provider}` | per-key integration secrets |

## Sources & ingestion

| Route | Purpose |
|---|---|
| `GET/POST /api/sources` · `GET/PUT/DELETE /api/sources/{id}` | source CRUD (configs redacted) |
| `POST /api/sources/{id}/sync` · `/activate` · `/deactivate` | sync + activation |
| `GET /api/sources/{id}/documents` · `/usage` | documents + usage stats |

## Search, answers, agent

| Route | Purpose |
|---|---|
| `GET /api/search` | hybrid search (`mode`, `topK`, filters `sourceType`/`pathPrefix`/`indexedAfter`/`language`) |
| `POST /api/ask` · `GET /api/ask/stream` | cited answer; SSE stream |
| `POST /api/agent` · `GET /api/agent/stream` | tool-calling agent loop; SSE (`token`/`tool_start`/`tool_end`/`awaiting_approval`/`done`/`error`, 15 s heartbeat) |
| `GET/POST /api/threads` · `GET /api/threads/{id}/messages` | conversation threads |

## Approvals & tools

| Route | Purpose |
|---|---|
| `GET /api/approvals` · `POST /api/approvals/{id}/approve|deny` | HITL approval queue |
| `GET /api/tools` · `POST /api/tools/{name}` | REST façade over the live MCP catalog (same tools as `tools/list`/`tools/call`) |

## Settings (Operational policy)

| Route | Purpose |
|---|---|
| `GET/PUT/DELETE /api/settings/chat` · `POST /api/settings/chat/test` | persisted chat config + connectivity test |
| `GET/PUT/DELETE /api/settings/graph` | GraphRAG `enabled`, `maxChunksPerSync`, `maxChunkChars`, `maxResults` — applies without restart |
| `GET/PUT/DELETE /api/settings/integrations/{provider}` | masked integration keys (firecrawl, deepwiki, tavily, context7) |

## Security & eval

| Route | Purpose |
|---|---|
| `GET /api/security/events` | prompt-injection flag audit (metadata only — never raw content) |
| `POST /api/eval/run` · `GET /api/eval/runs` · `GET /api/eval/runs/{id}` | retrieval eval harness (Recall@K/P@K/MRR/faithfulness) |

## Other

| Route | Purpose |
|---|---|
| `/mcp` (+ `/mcp/sse`, `/mcp/message`) | MCP transports — see README |
| `/hubs/mcp` | SignalR activity feed |
| `/metrics` | Prometheus scrape endpoint (`Telemetry:Metrics:Prometheus=true`) |
| `/framework-assets/{stem}/{ext}` | proxy-safe `_framework` asset mirror |
