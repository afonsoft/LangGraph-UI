# SPEC-20260916-claude-md-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `claude-md-sync` |
| Type | `Docs` (harness/context sync) |
| Stack | `Markdown` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-claude-md-sync` |
| Ticket | `GAP-documentation-claude-md-stale` |
| Status | `Approved` |

## 1. User Story

**As a** agente/engenheiro novo no repo
**I want** que `CLAUDE.md` descreva as features realmente implementadas
**So that** o contexto inicial não omita auth, proxies upstream e cache — evitando decisões baseadas em estado de ~20260915.

**Problem context:**
`CLAUDE.md` "Features implementadas" lista o estado pós-E17 mas omite: `SPEC-20260914-auth-login` (tela de login + policies), proxies upstream (`deepwiki`, `firecrawl`, `tavily` tools + `IntegrationSecretStore`), SPEC-20260915-* (rename, monitor, sources-edit, wasm boot fixes) e `SPEC-20260916-performance-memory-cache` (Redis opt-in). AGENTS.md/CLAUDE.md são citados como fonte de verdade para harness — drift neles contamina toda sessão futura.

## 2. Scope

**In scope:**
- Atualizar a lista de features do CLAUDE.md para refletir todas as SPECs `Done`.
- Conferir que comandos e convenções seguem corretos (build/test/run/format, branch policy).

**Out of scope:**
- Reescrever estrutura do arquivo ou traduzir.
- Atualizar `AGENTS.md` além do estritamente necessário (ele é thin e aponta para CLAUDE.md).

## 3. Technical Context

**Files to read:**
- `CLAUDE.md`, `AGENTS.md`
- `.specs/` (lista de SPECs Done — fonte da verdade)
- `.claude/memory/gap-analysis-20260916.md`

## 4. Requirements

### RF-001: Lista de features sincronizada
- **Description:** a seção de features do CLAUDE.md menciona cada SPEC Done, em uma linha por tema — incluindo auth-login, proxies upstream, cache Redis opt-in, monitor/activity, wasm boot fixes.
- **Rules:** conciso — CLAUDE.md é índice, não enciclopédia; detalhes ficam nas SPECs.

### RF-002: Comandos conferidos
- **Description:** comandos do CLAUDE.md executáveis conforme documentado (build/test/format/run/ef).

## 6. Acceptance Criteria

- **CA-001:** toda SPEC `Done` tem menção correspondente (por tema) no CLAUDE.md — verificável por checklist.
- **CA-002:** nenhum comando documentado falha quando executado.

## 7. Task Plan

| # | Tarefa | Arquivos |
|---|--------|----------|
| T1 | Reescrever seção de features | `CLAUDE.md` |
| T2 | Conferir comandos | `CLAUDE.md` |

## 8. Organization Guardrails

- CLAUDE.md como índice; sem duplicar conteúdo das SPECs.

## 9. Definition of Done

- [ ] Features listadas cobrem 100% das SPECs Done.
- [ ] Comandos verificados.
