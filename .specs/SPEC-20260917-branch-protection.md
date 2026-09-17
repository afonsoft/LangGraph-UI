# SPEC-20260917-branch-protection

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `branch-protection` |
| Type | `Infra` (governança GitHub — sem mudança de código) |
| Stack | `GitHub CLI / repo settings` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (settings-only; opcional `chore/Devin-20260917-branch-protection` para memory/report update) |
| Ticket | `[A DEFINIR]` (criar via `/create-issues` após aprovação) |
| Status | `Approved` |

## 1. User Story

**As a** mantenedor do repositório
**I want** que a branch `main` tenha branch protection habilitada (PR obrigatório + status checks)
**So that** a hard rule "proibido push/commit direto em main" (CLAUDE.md §Regras Críticas, AGENTS.md) seja enforced pela plataforma e não apenas por convenção.

**Problem context:**
`gh api repos/afonsoft/LangGraph-UI/branches/main/protection` retorna `404 Branch not protected`. Em 2026-09-16 o commit `0107c70` (feat mobile + api-key-settings) foi push direto em `main` — single-parent, sem PR — e introduziu trailing whitespace que deixou `dotnet format --verify-no-changes` vermelho na main (corrigido na PR #87). A regra existe; a enforcement não.

## 2. Scope

**In scope:**
- Habilitar branch protection em `main`: require pull request antes de merge + require status checks (checks CI existentes: `Build KnowledgeHub`, `Unit Tests`, `Integration Tests`, `Blazor WASM Client Validation`, `Docker Image Build`).
- Avaliar/manter `allow force pushes = off` e `allow deletions = off` (defaults seguros).
- Documentar a configuração resultante em CLAUDE.md ou `docs/` (uma linha na seção de convenções).

**Out of scope:**
- `require approvals` com N revisores — o repositório é mantido por uma pessoa; PR-required já cobre a regra.
- Regras para outras branches (`develop`, `feature/*`).
- Mudanças em workflows/CI (`.github/workflows/` é protegido por convenção, não por este SPEC).
- Signed commits, linear history, rulesets avançados.

## 3. Technical Context

**Where the change happens:** GitHub repo settings via `gh api -X PUT repos/afonsoft/LangGraph-UI/branches/main/protection` (ou UI Settings → Branches). Nenhum arquivo de código muda.

**Discovery findings (verified 2026-09-17):**

| Concern | Fact |
| --- | --- |
| Protection atual | `gh api .../branches/main/protection` → `404 Branch not protected` |
| Evidência de violação | `git show 0107c70` — commit direto em main (1 parent), sem PR; quebrou format gate |
| Checks disponíveis | `gh pr checks 87` → Build/Unit/Integration/Blazor/Docker/CodeQL/GitGuardian/Snyk pass |
| Regra documentada | CLAUDE.md "Branches Protegidas: Proibido push/commit direto em main"; AGENTS.md idem |

**Risks:** status-check names mudam se workflows forem renomeados (mitigação: usar `contexts` com os nomes atuais e revisitar se CI mudar); proteção pode bloquear merges administrativos legítimos (mitigação: `enforce_admins` = false, mantendo escape hatch documentado).

## 4. Requirements

### RF-001: Branch protection em `main`
- **Description:** ativar protection com `required_pull_request_reviews` (dismiss stale: false, require approval count pode ser 0 — PR obrigatório é o gate) ou, minimamente, `required_status_checks` + restrição de push direto.
- **Rules:** configuração via API/CLI auditável; `enforce_admins=false` para não travar o owner; registrar os `contexts` escolhidos.
- **Input → Output:** `gh api -X PUT .../protection` → `GET` retorna protection ativa.

### RF-002: Verificação do gate
- **Description:** provar que push direto em `main` é rejeitado (`git push origin main` de um commit de teste descartável, ou confirmação via API de que `required_pull_request_reviews`/`required_status_checks` estão ativos).
- **Rules:** não criar commits de teste permanentes; se usar commit de teste, reverter/fazer em clone descartável.
- **Input → Output:** push direto → `remote: error: GH006` ou equivalente.

### RF-003: Documentação
- **Description:** uma linha em CLAUDE.md §Convenções registrando que `main` tem protection ativa (PR + checks obrigatórios).
- **Input → Output:** CLAUDE.md atualizado via PR normal (dogfooding do novo gate).

## 6. Acceptance Criteria

- **CA-001:** `gh api repos/afonsoft/LangGraph-UI/branches/main/protection` retorna 200 com `required_status_checks` e/ou `required_pull_request_reviews` ativos.
- **CA-002:** push direto em `main` é rejeitado pelo remote (evidência: output do erro ou doc da config).
- **CA-003:** PRs existentes (#87, #88) continuam mergeáveis — checks verdes satisfazem o gate.
- **CA-004:** CLAUDE.md registra a proteção ativa.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Aplicar protection via `gh api -X PUT` (checks: Build, Unit Tests, Integration Tests, Blazor WASM, Docker Image Build) | `GET` retorna 200 |
| T2 | Verificar rejeição de push direto | erro GH006 ou config equivalente |
| T3 | Documentar em CLAUDE.md | PR com a linha de convenção |

## 8. Organization Guardrails

- Mudança de segurança/governança → **Tier 3**: exige aprovação explícita antes de aplicar.
- Não habilitar `enforce_admins` nesta iteração (repo single-maintainer).
- Não tocar `.github/workflows/` (protected).

## 9. Definition of Done

- [ ] Protection ativa em `main` e verificada via API.
- [ ] Push direto rejeitado (evidência registrada).
- [ ] CLAUDE.md documenta o gate.
- [ ] Memory report `gap-analysis-20260917.md` atualizado.
