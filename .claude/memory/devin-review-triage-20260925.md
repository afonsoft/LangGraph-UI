# Devin Review triage — 2026-09-25

Source: `devin-ai-integration[bot]` inline comments on merged PRs (#184–#206 + old era #9–#43 from 2026-09-14). 155 comments total; each verified against main@05571ea.

## Already resolved (in #200–#205) — no action

- #200–#203 review findings → delivered in PR #205 (embeddings-runtime-coherence, cache-key-consistency, review-docs-and-misc): GET fail-soft `providerError`, `cache-key:` bus topic, honest removal→502, `ServerReported` only-on-INFO, `LogSafe` anti log-forging, PUT dims≠store→400 + `storeDimensions`/`dimsMismatch` + UI warning, `index:version` bump on save, fingerprint in `emb:` keys, dispose previous provider, `chown data logs` docs, `minutes` doc field, `storageType` diagnostics, README.pt-br parity, Entidades sort.
- #191 "see server logs" → `EnrichError`/`ExceptionDigest` (PR #200).
- #190 SPEC-time notes (TTL prefixes `emb:`/`ans:`/`mcp:tool:`, embedding key role/fingerprint, otel span dup) → resolved by wave3/#205.
- #203 overlap cap 2000 → intentional (RF-006, >~40% chunk degrades coherence).

## CONFIRMED — open on main

### A. Ingestion/connector integrity (highest impact)

| # | Finding | Evidence |
|---|---------|----------|
| A1 | Reindex/chunking-change wipes unchanged remote docs — connector returns `TextContent=""` for fingerprint-unchanged items; `forceReindex` or `ChunkerConfigHash` change → `doc.RawContent=""` re-chunked → searchable content erased. Affects Notion, cloud connectors, per-source chunking edits. | `IngestionService.SyncViaConnectorAsync` (raw.Fingerprint dedup path falls through on forceReindex); `CloudConnectorBase.FetchAsync` emits empty-text RawDocument for unchanged. PRs #184/#187/#188 |
| A2 | Transient download failure → doc absent from `fetch.Documents` → treated as remote delete → doc+vectors purged. Same for `maxFiles` truncation (files beyond cap vanish → deleted). | `CloudConnectorBase:103` (warning+continue), `IngestionService` unseen→delete loop; `GoogleDriveGateway.ListAsync` `seen++ >= maxFiles` |
| A3 | `AzureShareGateway.ListAsync` — prefix defaults to `directoryPath`, then combined `"{rootDirectory}/{prefix}"` → `dir/dir` listing finds nothing | `CloudConnectorBase:42` (`prefix ?? directoryPath`), `AzureShareGateway:15` |
| A4 | Azure `accountKey` with `=` padding sent as `connectionString` → ShareClient ctor fails | `SourceEditDialog.razor:480` (`Contains('=')` heuristic) |
| A5 | Google Drive public flows broken: single-file link gets id-as-name (no ext → filtered); public folder has no md5/date → constant fingerprint → stale; Sheets→`.csv` not in `SupportedExtensions` | `GoogleDriveGateway:23/40/55/88`, `DocumentFileConnector.SupportedExtensions` (no `.csv`) |
| A6 | Doc failure leaves tracked entities dirty — catch logs but never detaches; later `SaveChangesAsync` may persist partial doc/chunks or fail repeatedly | `IngestionService:225-238` (vault) + connector loop |
| A7 | Queue full → `queued` job persisted BEFORE `TryWrite` → permanent dedup deadlock until restart | `IngestionQueue.EnqueueAsync` (SaveChanges → TryWrite → QueueFullException) |
| A8 | Source delete: vectors purged before SQLite delete commits (partial failure → source without vectors); no `index:version` bump → deleted docs served from `search:` cache ~5min | `KnowledgeSourceService.DeleteAsync` order |
| A9 | `PostgresVectorStore.UpsertBatchAsync` — ANALYZE/HNSW inside try → catch `RollbackAsync` on committed tx → false ingestion failure | `:95-125` |

### B. Cache

| # | Finding | Evidence |
|---|---------|----------|
| B1 | `CacheTtlPolicy` registered but never resolved → `Current` stays null → ALL region TTLs = 10min default (emb/search/ans/index policies dead) | `KnowledgeHubServiceCollectionExtensions:313` lazy `AddSingleton`; only `SafeCache:112` reads `Current` |
| B2 | `L1L2Cache._keyLocks` unbounded `ConcurrentDictionary` — per-key semaphores never pruned | `L1L2Cache:22,72` |
| B3 | `RedisInvalidationBus` ctor calls `Subscribe` — Redis down at boot → hosted-service construction throws → startup risk | `ICacheInvalidationBus.cs:45` |
| B4 | `cache-clear` on remote replicas evicts only L1 — remote-owned Redis keys persist → refilled stale | `InvalidationSubscriber.OnReceived` |
| B5 | Degraded vector results cached — fail-soft `[]` arm fused into result then cached `search:` 5min as legit | `SearchService:75-76` + `VectorSearchAsync` catch→`[]` |
| B6 | Minor: `ServerKeys` counts whole DB (`*` pattern) not app keys; SCAN unbounded w/o ct; L1 read grants full `_l1MaxTtl` ignoring remaining L2 TTL; `SetAsync` L2-first blocks L1 fill during Redis outage; in-flight L2 read repopulates evicted L1 (bounded race) | `CacheManagerService:124-145`, `L1L2Cache:36-58` |

### C. Embeddings/settings (from #205 review)

| # | Finding | Evidence |
|---|---------|----------|
| C1 | ONNX dims gate compares requested≠store but not actual model output (MiniLM=384 fixed); store=512+dims=512+onnx passes → runtime rejection | `SettingsEndpoints:189` PUT check |
| C2 | `Signature()` omits `Asymmetric.Auto` — flipping Auto doesn't rebuild provider nor change fingerprint → stale cached vectors | `EmbeddingProviderResolver.Signature` |
| C3 | Provider swap disposes old ONNX session on `Task.Run` immediately — in-flight inference killed | `EmbeddingProviderResolver:61` |
| C4 | Replica settings divergence — save writes local SQLite + bumps index-version; replicas never get new provider (bus only evicts cache) | `EmbeddingSettingsService:214` |
| C5 | Settings.razor "aplica sem reiniciar" vs dims rejection — cosmetic text | #205 comment |

### D. Search/RAG

| # | Finding | Evidence |
|---|---------|----------|
| D1 | Multi-query/HyDE expansion → `Task.WhenAll` variants share scoped `KnowledgeHubDbContext`/connection (LexicalSearchService + SqliteVec `_db`) → EF "second operation" / reader errors | `SearchService:500-520`, `LexicalSearchService:44`, `SqliteVecVectorStore:121,187` |
| D2 | `/api/ask/stream` bypasses corrective retrieval — different retry/abstain policy vs `/api/ask` | `StreamingEndpoints:53` |
| D3 | Corrective retry: worse retry keeps old results but adopts NEW grading → wrong abstention | `CorrectiveRetrievalService:63` (`grading = retryGrading` unconditional) |
| D4 | Eval baseline compare doesn't verify dataset hash — deltas across different datasets look like regressions | `EvalRunner:43-50` |

### E. pgvector

| # | Finding | Evidence |
|---|---------|----------|
| E1 | `SET LOCAL pgvector.iterative_scan` — wrong GUC; correct is `hnsw.iterative_scan` → opt-in feature never activates | `PostgresVectorStore:213` |
| E2 | halfvec migration: `ALTER COLUMN TYPE` before `DROP INDEX` → fails when HNSW index exists | `PostgresVectorStore:400-415` |
| E3 | halfvec fresh DB: `ResolveStorageTypeAsync` before `CREATE EXTENSION` → resolves `vector`; opt-in silently ignored | `PostgresVectorStore:311` |

### F. Log level

| # | Finding | Evidence |
|---|---------|----------|
| F1 | `Debug`+`minutes:0` → stays forever (contract says ≤0 restores default) | `LogLevelControl.Set` (`level != default && minutes > 0`) |
| F2 | Old timer callback can kill new session — `Dispose` doesn't wait pending callback | `LogLevelControl:40-49` (needs generation counter) |
| F3 | PUT response omits `configuredDefault` → UI shows "volta a  em" | `SettingsEndpoints:300-313` |
| F4 | Audit log lacks caller + previous level | `SettingsEndpoints:311` |

### G. UI/monitor

| # | Finding | Evidence |
|---|---------|----------|
| G1 | completed-with-warnings badge not clickable → warnings unreachable | `Sources.razor:53` (`case "completed"` plain Badge) |
| G2 | Orphan-failed jobs unreachable when source has no status (dash, no detail) | `Sources.razor:70` + `FailOrphanedJobsAsync` doesn't set source status |
| G3 | Status spans `role=button` no keyboard activation (a11y) | `Sources.razor:59` |
| G4 | Job modal race — closing A opening B can show A's job | `Sources.razor:396` |
| G5 | `JobsAsync` failure shows "Nenhum job registrado" | `Sources.razor:392` |
| G6 | Actions col 120px < ~156px buttons (app.css:308) | #185 comment |
| G7 | MCP Monitor: session-open/close not in activity list → "sessões" filter + CSV export empty of transitions; agent `ToolCall` lacks `Caller`; buffer 200 < server 500; session age freezes between renders | `McpMonitorReplay.Apply`, `CatalogToolAIFunction:74-85`, `McpMonitor.razor:203,55` |
| G8 | Eval UI: P95 column shows total `DurationMs`; reopen loses delta (no `compare` param); malformed gate JSON crashes form | `Eval.razor:41`, `EvalApiClient:16,34` |
| G9 | Login: SSE endpoint advertised unconditionally (broken in `SessionMode=Stateless`); form below fold on short screens; instructions diverge vs McpMonitor; copied config has `#` comments (invalid JSON) | `Login.razor:51,15,67`, `McpMonitor.razor:355` |
| G10 | sqlite-vec diagnostics: rows counts `Chunks.Embedding != null` not `vec_chunks`; `hnswIndex=true` for vec0 (not HNSW) | `VectorStoreDiagnostics:27` |

### H. Old era (#9–#43, spot-checked)

- `backup.sh` same-name vault overwrite — STILL VALID (`basename` slug collision).
- Most others superseded (VaultWatcherService gone, install.sh/IngestionService rewritten, OpenAiChatClient serialization fixed).
- Unverified remnants: restore.sh stop-failure path, AgentService concurrent resume double-write, health readiness without embeddings.

### I. Bookkeeping/process (no code)

- SPEC acceptance criteria unchecked on Done flips: #186, #192, #197, #201, #204.
- Memory append-only note (#206) — keep appending, don't rewrite history.
- Open decisions: serilog bootstrap logging scope (#195, WAF constraint), graph detach protection (#191), `eval-gate.sh` gate=null semantics (#184), shutdown graceful×abrupt test (#200 — pending item).
