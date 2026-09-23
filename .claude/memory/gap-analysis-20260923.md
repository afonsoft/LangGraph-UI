# Gap Analysis — 20260923 (proposta RAG/GraphRAG externa vs. AS-IS)

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: feature/Devin-20260923-rag-gap-specs
- Driver: user-provided "Guia Prático: RAG e GraphRAG com C#, MCP, Ollama e Bancos Vetoriais" — audited the codebase against the proposal plus performance/memory/latency/cache axes.
- Mode: fresh run. Prior runs: gap-analysis-20260914/16/17/17-r2/22 (all ENTREGUE).

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 75 SPECs — all `Done` |
| docs/, docs/architecture/ | present | — |
| .claude/ harness + memory | present | — |
| CLAUDE.md, AGENTS.md, README.md | present | — |
| GitHub | gh OK (afonsoft) | remote afonsoft/LangGraph-UI |
| Working tree | clean (only untracked `backups/`) | — |

## 2. Proposal coverage (already implemented — REJEITADO as gaps)

RAG-over-MCP, Ollama/OpenAI/ONNX embeddings, SQLite vector store, sqlite-vec opt-in, pgvector opt-in, FTS5+RRF hybrid, [n] citations, agent loop + HITL, threads + summarization, SSE streaming, IDistributedCache (memory|redis) for query embeddings/results/catalog, batch embed+upsert, DbContextPool, auth cookie+aft_* with audit, incremental ingestion by hash/fingerprint, health checks, ProblemDetails, graceful shutdown, config validation, PWA admin + SignalR monitor.

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-requirements-graphrag | requirements | CONFIRMADO | baixa (phase-4) | No entity/relationship extraction, no graph store, no `find_dependencies`/`analyze_impact` tools. Proposal §11. |
| GAP-requirements-reranker | requirements | CONFIRMADO | média | `RrfFuser.cs` is the only ranking stage; SPEC-20260914-hybrid-retrieval §5 explicitly deferred cross-encoder/LLM rerank. Proposal §2.1. |
| GAP-requirements-query-rewriting | requirements | CONFIRMADO | média | `SearchService.ExecuteAsync` embeds the raw query; no rewrite/expansion. Proposal §2.1, §10. |
| GAP-requirements-result-contract-metadata | requirements | CONFIRMADO | média | `SearchResultItem` (SearchDtos.cs:23-36) lacks ChunkId/DocumentId/Metadata/IndexedAt required by proposal §3.2. |
| GAP-requirements-metadata-filters | requirements | CONFIRMADO | média | Only `source`+`mode` filters reach tools; no sourceType/path/date/language filters. Proposal §2.1/§9.2. |
| GAP-tests-eval-harness | tests | CONFIRMADO | alta | Zero retrieval evaluation: no Recall@K/P@K/MRR, no eval dataset, no faithfulness check. Proposal §13.3. |
| GAP-security-prompt-injection | security | CONFIRMADO | alta | `AnswerService.BuildUserPrompt` (:129-143) concatenates raw chunk text into the prompt — no delimiters, detection or flagging. Proposal §14. |
| GAP-observability-metrics | observability | CONFIRMADO | média | No `Meter`/`ActivitySource`/OTel anywhere; only ILogger + health checks + SignalR feed. No per-stage latency. Proposal §13.3. |
| GAP-security-rate-limiting | security | CONFIRMADO | média | No rate limiter; `agent_chat`/`ask`/`tools/call` are unthrottled LLM-spend endpoints. Proposal §14. |
| GAP-implementation-pgvector-hnsw | implementation | CONFIRMADO | média | `PostgresVectorStore.EnsureInitializedAsync` (:99-109) creates only `model` btree — no HNSW → seq scan at scale; `UpsertBatchAsync` not overridden → N INSERTs. |
| GAP-implementation-agent-perf | implementation | CONFIRMADO | média | `AgentService.StreamAsync` (:54) `Channel.CreateUnbounded` grows under slow clients; `ChatOptions.Tools` reserializes schemas every iteration (H7 deferred in SPEC-20260916); no answer cache; named HttpClients (:40-58) lack resilience handlers. |
| GAP-security-source-authorization | security | CONFIRMADO | média | `ApiKey` has no source scoping — every key reads every source. Proposal §14 "propagação das permissões da origem". |
| GAP-implementation-code-chunking | implementation | CONFIRMADO | baixa | `MarkdownChunker` handles all content; no code/config-aware chunking (proposal §9.1). |
| enforce_admins escape hatch | security | INCONCLUSIVO (carried) | — | Still open from r2 — owner decision pending. |

## 4. SPECs produced (all `Draft`)

| SPEC | Gap(s) | Focus |
| --- | --- | --- |
| SPEC-20260923-retrieval-quality | reranker + query-rewriting + metadata-filters + result-contract-metadata | Query rewrite stage, pluggable `IReranker`, metadata filters, enriched `SearchResultItem` |
| SPEC-20260923-eval-harness | eval-harness | Eval dataset + Recall@K/P@K/MRR/faithfulness runner + report |
| SPEC-20260923-prompt-injection-guard | prompt-injection | Delimiters, detection heuristics, audit flag, red-team cases |
| SPEC-20260923-observability-metrics | observability-metrics | `Meter`+`ActivitySource`, per-stage histograms, OTLP/Prometheus |
| SPEC-20260923-rate-limiting | rate-limiting | ASP.NET rate limiter on LLM endpoints + per-key quotas |
| SPEC-20260923-pgvector-hnsw-scale | pgvector-hnsw | HNSW index, `UpsertBatchAsync` override, jsonb metadata |
| SPEC-20260923-agent-runtime-hardening | agent-perf | Bounded SSE channel, schema reuse, answer cache, HTTP resilience |
| SPEC-20260923-source-authorization | source-authorization | Per-API-key source scoping |
| SPEC-20260923-code-aware-chunking | code-chunking | `ITextChunker` abstraction + code/config chunkers |
| SPEC-20260923-graphrag | graphrag | Entity/rel extraction + graph store + `find_dependencies`/`analyze_impact` |

## 5. Outcome

SPECs written as `Draft` on `feature/Devin-20260923-rag-gap-specs`; implementation awaits per-SPEC approval via execute-specs. No Issues created (not requested).
