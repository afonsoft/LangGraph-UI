# SPEC-20260917-merge-pending-prs

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `merge-pending-prs` |
| Type | `Docs` (operação/higiene — merge de PRs entregues) |
| Stack | `GitHub CLI` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (merges em `main`) |
| Ticket | #91 |
| Status | `Done` — PRs #87 (`5c1f79b`), #88 (`cd662d0`), #89 (`7f3c261`) mergeadas em main; format gate exit 0 verificado |

## 1. User Story

**As a** mantenedor do repositório
**I want** que as PRs abertas e verdes (#87, #88) sejam mergeadas em `main`
**So that** o format gate volte a passar na main e o estado documentado (SPECs Done, skills catalog) reflita o código real.

**Problem context:**
Gap analysis 20260917 encontrou `main` com `dotnet format --verify-no-changes` vermelho (trailing whitespace em `McpContractTests.cs:60`, introduzido pelo push direto `0107c70`) e 3 SPECs `Approved` apesar de entregues. A correção está na PR #87 (checks verdes). A PR #88 entrega a atualização do catálogo de skills (checks verdes/pendentes na hora da auditoria).

## 2. Scope

**In scope:**
- Merge da PR #87 (`fix/Devin-20260917-format-gate`) — format gate + sync de Status das 3 SPECs.
- Merge da PR #88 (`feature/Devin-20260917-skills-update`) — catalog update + symlinks + lockfile.
- Verificar pós-merge: `main` limpa (`git pull`, `dotnet format --verify-no-changes` exit 0).
- Atualizar `.claude/memory/orchestrator_stats.md` (status das TASK-011 e PRs).

**Out of scope:**
- Novas mudanças de código.
- Squash/rebase policy change — usar o método de merge vigente no repo (merge commit, conforme histórico).
- Cleanup das branches de origem — coberto por `SPEC-20260917-merged-branch-cleanup`.

## 3. Technical Context

**Where the change happens:** `gh pr merge` — nenhum código.

**Files to read before implementing:**
- `gh pr view 87` / `gh pr checks 87` — all green (registrado 2026-09-17)
- `gh pr view 88` / `gh pr checks 88` — checks passando/pendentes; aguardar verde antes do merge

**Risks:** merge da #88 antes dos checks terminarem (mitigação: `--auto` ou aguardar); ordem — #87 primeiro pois corrige o gate; #88 não conflita (arquivos disjuntos, verificar com `gh pr view --json mergeable`).

## 4. Requirements

### RF-001: Merge ordenado com verificação
- **Description:** merge #87 → merge #88 (após checks verdes) → `git pull` + verificação local do format gate.
- **Rules:** só mergear com checks verdes; método = merge commit (convenção do repo).
- **Input → Output:** `gh pr merge 87 --merge` → `gh pr merge 88 --merge` → main `0` whitespace errors.

## 6. Acceptance Criteria

- **CA-001:** `gh pr list --state open` vazio.
- **CA-002:** `dotnet format --verify-no-changes` em `main` pós-merge → exit 0.
- **CA-003:** SPECs `settings-chat-config`, `api-key-settings`, `mobile-layout-responsive` = `Done` em main.
- **CA-004:** `orchestrator_stats.md` atualizado.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Merge PR #87 | `gh pr view 87` → MERGED |
| T2 | Aguardar checks #88 + merge | `gh pr checks 88` all pass → MERGED |
| T3 | Pull + format verify em main | exit 0 |
| T4 | Atualizar orchestrator_stats.md | arquivo commitado |

## 8. Organization Guardrails

- Merge em `main` = escalation gate → executar só após aprovação explícita do usuário.
- Se #88 tiver check vermelho → não mergear; reportar e tratar com `/diagnose`.

## 9. Definition of Done

- [ ] PRs #87 e #88 mergeadas.
- [ ] Format gate verde na main.
- [ ] Stats file sincronizado.
