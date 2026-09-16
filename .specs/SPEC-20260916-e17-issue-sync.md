# SPEC-20260916-e17-issue-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `e17-issue-sync` |
| Type | `Docs` (tracking/higiene — reconciliar GitHub Issues com código entregue) |
| Stack | `GitHub CLI` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (sem mudança de código; opcional `feature/Devin-20260916-e17-issue-sync` para o memory/report update) |
| Ticket | `#69` |
| Status | `Approved` |

## 1. User Story

**As a** mantenedor do repositório
**I want** que as Issues E17 abertas sejam fechadas com comentário de rastreabilidade (commit/PR que entregou cada slice)
**So that** o quadro de Issues reflita o estado real e auditorias futuras não releiam trabalho já feito.

**Problem context:**
Gap analysis 20260914 criou Epic #28 + slices #30–#42; o orchestrator entregou tudo (commits em `main`, suite verde, report `.claude/memory/gap-analysis-20260914.md` §6). Hoje só #29 está fechada — as demais permanecem OPEN embora o trabalho esteja mergeado (verificado: `gh issue list` vs `.specs/*` Status=Done).

## 2. Scope

**In scope:**
- Comentar em cada issue E17 aberta (#30–#42) citando o commit/issue-entrega conforme o mapa do report 20260914 §6.
- Fechar as slices e o Epic #28.
- Atualizar `.claude/memory/gap-analysis-20260916.md` com o resultado.

**Out of scope:**
- Revalidar funcionalmente cada slice (cobertura já verificada no run 20260914 + suite atual 197+140).
- Criar novas issues.

## 3. Technical Context

**Where the change happens:** GitHub Issues via `gh issue comment`/`gh issue close` — nenhum código.

**Files to read before implementing:**
- `.claude/memory/gap-analysis-20260914.md` (mapa gap→issue→commit, §6)
- `git log` para confirmar commits em `main`

## 4. Requirements

### RF-001: Fechamento com rastreabilidade
- **Description:** cada slice issue recebe comentário "Delivered in <commit> (PR #N); spec <path> Done; suite green" antes do close. Epic #28 fecha por último com sumário.
- **Rules:** comentário referencia evidência real (commit sha + spec path); nunca fechar sem evidência no report.
- **Input → Output:** issues #30–#42 + #28 → CLOSED com link de commit.

## 6. Acceptance Criteria

- **CA-001:** `gh issue list --state open` não mostra mais issues E17.
- **CA-002:** cada issue fechada tem comentário citando commit de entrega.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Comentar + fechar #30–#42 conforme mapa §6 do report | `gh issue view` por número |
| T2 | Fechar epic #28 com sumário | `gh issue list` vazio para E17 |
| T3 | Atualizar memory report | arquivo atualizado |

## 8. Organization Guardrails

- Não fechar issue cuja evidência de entrega não exista no report/log — marcar INCONCLUSIVO e reportar.
- Comentários em pt-BR ou inglês conforme convenção do repo (verificar corpo das issues existentes).

## 9. Definition of Done

- [ ] Todas as issues E17 fechadas com comentário de evidência.
- [ ] Memory report atualizado.
