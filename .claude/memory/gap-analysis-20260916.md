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

## 5. Draft SPECs

| Gap key | SPEC |
| --- | --- |
| GAP-security-redis-exposed | `.specs/SPEC-20260916-redis-exposure-risk.md` |
| GAP-operation-issue-tracking-e17 | `.specs/SPEC-20260916-e17-issue-sync.md` |
| GAP-documentation-deploy-env-vars | `.specs/SPEC-20260916-deploy-env-docs.md` |
| GAP-operation-compose-vault-mount-drift | `.specs/SPEC-20260916-compose-vault-mount.md` |
| GAP-implementation-warning-cs8604 | `.specs/SPEC-20260916-fix-cs8604-warning.md` |
| GAP-documentation-claude-md-stale | `.specs/SPEC-20260916-claude-md-sync.md` |
