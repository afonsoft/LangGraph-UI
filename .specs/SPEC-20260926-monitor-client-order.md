# SPEC — Monitor MCP: reordenar "Como conectar um client"

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-monitor-client-order` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `Blazor WASM`, `BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | pedido do usuário 2026-09-26 |

## 1. User Story

**As a** usuário abrindo o Monitor MCP
**I want** os snippets de client na ordem Claude Code → Devin → opencode → agy
e os genéricos agrupados
**So that** os clients mais usados apareçam primeiro e o painel fique igual ao
da tela de login.

## 2. Contexto

`McpMonitor.razor` tem o card "Como conectar um client" (SPEC-20260915) com a
ordem Claude Desktop → Claude Code → Devin. Pedido: remover Claude Desktop,
reordenar para Claude Code → Devin → opencode → agy e agrupar os clients
genéricos (configuração SSE/HTTP arbitrária via `mcp-remote` ou `?access_token=`).
A mesma ordem já foi aplicada na `/login` (SPEC-20260926-login-split-layout).

## 3. Requisitos Funcionais

- **RF-001** Ordem dos blocos: 1. Claude Code, 2. Devin, 3. opencode,
  4. agy (Antigravity).
- **RF-002** Remover o bloco "Claude Desktop" e a propriedade
  `ClaudeDesktopConfig`.
- **RF-003** Bloco final "Genérico (SSE / qualquer client)": URL do endpoint
  legado `{base}/mcp/sse` + nota `?access_token=` — cobre qualquer client não
  listado.
- **RF-004** Adicionar snippets `opencode.json` e `mcp_config.json` (agy) —
  mesmo formato já usado no `Login.razor`.
- **RF-005** Manter o botão copiar por bloco e o parágrafo introdutório
  (API key + transporte principal).

## 4. Requisitos Não-Funcionais

- URLs continuam derivadas de `Nav.BaseUri` — funcionam em qualquer host.
- Zero mudança funcional no feed/audit do monitor.

## 5. Acceptance Criteria

- [ ] Ordem Claude Code → Devin → opencode → agy → Genérico; sem Claude Desktop.
- [ ] Cada bloco copia o snippet correto com a URL real.

## 6. Riscos

- Nenhum — markup + strings.
