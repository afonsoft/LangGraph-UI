# SPEC-20260917-upstream-tools-passthrough

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `upstream-tools-passthrough` |
| Type | `Feature` |
| Stack | `.NET 10 / MCP` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260917-tools-passthrough` |
| Ticket | #110 |
| Status | `Done — PR #113 (ea2276c)` |

## 1. User Story

**As a** usuário com DeepWiki privado (org Devin)
**I want** que o `tools/list` upstream seja re-exposto dinamicamente (incluindo `devin_*` quando ApiKey configurada)
**So that** as tools privadas da org ficam disponíveis sem hardcode de nomes.

**Problem context:**
O proxy DeepWiki re-expõe 3 tools fixas (`ask_question`, `read_wiki_structure`, `read_wiki_contents`). Com `ApiKey` + endpoint privado (`mcp.devin.ai`), o upstream oferece tools adicionais `devin_*` que hoje não passam. Backlog #4 (SPEC-20260913-deepwiki-mcp-proxy §Out of scope): "Upstream tools/list passthrough — needs ApiKey + dynamic upstream discovery".

## 2. Scope

**In scope:**
- `DeepWikiToolsProvider` passa a descobrir tools via `McpClient.ListToolsAsync()` quando a effective key aponta para o endpoint privado; mantém as 3 fixas no modo público (upstream pode estar down no list-time — comportamento já documentado).
- Passthrough de schema upstream-fiel (inputSchema copiado).
- Invalidação: troca de key (store→env) ou falha de transporte → re-resolve na próxima chamada (mesmo padrão `_connectedKey` do client).

**Out of scope:**
- Generic McpProxy sources (SPEC `mcp-proxy-source-type` separada).
- Resources/prompts upstream.
- Cache longo de tools (discovery no list, não persistente).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Mcp/Upstream/DeepWikiToolsProvider.cs` + `DeepWikiUpstreamClient.cs`.

**Files to read before implementing:**
- `DeepWikiToolsProvider.cs` (lista atual hardcoded)
- `DeepWikiUpstreamClient.cs` (`ResolveApiKeyAsync`, `EffectiveEndpoint`, `_connectedKey` reconnect)
- `tests/KnowledgeHub.Tests.Integration/McpContractTests.cs` (lista de tools pinada — vai mudar)
- `SPEC-20260913-deepwiki-mcp-proxy.md` RF-001/RF-004

**Risks:** `tools/list` dinâmico depende de rede (mitigação: cache curto + fallback para as 3 fixas em modo público/falha); ordem/nomes instáveis quebram McpContractTests (mitigação: teste verifica presença mínima + mark dinâmico); tools privadas podem exigir escopos (propagar IsError upstream).

## 4. Requirements

### RF-001: Discovery dinâmico (modo privado)
- **Description:** com ApiKey resolvida (private endpoint), `tools/list` retorna as tools que o upstream reporta via `ListToolsAsync`, upstream-faithful.
- **Input → Output:** `initialize`/`tools/list` → conjunto `devin_*` + as 3 públicas.

### RF-002: Fallback público
- **Description:** sem ApiKey ou falha de discovery, mantém exatamente as 3 tools atuais (compat).
- **Input → Output:** modo público → comportamento idêntico ao atual.

### RF-003: Call routing
- **Description:** `tools/call` de qualquer nome descoberto roteia via `DeepWikiUpstreamClient.CallAsync`; nomes desconhecidos → erro MCP padrão.
- **Input → Output:** call em `devin_*` → resposta upstream ou `IsError`.

## 6. Acceptance Criteria

- **CA-001:** Given ApiKey configurada e upstream fake com `devin_x`, when `tools/list`, then `devin_x` presente com schema upstream.
- **CA-002:** Given sem ApiKey, when `tools/list`, then somente as 3 tools (sem regression).
- **CA-003:** McpContractTests adaptado (base fixa pública + seção dinâmica privada).
- **CA-004:** Revogar a key → próxima list volta ao conjunto público.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | `ListToolsAsync` no client + cache por key-state | unit c/ transport fake |
| T2 | Provider dinâmico + fallback | integração |
| T3 | McpContractTests + docs | suite verde |

## 8. Organization Guardrails

- Compat: modo público inalterado.
- Sem secrets em logs; key state via resolução existente apenas.

## 9. Definition of Done

- [ ] Passthrough privado funcional com fake upstream.
- [ ] Modo público sem regressão.
- [ ] Contrato de testes atualizado.
