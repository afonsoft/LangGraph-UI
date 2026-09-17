# SPEC-20260917-merged-branch-cleanup

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `merged-branch-cleanup` |
| Type | `Docs` (higiene de repositório) |
| Stack | `git` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (deleção de branches mergeadas) |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** mantenedor do repositório
**I want** que branches locais e remotas já mergeadas em `main` sejam deletadas
**So that** `git branch -a` reflita apenas trabalho ativo e auditorias não confundam branches mortas com trabalho pendente.

**Problem context:**
Gap analysis 20260917 encontrou **10 branches locais** e **13 remotas** mergeadas em `main` (`git branch --merged main` / `git branch -r --merged origin/main`), incluindo branches de PRs já mergeadas (#43–#85) e variantes obsoletas (`feature/Devin-20260916-gap-tickets`, `e18-done`, etc.).

## 2. Scope

**In scope:**
- Listar branches locais mergeadas (`git branch --merged main`) e remotas mergeadas (`git branch -r --merged origin/main`), excluindo `main`/`develop`/`HEAD`.
- Confirmar cada uma contra PRs mergeadas (`gh pr list --state merged --head <branch>`) antes de deletar — só deletar com evidência de merge.
- Deletar locais (`git branch -d`) e remotas (`git push origin --delete`).
- Branches das PRs #87/#88 entram no cleanup **somente depois** do merge (dependência de SPEC-20260917-merge-pending-prs).

**Out of scope:**
- Branches não-mergeadas ou com PR aberta — nunca deletar.
- `develop` (não existe atualmente; se existir, preservar).
- Tags.

## 3. Technical Context

**Where the change happens:** `git branch -d` / `git push origin --delete`. Nenhum arquivo muda.

**Files to read before implementing:**
- `git branch --merged main`, `git branch -r --merged origin/main`
- `gh pr list --state merged --json headRefName,number` — mapa branch → PR

**Risks:** deletar branch com commit não-mergeado (mitigação: usar `-d` não `-D` — git recusa se não mergeada; para remotas, cruzar com lista de PRs mergeadas).

## 4. Requirements

### RF-001: Deleção segura com evidência
- **Description:** para cada branch candidata, verificar PR mergeado correspondente; deletar local + remota; reportar o mapa branch→PR deletado.
- **Rules:** `-d` apenas (nunca `-D`); remota só se `gh pr list --state merged --head <branch>` retornar PR; branches sem PR associado mas mergeadas (ex.: `feature/Devin-20260916-gap-analysis`) podem ser deletadas se `git merge-base --is-ancestor <branch> main` confirmar.
- **Input → Output:** `git branch -a` pós-cleanup → só `main` + branches de PRs abertas.

## 6. Acceptance Criteria

- **CA-001:** `git branch --merged main | wc -l` = 0 (excluindo main).
- **CA-002:** `git branch -r --merged origin/main` = 0 (excluindo origin/main, origin/HEAD).
- **CA-003:** nenhuma branch com PR aberta ou commits não-mergeados foi deletada.
- **CA-004:** relatório do cleanup registrado no memory report.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Inventário + mapa branch→PR | lista com evidência por branch |
| T2 | Delete locais (`-d`) | `git branch` limpo |
| T3 | Delete remotas mergeadas | `git branch -r` limpo |
| T4 | Atualizar memory report | arquivo atualizado |

## 8. Organization Guardrails

- Deleção de branches = operação destrutiva → **executar só com aprovação explícita** e evidência por branch.
- Nunca deletar `main`, `develop`, ou branch de PR aberta.

## 9. Definition of Done

- [ ] Zero branches mergeadas locais/remotas.
- [ ] Evidência (branch→PR) registrada.
- [ ] Memory report atualizado.
