# SPEC-20260917-harness-files

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `harness-files` |
| Type | `Docs` (harness do agente — arquivos de governança `.claude/`) |
| Stack | `Markdown` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `chore/Devin-20260917-harness-files` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** agente operando este repositório
**I want** que os artefatos de harness do checklist do orchestrator existam em `.claude/`
**So that** sessões futuras tenham contexto, regras e memória consistentes sem depender de arquivos fora do repo.

**Problem context:**
O audit do orchestrator (Phase 2) espera: `.claude/settings.json`, `.claude/rules/global-rules.md` + rules stack-scoped, `.claude/agents/` (review.md, plan.md, test.md), `.claude/MEMORY.md`, `.claude/CONTEXT.md`, `.claude/RULES.md`, `.claude/TOOLS.md`, `.claude/WORKFLOWS.md`, `.claude/README.md`. Hoje `.claude/` contém apenas `memory/` e `skills/` (`collect-sources.sh` 2026-09-17: CONTEXT.md ABSENT, MEMORY.md ABSENT, rules/ ABSENT, agents/ ABSENT). O projeto opera bem assim — a questão é se o harness completo é desejado ou se o checklist deve ser relaxado para este repo.

## 2. Scope

**In scope:**
- Decidir e documentar: (a) provisionar os arquivos ausentes via `/create-agent-harness`, ou (b) declarar no CLAUDE.md que o harness mínimo (`memory/` + `skills/` + CLAUDE.md/AGENTS.md) é o estado intencional.
- Se (a): criar `.claude/settings.json`, `.claude/rules/global-rules.md`, `.claude/agents/{review,plan,test}.md`, `.claude/{CONTEXT,MEMORY,RULES,TOOLS,WORKFLOWS}.md`, `.claude/README.md` — derivados do estado real do repo (stack .NET 10, comandos do CLAUDE.md).
- Se (b): registrar a decisão em CLAUDE.md/AGENTS.md para que futuras auditorias marquem REJEITADO em vez de CONFIRMADO.

**Out of scope:**
- Hooks/settings com efeitos colaterais (permissões destrutivas, hooks que executam comandos) — qualquer settings.json criado fica restrictivo e documentado.
- `.agents/` harness files (gitignored, gerenciado pelo skills CLI).
- Mudança de workflow CI.

## 3. Technical Context

**Where the change happens:** `.claude/` — apenas documentação/configuração de agente.

**Files to read before implementing:**
- `CLAUDE.md`, `AGENTS.md` (convenções existentes — fonte das rules)
- `.claude/memory/orchestrator_stats.md`, `gap-analysis-*.md` (decisões acumuladas → CONTEXT/MEMORY)
- `.agents/skills/create-agent-harness/` (templates oficiais)

**Risks:** duplicar CLAUDE.md em CONTEXT/RULES (mitigação: thin files que referenciam CLAUDE.md — padrão AGENTS.md-as-index); settings.json permissivo demais (mitigação: copiar modelo mínimo do create-agent-harness).

## 4. Requirements

### RF-001: Decisão registrada
- **Description:** escolha (a) ou (b) documentada — se (b), uma linha em CLAUDE.md: "harness mínimo intencional: memory/ + skills/; arquivos opcionais do checklist não são usados neste repo".
- **Input → Output:** decisão commitada.

### RF-002: (se (a)) Arquivos provisionados
- **Description:** cada arquivo do checklist existe e referencia — não duplica — CLAUDE.md como fonte da verdade.
- **Rules:** `settings.json` mínimo e seguro; rules derivadas das Hard/Soft rules do CLAUDE.md; agents stubs nos 3 papéis padrão.
- **Input → Output:** checklist do orchestrator Phase 2 passa sem gaps.

## 6. Acceptance Criteria

- **CA-001:** ou os arquivos existem (a), ou a decisão "harness mínimo intencional" está registrada (b).
- **CA-002:** próxima execução de gap-analysis não reporta os mesmos ABSENT como CONFIRMADO.

## 7. Task Plan

| # | Tarefa | Validação |
|---|--------|-----------|
| T1 | Decisão (a)/(b) com o usuário | resposta registrada |
| T2 | Executar escolha (provisionar ou documentar) | checklist/diff |
| T3 | Commit + atualizar memory report | arquivo atualizado |

## 8. Organization Guardrails

- Apenas documentação/config — nenhum código de produção.
- `settings.json` não pode conter permissões que executem comandos destrutivos sem prompt.

## 9. Definition of Done

- [ ] Decisão implementada e commitada.
- [ ] Checklist consistente em auditoria futura.
