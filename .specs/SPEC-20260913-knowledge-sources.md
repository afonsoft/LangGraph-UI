# SPEC-20260913-knowledge-sources

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `knowledge-sources` |
| Type | `Feature` |
| Stack | `.NET 10 / C# 14 + EF Core SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-knowledge-hub-platform` |
| Ticket | `[A DEFINIR — GitHub Issue via /create-issues]` |
| Status | `Approved` |

## 1. User Story

**As a** platform administrator
**I want** to register, edit, activate/deactivate and synchronize knowledge sources through a REST API
**So that** heterogeneous content (Obsidian vaults, web pages, documents, REST APIs, SQL databases) becomes available to agents via MCP.

**Problem context:** There is no persistence layer today. All catalog data, indexed documents and vector chunks must live in an embedded SQLite database — zero external infrastructure.

## 2. Scope

**In scope:**
- EF Core `KnowledgeHubDbContext` on SQLite with entities: `KnowledgeSource`, `KnowledgeDocument`, `DocumentChunk`.
- `SourceType` enum: `WebPage=1, ObsidianVault=2, DocumentFile=3, RestApi=4, SqlDatabase=5`.
- `DocumentChunk.Embedding` persisted as a serialized `float[]` (BLOB column) + `DocumentChunk.EmbeddingModel` (string — provider/model that produced the vector); cosine similarity computed in-process at query time.
- `IVectorStore` abstraction over chunk embeddings:
  - `SqliteVectorStore` (default, zero-infra): embeddings in the `DocumentChunks` table, similarity in-process.
  - `PostgresVectorStore`: embeddings in PostgreSQL + `pgvector` (Npgsql + `Pgvector.EntityFrameworkCore`), keyed by `DocumentChunkId`; similarity via `<=>` distance operator server-side.
  - Config: `VectorStore:Provider` = `sqlite` (default) | `postgres`; `VectorStore:ConnectionString` for postgres. Catalog/documents stay on SQLite regardless — only embeddings move.
- REST endpoints under `/api/sources`: list (with filters), get by id, create, update, delete, activate/deactivate, `POST /api/sources/{id}/sync` (immediate sync trigger), `GET /api/sources/{id}/documents`.
- `GET /api/search?query=&topK=` — unified semantic search over active sources (feeds the Playground).
- Per-source-type `ConfigurationJson` contract: `{ "path": "..." }` for ObsidianVault/LocalFolder; `{ "url": "..." }` for WebPage; `{ "endpoint": "...", "headers": {} }` for RestApi; `{ "connectionString": "...", "query": "..." }` for SqlDatabase; `{ "filePath": "..." }` for DocumentFile.
- Database auto-created/migrated on startup (`EnsureCreated` or migrations) at `knowledgehub.db` next to the executable (path configurable via `appsettings`).

**Out of scope:**
- sqlite-vec / native SQLite vector extension (backlog — in-process cosine suffices at MVP scale).
- Other vector DBs (Qdrant, Pinecone, pgvector is the single external option for now).
- Web scraping, PDF/DOCX extraction, SQL introspection and REST fetching implementations (only ObsidianVault ingestion is in SPEC-03; other types persist config and return `501`-style "connector not implemented" on sync).
- Authentication/authorization on `/api/*`.
- Multi-user tenancy.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server` — `Domain/Entities`, `Data/KnowledgeHubDbContext`, `Services/` (source service, search service), `Api/` minimal endpoint groups. DTOs in `src/KnowledgeHub.Shared/Contracts/` so the Blazor client reuses them.

**Files to read before implementing:**
- `CLAUDE.md` · `AGENTS.md`
- `src/KnowledgeHub.Server/Program.cs` · `appsettings.json`

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/SourceType.cs
src/KnowledgeHub.Shared/Contracts/KnowledgeSourceDtos.cs
src/KnowledgeHub.Shared/Contracts/SearchDtos.cs
src/KnowledgeHub.Server/Domain/Entities/KnowledgeSource.cs
src/KnowledgeHub.Server/Domain/Entities/KnowledgeDocument.cs
src/KnowledgeHub.Server/Domain/Entities/DocumentChunk.cs
src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs
src/KnowledgeHub.Server/Services/IKnowledgeSourceService.cs
src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs
src/KnowledgeHub.Server/Services/ISearchService.cs
src/KnowledgeHub.Server/Services/SearchService.cs
src/KnowledgeHub.Server/VectorStore/IVectorStore.cs
src/KnowledgeHub.Server/VectorStore/SqliteVectorStore.cs
src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs
src/KnowledgeHub.Server/VectorStore/VectorStoreOptions.cs
src/KnowledgeHub.Server/Api/SourcesEndpoints.cs
src/KnowledgeHub.Server/Api/SearchEndpoints.cs
src/KnowledgeHub.Server/Program.cs (wire-up)
tests/KnowledgeHub.Tests.Unit/Server/*Tests.cs
tests/KnowledgeHub.Tests.Integration/SourcesApiTests.cs
```

## 4. Requirements

### RF-001: Entity model
- **Description:** Persist `KnowledgeSource` (Id, Name, Description, SourceType, ConfigurationJson, IsActive, AutoSyncEnabled, SyncIntervalMinutes, CreatedAt, LastSyncAt), `KnowledgeDocument` (Id, KnowledgeSourceId, Title, UriReference, ContentHash, RawContent, IndexedAt), `DocumentChunk` (Id, KnowledgeDocumentId, ChunkIndex, TextContent, Embedding).
- **Rules:** `Name` required and unique per source; `UriReference` unique per source (upsert key); cascade delete source → documents → chunks.
- **Input → Output:** EF Core model → SQLite schema.

### RF-002: Sources CRUD API
- **Description:** `GET /api/sources` (optional `?type=&active=`), `GET /api/sources/{id}`, `POST /api/sources`, `PUT /api/sources/{id}`, `DELETE /api/sources/{id}`, `POST /api/sources/{id}/activate`, `POST /api/sources/{id}/deactivate`.
- **Rules:** validation — Name required, SourceType valid enum, ConfigurationJson parses as JSON and contains required keys for the type; `404` on missing id; `409` on duplicate name.
- **Input → Output:** DTO in → DTO out; domain entities never serialized directly.

### RF-003: Sync trigger
- **Description:** `POST /api/sources/{id}/sync` enqueues/runs ingestion for that source via `IIngestionService` (SPEC-03) and returns `202` with a job summary (`documentsProcessed`, `chunksCreated`, `durationMs`, `status`). Connector not implemented → `200` with `status: "skipped"` and reason.
- **Input → Output:** source id → sync job result.

### RF-004: Semantic search
- **Description:** `GET /api/search?query={q}&topK={n}` embeds the query via `IEmbeddingProvider`, computes cosine similarity over all chunks of active sources in-process, returns top-K results `{ chunkText, documentTitle, sourceName, sourceId, score, uriReference }` ordered by score desc.
- **Rules:** chunks with null embedding are skipped; empty corpus → `200` with empty list.

### RF-005: Startup persistence
- **Description:** On startup the Server ensures the SQLite schema exists (idempotent) and logs the database path.

### RF-006: Pluggable vector store
- **Description:** `IVectorStore.UpsertAsync(chunkId, vector, model)` / `DeleteByDocumentAsync(documentId)` / `SearchAsync(queryVector, model, topK, sourceIds?)` → ranked `(chunkId, score)`. `sqlite` = in-process cosine over `DocumentChunks`; `postgres` = pgvector `vector` column + `<=>` cosine distance. Selected by `VectorStore:Provider`.
- **Rules:** search only ranks vectors whose `EmbeddingModel` matches the currently configured embedding model (mismatched rows are skipped and counted); deleting a document purges its vectors.
- **Input → Output:** query vector + filters → ordered hits.

## 5. API Contract

**Endpoint:** `POST /api/sources`
**Request:**
```json
{
  "name": "Meu Vault",
  "description": "Notas pessoais do Obsidian",
  "type": "ObsidianVault",
  "configuration": { "path": "/home/ubuntu/vault" },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 30
}
```
**Response (success):** `201` + `KnowledgeSourceDto`.
**Expected errors:** `400` validation · `404` not found · `409` duplicate name — generic `{ "error": "..." }` envelope, no stack traces.

**Endpoint:** `GET /api/search?query=arquitetura&topK=5`
**Response:**
```json
{ "results": [ { "chunkText": "...", "documentTitle": "...", "sourceName": "...", "sourceId": "...", "score": 0.83, "uriReference": "..." } ] }
```

## 6. Acceptance Criteria

- [ ] **Given** a valid ObsidianVault payload **when** POST `/api/sources` **then** `201` and the source appears in `GET /api/sources`.
- [ ] **Given** a duplicate name **when** POST `/api/sources` **then** `409`.
- [ ] **Given** `configuration` without `path` for ObsidianVault **when** POST **then** `400` with validation message.
- [ ] **Given** an active source with indexed chunks **when** GET `/api/search?query=...` **then** results ordered by score desc, ≤ topK.
- [ ] **Given** a deactivated source **when** GET `/api/search` **then** its chunks are excluded.
- [ ] **Given** a deleted source **when** listing documents **then** its documents/chunks no longer exist (cascade).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Invalid SourceType | `"type": "Foo"` | 400 |
| Malformed configuration | `configuration: "abc"` | 400 |
| topK ≤ 0 or > 50 | `topK=0` | clamp to default 5 / max 50 |
| Empty corpus | any query | 200 + `results: []` |

## 7. Task Plan

- [ ] **T1 — Discovery:** read `Program.cs`, Shared contracts.
- [ ] **T2 — Implementation:** entities → DbContext → services → endpoints → Program wire-up → schema bootstrap.
- [ ] **T3 — Verification:** unit tests for validation + cosine ranking; integration tests with `WebApplicationFactory` + temp SQLite file.
- [ ] **T4 — Validation:** `dotnet build` + `dotnet test`; manual `curl` round-trip of CRUD + search.
- [ ] **T5 — Done + PR:** fill DoD, `Status = Done`.

## 8. Organization Guardrails

- **Branches:** feature branch only.
- **Security:** `ConfigurationJson` may contain connection strings — never log its content; REST responses never echo sensitive keys (`connectionString`, `headers`) back.
- **Scope:** only ObsidianVault ingestion is functional in this MVP; other types are persisted but sync is a no-op with explicit status.
- **Architecture:** minimal API endpoints are thin; all logic in services; EF Core stays in the Server project.

## 9. Definition of Done

- [ ] All requirements implemented.
- [ ] Acceptance criteria covered by tests.
- [ ] Edge cases handled.
- [ ] `dotnet build` + `dotnet test` green.
- [ ] No secrets echoed by the API; guardrails respected.

## Open Questions / Pending Ambiguity

- `[A DEFINIR]` Embedding storage format: JSON array vs binary BLOB. Recommended: BLOB (`float[]` ↔ `byte[]`) — compact and faster to deserialize.
- `[A DEFINIR]` Postgres mode also moves the catalog (full Npgsql DbContext)? Recommended: no — catalog stays SQLite; only embeddings go to pgvector.
