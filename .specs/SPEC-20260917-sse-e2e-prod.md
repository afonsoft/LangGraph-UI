# SPEC-20260917-sse-e2e-prod

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `sse-e2e-prod` |
| Type | `Infra` (verificação operacional) |
| Stack | `curl / MCP SSE` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (verificação read-only em prod) |
| Ticket | #112 |
| Status | `Done — verified in prod 2026-09-17; access_token gap fixed in PR #120` |

## 1. User Story

**As a** mantenedor
**I want** exercitar o caminho MCP SSE legado autenticado contra `https://rag.afonsoft.dev`
**So that** o transporte `/mcp/sse` + `/mcp/message` em produção deixe de ser INCONCLUSIVO.

**Problem context:**
Carried de gap-analysis-20260916/17: SSE E2E autenticado nunca exercitado em prod. Requer API key `aft_*` válida — credencial que o agente não possui. O caminho já passa em testes de integração locais; falta a prova ponta-a-ponta pelo domínio real (tunnel/reverse proxy inclusos).

## 2. Scope

**In scope:**
- Com uma `aft_*` key fornecida pelo usuário (ou criada na UI admin), exercitar: `GET /mcp/sse` (handshake + endpoint event) → `POST /mcp/message` (`initialize`, `tools/list`, `tools/call search_knowledge`) → encerrar.
- Validar através do proxy público: headers SSE, heartbeat, auth Bearer e `?access_token=` fallback.
- Registrar evidência (outputs redacted — nunca a key).

**Out of scope:**
- Mudança de código; load/stress test; Streamable HTTP `/mcp` (já exercitável sem credencial? não — auth exigida em tudo exceto health/login, mas o foco aqui é o legado SSE).
- Criar/rotacionar keys — usuário fornece.

## 3. Technical Context

**Files to read:** `McpEngine/` SSE session code, `StreamingEndpoints` (não confundir: SSE REST vs MCP SSE), integration tests `McpTransportTests` (o que já é coberto localmente).

**Risks:** a key é secret — usar só via env var na sessão, nunca commitar/logar valor; prod real — calls devem ser read-only (`search_knowledge`, `tools/list`).

## 4. Requirements

### RF-001: Handshake SSE autenticado
- **Input → Output:** `GET /mcp/sse` com Bearer → `event: endpoint` + heartbeat ≤15 s.

### RF-002: JSON-RPC sobre `/mcp/message`
- **Input → Output:** `initialize` → serverInfo; `tools/list` → catálogo; `tools/call search_knowledge` → resultado ou erro MCP bem-formado.

### RF-003: Fallback query param
- **Input → Output:** `?access_token=` aceito onde header não é possível.

## 6. Acceptance Criteria

- **CA-001:** handshake + initialize + tools/list + 1 call bem-sucedidos em prod.
- **CA-002:** nenhuma credencial vazada em logs/commits.
- **CA-003:** evidência registrada; backlog item fechado.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Obter `aft_*` do usuário (env var efêmera) | key carregada |
| T2 | Script de verificação curl | outputs OK |
| T3 | Registrar evidência + fechar pendência | memory atualizado |

## 8. Organization Guardrails

- Read-only calls apenas; zero writes em prod.
- Credencial via env, descartada ao fim da sessão.

## 9. Definition of Done

- [ ] SSE legado provado em prod ou bug reportado.
- [ ] Pendência removida dos gap reports futuros.
