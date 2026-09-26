# SPEC — Alinhamento MCP com a spec 2026-07-28 e a C# SDK 2.2 (metadados, outputSchema, MRTR/HITL, Tasks)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-mcp-sdk-alignment` |
| Data | 2026-09-26 |
| Autor | Devin |
| Tipo | `Feature` / `Interop` |
| Stack | `.NET 10`, `ModelContextProtocol` + `ModelContextProtocol.AspNetCore` 2.2.0 |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise solicitada pelo usuário sobre `csharp-sdk`, spec `2026-07-28` e docs da SDK v2 — "veja o que podemos melhorar" |

## 1. User Story

**As a** operador do Knowledge MCP Hub
**I want** que o servidor MCP exponha metadados ricos de tools e use os mecanismos padrão da spec 2026-07-28 (input-required/MRTR, Tasks extension) para aprovações e operações longas
**So that** clients MCP padrão (Claude, VS Code, Cursor, MCPJam) renderizam confirmações e progresso nativamente — em vez do fluxo HITL custom que só a UI própria entende.

## 2. Contexto

AS-IS: o servidor usa a SDK 2.2.0 com handlers custom (`ListTools`/`CallTool`/`ListResources`/`ReadResource`), modo híbrido de sessão (`StatefulForInitializeClients` — já alinhado com a detecção de era da spec nova), `listChanged` push para sessões, gate de escrita por API key (`AllowWrite`, PR #252), scope por key, rate limit e cache no dispatch. Mas:

- `ToolAnnotations` emite só `ReadOnlyHint` — `Title`, `DestructiveHint`, `IdempotentHint`, `OpenWorldHint` existem na SDK e ficam `null`;
- tools não têm `title`/`icons`/`outputSchema` e resultados são texto puro (`StructuredContent` existe no helper `ToolResults` mas nenhuma tool usa);
- HITL é um canal paralelo (`awaiting_approval` SSE + `POST /api/approvals` + `/api/agent/resume`) — invisível para clients MCP padrão;
- operações longas (`agent_chat`, sync tools) bloqueiam o `tools/call` até completar;
- `prompts/*`, `completion/complete`, elicitation e `resources/subscribe` não são implementados;
- `ServerInfo.Name = "knowledge"` ficou para trás do rename para Knowledge MCP Hub.

TO-BE: metadados completos no `tools/list`, resultados estruturados nas tools de busca, e aprovação/execução longa via mecanismos da spec (MRTR `input_required` + elicitation; Tasks extension com `IMcpTaskStore`) — opt-in por capability do client, sem quebrar clients antigos.

## 3. Requisitos Funcionais

### RF-001 — Metadados completos das tools (annotations + title)
- Todo `CatalogTool` ganha campos opcionais `Title`, `DestructiveHint`, `IdempotentHint`, `OpenWorldHint`, propagados para `Tool` em `ListToolsHandler` (`Tool.Title`, `ToolAnnotations.*`).
- Defaults por provider: write tools (`write_knowledge`, `set_api_key_settings`, `*_crawl`, `*_research`, tools de proxy sem `ReadOnlyHint`) → `DestructiveHint=true` onde a operação é destrutiva; read tools → `IdempotentHint=true` quando aplicável; tools que chamam serviços externos (upstream proxies, firecrawl/tavily/deepwiki/context7) → `OpenWorldHint=true`.
- `query_*`/sync de sources continuam com a mesma semântica de hoje — só ganham hints honestos.
- `ServerInfo.Name` → `"knowledge-mcp-hub"` (alinha com o rename de produto).

### RF-002 — `outputSchema` + `structuredContent` nas tools de busca
- `search_knowledge`, `ask_knowledge` e `query_*` declaram `outputSchema` (hits: `title`, `uri`, `source`, `score`, `chunk`) e retornam `structuredContent` correspondente, mantendo o bloco de texto atual (compat com clients que só leem `content`).
- O cache de tool (`ToolCacheService`) já persiste `StructuredContent` — validar round-trip; `REST /api/tools/{name}` devolve o mesmo payload (campo já serializa).

### RF-003 — Aprovação HITL via MRTR `input_required` + elicitation
- Quando uma write tool chamada via `tools/call` exige aprovação (policy de HITL do `AgentOptions.RequireApprovalFor`) **e** o client declarou elicitation (capability `elicitation` no request/`_meta`): responder `resultType: "input_required"` com `inputRequests` contendo `elicitation/create` (form: resumo da tool + argumentos, `accept`/`decline`) e `requestState` opaque apontando para o `ToolApproval` pendente.
- O retry do `tools/call` com `inputResponses`+`requestState` resolve a aprovação existente (reutiliza `IApprovalService`/`SuspendAsync`); `decline`/`cancel` → `isError` informativo.
- Clients sem elicitation → comportamento atual (sem mudança — o gate continua REST/SignalR).
- O write gate por key (`AllowWrite=false`) permanece check anterior ao MRTR — "requires write access" primeiro.

### RF-004 — Tasks extension para operações longas
- Referência ao `ModelContextProtocol.Extensions.Tasks` (mesma versão da SDK, 2.2.0).
- `IMcpTaskStore` implementado sobre EF (durável — `InMemoryMcpTaskStore` é dev-only): `tasks/get`, `tasks/update`, `tasks/cancel` registrados pelo pacote.
- `agent_chat` e tools de sync/ingestão retornam `CreateTaskResult` (`resultType: "task"`) quando o client declara `io.modelcontextprotocol/tasks` no `_meta` — reaproveitando `IngestionQueue`/jobs e o loop do agente; `input_required` no task mapeia aprovação HITL pendente.
- Client sem a capability → bloqueio atual (comportamento inalterado — opt-in estrito por capability, nunca devolver task para quem não declarou).

### RF-005 — `tools/list` com metadados de cache
- Resposta do `tools/list` inclui `ttlMs` + `cacheScope` (`private` — o catálogo é por credencial) para clients cachearem corretamente; ordem de emissão já é determinística (manter).

### RF-006 — `prompts/list` + `prompts/get` (opcional, baixa prioridade)
- Expor 1–2 templates: `rag_answer` (pergunta → prompt de síntese com citações) e `source_overview` (slug → resumo da fonte). Só se for trivial sobre `McpServerOptions.Handlers`; senão fica fora do escopo desta entrega.

## 4. Requisitos Não-Funcionais

- Compat backward total: clients ≤2025-11-25 (initialize/SSE legado) e clients que não declaram `elicitation`/`tasks` veem exatamente o comportamento atual.
- `requestState` opaque (sem dados do tool/approval em claro — referência ao `ToolApproval.Id` assinada/criptografada).
- Tasks duráveis — sobrevivem a restart (EF store, não in-memory em produção).
- Nenhum campo novo quebra o contrato REST `/api/tools` existente.

## 5. Restrições / Não-fazer

- Não implementar OAuth/resource-server (authorization spec) — auth atual (`aft_*`/cookie) permanece.
- Não implementar MCP Apps (extension de UI) nem sampling/roots.
- Não remover o fluxo HITL REST/SignalR — MRTR/Tasks coexistem.
- Não paginar `tools/list` (catálogo pequeno; `cacheScope` já cobre o caso de uso).
- Sem upgrade de versão da SDK além de adicionar `Extensions.Tasks` na mesma versão.

## 6. Critérios de Aceite

- `tools/list` mostra `title`/`annotations` completas; write tools têm `destructiveHint`.
- `search_knowledge` retorna `structuredContent` validável contra o `outputSchema` declarado.
- Write tool sob política HITL + client com elicitation → `input_required` → retry com accept executa, decline retorna isError.
- `agent_chat` + client com tasks → `taskId` → `tasks/get` até `completed` com o resultado final; cancel via `tasks/cancel`.
- Clients antigos (initialize/SSE, sem extensions) inalterados — `McpContractTests`/`McpToolsTests`/`McpTransportTests` verdes.
- `dotnet test` verde incluindo testes novos de MRTR/Tasks.

## 7. Definição de Pronto

- [ ] Campos de metadata no `CatalogTool` + providers preenchidos
- [ ] `outputSchema`/`structuredContent` nas tools de busca
- [ ] MRTR `input_required` no dispatch de `tools/call` + `requestState` opaque
- [ ] `IMcpTaskStore` EF + wiring do `Extensions.Tasks` para `agent_chat`/sync
- [ ] `ttlMs`/`cacheScope` no `tools/list`; `ServerInfo.Name` atualizado
- [ ] Testes: annotations, structuredContent, MRTR accept/decline, tasks lifecycle, compat clients legados
- [ ] README EN+PT + CLAUDE.md atualizados
