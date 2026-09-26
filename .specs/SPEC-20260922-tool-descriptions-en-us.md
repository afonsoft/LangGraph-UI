# SPEC-20260922-tool-descriptions-en-us

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `tool-descriptions-en-us` |
| Type | `Feature` (MCP contract change — descriptions, citation schema, one tool rename) |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260922-tool-descriptions-en-us` |
| Ticket | [#151](https://github.com/afonsoft/LangGraph-UI/issues/151) |
| Status | `Done` |
| Origin | Pedido direto do usuário (2026-09-22): traduzir descrições das tools para en-US, torná-las mais completas e genéricas (sem referência ao sistema), citação do `ask_knowledge` deve expor o nome do arquivo usável em `read_document`, `query_openclaw_vault` → `query_knowledge`. |

## 1. User Story

**As a** MCP client/LLM consuming the Knowledge MCP Hub tool catalog
**I want** tool names, descriptions and input schemas in English, self-explanatory and free of implementation-specific references (product names, config keys)
**So that** any agent can pick and use the right tool without knowing the host system internals.

**Problem context:**
All local tools are described in pt-BR (`KnowledgeToolsProvider`, `ObsidianToolsProvider`, `SourceQueryToolsProvider`) and leak internals: `Chat:Provider`, `ObsidianVault`, `KnowledgeHub`. `ask_knowledge` citations carry `Uri` but do not explicitly expose the vault-relative file path needed to call `read_document`. The per-source tool `query_openclaw_vault` is generated from the deployed source name "OpenClaw Vault" (`query_{slug}`) — it should surface as `query_knowledge`.

## 2. Scope

**In scope:**
- en-US rewrite of `Description` + every `inputSchema` field `description`/`examples` for all local tools: `search_knowledge`, `ask_knowledge`, `agent_chat`, `write_knowledge`, `read_document`, `write_note`, `query_{slug}` (dynamic), plus `KnowledgeResourceProvider` descriptions that mention Obsidian.
- Richer descriptions: what the tool does, when to use it, what it returns, and relations between tools (`ask_knowledge` → `read_document`; `search_knowledge` vs scoped `query_*`).
- `CitationDto` gains `path` (document-relative path, equal to `read_document`'s `path` argument; null for non-file sources); `ask_knowledge` text output prints the path.
- `query_{slug}` description template → en-US, drops the `({SourceType})` suffix, keeps the user-provided source name/description.
- Ops step on the deployed instance: rename source "OpenClaw Vault" → "Knowledge" so the generated tool becomes `query_knowledge` (data change, no code).
- Update pinned schemas in `McpContractTests.cs` in the same commit; update README/architecture tool tables where descriptions are quoted.

**Out of scope:**
- Upstream/proxied tools (firecrawl_*, tavily_*, deepwiki, context7, McpProxy) — already en-US, unchanged.
- `set_api_key_settings` — already en-US.
- Runtime error strings and result bodies (already en-US).
- No new MCP tools; no behavior change in search/write logic beyond the new citation field.
- Per-source custom tool naming (rejected — plain source rename suffices).

## 3. Technical Context

**Where the change happens:**
MCP tool catalog — `IToolProvider` implementations under `src/KnowledgeHub.Server/Mcp/ToolProviders/`. Descriptions and `JsonObject` schemas are literals in each provider; `SourceQueryToolsProvider` builds one tool per active source via `ToolSlugger.Assign`. `ask_knowledge` output is assembled in `KnowledgeToolsProvider.AskKnowledgeAsync` from `AskResponse.Citations` produced by `AnswerService.ExtractCitations` (`CitationDto` in `KnowledgeHub.Shared`). The `UriReference` of a vault document IS the vault-relative path `read_document` accepts — exposing it as `path` is a copy, not a lookup.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs`
- `src/KnowledgeHub.Server/Mcp/ToolProviders/ObsidianToolsProvider.cs`
- `src/KnowledgeHub.Server/Mcp/ToolProviders/SourceQueryToolsProvider.cs`
- `src/KnowledgeHub.Server/Mcp/KnowledgeResourceProvider.cs`
- `src/KnowledgeHub.Server/Services/AnswerService.cs` (`ExtractCitations`, `BuildUserPrompt`)
- `src/KnowledgeHub.Shared/` — `CitationDto` definition
- `tests/KnowledgeHub.Tests.Integration/McpContractTests.cs` (pinned schemas)
- `tests/KnowledgeHub.Tests.Unit/` — `AnswerService` citation tests

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs   # 4 descriptions + 4 schemas + citation text
src/KnowledgeHub.Server/Mcp/ToolProviders/ObsidianToolsProvider.cs    # 2 descriptions + 2 schemas
src/KnowledgeHub.Server/Mcp/ToolProviders/SourceQueryToolsProvider.cs # dynamic description + schema
src/KnowledgeHub.Server/Mcp/KnowledgeResourceProvider.cs              # "Obsidian note" → generic
src/KnowledgeHub.Shared/Contracts/… (CitationDto)                     # + Path property
src/KnowledgeHub.Server/Services/AnswerService.cs                     # ExtractCitations fills Path
tests/KnowledgeHub.Tests.Integration/McpContractTests.cs              # re-pin schemas
tests/KnowledgeHub.Tests.Unit/…                                     # citation path coverage
README.md / docs/architecture/system-architecture.md                  # quoted descriptions, if stale
```

## 4. Requirements

### RF-001: en-US, generic, richer tool descriptions
- **Description:** Every local tool `Description` is rewritten in en-US, states purpose + return shape + when to use, and contains no system-specific references: no `KnowledgeHub`, `Chat:Provider`, `Obsidian`/`ObsidianVault` product names. Vault tools say "markdown vault source"; chat-dependent tools say "a configured chat provider".
- **Rules:** reference texts (implementation may adjust wording but not meaning):
  - `search_knowledge`: "Unified semantic search across all active knowledge sources. Returns ranked passages with source name, document title, score and URI. Use for exploratory lookups; use a scoped query_* tool to search a single source."
  - `ask_knowledge`: "Answers a natural-language question using the indexed knowledge base. When a chat provider is configured, returns a synthesized answer with [n] citations — each citation includes the document title, source name and file path, which can be passed to read_document to fetch the full document. Without a provider (or generate=false) returns the raw aggregated context."
  - `agent_chat`: "Multi-step agent: iterates model → tools → model over the live tool catalog until it can answer the prompt. Read-only tools are available by default; set allowWrite to expose write tools. Requires a configured chat provider."
  - `write_knowledge`: "Persists content into the knowledge base. For markdown-vault sources it creates a .md file; for other sources it stores a document that is indexed and immediately searchable."
  - `read_document`: "Reads the full content of a markdown document from an active vault source, addressed by its vault-relative path."
  - `write_note`: "Writes a markdown note into an active vault source and re-indexes it. Appends .md when missing. Fails on read-only vaults."
  - `query_{slug}`: $"Semantic search scoped exclusively to the '{source.Name}' source" + ` — {source.Description}` when set.
- **Input → Output:** `tools/list` → en-US descriptions for all local tools.

### RF-002: en-US input schemas
- **Description:** All `inputSchema` property `description`s and `examples` for the tools above are en-US (e.g. "Text or question to search", "Source slug (default: all active sources)", "Vault-relative note path (.md appended if missing)").
- **Rules:** example values also in English ("Summarize this week's notes", "Example note", "# Title\n\nMarkdown content."); JSON structure, names, required fields and defaults unchanged.

### RF-003: Citation file path for `read_document`
- **Description:** `CitationDto` gains `Path` (string?). `ExtractCitations` sets it to `UriReference` when the source is a vault source (file-backed), else null. `ask_knowledge` text output prints `[n] Title — Source (path: {Path})` when `Path` is present, else `(uri)`.
- **Rules:** `path` is exactly the value accepted by `read_document.path` for vault documents; existing `Uri`/`Title`/`Source`/`Score` fields unchanged; `structuredContent.citations[].path` serialized camelCase.

### RF-004: `query_knowledge` on the deployed instance
- **Description:** Rename the deployed source "OpenClaw Vault" to "Knowledge" (`PUT /api/sources/{id}` or UI) so `ToolSlugger` yields `query_knowledge`.
- **Rules:** ops step executed against the running container after deploy; verified via `tools/list` showing `query_knowledge` and no `query_openclaw_vault`. Slug collisions fall back to `query_knowledge_2` (existing ToolSlugger behavior — flag to user if it happens).

**Business rules / invariants:**
- Tool names are unchanged except the data-driven `query_openclaw_vault` → `query_knowledge`.
- `annotations.readOnlyHint` mapping unchanged (`write_knowledge`, `write_note`, `set_api_key_settings` remain write).
- Descriptions describe capability, never deployment internals.

## 5. API Contract

`tools/list` payload changes: `description` strings and `inputSchema` contents for the 6 static local tools + dynamic `query_*` tools. `tools/call ask_knowledge` `structuredContent.citations[]` gains `path`. REST `PUT /api/sources/{id}` reused for the rename — no endpoint changes.

**Expected errors:** unchanged.

## 6. Acceptance Criteria

- [x] **Given** `tools/list` **when** inspecting any local tool **then** `description` and all `inputSchema` field descriptions/examples are en-US and contain none of: `KnowledgeHub`, `Chat:Provider`, `Obsidian`, pt-BR words. — contract test asserting absence of a pt-BR marker list + pinned schemas
- [x] **Given** an indexed vault document **when** `ask_knowledge` synthesizes an answer **then** `structuredContent.citations[].path` equals the vault-relative path and the text shows `(path: …)`; feeding that path to `read_document` returns the document.
- [x] **Given** a citation to a non-vault source **when** rendered **then** `path` is null and the text falls back to `(uri)`.
- [x] **Given** an active source **when** `tools/list` runs **then** its `query_*` description is en-US, names the source, and has no `(SourceType)` suffix.
- [x] **Given** the deployed instance **when** the source is renamed to "Knowledge" **then** `tools/list` exposes `query_knowledge` and no `query_openclaw_vault`. — verified live 2026-09-22

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Source without description | `query_{slug}` for it | description ends after `'{name}' source.` — no dangling `—` |
| Slug collision on rename | two sources slug to `knowledge` | second gets `query_knowledge_2` (existing) — surface to user |
| ask_knowledge without provider | `generate` unresolved | raw context path; citations list unaffected |
| Model emits no [n] | all passages attached | every citation still carries `path` |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** read section-3 files; locate `CitationDto`; check `SearchResultItem` for a source-type signal to gate `Path`.
- [x] **T2 — Implementation:** rewrite descriptions/schemas (RF-001/RF-002); add `CitationDto.Path` + `ExtractCitations` fill + text rendering (RF-003); `query_*` template (RF-001); resource description.
- [x] **T3 — Tests:** re-pin `McpContractTests` schemas; unit test `ExtractCitations` path fill for vault vs non-vault; integration test asserting no pt-BR/system markers in local descriptions.
- [x] **T4 — Validation:** `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.
- [x] **T5 — Done + PR:** `Status = Done`, PR on `feature/Devin-20260922-tool-descriptions-en-us`.
- [x] **T6 — Deploy/ops:** redeploy container; rename source → verify `query_knowledge` (RF-004).

**7.1 Validation strategy:** .NET — unit tests for `ExtractCitations`/formatting; integration contract tests; coverage must not decrease.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260922-tool-descriptions-en-us` from `main`; never commit to `main`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Contract:** `McpContractTests` pins updated in the same commit (public-interface rule documented in that file).
- **Scope:** no upstream provider description changes; no new tools.
- **Security:** no secrets; descriptions must not leak env/config key names.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests.
- [x] Edge cases handled.
- [x] `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes` green.
- [x] Guardrails respected.
- [x] Deployed instance exposes `query_knowledge` (RF-004 verified post-deploy).

**Next action after DoD:** set `Status = Done` and open the PR referencing the ticket.

## Open Questions / Pending Ambiguity

- None — quatro decisões aprovadas pelo usuário em 2026-09-22 (escopo total, rename da source, campo `path`, remoção de referências ao sistema).
