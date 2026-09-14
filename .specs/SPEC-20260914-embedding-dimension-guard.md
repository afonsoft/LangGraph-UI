# SPEC-20260914-embedding-dimension-guard

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `embedding-dimension-guard` |
| Type | `Feature` |
| Stack | `.NET 10`, `EF Core`, `SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-persistence-hardening` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** platform operator
**I want** the app to detect at startup when persisted embeddings don't match the configured provider/model/dimensions
**So that** switching `Embeddings:Provider` (deterministic → ollama → openai) or `Dimensions` doesn't silently corrupt search results.

**Problem context:** `SqliteVectorStore` stores `Embedding` (float[] as BLOB) + `EmbeddingModel` per chunk and filters searches by `EmbeddingModel == model`. Changing the configured model already scopes queries correctly, but changing `Dimensions` while reusing a model name — or mixing providers — yields vectors of mismatched length compared by cosine similarity in-process (`EmbeddingVectorCodec.CosineSimilarity`), which silently misbehaves or skews scores. There is no startup validation.

## 2. Scope

**In scope:**
- Startup check after DB init: sample persisted embeddings; compare stored vector length (`Embedding.Length / 4`) and distinct `EmbeddingModel` values against configured `Embeddings:Dimensions` and the provider's reported model name.
- Clear, actionable log at `Warning`/`Error` level: what was found (count of mismatched chunks, their model/dims) vs what's configured, and the remediation (re-sync sources / clear embeddings).
- Unit + integration tests for the guard.

**Out of scope:**
- Automatic re-embedding/re-indexing (operator decides).
- Per-source embedding config.
- Postgres/pgvector store guard (the check targets the EF/SQLite path; Postgres provider already takes Dimensions at construction).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Program.cs` (startup scope) or a small `EmbeddingCompatibilityCheck` service under `src/KnowledgeHub.Server/Embeddings/`.

**Files to read:**
- `src/KnowledgeHub.Server/VectorStore/SqliteVectorStore.cs` — storage shape (`Embedding` byte[], `EmbeddingModel`).
- `src/KnowledgeHub.Server/Embeddings/EmbeddingVectorCodec.cs` — ToBytes/FromBytes format.
- `src/KnowledgeHub.Server/Embeddings/EmbeddingOptions.cs` — `Dimensions`, `Model`.
- `src/KnowledgeHub.Server/Embeddings/IEmbeddingProvider.cs` — how the model name surfaces.
- `src/KnowledgeHub.Server/Program.cs`, `Domain/Entities/DocumentChunk.cs`.

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Embeddings/EmbeddingCompatibilityCheck.cs   (new)
src/KnowledgeHub.Server/Program.cs                                   (invoke after Migrate)
tests/KnowledgeHub.Tests.Unit/Server/EmbeddingCompatibilityCheckTests.cs (new)
```

## 4. Requirements

### RF-001: Startup compatibility check
- **Description:** After `Migrate()`, load `EmbeddingModel` values + `Embedding.Length` for chunks where `Embedding != null` (grouped, no full scan into memory — `GroupBy`/`Distinct` query). Compare each distinct (model, dims) against the configured provider's model and `Embeddings:Dimensions`.
- **Input → Output:** startup → log line `Embedding store OK (N chunks, model=X, dims=D)` or warning detail.

### RF-002: Dimension mismatch → error-level warning
- **Description:** Stored `Embedding.Length/4 != Dimensions` → `LogError`: "N chunks embed with dims {stored} but Embeddings:Dimensions={configured}. Search results are unreliable — re-sync sources or restore matching config." Non-fatal (app still serves), because degraded search > no app.

### RF-003: Model mismatch → warning
- **Description:** `EmbeddingModel` values not equal to the active provider's model → `LogWarning` listing the distinct models found. Search already filters by model (stale chunks simply don't match) — informational.

### RF-004: No-op when empty or clean
- **Description:** Zero embedded chunks or full match → single `LogInformation`, no noise.

## 5. API Contract

N/A — startup internal check.

## 6. Acceptance Criteria

- [ ] **Given** a DB whose chunks were embedded at dims=384 **when** started with `Embeddings:Dimensions=768` **then** startup logs Error naming both numbers and the remediation.
- [ ] **Given** chunks embedded with model `deterministic` **when** provider is ollama/`nomic-embed-text` **then** a Warning lists `deterministic`.
- [ ] **Given** a clean store **when** app starts **then** one Info line, no warnings.
- [ ] **Given** the check **when** the DB is empty **then** it completes in <100ms and never throws.

## 7. Task Plan

- [ ] **T1 — Check service:** `EmbeddingCompatibilityCheck.RunAsync(db, IOptions<EmbeddingOptions>, IEmbeddingProvider, ILogger)`.
- [ ] **T2 — Wire startup:** call in `Program.cs` after `Migrate()`.
- [ ] **T3 — Tests:** unit tests with in-memory/SQLite context covering RF-001..004.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-persistence-hardening`.
- **Behavior:** non-fatal by design; never delete or mutate embeddings.
- **Perf:** grouped query, no BLOB decode of the full table.

## 9. Definition of Done

- [ ] Guard runs at startup; three ACs verified.
- [ ] `dotnet test` green incl. new tests.
- [ ] No behavior change to search results when config matches.

## Open Questions / Pending Ambiguity

- None.
