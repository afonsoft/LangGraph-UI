# SPEC — Human-in-the-loop: aprovação de tools mutating

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260914-hitl-tool-approval` |
| Data | 2026-09-14 |
| Autor | Devin |
| Stack | `.NET 10`, `EF Core`, `SignalR` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-hitl-tool-approval` |
| DependsOn | `SPEC-20260914-agent-chat-loop` |
| Status | `Approved` |

## 1. User Story

**As a** administrador do KnowledgeHub
**I want** que tools de escrita (`write_knowledge`, `write_note`) executadas pelo agente paus esperem minha aprovação
**So that** o agente não muta a base de conhecimento sem supervisão — equivalente ao `interrupt_on` do DeepAgents/LangGraph.

## 2. Contexto

Hoje tools mutating executam imediatamente — na UI do Playground é o próprio usuário quem clica, mas no **loop agêntico** o modelo pode decidir escrever. A SPEC do agente já exige `allowWrite:true`; esta SPEC adiciona o gate humano. Em MCP exposto para clientes externos (Cursor), o cliente já tem sua própria aprovação — o gate se aplica às execuções **originadas pelo agente interno e pela UI**.

## 3. Requisitos Funcionais

### RF-001 — Modelo de aprovação
- Entidade `ToolApproval {Id, ToolName, ArgumentsJson, RequestedBy(ui|agent|thread), ThreadId?, Status(pending|approved|denied|expired), CreatedAt, ResolvedAt}` — migration.
- Config `Agent:RequireApprovalFor` (default: todas não-readOnly); `Agent:ApprovalTimeoutMinutes` (default 30 → `expired`).

### RF-002 — Fluxo no agente
- No `AgentService`, antes de executar tool mutating: cria `ToolApproval` pending, suspende a iteração retornando resposta parcial `awaiting_approval {approvalId, tool, args}` (ou, em modo síncrono, aguarda polling até timeout).
- Endpoints: `GET /api/approvals?status=pending`, `POST /api/approvals/{id}/approve`, `POST /api/approvals/{id}/deny`.
- `POST /api/agent/resume` com `approvalId`: ao aprovar, o agente reexecuta o tool call aprovado e continua o loop a partir do estado salvo (args originais; edição dos args opcional no approve — `approvedArgs` override).

### RF-003 — UI
- Página/aba "Aprovações": lista pending com tool, args (JSON legível), origem; aprovar/editar-args/negar.
- SignalR: evento `ApprovalRequested` no hub existente → badge/tempo real; `ApprovalResolved`.
- Playground: quando chamada de tool mutating vem da UI, confirmação inline obrigatória (dialog com diff/args) — já exigida pela SPEC do playground, aqui formalizada.

### RF-004 — MCP externo
- Chamadas `tools/call` vindas de clientes MCP **não** passam pelo gate (o cliente MCP é a fronteira de confiança — documentado). O gate cobre `agent_chat`, `/api/agent`, `/api/tools/{name}` quando `requestedBy` exige. Config global `Tools:ExternalRequireApproval` (default false) permite endurecer se desejado.

## 4. Requisitos Não-Funcionais

- Aprovação expirada nunca executa; resume explícito com `deny`/`expired` retorna erro claro.
- `ArgumentsJson` auditável — nunca logar secrets (args de write são título/conteúdo, ok; mascarar campos `*key*`, `*token*` se presentes).
- Concurrency: resolver 2× → segunda retorna 409.

## 5. Fora de escopo

- Edição de state mid-run genérica (LangGraph interrupts completos).
- Aprovação via MCP protocol (`elicitation`) — fase 2.
- Roles/permissões por usuário (single-tenant).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `ToolApproval` entity + migration + `ApprovalService` |
| T2 | Gate no `AgentService` + `awaiting_approval` + `POST /api/agent/resume` |
| T3 | Endpoints REST + eventos SignalR |
| T4 | UI aprovações + confirmação inline no Playground |
| T5 | Testes: aprovação continua o loop; deny/expired não executa; 409 em double-resolve |

## 7. Critérios de aceite

- [ ] Agente tentando `write_note` com gate ativo → resposta `awaiting_approval`, nada escrito no vault.
- [ ] `approve` → loop continua e arquivo é criado; `deny` → agente recebe tool result "denied" e responde sem escrever.
- [ ] Timeout → `expired`, não executa.
- [ ] Cliente MCP externo chamando `write_note` direto → executa (comportamento atual preservado).
- [ ] UI lista pending em tempo real via SignalR.
- [ ] Suite verde.

## 8. Riscos

| Risco | Mitigação |
|---|---|
| Loop fica "preso" esperando aprovação | estado persistido + resume explícito, nunca thread bloqueada |
| Args aprovados editados causam drift | `approvedArgs` auditado junto ao original |
