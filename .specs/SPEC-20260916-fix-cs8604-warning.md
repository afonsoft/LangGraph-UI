# SPEC-20260916-fix-cs8604-warning

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `fix-cs8604-warning` |
| Type | `Bugfix` (compilação limpa — regra do repo) |
| Stack | `.NET 10 / C# 14` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-fix-cs8604-warning` |
| Ticket | `#72` |
| Status | `Done` |

## 1. User Story

**As a** mantenedor
**I want** build sem warnings
**So that** a convenção "corrija warnings antes de commitar" (CLAUDE.md) volte a valer e warnings reais não se escondam atrás de ruído conhecido.

**Problem context:**
`dotnet build` emite `CS8604: Possible null reference argument for parameter 'path1' in Path.Combine` em `src/KnowledgeHub.Server/Ingestion/IngestionService.cs:396` — `root` é `string?` no ponto de uso (`Path.Combine(root, relativePath)`), embora o fluxo garanta não-nulo algumas linhas acima (o compilador não prova isso através do controle de fluxo atual). Carregado como pendência desde o gap-analysis-20260914.

## 2. Scope

**In scope:**
- Eliminar o warning com guard explícito (`if (string.IsNullOrEmpty(root)) return;`) ou null-forgiving justificado com comentário do invariante.

**Out of scope:**
- Refatorar a função além do mínimo para o warning.
- Tratar outros warnings (não há outros hoje).

## 3. Technical Context

**Files to read:**
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs` (~linha 380–400: origem de `root` e `relativePath`)

## 4. Requirements

### RF-001: Zero warnings no build
- **Description:** `dotnet build KnowledgeHub.slnx` → `0 Warning(s)`; comportamento inalterado (path inválido continua sendo ignorado).
- **Input → Output:** mesmos inputs → mesmos outputs; só o aviso some.

## 6. Acceptance Criteria

- **CA-001:** build com `0 Warning(s)` em todos os projetos.
- **CA-002:** suíte unit + integration verde.

## 7. Task Plan

| # | Tarefa | Arquivos |
|---|--------|----------|
| T1 | Guard/null-check em `root` antes do `Path.Combine` | `IngestionService.cs` |
| T2 | Build + test | — |

## 8. Organization Guardrails

- Mudança mínima — sem refactor adjacente.

## 9. Definition of Done

- [ ] Build sem warnings.
- [ ] Testes verdes.
