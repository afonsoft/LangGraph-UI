# SPEC-20260917-mcp-proxy-source-type

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-proxy-source-type` |
| Type | `Feature` |
| Stack | `.NET 10 / MCP` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260917-mcp-proxy-source` |
| Ticket | #109 |
| Status | `Done — PR #114 (5e6a407)` |

## 1. User Story

**As a** administrador do KnowledgeHub
**I want** registrar servidores MCP upstream como sources (`SourceType = McpProxy`) via `/api/sources`
**So that** qualquer MCP server vira um proxy gerenciado — sem código dedicado por upstream como DeepWiki/Firecrawl/Tavily.

**Problem context:**
Hoje cada upstream MCP é hardcoded: `DeepWikiUpstreamClient`/`FirecrawlUpstreamClient`/`TavilyUpstreamClient` + `ToolsProvider` próprios. Backlog #3 (SPEC-20260913-deepwiki-mcp-proxy §Out of scope): `McpProxy` SourceType catalog-driven via `/api/sources`.

## 2. Scope

**In scope:**
- `SourceType.McpProxy` no enum + config da source: `{ endpoint, transport(auto|http|sse), apiKeyRef|apiKey, enabled }` em `configuration` JSON.
- `McpProxyUpstreamClient` genérico (mesmo padrão lazy `McpClient` AutoDetect do `DeepWikiUpstreamClient`) instanciado por source ativa.
- Tools upstream expostas com namespacing configurável (ex.: `{slug}_{tool}` ou upstream-identical — decidir padrão e colisão).
- UI/REST: `/api/sources` aceita o novo type; edit dialog com campos do proxy.

**Out of scope:**
- `tools/list` passthrough dinâmico por key — SPEC separada (`upstream-tools-passthrough`).
- Resources/prompts upstream — tools apenas.
- Migrar DeepWiki/Firecrawl/Tavily existentes para o novo tipo (podem coexistir; migração é follow-up).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Shared/Contracts/SourceType.cs`, `src/KnowledgeHub.Server/Mcp/Upstream/` (novo client genérico + provider), `SourcesEndpoints`/source service (type no CRUD), `Client` (edit dialog).

**Files to read before implementing:**
- `Mcp/Upstream/DeepWikiUpstreamClient.cs`, `DeepWikiToolsProvider.cs`, `DeepWikiOptions.cs` (padrão a generalizar)
- `Mcp/Upstream/FirecrawlUpstreamClient.cs`, `TavilyUpstreamClient.cs` (segunda variação do padrão)
- `Shared/Contracts/SourceType.cs`, `KnowledgeSourceDtos.cs`
- `SourcesEndpoints.cs` + UI `Pages/Sources.razor`/edit dialog
- `SPEC-20260913-deepwiki-mcp-proxy.md` (upstream contract, secrets por integração)

**Risks:** colisão de tool names entre upstreams (mitigação: prefixo `{slug}_` default, opt-out upstream-identical); secrets por source no `IIntegrationSecretStore` ou config (decidir); upstream down → tools listadas mas falham no call (padrão já estabelecido).

## 4. Requirements

### RF-001: Novo SourceType
- **Description:** `SourceType.McpProxy`; source com `configuration.endpoint`, `transport` (default auto), `apiKey` (via secret store, nunca em plain config retornado pelo GET), `namePrefix` opcional.
- **Input → Output:** POST `/api/sources` type=McpProxy → source registrada, health/status no catálogo.

### RF-002: Client genérico + tools
- **Description:** para cada source McpProxy ativa, um client lazy conecta ao endpoint e re-expõe `tools/list` upstream como tools locais (`{prefix}{tool}` ou nome puro conforme config); `tools/call` roteia para o upstream correspondente.
- **Rules:** falha de conexão ≠ derruba o servidor; tool call retorna `IsError` com mensagem do upstream; reconnect com 1 retry (padrão DeepWiki).
- **Input → Output:** tools upstream aparecem em `tools/list` e respondem em `tools/call`.

### RF-003: Segredos
- **Description:** apiKey por source vai para o encrypted store (mesmo modelo das integrations); GET nunca ecoa o valor.
- **Input → Output:** GET `/api/sources/{id}` → `hasKey` apenas.

## 6. Acceptance Criteria

- **CA-001:** Given source McpProxy apontando a um MCP server real, when `tools/list`, then tools upstream presentes com schema fiel.
- **CA-002:** Given upstream down, when `tools/call`, then `IsError` propagado sem crash.
- **CA-003:** Given 2 proxies com tools homônimas, then prefixo evita colisão.
- **CA-004:** McpContractTests atualizado; testes de integração com fake upstream.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Enum + DTO + secret handling | unit: serialização/GET masked |
| T2 | `McpProxyUpstreamClient` genérico | integração c/ fake MCP |
| T3 | Tools provider dinâmico + namespacing | tools/list + call E2E |
| T4 | UI edit dialog + docs | smoke manual + README |

## 8. Organization Guardrails

- Sem quebrar providers existentes (DeepWiki/Firecrawl/Tavily continuam).
- Secrets nunca retornados em GET nem logados.

## 9. Definition of Done

- [ ] McpProxy registrável via API/UI e funcional E2E.
- [ ] Testes + McpContractTests verdes.
- [ ] Docs atualizadas.
