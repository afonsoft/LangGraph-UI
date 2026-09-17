# Gap Analysis — 20260917

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: feature/Devin-20260917-skills-update | Commit: a1d2c7d (main = 0107c70)
- Phase reached: gate (aguardando aprovação)
- Mode: full
- Build: green 0 warnings. Tests: 231 unit + 151 integration green. Format: vermelho em `main`, verde na PR #87.
- Prior runs: gap-analysis-20260914.md, gap-analysis-20260916.md (6/6 CONFIRMADO entregues via PR #76)

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 52 SPECs + 5 novos Draft desta rodada; 3 com Status stale `Approved` (fix na PR #87) |
| docs/ | present | docs/architecture/ com mermaid/drawio |
| .claude/CONTEXT.md | absent | — |
| .claude/MEMORY.md | absent | — |
| .claude/memory/ | present | stats + 2 gap reports |
| .claude/rules/ , .claude/agents/ | absent | checklist do orchestrator Phase 2 |
| .claude/settings.json | absent | — |
| CLAUDE.md / AGENTS.md / README.md | present | CLAUDE.md sync feito (#73); README sem features pós-0916 |
| tests / CI | present | 231+151 green; workflows OK |
| gh auth + remote | ok | afonsoft/LangGraph-UI |
| GitHub Issues | 0 open | E17/E18 reconciliadas |
| GitHub PRs | 2 open | #87 (fix gate, checks verdes), #88 (skills update) |
| Branch protection main | ABSENT | `gh api .../protection` → 404 |

## 2. AS-IS × TO-BE matrix

| Topic | AS-IS | TO-BE | Sources |
| --- | --- | --- | --- |
| main protection | push direto permitido (0107c70 direto, sem PR) | "Proibido push/commit direto em main" | CLAUDE.md/AGENTS.md hard rules; gh api 404 |
| format gate | vermelho em main (McpContractTests.cs:60) | gate verde | `dotnet format --verify-no-changes` exit 2 → corrigido PR #87 |
| SPECs status | 3 `Approved` entregues | Status=Done | PR #87 |
| skills catalog | desatualizado, execute-spec obsoleto | catalog bd78a76, execute-specs+gap-analysis | PR #88 |
| branches | 10 local + 13 remote mergeadas | só trabalho ativo | `git branch --merged` |
| .claude harness | só memory/ + skills/ | checklist orchestrator Phase 2 | orchestrator SKILL.md |
| README | sem chat settings/per-key/mobile | features documentadas | SPECs 0916 |
| install.sh --systemd | nunca verificado em host real | verificado | carried de 20260916 — INCONCLUSIVO |
| SSE E2E autenticado prod | não exercido | exercido | carried — INCONCLUSIVO |

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Spec | Issue | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| GAP-automation-branch-protection | automation | CONFIRMADO | high | SPEC-20260917-branch-protection | — | gh api 404; 0107c70 push direto; CLAUDE.md hard rule sem enforcement |
| GAP-operation-pending-merges | operation | CONFIRMADO | medium | SPEC-20260917-merge-pending-prs | — | gh pr list → #87, #88 abertas; #87 corrige gate vermelho |
| GAP-operation-merged-branch-cleanup | operation | CONFIRMADO | low | SPEC-20260917-merged-branch-cleanup | — | `git branch --merged main` = 10; `-r --merged` = 13 |
| GAP-documentation-harness-files | documentation | CONFIRMADO | low | SPEC-20260917-harness-files | — | collect-sources: CONTEXT/MEMORY/rules/agents/settings ABSENT |
| GAP-documentation-readme-feature-sync | documentation | CONFIRMADO | low | SPEC-20260917-readme-feature-sync | — | grep README: 0 matches p/ settings/chat, set_api_key_settings, mobile |
| GAP-implementation-format-gate | implementation | DUPLICADO | — | — | PR #87 | fix já aberto, checks verdes |
| GAP-documentation-spec-status-stale | documentation | DUPLICADO | — | — | PR #87 | sync Approved→Done já no PR |
| GAP-automation-skills-catalog | automation | DUPLICADO | — | — | PR #88 | update já aberto |
| GAP-operation-systemd-unverified | operation | INCONCLUSIVO | — | — | — | requer mutação de host (units/systemd) — precisa aprovação |
| GAP-operation-streaming-e2e-public | operation | INCONCLUSIVO | — | — | — | requer credencial prod — precisa aprovação |
| GAP-tests-suite-health | tests | REJEITADO | — | — | — | 231+151 green cobre |
| GAP-security-secrets-handling | security | REJEITADO | — | — | — | RNF-002 honrado (run 0916); sem secrets em IDistributedCache |
| backlog ONNX/sqlite-vec/McpProxy/passthrough | requirements | DUPLICADO | — | — | — | orchestrator_stats.md Backlog #1–4 pending_approval |

## 4. Approval gate

- Aguardando decisão do usuário sobre os 5 SPECs Draft.

## 5. Issues

- (nenhuma — gate pendente)

## 6. Orchestrator handoff

- Pre-conditions: tree tem 5 SPECs Draft não-commitados + report; branch feature/Devin-20260917-skills-update.
- Resultado: pendente.

## 7. Pendencies

- Decisão do usuário: aprovar SPECs → issues + execução; ou registrar apenas.
- INCONCLUSIVO: systemd verify (host mutation), SSE E2E autenticado (credencial).
- Branch protection é Tier 3 — mesmo aprovando o SPEC, a aplicação do gate exige confirmação no ato.
