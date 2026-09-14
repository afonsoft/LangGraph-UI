# SPEC — Threads de conversa persistentes + memória com sumarização

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-conversation-threads` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `EF Core SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-conversation-threads` |
| DependsOn | `SPEC-20260914-agent-chat-loop` |
| Status | `Done` |
| Ticket | `#31` |

## 1. User Story

**As a** usuário do agente
**I want** continuar uma conversa em chamadas subsequentes e ter o histórico resumido quando ficar longo
**So that** o agente mantém contexto multi-turno (análogo a checkpointer/threads do LangGraph e *context management* do DeepAgents) sem estourar a janela de tokens.

## 2. Contexto

O `agent_chat` é stateless — cada chamada começa do zero. LangGraph resolve com *threads* + checkpointer; DeepAgents adiciona sumarização de histórico e offload de resultados grandes. Em EF Core já temos persistência — falta o modelo de conversa.

## 3. Requisitos Funcionais

### RF-001 — Modelo e API de threads
- Entidades novas (migration): `ConversationThread {Id, Title, CreatedAt, LastActivityAt, Summary?, MetadataJson}` e `ConversationMessage {Id, ThreadId, Role(user|assistant|tool), Content, ToolName?, CreatedAt, TokenEstimate}`.
- REST: `GET/POST /api/threads`, `GET /api/threads/{id}`, `DELETE /api/threads/{id}`, `POST /api/threads/{id}/messages` (atalho que invoca o agente e persiste ambos os lados).
- `agent_chat` e `POST /api/agent` aceitam `threadId` opcional — cria thread nova se ausente e `persist:true`.

### RF-002 — Janela de contexto + sumarização
- Ao montar `messages` para o modelo: system + `thread.Summary` (se houver) + últimas N mensagens que caibam em `Agent:MaxContextTokens` (default ~8000 estimado chars/4).
- Quando o histórico excede a janela: mensagens antigas são condensadas num `Summary` gerado pelo `IChatClient` ("rolling summary" — DeepAgents summarization); o resumo é persistido e o processo é incremental (resume o que saiu da janela desde o último resumo).
- Mensagens de tool podem ser colapsadas no resumo (`tool X(args) → resultado resumido`).

### RF-003 — UI
- Nova página (ou aba no Playground): lista de threads, histórico renderizado (user/assistant/tool com steps), input contínuo, indicador "contexto resumido" quando `Summary` ativo.
- Threads nomeáveis/renomeáveis; título auto-gerado da 1ª mensagem.

## 4. Requisitos Não-Funcionais

- Sumarização nunca bloqueia a resposta atual além do tempo do LLM — roda pós-resposta (fire-and-forget com erro logado).
- Thread de outro "usuário" não existe (single-tenant) — mas `threadId` inexistente → 404 claro, nunca cria implícito silencioso em GET/DELETE.
- Token estimate por chars/4 consistente com o chunker existente.

## 5. Fora de escopo

- Long-term memory cross-thread estilo `AGENTS.md` injetado no system prompt (SPEC futura candidata: `agent-memory`).
- Compartilhamento/export de threads.
- Compressão de tool results grandes via filesystem virtual (DeepAgents offloading completo).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Entidades + migration + `ConversationService` (CRUD + append) |
| T2 | Janela de contexto: seleção de mensagens + rolling summary no `AgentService` |
| T3 | `threadId` em `agent_chat`/`/api/agent` + endpoints de threads |
| T4 | Página de chat com histórico; testes (janela, resumo incremental, persistência) |

## 7. Critérios de aceite

- [x] `threadId` preserva contexto entre chamadas (pergunta de follow-up resolve referência anterior).
- [x] Histórico longo → `Summary` populado, janela respeita `MaxContextTokens`, conversa continua coerente.
- [x] CRUD de threads via REST + UI funcional.
- [x] Suite verde; migration aplica sobre DB existente.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Resumo perde detalhe crítico | resumo incremental + janela recente sempre verbatim |
| Estimativa de tokens imprecisa | margem conservadora; ajustável por config |
