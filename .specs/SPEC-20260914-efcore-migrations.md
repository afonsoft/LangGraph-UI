# SPEC-20260914-efcore-migrations

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `efcore-migrations` |
| Type | `Infra` |
| Stack | `.NET 10`, `EF Core 10`, `SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-persistence-hardening` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** platform operator
**I want** the SQLite schema managed by EF Core Migrations instead of `EnsureCreated`
**So that** schema changes in new releases upgrade existing databases instead of breaking them — the equivalent of LangGraph's checkpoint-compat problem.

**Problem context:** `Program.cs` calls `db.Database.EnsureCreated()`. `EnsureCreated` creates the schema only when the DB doesn't exist and **never applies changes** — any entity change makes the published binary incompatible with existing `knowledgehub.db` files (the "checkpoint de ontem não abre no runtime de hoje" scenario).

## 2. Scope

**In scope:**
- `Microsoft.EntityFrameworkCore.Design` reference + `dotnet ef` tooling for the Server project.
- Initial migration `InitialCreate` matching the current model exactly (Sources, Documents, Chunks — same columns, indexes, FK cascade, `BLOB` embedding).
- `Program.cs`: replace `EnsureCreated()` with `Migrate()` (creates DB if absent, applies pending migrations).
- README note on adding migrations (`dotnet ef migrations add <Name>`).
- Integration test compatibility (Migrate works for fresh test DBs too).

**Out of scope:**
- Data migrations/seed, Postgres schema management (pgvector store is a separate provider, out of EF scope today).
- Migration bundles or `migrate.exe` tooling in the Docker image (Migrate() runs at startup).

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Server/Program.cs`, `src/KnowledgeHub.Server/KnowledgeHub.Server.csproj`, new `src/KnowledgeHub.Server/Migrations/`, `README.md`.

**Files to read:**
- `src/KnowledgeHub.Server/Program.cs` — current `EnsureCreated` block.
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` — model to snapshot.
- `tests/KnowledgeHub.Tests.Integration/` — test hosts that rely on schema creation.

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Migrations/                    (new — generated)
src/KnowledgeHub.Server/Program.cs                     (Migrate)
src/KnowledgeHub.Server/KnowledgeHub.Server.csproj     (Design package, PrivateAssets)
README.md                                              (migration workflow note)
```

## 4. Requirements

### RF-001: Initial migration snapshot
- **Description:** `dotnet ef migrations add InitialCreate` generates a migration whose `Up()` reproduces the existing schema byte-for-byte (table names `Sources`, `Documents`, `Chunks`, unique indexes on `Sources.Name` and `Documents.(KnowledgeSourceId, UriReference)`, cascade deletes, `Embedding BLOB`). Verified by diffing `db.schema`/`sqlite_master` of an EnsureCreated DB vs a migrated DB.
- **Input → Output:** `dotnet ef migrations add` → `Migrations/YYYYMMDDHHMMSS_InitialCreate.cs` + `KnowledgeHubDbContextModelSnapshot.cs`.

### RF-002: Migrate at startup
- **Description:** `Program.cs` replaces `db.Database.EnsureCreated()` with `db.Database.Migrate()`. Fresh install → schema created; existing DB from the EnsureCreated era → works (see RF-003); pending migrations → applied.

### RF-003: Existing-DB compatibility path
- **Description:** A `knowledgehub.db` created before this change has the schema but no `__EFMigrationsHistory`. Startup must not fail: the initial migration is skipped via baseline — either (a) `Migrate()` handles it because history table absence + existing tables raises "table already exists" → mitigate by checking `Database.GetAppliedMigrations()`/`GetPendingMigrations()` and, when history is empty AND `Sources` table exists, inserting the initial migration row manually (idempotent, logged); or (b) documented fallback: if pending-migration apply fails on an EnsureCreated DB, log a clear actionable error. Preferred: (a) seamless baseline insertion.
- **Input → Output:** old DB + new binary → app starts, `__EFMigrationsHistory` contains `InitialCreate`, zero data loss.

### RF-004: Dev workflow documented
- **Description:** README "Development" gains: `dotnet ef migrations add <Name> -p src/KnowledgeHub.Server` for schema changes; CI/verify uses normal `dotnet test`.

## 5. API Contract

N/A — persistence internals.

## 6. Acceptance Criteria

- [ ] **Given** a fresh install **when** the app starts **then** the DB is created via migrations with `__EFMigrationsHistory` present and identical schema to before.
- [ ] **Given** a `knowledgehub.db` produced by the current (EnsureCreated) build with real rows **when** the new binary starts **then** startup succeeds, all rows intact, history table records `InitialCreate` as applied.
- [ ] **Given** a new entity field added later **when** a second migration exists **then** `Migrate()` applies it on startup without data loss.
- [ ] **Given** `dotnet test` **when** integration tests run **then** all pass (test hosts create schema via Migrate).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| EnsureCreated-era DB | old file, no history table | baseline-seed `InitialCreate`, start normally |
| Empty dir | no DB file | full `InitialCreate` applied |
| Concurrent first start | two processes | SQLite lock surfaces; acceptable — second retries on restart |

## 7. Task Plan

- [ ] **T1 — Tooling:** add `Microsoft.EntityFrameworkCore.Design` (PrivateAssets=all) to Server csproj; `dotnet ef` available.
- [ ] **T2 — InitialCreate:** generate migration; verify schema parity vs EnsureCreated output.
- [ ] **T3 — Startup:** `Migrate()` + EnsureCreated-era baseline detection (RF-003).
- [ ] **T4 — Tests + docs:** run full suite (new + old DB paths), README note, PR.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-persistence-hardening`; never commit to main.
- **Security:** no secrets; migrations contain schema only.
- **Compat:** zero data loss on existing `knowledgehub.db` files is a hard requirement.

## 9. Definition of Done

- [ ] Migrations generated and applied at startup via `Migrate()`.
- [ ] Old EnsureCreated DBs start cleanly (verified with the real `./data/knowledgehub.db` in this repo).
- [ ] `dotnet build` + `dotnet test` green; Docker image still builds and container healthy.

## Open Questions / Pending Ambiguity

- None.
