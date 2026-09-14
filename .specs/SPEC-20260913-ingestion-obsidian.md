# SPEC-20260913-ingestion-obsidian

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ingestion-obsidian` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `#4` |
| Status | `Done` |

## 1. User Story

**As a** platform administrator
**I want** a local Obsidian vault folder to be continuously watched and indexed (markdown parsing, frontmatter, chunking, embeddings)
**So that** agents querying the knowledge hub always see fresh note content without manual re-upload.

**Problem context:** Knowledge lives in Markdown files on disk. The system must detect creates/changes/deletes incrementally and keep the SQLite index consistent.

## 2. Scope

**In scope:**
- `MarkdownNoteParser`: splits YAML frontmatter from body; extracts `tags`, title (`# H1` or filename); collects `[[WikiLink]]` references into metadata (not resolved into content).
- `MarkdownChunker`: header-aware chunking — splits on `#`/`##`/`###` boundaries first, then paragraphs; target ~500 tokens (approx `chars/4`), overlap ~50 tokens; never emits empty chunks.
- `IEmbeddingProvider` abstraction — pluggable and fully configurable:
  - `DeterministicEmbeddingProvider` (default, offline): feature-hashing bag-of-words → 384-dim L2-normalized vector.
  - `OllamaEmbeddingProvider`: `POST {Endpoint}/api/embeddings` `{model, input}` (Ollama local, ex. `nomic-embed-text`, `mxbai-embed-large`).
  - `OpenAiEmbeddingProvider`: `POST {Endpoint}/v1/embeddings` `{model, input}` + `Authorization: Bearer {ApiKey}` — covers OpenAI, Azure-compatible and any OpenAI-compatible server (LM Studio, vLLM, etc.).
  - Config: `Embeddings:{Provider, Endpoint, ApiKey, Model, Dimensions}` — provider selected by name (`deterministic`|`ollama`|`openai`), `Dimensions` validates/slices the returned vector.
- `EmbeddingModel` identity (`{provider}:{model}`) stamped on every chunk; model swap ⇒ vectors must be regenerated (SPEC-02 RF-006 skips mismatched vectors and counts them).
- `IIngestionService` / `IngestionService`: full scan of a source directory; per-file SHA-256 `ContentHash` short-circuit (unchanged files skipped); upsert `KnowledgeDocument` by `(KnowledgeSourceId, UriReference)`; replace chunks on change; delete documents whose files disappeared.
- `VaultWatcherService` (`BackgroundService`): one `FileSystemWatcher` per active `ObsidianVault` source (debounced ~500ms, handles `Created/Changed/Deleted/Renamed`); rebuilds watchers when sources change; periodic auto-sync respecting `SyncIntervalMinutes` when `AutoSyncEnabled`.
- Sync concurrency guard: at most one sync per source at a time.

**Out of scope:**
- Other connectors (WebPage, RestApi, SqlDatabase, DocumentFile) — registered no-op per SPEC-02.
- Binary file ingestion, image OCR.
- `.obsidian/` config directory indexing (explicitly excluded).
- ONNX Runtime local provider (`Microsoft.ML.OnnxRuntime` + all-MiniLM-L6-v2) — interface-ready, postponed to backlog (adds ~90 MB native payload to the single-file binary).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Ingestion/` + `src/KnowledgeHub.Server/Embeddings/` + `BackgroundServices/`. Depends on SPEC-02 entities/DbContext.

**Files to read before implementing:**
- `.specs/SPEC-20260913-knowledge-sources.md`
- `src/KnowledgeHub.Server/Domain/Entities/*` · `Data/KnowledgeHubDbContext.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Ingestion/ParsedNote.cs
src/KnowledgeHub.Server/Ingestion/MarkdownNoteParser.cs
src/KnowledgeHub.Server/Ingestion/MarkdownChunker.cs
src/KnowledgeHub.Server/Ingestion/IIngestionService.cs
src/KnowledgeHub.Server/Ingestion/IngestionService.cs
src/KnowledgeHub.Server/Embeddings/IEmbeddingProvider.cs
src/KnowledgeHub.Server/Embeddings/EmbeddingOptions.cs
src/KnowledgeHub.Server/Embeddings/DeterministicEmbeddingProvider.cs
src/KnowledgeHub.Server/Embeddings/OllamaEmbeddingProvider.cs
src/KnowledgeHub.Server/Embeddings/OpenAiEmbeddingProvider.cs
src/KnowledgeHub.Server/Embeddings/EmbeddingMath.cs
src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs
src/KnowledgeHub.Server/Program.cs (wire-up)
tests/KnowledgeHub.Tests.Unit/Ingestion/*Tests.cs
tests/KnowledgeHub.Tests.Unit/Embeddings/*Tests.cs
```

## 4. Requirements

### RF-001: Markdown parsing
- **Description:** Parse `.md` files: extract YAML frontmatter (`---` fenced block via YamlDotNet; tolerate malformed YAML by ignoring it), body text, `tags` (frontmatter `tags`/`tag` + inline `#tag`), title (first `# H1`, else filename), `[[WikiLink]]` targets.
- **Input → Output:** raw `.md` text → `ParsedNote { Title, Frontmatter, Body, Tags, WikiLinks }`.

### RF-002: Header-aware chunking
- **Description:** Split `Body` on markdown headers keeping the header line with its section; when a section exceeds `maxTokens` (default 500, ~2000 chars), split on paragraph boundaries; apply `overlapTokens` (default 50) by prefixing trailing text of the previous chunk. Configurable via `appsettings` (`Ingestion:MaxTokens`, `Ingestion:OverlapTokens`).
- **Rules:** chunk count ≥ 1 for non-empty docs; no chunk empty/whitespace; indices sequential.

### RF-003: Configurable embeddings
- **Description:** `IEmbeddingProvider.EmbedAsync(text)` → `float[]` (L2-normalized). Providers selected via `Embeddings:Provider`:
  - `deterministic` (default): feature-hashing → 384 dims; identical texts → identical vectors; shared terms → higher cosine.
  - `ollama`: `POST {Endpoint}/api/embeddings` `{model, input}` → `embeddings[0]`.
  - `openai`: `POST {Endpoint}/v1/embeddings` `{model, input}` + Bearer `{ApiKey}` → `data[0].embedding`.
- **Rules:** `Dimensions` mismatched with provider output → startup warning; HTTP failure → sync marks the chunk failed, logs sanitized error (never the ApiKey); batch embed calls per document when the provider supports array `input`.
- **Input → Output:** string → `float[]` persisted on `DocumentChunk.Embedding` + `EmbeddingModel`.

### RF-004: Incremental sync
- **Description:** `SyncSourceAsync(sourceId)` enumerates `**/*.md` under `configuration.path` (excluding `.obsidian/` and hidden dirs); skips files whose SHA-256 hash matches `ContentHash`; upserts changed files (re-chunk + re-embed); removes documents for deleted files; updates `LastSyncAt` and returns a `SyncResult` summary.
- **Rules:** missing/invalid path → result `status: "failed"` with error message, no exception escaping to the endpoint.

### RF-005: Continuous watching
- **Description:** `VaultWatcherService` maintains watchers for active ObsidianVault sources; file events debounce then trigger incremental sync of that file only; watcher set refreshes on source create/update/activate/deactivate/delete and honors `SyncIntervalMinutes` for full re-scan.
- **Rules:** a file write burst produces a single sync; service failures are logged, never crash the host.

## 5. API Contract

No public endpoint added here — sync is exposed via `POST /api/sources/{id}/sync` (SPEC-02) returning:

```json
{ "sourceId": "...", "status": "completed", "documentsProcessed": 12, "documentsSkipped": 40, "documentsRemoved": 1, "chunksCreated": 87, "durationMs": 230 }
```

## 6. Acceptance Criteria

- [ ] **Given** a vault with 3 `.md` files **when** sync runs **then** 3 documents with ≥1 chunk each exist in SQLite.
- [ ] **Given** a note with frontmatter `tags: [a, b]` and `[[Other]]` links **when** parsed **then** tags and wikilinks are captured and frontmatter is stripped from the body.
- [ ] **Given** a note with a 3000-char section **when** chunked with max 500 tokens **then** it splits into ≥2 overlapping chunks on paragraph boundaries.
- [ ] **Given** an unchanged file (same hash) **when** re-synced **then** it is skipped (`documentsSkipped` increments).
- [ ] **Given** a deleted `.md` file **when** sync runs **then** its document and chunks are removed.
- [ ] **Given** a watcher active **when** a `.md` file is edited **then** the file is re-indexed within ~2s.
- [ ] **Given** identical input text **when** embedded twice **then** vectors are byte-identical.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Malformed YAML frontmatter | `---\n: bad\n---` | body indexed, frontmatter ignored |
| Empty `.md` file | 0 bytes | document indexed with 0 chunks or skipped gracefully |
| Nonexistent vault path | `/nope` | sync result `failed`, API returns error status |
| File locked during read | concurrent write | logged + retried on next event, no crash |
| Path traversal in vault | `../` symlinks | files outside root are not indexed |

## 7. Task Plan

- [ ] **T1 — Discovery:** read SPEC-02 entities/DbContext.
- [ ] **T2 — Implementation:** parser → chunker → embedding provider → ingestion service → watcher.
- [ ] **T3 — Verification:** unit tests for parser/chunker/embedder determinism; integration test for sync over a temp vault dir.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; manual sync of a real folder via `POST /api/sources/{id}/sync`.
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** vault paths must resolve under the configured root; file reads are bounded (skip files > 5 MB); never index `.obsidian/` contents.
- **Scope:** only `.md` files; no OCR, no binary parsing.
- **Architecture:** ingestion depends on domain abstractions only; no ASP.NET types inside parser/chunker/embedder (unit-testable without a host).

## 9. Definition of Done

- [ ] All requirements implemented.
- [ ] Acceptance criteria covered by tests.
- [ ] Edge cases handled.
- [ ] `dotnet build` + `dotnet test` green.
- [ ] Guardrails respected; no PII/tokens in logs.

## Open Questions / Pending Ambiguity

- None blocking. Embedding provider upgrade path documented in `.claude/memory/orchestrator_stats.md` backlog.
