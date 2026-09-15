# SPEC-20260915-mcp-monitor-client-config

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-monitor-client-config` |
| Type | `Feature (Frontend)` |
| Stack | `.NET 10 (Blazor WASM)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Devin-20260915-mcp-monitor-client-config` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** usuário do KnowledgeHub
**I want** ver na tela do Monitor MCP exemplos prontos de como configurar o servidor MCP no Claude e no Devin
**So that** eu consiga conectar meu client em segundos copiando a config, sem precisar ler documentação externa.

**Problem context:**
A página `/mcp-monitor` mostra sessões e atividade em tempo real, mas não orienta o usuário sobre como conectar um client. O usuário precisa saber de cor a URL (`/mcp` Streamable HTTP), o header `Authorization: Bearer aft_*` e o formato de config de cada ferramenta — hoje isso não está em lugar nenhum da UI.

## 2. Scope

**In scope:**
- Novo card "Como conectar um client" na página `/mcp-monitor`.
- Snippets copiáveis para: **Claude Desktop** (`claude_desktop_config.json` via `mcp-remote`), **Claude Code** (`claude mcp add --transport http`), **Devin** (JSON de MCP server com `url` + `headers`).
- URL do servidor montada dinamicamente via `NavigationManager.BaseUri` (funciona em qualquer host/porta).
- Botão copiar por snippet (`navigator.clipboard.writeText` via `IJSRuntime`, padrão já usado em `ApiKeys.razor`).
- Nota orientando a criar a chave em `/api-keys` antes.

**Out of scope:**
- Detecção do client/OS do usuário.
- Wizard interativo ou geração de chave inline.
- Cursor/VS Code/outros clients (estrutura do card deve permitir adicionar depois).
- Alterações no servidor MCP ou no hub.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor` — novo card abaixo dos cards de sessões/atividade (ou acima — preferência: abaixo, não atrapalha o monitoramento).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor` (estrutura do layout)
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` (padrão de copy-to-clipboard via `IJSRuntime`)
- `src/KnowledgeHub.McpEngine/McpEndpointExtensions.cs` (rotas: `/mcp` streamable, `/mcp/sse` legado)

**Files to create or modify:**
```text
src/KnowledgeHub.Client/Pages/McpMonitor.razor   (modified)
```

## 4. Requirements

### RF-001: Card de configuração
- **Description:** Card BootstrapBlazor na página `/mcp-monitor` intitulado "Como conectar um client", com um snippet por client.
- **Rules:** snippets em `<pre>`/`code` font-mono; um `Button` `fa-copy` por snippet; URL = `BaseUri` + `mcp` (trim de barra final); placeholder `aft_SUA_CHAVE` no header.
- **Input → Output:** página renderiza → card com 3 snippets visíveis.

### RF-002: Snippet Claude Desktop
- **Description:** JSON de `claude_desktop_config.json` usando `mcp-remote` (bridge stdio→HTTP, padrão para MCP remoto no Claude Desktop):
```json
{
  "mcpServers": {
    "knowledgehub": {
      "command": "npx",
      "args": [
        "mcp-remote",
        "{BaseUri}mcp",
        "--header",
        "Authorization: Bearer aft_SUA_CHAVE"
      ]
    }
  }
}
```
- **Rules:** texto renderizado com a URL real resolvida via `BaseUri`.

### RF-003: Snippet Claude Code
- **Description:** comando CLI:
```bash
claude mcp add --transport http knowledgehub {BaseUri}mcp --header "Authorization: Bearer aft_SUA_CHAVE"
```

### RF-004: Snippet Devin
- **Description:** JSON de configuração de MCP server:
```json
{
  "mcpServers": {
    "knowledgehub": {
      "url": "{BaseUri}mcp",
      "headers": {
        "Authorization": "Bearer aft_SUA_CHAVE"
      }
    }
  }
}
```
- **Rules:** indicar que a configuração fica nas settings de MCP do Devin (Settings → MCP Servers / `.devin/config.json` conforme a instalação).

### RF-005: Orientação prévia + transporte legado
- **Description:** Nota no card: "Crie uma API key em `/api-keys` primeiro" (link para a página) + linha secundária informando que clientes SSE-only podem usar `{BaseUri}mcp/sse` (transporte legado).
- **Rules:** link interno `/api-keys` via `<a href>` normal.

## 5. API Contract

N/A — somente UI.

## 6. Acceptance Criteria

- **Given** a página `/mcp-monitor`, **when** renderiza, **then** exibe card com snippets de Claude Desktop, Claude Code e Devin.
- **Given** deploy em `https://rag.afonsoft.dev`, **then** os snippets contêm `https://rag.afonsoft.dev/mcp` (sem hardcode — derivado do `BaseUri`).
- **Given** clique no botão copiar de um snippet, **then** o texto completo vai para o clipboard.
- **Given** o card, **then** há link para `/api-keys` e menção ao endpoint legado `/mcp/sse`.
- **Edge:** clipboard API indisponível → sem crash (try/catch silencioso ou toast informativo).

## 7. Task Plan

| # | Task | Validation |
|---|------|-----------|
| T1 | Adicionar card + snippets com URL dinâmica em `McpMonitor.razor` | `dotnet build` |
| T2 | Botões copiar via `IJSRuntime` + nota/link `/api-keys` + menção SSE | `dotnet build`; smoke local |
| T3 | `dotnet format` + suíte completa | gates verdes |

## 8. Organization Guardrails

- Branch `feature/Devin-20260915-mcp-monitor-client-config` — nunca commitar em `main`.
- Sem secrets em snippets — sempre placeholder `aft_SUA_CHAVE`.
- Sem hardcode de host — sempre `NavigationManager.BaseUri`.

## 9. Definition of Done

- [ ] Card visível em `/mcp-monitor` com 3 snippets
- [ ] URL dinâmica via `BaseUri`
- [ ] Botão copiar funcional por snippet
- [ ] Link `/api-keys` + menção a `/mcp/sse`
- [ ] Build + format + suíte verdes; PR; merge; redeploy; smoke `/mcp-monitor`
