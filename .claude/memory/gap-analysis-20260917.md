# Gap Analysis — 20260917

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: feature/Devin-20260917-skills-update | Commit: a1d2c7d (main = 0107c70)
- Phase reached: done — todos os gaps confirmados entregues (2026-09-17)
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
| GAP-automation-branch-protection | automation | CONFIRMADO → ENTREGUE | high | SPEC-20260917-branch-protection | #92 | protection ativa (PR + 5 checks, enforce_admins=false); doc via PR #97 |
| GAP-operation-pending-merges | operation | CONFIRMADO → ENTREGUE | medium | SPEC-20260917-merge-pending-prs | #91 | #87 `5c1f79b`, #88 `cd662d0`, #89 `7f3c261` mergeadas; format gate exit 0 em main |
| GAP-operation-merged-branch-cleanup | operation | CONFIRMADO → ENTREGUE | low | SPEC-20260917-merged-branch-cleanup | #93 | 13 local + 16 remote deletadas; 2 unmerged mantidas |
| GAP-documentation-harness-files | documentation | CONFIRMADO → ENTREGUE | low | SPEC-20260917-harness-files | #94 | opção (a); harness completo em .claude/; validate-harness.sh PASS; PR #98 |
| GAP-documentation-readme-feature-sync | documentation | CONFIRMADO → ENTREGUE | low | SPEC-20260917-readme-feature-sync | #95 | PR #96 `00f1c18` mergeada |
| GAP-implementation-format-gate | implementation | DUPLICADO | — | — | PR #87 | fix já aberto, checks verdes |
| GAP-documentation-spec-status-stale | documentation | DUPLICADO | — | — | PR #87 | sync Approved→Done já no PR |
| GAP-automation-skills-catalog | automation | DUPLICADO | — | — | PR #88 | update já aberto |
| GAP-operation-systemd-unverified | operation | INCONCLUSIVO | — | — | — | requer mutação de host (units/systemd) — precisa aprovação |
| GAP-operation-streaming-e2e-public | operation | INCONCLUSIVO | — | — | — | requer credencial prod — precisa aprovação |
| GAP-tests-suite-health | tests | REJEITADO | — | — | — | 231+151 green cobre |
| GAP-security-secrets-handling | security | REJEITADO | — | — | — | RNF-002 honrado (run 0916); sem secrets em IDistributedCache |
| backlog ONNX/sqlite-vec/McpProxy/passthrough | requirements | DUPLICADO | — | — | — | orchestrator_stats.md Backlog #1–4 pending_approval |

## 4. Approval gate

- Usuário aprovou os 5 SPECs ("aprovadas", 2026-09-17). Harness-files: usuário escolheu opção (a) "Provisionar tudo". Branch protection aplicada após aprovação do SPEC + instrução de continuação.

## 5. Issues

- Epic #90 (gap-analysis-20260917) com slices #91–#95 — todas fechadas com evidência.

## 6. Orchestrator handoff

- SPECs commitados na PR #89 (`docs/Devin-20260917-gap-specs`), mergeada `7f3c261`.
- Execução: merges #87/#88/#89 → cleanup de branches → README sync (PR #96) → branch protection + doc (PR #97) → harness provisionado (PR #98).
- Resultado: 5/5 gaps confirmados entregues; SPECs marcados `Done` com evidência.

## 7. Pendencies

- INCONCLUSIVO (carried): systemd verify em host real (mutação — precisa aprovação), SSE E2E autenticado em prod (precisa credencial).
- Backlog pending_approval: ONNX embeddings, sqlite-vec, McpProxy SourceType, tools passthrough.
- Nota: `enforce_admins=false` — o owner (afonsoft) ainda pode bypassar a protection de main; é o escape hatch documentado, não enforced.
