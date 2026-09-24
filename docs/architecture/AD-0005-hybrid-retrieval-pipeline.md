# AD-0005 — Hybrid retrieval: FTS5 + vector RRF with optional rewrite/rerank

## Context
Pure vector search misses exact terms; pure lexical misses paraphrase. The
pipeline must also be measurable and safe against poisoned chunks.

## Decision
`SearchService` runs FTS5 (sanitized query) and vector KNN in parallel, fuses
with RRF (k=60), then applies: metadata filters (`sourceType`, `pathPrefix`,
`indexedAfter`, `language`), per-API-key source authorization, and
prompt-injection exclusion. Optional stages behind config flags: LLM query
rewriting (`Search:QueryRewrite:*`) and an `IReranker` (`Search:Rerank:*`).
Every stage is instrumented (`search.*` histograms, activity spans).

## Consequences
- Positive: recall and precision both measurable via the eval harness
  (`/api/eval/run` — Recall@K/P@K/MRR/faithfulness on versioned datasets).
- Trade-off: rewrite/rerank add LLM latency — off by default.

## Related SPEC
- SPEC-20260914-hybrid-retrieval, SPEC-20260923-retrieval-quality,
  SPEC-20260923-eval-harness (`.specs/`)
