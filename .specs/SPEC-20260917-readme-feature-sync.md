# SPEC-20260917-readme-feature-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `readme-feature-sync` |
| Type | `Docs` |
| Stack | `Markdown` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `docs/Devin-20260917-readme-feature-sync` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** usuário novo da plataforma
**I want** que o README cubra as features entregues recentemente (per-API-key settings, chat config em `/settings`, layout mobile)
**So that** a documentação de entrada reflita o produto real.

**Problem context:**
Entregas pós-0916 não estão no README: SPEC-20260916-settings-chat-config (card Chat em `/settings` + endpoints `/api/settings/chat*`), SPEC-20260916-api-key-settings (`set_api_key_settings` MCP tool + endpoints de settings por API key) e SPEC-20260916-mobile-layout-responsive (layout mobile). `grep -in 'mobile\|settings/chat\|set_api_key_settings' README.md` → 0 matches. O orchestrator (Phase 5.8) exige README sincronizado ao final de cada Epic.

## 2. Scope

**In scope:**
- Seção/linhas no README cobrindo: card "Chat (LLM)" em `/settings` (endpoint+model+key, testar conexão), per-API-key chat/integration settings, e menção ao layout responsivo/mobile.
- Tabela de endpoints: adicionar `/api/settings/chat*` e os endpoints de per-key settings se existirem rota pública documentável.

**Out of scope:**
- Reescrever o README inteiro ou rebranding.
- Documentar internals (ChatSettingsService etc.) — README é user-facing.
- CHANGELOG (repo não mantém CHANGELOG.md hoje — registrar apenas se o usuário quiser instituir um).

## 3. Technical Context

**Where the change happens:** `README.md` apenas.

**Files to read before implementing:**
- `.specs/SPEC-20260916-{settings-chat-config,api-key-settings,mobile-layout-responsive}.md` §5 (contratos)
- `src/KnowledgeHub.Server/Api/{SettingsEndpoints,ApiKeySettingsEndpoints}.cs` (rotas reais)
- `src/KnowledgeHub.Server/Mcp/ToolProviders/SettingsToolsProvider.cs` (`set_api_key_settings`)

## 4. Requirements

### RF-001: Cobertura das features novas
- **Description:** README menciona as 3 features com o nível de detalhe das seções existentes (tabela de endpoints, bloco de config).
- **Rules:** seguir estilo atual (pt/en conforme o doc); sem segredos; exemplos com placeholders.
- **Input → Output:** `grep 'settings/chat\|set_api_key_settings\|mobile' README.md` → matches.

## 6. Acceptance Criteria

- **CA-001:** README cobre as 3 features listadas.
- **CA-002:** endpoints/tables citados existem no código (`grep` confirma).

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Ler SPECs + endpoints reais | diff de rotas vs doc |
| T2 | Atualizar README | grep + leitura |
| T3 | PR + memory report | PR aberto |

## 8. Organization Guardrails

- Docs only; nada de código.
- Não expor valores de config reais (endpoints internos ok, secrets nunca).

## 9. Definition of Done

- [ ] README sincronizado com as features.
- [ ] PR aberto/mergeado.
