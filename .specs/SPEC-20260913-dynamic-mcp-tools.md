# SPEC-20260913-dynamic-mcp-tools

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `dynamic-mcp-tools` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` + `ModelContextProtocol.AspNetCore` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `[A DEFINIR — GitHub Issue via /create-issues]` |
| Status | `Approved` |

## 1. User Story

**As a** connected MCP agent
**I want** `tools/list` to reflect the currently active knowledge sources and `tools/call` to execute real searches/reads/writes
**So that** I always invoke tools that match the live catalog — and get a `ToolListChangedNotification` when the catalog changes.

**Problem context:** Static `[McpServerTool]` registration can't reflect runtime catalog changes. Tools must be generated from the database and clients notified on change (SDK supports this in stateful mode — which SPEC-01 already requires for SSE).

## 2. Scope

**In scope:**
- `IDynamicToolCatalog` (Server): builds `McpServerTool` instances per request via `McpServerTool.Create(delegate)` / derived `McpServerTool`, fed by the live DB:
  - `search_knowledge(query, topK)` — unified vector search across active sources; returns ranked hits.
  - `ask_knowledge(question, topK)` — question-oriented retrieval: vector search → aggregated answer context (ranked passages with source/title citations) ready for the calling LLM to synthesize.
  - `query_{source_slug}(query, topK)` — one per active source; slug = lowercase, non-alphanumeric → `_`, deduplicated.
  - `read_document(path, source?)` — reads a note relative to a vault root; `source` selects the vault slug when >1 active ObsidianVault (defaults to first).
  - `write_note(path, content, tags?, source?)` — writes a markdown note into a vault (frontmatter from tags) + incremental re-index.
  - `write_knowledge(title, content, source?, tags?)` — persists content INTO the knowledge base: on ObsidianVault sources writes a `.md` note; on other source types stores an indexed `KnowledgeDocument` (chunked + embedded, immediately searchable).
  - `ask_question`, `read_wiki_structure`, `read_wiki_contents` — DeepWiki proxy tools (upstream-identical names) when `DeepWiki:Enabled` (SPEC-07).
- Dynamic registration via SDK `McpServerHandlers` (custom `ListToolsRequest`/`CallToolRequest` handlers backed by `IDynamicToolCatalog`) — so `tools/list` always reflects the current DB state, no stale cache.
- `IToolCatalogChangeNotifier`: on source create/update/activate/deactivate/delete, rebuild catalog and broadcast `NotificationMethods.ToolListChangedNotification` via `McpServer.SendNotificationAsync` (requires stateful — satisfied by SPEC-01).
- `McpResourceProvider` via SDK resource handlers: `knowledge://sources` (catalog JSON), `obsidian://{source-slug}/{path}` per indexed document; `resources/read` returns contents.
- Error semantics per SDK: invalid params → `McpProtocolException(McpErrorCode.InvalidParams)`; execution failures → `CallToolResult { IsError = true }` (SDK auto-wraps non-protocol exceptions).

**Out of scope:**
- `tools/list` pagination (`nextCursor`) — catalogs are small.
- Per-session tool filtering / per-user scoping.
- Prompts capability.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Mcp/` — the catalog + handlers, registered through `AddKnowledgeHubMcp()` (SPEC-01). Depends on SPEC-02 (catalog/search) and SPEC-03 (ingestion/embeddings).

**Files to read before implementing:**
- `.specs/SPEC-20260913-mcp-sse-engine.md` · `.specs/SPEC-20260913-knowledge-sources.md` · `.specs/SPEC-20260913-ingestion-obsidian.md`
- SDK tools doc: `McpServerTool.Create`, `McpServerHandlers`, `McpProtocolException`, `SendNotificationAsync`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Mcp/IDynamicToolCatalog.cs
src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs
src/KnowledgeHub.Server/Mcp/ToolSlugger.cs
src/KnowledgeHub.Server/Mcp/DynamicMcpHandlers.cs
src/KnowledgeHub.Server/Mcp/ToolCatalogChangeNotifier.cs
src/KnowledgeHub.Server/Mcp/ObsidianNoteWriter.cs
src/KnowledgeHub.Server/Mcp/KnowledgeResourceProvider.cs
tests/KnowledgeHub.Tests.Unit/Mcp/*Tests.cs
tests/KnowledgeHub.Tests.Integration/McpToolsTests.cs
```

## 4. Requirements

### RF-001: Dynamic tool catalog
- **Description:** `IDynamicToolCatalog.GetToolsAsync()` returns `search_knowledge` + `ask_knowledge` + `write_knowledge` always; one `query_{slug}` per active source (description = source name + semantic description telling the LLM when to use it); `read_document`/`write_note` only with ≥1 active ObsidianVault source; DeepWiki tools when enabled.
- **Input → Output:** DB catalog state → `McpServerTool[]` with JSON-Schema-derived `inputSchema`.

### RF-002: tools/list + tools/call handlers
- **Description:** Custom `McpServerHandlers` resolve `ListTools`/`CallTool` against the catalog per request; unknown tool → `McpProtocolException(McpErrorCode.MethodNotFound)` or `isError` result per SDK norms; missing required arg → `McpErrorCode.InvalidParams` with an actionable message.
- **Input → Output:** `tools/call {name, arguments}` → `CallToolResult` text content (markdown hits: title, uri, score, chunk).

### RF-002b: ask_knowledge
- **Description:** `ask_knowledge {question, topK?}` runs the same vector search as `search_knowledge` but returns an aggregated answer context — passages concatenated with `source/title/score` citations, capped by token budget — so the calling LLM can answer directly.
- **Input → Output:** question → formatted context text.

### RF-002c: write_knowledge
- **Description:** `write_knowledge {title, content, source?, tags?}` upserts a `KnowledgeDocument` in the target source: ObsidianVault → writes `{title}.md` under vault root (frontmatter tags) via the same path-safety rules; other sources → persists `RawContent`, chunks + embeds immediately. Title → slug filename for vault writes.
- **Input → Output:** content → `documentId` + chunk count summary.

### RF-003: Obsidian tools
- **Description:** `read_document {path, source?}` reads under the vault root (traversal rejected); `write_note {path, content, tags?, source?}` writes UTF-8 markdown, generates frontmatter when tags given, triggers incremental re-index. `source` disambiguates multiple vaults (slug); absent → first active vault.
- **Rules:** relative paths only; `..` rejected; non-`.md` extension coerced; canonical path must stay under vault root.

### RF-004: Resources
- **Description:** `resources/list` → `knowledge://sources` + `obsidian://{slug}/{path}` per indexed document; `resources/read` returns catalog JSON / note content respectively.

### RF-005: Change notification
- **Description:** Catalog mutations (source CRUD, activate/deactivate) rebuild the tool set and broadcast `notifications/tools/list_changed` to connected sessions — clients refresh without restart.

## 5. API Contract

MCP JSON-RPC via SPEC-01 transports (SDK-defined schemas).

`tools/call`:
```json
{ "method": "tools/call", "params": { "name": "query_meu_vault", "arguments": { "query": "arquitetura", "topK": 3 } } }
```
Result:
```json
{ "content": [ { "type": "text", "text": "### Nota X\nscore: 0.81\n..." } ], "isError": false }
```

## 6. Acceptance Criteria

- [ ] **Given** 2 active sources **when** `tools/list` **then** `search_knowledge`, `ask_knowledge`, `write_knowledge` + 2 `query_*` tools (+ Obsidian/DeepWiki tools as applicable).
- [ ] **Given** a source is deactivated **when** `tools/list` re-runs **then** its `query_*` tool is gone — and connected clients receive `tools/list_changed`.
- [ ] **Given** indexed content **when** `tools/call search_knowledge {query}` **then** top-K ranked snippets.
- [ ] **Given** indexed content **when** `tools/call ask_knowledge {question}` **then** aggregated answer context with source citations.
- [ ] **Given** `write_knowledge {title, content}` on a vault source **then** `{title-slug}.md` exists on disk and is immediately searchable via `search_knowledge`.
- [ ] **Given** `write_knowledge` on a non-vault source **then** a `KnowledgeDocument` with chunks+embeddings exists in SQLite.
- [ ] **Given** an Obsidian vault **when** `read_document` valid path **then** full note content; **when** `write_note` **then** file on disk + re-indexed.
- [ ] **Given** `path: "../evil.md"` **when** read/write **then** `isError`/InvalidParams — nothing outside the vault.
- [ ] **Given** `resources/read knowledge://sources` **then** active catalog JSON.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Slug collision | "Meu Vault!" vs "meu-vault" | `query_meu_vault`, `query_meu_vault_2` |
| Tool for inactive source | `query_x` post-deactivate | `isError`/method-not-found |
| Missing `query` | `{}` | InvalidParams naming the field |
| Unknown tool | `tools/call nope` | protocol error or `isError` per SDK |

## 7. Task Plan

- [ ] **T1 — Discovery:** confirm `McpServerHandlers`/`McpServerTool.Create` API on the pinned SDK version.
- [ ] **T2 — Implementation:** slugger → catalog → handlers → notifier → resource provider → obsidian writer → DI wire-up.
- [ ] **T3 — Verification:** unit tests (slug dedup, arg validation, path safety, catalog diff→notification); integration `tools/list` ↔ DB toggle round-trip.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; manual JSON-RPC round-trip via curl (both `/mcp/message` and `/mcp`).
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** Obsidian read/write confined to vault root (canonical path check); tool outputs never include connection strings.
- **Scope:** synchronous tool execution only; no streaming progress in this MVP.
- **Architecture:** catalog/handlers in Server; McpEngine stays persistence-agnostic (SPEC-01).

## 9. Definition of Done

- [ ] All requirements implemented.
- [ ] Acceptance criteria covered by tests.
- [ ] Edge cases handled.
- [ ] `dotnet build` + `dotnet test` green; `tools/list` reactivity verified live.
- [ ] Guardrails respected.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Should `write_note`/`write_knowledge` require a confirmation flag? Recommended: no — agents are expected to write; revisit on misuse.
- `[A DEFINIR]` `ask_knowledge` does not call an LLM — it returns assembled retrieval context. Recommended: keep LLM-free (client LLM synthesizes); a real answering mode would need an `IChatClient` provider (backlog).
