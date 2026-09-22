# Gap Analysis — 20260922 (pós-entrega Context7)

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: feature/Devin-20260922-context7-mcp-proxy | Commit: 7594186
- Phase reached: re-verdicts executados; 1 gap confirmado consolidado → SPEC a ser escrita
- Mode: re-run (resume) — re-verificação dos vereditos do run anterior
- Prior runs: gap-analysis-20260914/16/17/17-r2 (todos ENTREGUE)

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 73 SPECs — todas `Done` (inclui SPEC-20260922-context7-mcp-proxy) |
| docs/, docs/architecture/ | present | — |
| .claude/ harness + memory | present | CONTEXT/RULES/MEMORY + memory.md + reports anteriores |
| CLAUDE.md, AGENTS.md, README.md | present | sync com Context7 feito em 7594186 |
| ORCHESTRATOR-ROADMAP.md | absent | sem TO-BE que o exija |
| GitHub | gh OK (afonsoft) | open: #146 (fecha via PR #147); #128 CLOSED |
| Branches | — | remotas stale já limpas: só `main` + feature atual |
| Working tree | clean após commit deste report | — |

## 2. Candidates and verdicts (re-verified 2026-09-22)

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-implementation-per-key-upstream-secrets | implementation | CONFIRMADO | média | `GetIntegrationSecretAsync` (ApiKeyChatSettingsService.cs:165-170) tem **0 callers** — confirmado via grep em src/+tests/. `ResolveApiKeyAsync` de Context7UpstreamClient.cs:34-40 (e equivalentes Firecrawl/Tavily/DeepWiki) resolve apenas `secrets.GetAsync(provider)` → env. Overrides `apikey-{provider}-{keyId}` persistidos via `SaveIntegrationKeyAsync` (:134-137) / `set_api_key_settings` / `PUT /api/api-keys/{id}/settings/integrations/{provider}` nunca são consumidos em call time. Caminho de fix existe: handlers recebem `ctx.Services` → `IHttpContextAccessor` → `KeyIdClaim` (padrão em SettingsToolsProvider.cs:42-44). TO-BE: SPEC-20260916-api-key-settings (per-key overrides como intenção) + superfície de persistência já existente. |
| GAP-implementation-perkey-deepwiki-orphan | implementation | CONFIRMADO (folded) | baixa | `ApiKeySettingsEndpoints` aceita `deepwiki` via `IntegrationProviders.All` (:56, :73), mas `IntegrationKeys` em DescribeAsync (ApiKeyChatSettingsService.cs:79-84), `RemoveAsync` (:158-161) e o enum de `set_api_key_settings` (SettingsToolsProvider.cs:18, :67) omitem deepwiki → key per-key deepwiki vira órfã invisível. Folded no SPEC do gap anterior. |
| GAP-documentation-readme-claudemd-context7 | documentation | REJEITADO | — | Resolvido em 7594186: README.md:21,82,284-286 e CLAUDE.md "Estado Atual" já mencionam Context7. |
| GAP-operation-stale-remote-branches | operation | REJEITADO | — | `git branch -r` mostra apenas origin/main + branch atual; stale branches removidas. |
| GAP-operation-epic-128-tracking | operation | REJEITADO | — | `gh issue view 128` → state CLOSED. |
| enforce_admins escape hatch | security | INCONCLUSIVO (carried de r2) | — | decisão aberta: enforce_admins=false intencional vs gate total — pendência do usuário. |
| Context7 OAuth /mcp/oauth | requirements | REJEITADO | — | fora de escopo explícito de SPEC-20260922 |
| Context7 keyless passthrough | requirements | REJEITADO | — | decisão aprovada: friendly isError sem key |
| Context7 tool-name collision | architecture | REJEITADO | — | verbatim names aprovados; last-wins documentado na SPEC |

## 3. Consolidação

Gap único consolidado: **GAP-implementation-per-key-upstream-secrets** (absorve perkey-deepwiki-orphan).
Ação: SPEC `per-key-integration-secrets` → implementação consome `GetIntegrationSecretAsync`
com o `KeyIdClaim` do chamador em call time + inclui deepwiki no enum/IntegrationKeys/RemoveAsync.

## 4. Pendencies carried

- `enforce_admins=true` — decisão aberta desde r2.
- PR #147 (Issue #146) — merge solicitado pelo usuário nesta sessão.
