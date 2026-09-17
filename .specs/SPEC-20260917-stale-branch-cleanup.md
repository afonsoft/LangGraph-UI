# SPEC-20260917-stale-branch-cleanup

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `stale-branch-cleanup` |
| Type | `Chore` (higiene operacional git) |
| Stack | `git / GitHub CLI` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | n/a (deleção de branches) |
| Ticket | #100 |
| Status | `Approved` |

## 1. User Story

**As a** mantenedor do repositório
**I want** remover branches cujo conteúdo já foi entregue em `main` por outro caminho
**So that** o namespace de branches reflita apenas trabalho ativo.

**Problem context:**
Gap-analysis r2 (2026-09-17): 3 branches residuais pós-cleanup —

- `origin/chore/Devin-20260917-harness-files` — merged (`524947b`) mas remota não deletada.
- `feature/Devin-20260915-rename-mcp-monitor` (local + remota) — `git cherry` marca commits como `+`, mas o conteúdo está em main: rename "Knowledge" (README `# Knowledge`), `McpMonitorDtos.cs`, `McpMonitorEventMapper.cs`, `McpMonitorReplayTests.cs` — entregue via outra linha de commits.
- `feature/Devin-20260916-specs-mobile-apikey-settings` (local + remota) — SPECs + feats entregues via `0107c70` e PRs subsequentes; SPECs constam `Done` em main.

## 2. Scope

**In scope:** deletar as 3 branches (local + remote) após verificação final de conteúdo.
**Out of scope:** qualquer branch com trabalho real não-entregue — se o diff de conteúdo mostrar delta relevante, a branch fica e vira item de backlog.

## 3. Technical Context

**Risks:** deleção é irreversível — confirmar ausência de commits únicos com `git cherry`/diff tip-a-tip antes de cada delete; documentar evidência.

## 4. Requirements

### RF-001: Verificação de conteúdo
- **Description:** para cada branch, provar que todo conteúdo relevante existe em `main` (diff nos arquivos tocados, não só `git cherry` — patches diferem quando main evoluiu).
- **Input → Output:** evidência registrada no memory report.

### RF-002: Deleção
- **Description:** `git branch -D` local (após RF-001, `-d` falha em unmerged) + `git push origin --delete` remota.
- **Input → Output:** `git branch -a` lista apenas `main` + branches de trabalho ativo.

## 6. Acceptance Criteria

- **CA-001:** 0 branches merged ou superseded locais/remotas.
- **CA-002:** evidência de verificação de conteúdo registrada por branch.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Diff conteúdo por branch vs main | diff vazio/superseded |
| T2 | Delete local + remote | `git branch -a` limpo |
| T3 | Registrar no memory report | arquivo atualizado |

## 8. Organization Guardrails

- Destrutivo — executar só após aprovação explícita.
- Não deletar nada com delta de conteúdo não-entregue.

## 9. Definition of Done

- [ ] 3 branches removidas ou justificadas.
- [ ] `git branch -a` mostra só trabalho ativo.
