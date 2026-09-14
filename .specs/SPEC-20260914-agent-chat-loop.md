# SPEC — Loop agêntico com tool-calling (`agent_chat`)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-agent-chat-loop` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `Microsoft.Extensions.AI` function calling |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-agent-chat-loop` |
| DependsOn | `SPEC-20260914-llm-answer-synthesis` (IChatClient) |
| Status | `Draft` |

## 1. User Story

**As a** usuário do KnowledgeHub
**I want** um agente que itere — buscar, ler documento, refinar a query, escrever nota — até responder
**So that** perguntas complexas que exigem múltiplos passos de tool use sejam resolvidas numa única chamada, como um agente LangGraph/`create_agent`, em vez de uma única rodada de retrieve.

## 2. Contexto

Hoje cada tool é uma chamada isolada — não existe orquestração multi-step. O equivalente LangChain/`create_agent` e DeepAgents: um **loop model→tools→model** onde o LLM decide qual tool chamar, observa o resultado e itera.

Temos todas as peças: catálogo dinâmico vivo (`IDynamicToolCatalog`), handlers desacoplados do SDK (`ToolCallContext`), feed de atividade SignalR (`McpActivityEvent`). O que falta é o loop: adaptar o catálogo para `AIFunction`s do `Microsoft.Extensions.AI` e deixar o `IChatClient` dirigir.

## 3. Requisitos Funcionais

### RF-001 — `AgentService` (loop)
- Novo serviço: recebe `messages[]` (ou `prompt`), monta `ChatOptions.Tools` a partir do catálogo vivo (cada `CatalogTool` vira um `AIFunction` que invoca o handler via `ToolCallContext`).
- Loop `while`: `GetResponseAsync` → se houver `FunctionCallContent`, executa cada call (paralelo ok), anexa `FunctionResultContent`, repete — até resposta final ou teto.
- Teto: `Agent:MaxIterations` (default 10) e `Agent:MaxToolCalls` (default 20); ao estourar, resposta final inclui aviso "iteration limit reached".
- Tool allowlist opcional por chamada (`tools:["search_knowledge",...]`) — restringe a superfície.

### RF-002 — Superfícies
- MCP tool nova: `agent_chat` (args: `prompt`, opcional `topK hint` não — args: `prompt`, `tools?`, `maxIterations?`).
- REST: `POST /api/agent` → `{answer, steps[], toolCalls[], iterations, latencyMs}`.
- Playground: seleção de `agent_chat` renderiza `steps[]` (timeline: tool → args → resultado resumido).

### RF-003 — Visibilidade
- Cada tool call interna emite `McpActivityEvent` no hub existente (kind `ToolCall`) — o MCP Monitor mostra a atividade do agente em tempo real, igual a chamadas MCP externas.
- `steps[]` no resultado: `{iteration, tool, argsSummary, isError, elapsedMs}`.

### RF-004 — Segurança
- Tools não-`readOnly` (`write_knowledge`, `write_note`) executam de verdade dentro do loop — até a SPEC de HITL chegar, `agent_chat` exige opt-in explícito `allowWrite:true` para incluí-las; sem o flag, o catálogo exposto ao modelo exclui mutating tools.
- Sem `IChatClient` configurado → `agent_chat` retorna `isError` explicando que requer `Chat:Provider`.

## 4. Requisitos Não-Funcionais

- Cada iteração é isolável em log; `steps[]` nunca inclui conteúdo de variáveis sensíveis de ambiente.
- `CancellationToken` propaga — cancelar o request REST aborta o loop entre iterações.
- Contexto do prompt gerenciado: resultados de tools truncados em `Agent:MaxToolResultChars` (default 4000) antes de voltar ao modelo — análogo ao *context offloading* do DeepAgents.

## 5. Fora de escopo

- Threads persistentes de conversa (SPEC `conversation-threads`).
- Subagentes paralelos com contexto isolado (DeepAgents `task` tool) — fase 2.
- Streaming do loop (SPEC `streaming-answers`).
- Planning tool `write_todos` — pode entrar depois como mais uma tool do catálogo.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `CatalogToolAIFunction` — adapter `CatalogTool`→`AIFunction` via `ToolCallContext` |
| T2 | `AgentService` loop + limites + truncamento de resultados |
| T3 | Emissão de `McpActivityEvent` por tool call |
| T4 | Tool `agent_chat` (schema + contract test) + `POST /api/agent` |
| T5 | UI Playground: timeline de steps; testes de integração (loop com provider fake) |

## 7. Critérios de aceite

- [ ] Pergunta que exige 2 passos (search → read_document) é respondida com `iterations ≥ 2` e `steps[]` corretos.
- [ ] `allowWrite` ausente → modelo não vê tools mutating; presente → consegue `write_note` (teste com vault tmp).
- [ ] Teto de iterações estoura graciosamente com aviso.
- [ ] Cada tool call aparece no SignalR feed.
- [ ] Sem `IChatClient` → `isError` claro.
- [ ] Suite verde incluindo `McpContractTests` atualizado.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Loop infinito de tool calls | `MaxIterations`/`MaxToolCalls` hard caps |
| Modelo chama write tool indevidamente | `allowWrite` opt-in; HITL chega na SPEC separada |
| Resultado gigante estoura contexto | `MaxToolResultChars` trunca |
| Custos latência por iteração sequencial | tool calls independentes na mesma iteração executam em paralelo |
