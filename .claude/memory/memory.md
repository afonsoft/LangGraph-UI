# Short-term memory — session state (overwritten each session, ≤100 lines)

- **Last verified commit on `main`**: `5b3339f` (PR #153 — SPEC tool-descriptions Done).
- **Baseline**: `dotnet build` 0 warnings · `dotnet test` 369 unit + 168 integration green · `dotnet format` clean.
- **Branch protection**: `main` protected (PR + 5 status checks, enforce_admins=false) — owner bypass used for docs/SPEC merges on explicit request.
- **Done hoje (2026-09-23)**: gap-analysis vs. external RAG/GraphRAG proposal → 13 candidates, 13 CONFIRMADO, 10 SPECs Draft written on `feature/Devin-20260923-rag-gap-specs`: retrieval-quality, eval-harness, prompt-injection-guard, observability-metrics, rate-limiting, pgvector-hnsw-scale, agent-runtime-hardening, source-authorization, code-aware-chunking, graphrag. Report: `.claude/memory/gap-analysis-20260923.md`.
- **Blockers**: none.
- **Next**: user reviews Draft SPECs → approve → implement via execute-specs. Priority suggestion: eval-harness + prompt-injection-guard first (alta).

## Session summary (2026-09-23 — RAG/GraphRAG proposal gap analysis)

- Audited repo against external proposal "RAG e GraphRAG com C#, MCP, Ollama e Bancos Vetoriais".
- Repo already covers most of the proposal: hybrid FTS5+RRF, pgvector+sqlite-vec opt-ins, IDistributedCache, batch embeddings, HITL, per-key secrets, MCP proxies.
- Confirmed gaps → 10 Draft SPECs (see gap-analysis-20260923.md for verdicts+evidence).
- Carried pendency: `enforce_admins=true` decision still open.
