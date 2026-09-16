# Gap Analysis — 20260916

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: feature/Devin-20260916-performance-cache | Commit: f3316e1 (main = c6c9d76, PR #65 merged)
- Phase reached: verdicts → awaiting gate
- Build: green (1× CS8604 warning). Tests: 197 unit + 140 integration green. `dotnet format` clean.
- Prior run: gap-analysis-20260914.md (14 gaps → all implemented; issues left open)

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 41 SPECs — all `Done` (SPEC-20260915-boot-cache-revalidation uses prose `**Status:** Done` instead of table format) |
| docs/architecture/ | present | system-architecture.md + mermaid/drawio |
| .claude/memory/ | present | gap-analysis-20260914.md, orchestrator_stats.md |
| CLAUDE.md / AGENTS.md / README.md | present | CLAUDE.md feature list predates firecrawl/tavily/cache/auth |
| tests / CI | present | 197+140 green; workflows pass on main (PR #65) |
| gh auth | ok | afonsoft/LangGraph-UI |
| GitHub Issues | open | E17 slices #30–#42 + epic #28 OPEN though all work Done+merged |
| deploy drift | observed | docker-compose.yml dirty: local vault mount uncommitted |

## 2. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-security-redis-exposed | security | CONFIRMADO | **high** | Host Redis `bind 0.0.0.0`, `protected-mode no`, sem `requirepass` (redis-cli config get); KnowledgeHub escreve `index:version`/search-cache no db3 — se 6379 for alcançável publicamente, cache é lido/envenenado por terceiros |
| GAP-operation-issue-tracking-e17 | operation | CONFIRMADO | medium | `gh issue list`: #28 epic + #30–#42 slices OPEN; trabalho Done+merged (gap-analysis-20260914 §6, commits em main) — só #29 fechada |
| GAP-documentation-deploy-env-vars | documentation | CONFIRMADO | medium | docker-compose.yml:35-46 referencia `FIRECRAWL_*`, `TAVILY_*`, `CACHE_PROVIDER`, `REDIS_CONNECTIONSTRING`; README não documenta nenhuma; `.env.example` ausente (`ls .env*` → só `.env`) |
| GAP-operation-compose-vault-mount-drift | operation | CONFIRMADO | medium | `git status`: docker-compose.yml dirty — mount `/home/ubuntu/.openclaw/workspace/vault:/vaults/obsidian` (rw) existe só localmente; redeploy de checkout limpo perde o backend do `obsidian_write` |
| GAP-implementation-warning-cs8604 | implementation | CONFIRMADO | low | `IngestionService.cs:396` CS8604 `Path.Combine(path1 anulável)` — viola regra do repo "corrija warnings antes de commitar" (CLAUDE.md convenções) |
| GAP-documentation-claude-md-stale | documentation | CONFIRMADO | low | CLAUDE.md "Features implementadas" omite auth-login, upstream proxies (DeepWiki/Firecrawl/Tavily), cache Redis — leitor novo vê estado de ~20260915 |
| GAP-operation-systemd-unverified | operation | INCONCLUSIVO | — | carregado de 20260914: install.sh `--systemd` nunca verificado em host real; verificação exige alterar o sistema (units/systemd) — precisa de aprovação |
| GAP-operation-streaming-e2e-public | operation | INCONCLUSIVO | — | carregado: SSE via https://rag.afonsoft.dev requer auth — host responde 200 (healthz + /), mas o path autenticado não foi exercido |
| GAP-implementation-sourcelocks-leak | implementation | REJEITADO | — | decisão documentada na SPEC-20260916 §10 (deferido; bounded por nº de sources; sem fix seguro trivial) |
| GAP-implementation-pgvector-threshold | architecture | REJEITADO | — | open question documentada na mesma SPEC (~50k chunks) — `PostgresVectorStore` já existe e é opt-in |
| GAP-security-secrets-in-redis | security | REJEITADO | — | RNF-002 honrado: secrets vão só para `IMemoryCache` (IntegrationSecretStore.cs) — nunca `IDistributedCache` |
| GAP-tests-cache-coverage | tests | REJEITADO | — | PerformanceCacheTests + SearchAllocBenchTests cobrem catálogo, fallback, invalidação, alocação |

## 3. Dedup

- Nenhum gap CONFIRMADO possui SPEC/Issue equivalente (verificado `.specs/` + `gh issue list --state all`).
- E17 issues abertas não são gaps novos — são tracking pendente (GAP-operation-issue-tracking-e17).

## 4. Approval gate

- User approved SPECs + Issues for all confirmed gaps (2026-09-16).
- Redis: scope = document risk only (no infra change this round).

## 5. SPECs (Approved) + Issues

| Gap key | SPEC | Issue |
| --- | --- | --- |
| GAP-security-redis-exposed | SPEC-20260916-redis-exposure-risk | #68 |
| GAP-operation-issue-tracking-e17 | SPEC-20260916-e17-issue-sync | #69 |
| GAP-documentation-deploy-env-vars | SPEC-20260916-deploy-env-docs | #70 |
| GAP-operation-compose-vault-mount-drift | SPEC-20260916-compose-vault-mount | #71 |
| GAP-implementation-warning-cs8604 | SPEC-20260916-fix-cs8604-warning | #72 |
| GAP-documentation-claude-md-stale | SPEC-20260916-claude-md-sync | #73 |

- Epic: **#67** (E18) — slices #68–#73 linkadas no corpo.
- SPECs mergeadas em `main` (PRs #66, #74).

## 6. Orchestrator handoff — OUTCOME (2026-09-16)

All 6 slices delivered on `feature/Devin-20260916-e18-gaps-impl` (PR #76, merge `2a21ea0`) and issues #67–#73 closed with evidence comments:

| Issue | Entrega |
| --- | --- |
| #72 fix-cs8604-warning | null guard `IngestionService.cs` — build `0 Warning(s)` |
| #68 redis-exposure-risk | `ConfigurationValidator.CollectWarnings` + `Program.cs` startup log; README "Redis security" (3 opções). Warning confirmado em produção (`Cache:Provider=redis` sem `password=`). Infra inalterada (decisão do operador). |
| #70 deploy-env-docs | `.env.example` commitado; tabela README cobre 100% das vars do compose + `Chat__*`/`KnowledgeHub__DatabasePath` |
| #71 compose-vault-mount | mount movido p/ `docker-compose.override.yml` (gitignored) + `.example` commitado; `docker compose config` confirma `/vaults/obsidian`; `docker exec ls /vaults/obsidian` OK pós-redeploy |
| #73 claude-md-sync | lista de features cobre 100% das SPECs Done |
| #69 e17-issue-sync | issues E17 #28, #30–#42 fechadas com comentário de evidência (commit + PR #43) |

Gates: build 0 warnings · unit 201/201 · integration 140/140 · format limpo.
Redeploy: container `knowledgehub` healthy na `:5550`, `/health/live` 200.

Inconclusivos carregados: `install.sh --systemd` (host real), SSE E2E autenticado via `https://rag.afonsoft.dev`.
