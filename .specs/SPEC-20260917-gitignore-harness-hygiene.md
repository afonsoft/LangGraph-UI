# SPEC-20260917-gitignore-harness-hygiene

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `gitignore-harness-hygiene` |
| Type | `Chore` (higiene de harness) |
| Stack | `gitignore` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `chore/Devin-20260917-gitignore-harness` |
| Ticket | #101 |
| Status | `Approved` |

## 1. User Story

**As a** agente operando este repositório
**I want** que artefatos locais de harness (`.devin/`, `.claude/settings.local.json`) sejam gitignored
**So that** configuração local de agente nunca seja commitada por acidente.

**Problem context:**
Gap-analysis r2: `.gitignore` cobre `.agents/` mas não `.devin/` nem `.claude/settings.local.json`. O skill `create-agent-harness` §3.10 define `settings.local.json` como override local não-versionado — sem entrada no `.gitignore`, um `git add .` o commita. Mesmo risco para `.devin/` (config/memória local do Devin CLI).

## 2. Scope

**In scope:** adicionar a `.gitignore`: `.devin/`, `.claude/settings.local.json`.
**Out of scope:** outros padrões; mudança de harness.

## 3. Technical Context

**Where:** `.gitignore` (raiz). Evidência: `grep -n "devin\|settings.local" .gitignore` → 0 matches; create-agent-harness SKILL.md §3.10 declara `settings.local.json` não-versionado.

## 4. Requirements

### RF-001: Entradas de ignore
- **Description:** `.gitignore` ignora `.devin/` e `.claude/settings.local.json`.
- **Input → Output:** `git check-ignore .devin/x .claude/settings.local.json` → match.

## 6. Acceptance Criteria

- **CA-001:** `git check-ignore` confirma ambos os padrões.
- **CA-002:** PR mergeado pelo gate normal.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Editar `.gitignore` + commit + PR | check-ignore passa |

## 8. Organization Guardrails

- Docs/config only; sem código.

## 9. Definition of Done

- [ ] Padrões no `.gitignore` e verificados.
