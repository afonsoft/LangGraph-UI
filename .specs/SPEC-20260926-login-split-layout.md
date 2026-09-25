# SPEC — Login split: instruções MCP à esquerda, login à direita

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-login-split-layout` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `Blazor WASM`, `Bootstrap 5 grid`, `BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | pedido do usuário 2026-09-26 |

## 1. User Story

**As a** usuário abrindo a plataforma
**I want** a tela de login mostrando como conectar meu client MCP
**So that** eu já saiba gerar a API key e apontar Claude Code/Devin/opencode/agy
antes mesmo de entrar.

## 2. Contexto

`Login.razor` hoje é um card centralizado (360px). A tela tem espaço
desperdiçado e nenhuma orientação para quem chega querendo conectar um client
MCP — a info existe só esparsa no README. Pedido: painel esquerdo com
"Como conectar um client" (ordem: Claude Code → Devin → opencode → agy; remover
Claude Desktop), login à direita; mobile empilha login primeiro.

## 3. Requisitos Funcionais

- **RF-001** Layout split em `/login` (EmptyLayout):
  - Desktop (`lg+`): coluna esquerda ~7/12 com instruções, direita ~5/12 com o
    card de login (conteúdo inalterado: user, senha, erro, botão).
  - Mobile (`<lg`): login primeiro (`order-1`), instruções abaixo (`order-2`).
- **RF-002** Painel "Como conectar um client" com passo 0 comum: gerar API key
  `aft_*` em `/api-keys` (após login); URLs derivadas de `Nav.BaseUri`:
  `POST {base}mcp` (Streamable HTTP, recomendado), `{base}mcp/sse` +
  `?access_token=` para clients sem header.
- **RF-003** Seções na ordem exata pedida:
  1. **Claude Code** — `claude mcp add --transport http` com header Bearer.
  2. **Devin** — MCP server custom (org/user settings) com URL + header; nota
     `?access_token=` para client SSE.
  3. **opencode** — bloco `opencode.json` `"mcp"` remote + headers.
  4. **agy (Antigravity)** — `mcp_config.json` url+headers.
  - Claude Desktop removido (não aparece).
- **RF-004** Snippets com a URL real do deployment interpolada + botão copiar
  por bloco (reusa `khClipboard`/`CopyAsync` se existir, senão simples
  `navigator.clipboard` via JS helper já presente em `index.html`).
- **RF-005** Visual discreto: painel com fundo `bg-body-tertiary`/borda, texto
  `small`, monospaced nos comandos; sem depender de ícone externo novo.

## 4. Requisitos Não-Funcionais

- Funciona em `EmptyLayout` sem auth — só conteúdo estático + `Nav.BaseUri`.
- Altura: painel scrolla independente se o conteúdo exceder a viewport.
- Tema claro existente — reusar variáveis Bootstrap (suporta dark se houver).

## 5. Fora de Escopo

- Mudanças no fluxo de login/auth em si.
- Instruções para outros clients (Cursor, Claude Desktop) — removidos por
  pedido; endpoint SSE legado continua existindo.

## 6. Plano de Tarefas

1. `Login.razor`: grid `row`/`col` + `order-*` para mobile.
2. Componente inline das instruções com `_base = Nav.BaseUri`.
3. Snippets por client com a ordem pedida.
4. Build + verificação visual manual (container).

## 7. Acceptance Criteria

- [ ] Desktop: instruções esquerda, login direita.
- [ ] Mobile (<992px): login em cima, instruções embaixo.
- [ ] Ordem: Claude Code → Devin → opencode → agy; sem Claude Desktop.
- [ ] URL dos snippets reflete o host real.

## 8. Riscos

- Snippets de CLI podem divergir por versão do client — texto marca o endpoint
  canônico (`/mcp` HTTP) como fallback universal.
