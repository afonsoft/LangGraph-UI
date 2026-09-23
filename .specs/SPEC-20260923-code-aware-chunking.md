# SPEC-20260923-code-aware-chunking

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `code-aware-chunking` |
| Type | `Feature` (ingestion quality) |
| Stack | `.NET 10 / C# 14` — pure text processing, no new native deps |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-code-aware-chunking` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |
| Origin | `gap-analysis-20260923` — GAP-implementation-code-chunking (baixa). Proposal §9.1 (chunking orientado à estrutura). |

## 1. User Story

**As a** user indexing code/config-bearing sources
**I want** chunking that respects code structure (namespaces, classes, methods) and config blocks instead of naive token windows
**So that** retrieved chunks are semantically coherent — a method isn't split mid-signature and a JSON/YAML block isn't torn apart.

**Problem context:**
All content goes through `MarkdownChunker.Chunk(text, maxTokens, overlap)` (`IngestionService.cs:106-109`, :262-265). It's structure-aware for markdown headings but treats `.cs`, `.json`, `.yaml`, `.sql` etc. as plain prose. The proposal requires preserving classes/methods/namespaces and config blocks.

## 2. Scope

**In scope:**
- `ITextChunker` abstraction: `IReadOnlyList<string> Chunk(string text, int maxTokens, int overlapTokens)`; `MarkdownChunker` becomes `MarkdownTextChunker : ITextChunker` (behavior preserved).
- Chunker selection by document kind: `SourceType`/file extension → `markdown` (existing), `code` (C#/generic brace-language aware: namespace→class→method boundaries via brace/indentation heuristics), `config` (JSON/YAML/XML: split on top-level keys/array items, keep parent path preamble), `prose` fallback.
- Chunk metadata: each `DocumentChunk` gains `ChunkKind` + `SymbolPath` (e.g. `Namespace.Type.Method`, `$.server.port`) — stored in a `Metadata` JSON column or dedicated columns; feeds the metadata filters from SPEC-20260923-retrieval-quality.
- Apply selection in both ingestion paths (vault + connectors) and `write_knowledge`.

**Out of scope:**
- Real parsing (Roslyn/tree-sitter) — heuristic line/brace-based chunking only; a Roslyn-accurate chunker can plug into `ITextChunker` later.
- Embedding-time enrichment (contextual chunk headers) — candidate for a later spec.
- Re-chunking existing indexes automatically (re-sync required; documented).

## 3. Technical Context

`MarkdownChunker` is a static class today — introduce `ITextChunker` and resolve via `IEnumerable<ITextChunker>`/`ChunkerSelector` in `IngestionService`. `DocumentChunk` entity extension requires an EF migration. `DocumentFileConnector` already classifies extensions (`SupportedExtensions`).

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Ingestion/MarkdownChunker.cs`, `MarkdownNoteParser.cs`, `IngestionService.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/DocumentFileConnector.cs`
- `src/KnowledgeHub.Server/Domain/Entities/DocumentChunk.cs`
- `src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs` (`write_knowledge`)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Ingestion/Chunking/ITextChunker.cs        (new)
src/KnowledgeHub.Server/Ingestion/Chunking/ChunkerSelector.cs     (new — kind/extension → chunker)
src/KnowledgeHub.Server/Ingestion/Chunking/CodeTextChunker.cs     (new)
src/KnowledgeHub.Server/Ingestion/Chunking/ConfigTextChunker.cs   (new)
src/KnowledgeHub.Server/Ingestion/MarkdownChunker.cs              (modify — implement ITextChunker)
src/KnowledgeHub.Server/Domain/Entities/DocumentChunk.cs          (modify — ChunkKind/SymbolPath)
src/KnowledgeHub.Server/Ingestion/IngestionService.cs             (modify — use selector)
src/KnowledgeHub.Server/Migrations/*                              (new)
tests/KnowledgeHub.Tests.Unit/Ingestion/Chunking/*                (new)
```

## 4. Requirements

### RF-001 — Chunker abstraction + selection
- **Description:** `ITextChunker` with `CanHandle(ChunkKind)` or a selector mapping `(sourceType, extension) → chunker`; unknown kinds → `prose`/`markdown` fallback (current behavior).
- **Rules:** selection is deterministic and unit-tested per extension matrix.

### RF-002 — Code chunker
- **Description:** Splits C#-like code at top-level boundaries: file-header/using block chunk, then per namespace → type → member, keeping each member whole when ≤maxTokens; oversized members split at blank-line boundaries with a `SymbolPath` preamble (`// Namespace.Type.Method (part k/n)`).
- **Rules:** brace/indent heuristics only — no Roslyn; never emits a chunk that starts mid-expression when a boundary exists above it; partial chunks carry the symbol context line.

### RF-003 — Config chunker
- **Description:** JSON/YAML/XML documents split at top-level properties/elements; each chunk prefixed with its path preamble (`$.a.b:` / `a.b:` / `<a><b>`). Undersized groups merge up to maxTokens; oversized single nodes fall back to line-window split with the path preamble.
- **Rules:** invalid JSON/YAML/XML → fall back to prose chunking (never throw on malformed input).

### RF-004 — Chunk metadata
- **Description:** `DocumentChunk` persists `ChunkKind` (`markdown|code|config|prose`) and `SymbolPath` (nullable); `SearchResultItem.Metadata` (from SPEC-20260923-retrieval-quality) surfaces them.
- **Rules:** additive migration; existing chunks get `ChunkKind=markdown`, `SymbolPath=null`.

### RF-005 — Wiring
- **Description:** `IngestionService` + `write_knowledge` resolve chunker per document kind; `SyncResultDto` unchanged.

**Business rules / invariants:**
- Same input + same config ⇒ identical chunks (deterministic — required for dedup stability).
- `MarkdownChunker` output byte-identical for existing markdown inputs (regression fixture).

## 5. API Contract

None (internal). New chunk metadata flows through the enriched `SearchResultItem` contract.

## 6. Acceptance Criteria

- [ ] **Given** a C# file with a class of 3 methods **when** chunked **then** each method is intact with `SymbolPath` set.
- [ ] **Given** a 2000-token method > maxTokens=500 **when** chunked **then** parts carry `// Ns.Type.Method (part k/n)` preamble.
- [ ] **Given** a JSON config **when** chunked **then** chunks carry `$.path` preambles and never split a scalar value.
- [ ] **Given** malformed JSON **when** chunked **then** prose fallback — no exception.
- [ ] **Given** existing markdown fixtures **when** chunked by `MarkdownTextChunker` **then** output identical to current `MarkdownChunker` (regression).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| File-scoped namespace + records | modern C# | treated as members correctly |
| Minified JSON (one line) | `{"a":…200KB}` | line-window fallback with path preamble |
| Mixed doc (md + code fences) | markdown | markdown chunker (fences stay intact — existing behavior) |
| Extensionless file | `Dockerfile` | kind heuristic: shebang/`FROM` → config-ish prose fallback acceptable |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3; freeze current MarkdownChunker outputs as golden fixtures.
- [ ] **T2 — `ITextChunker` + selector + markdown adapter** (golden tests first).
- [ ] **T3 — `CodeTextChunker`** + unit tests on C# fixtures (nested types, file-scoped ns, big methods).
- [ ] **T4 — `ConfigTextChunker`** + unit tests (JSON/YAML/XML valid + malformed).
- [ ] **T5 — Entity + migration + IngestionService/write_knowledge wiring.**
- [ ] **T6 — Verification:** build/test/format green. Done + PR.

## 8. Organization Guardrails

- No external parser packages this round (heuristics only).
- Determinism is a hard requirement — dedup checksums assume stable chunking.
- Migration additive only.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Golden-file regression on markdown chunking green.
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- `.cs` only or generic brace languages (java/js/ts/go)? — implement generic brace-language heuristics with `.cs` as the tested target; other extensions map to `code` conservatively.
