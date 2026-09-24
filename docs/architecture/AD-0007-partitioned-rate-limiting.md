# AD-0007 — Partitioned rate limiting with per-API-key overrides

## Context
LLM-spending and sync endpoints need throttling without penalizing unrelated
callers; JSON-RPC has no 429.

## Decision
`System.Threading.RateLimiting` partitions every request by caller identity:
`key:{apiKeyId}` → `user:{id}` → `anon:{ip}` (X-Forwarded-For opt-in) →
`mcp:global`. Three policies: `llm` (sliding 20/60 s, anonymous 5/60 s),
`sync` (fixed 10/3600 s), `general` (fixed 300/60 s). REST over-limit → 429 +
`Retry-After`; MCP tools/call → `isError` + retry hint. Per-key overrides
(nullable `ApiKey.*RateLimit*` columns, admin-only endpoints) ride a
fingerprinted partition key so edits create fresh buckets immediately —
`PartitionedRateLimiter` never rebuilds an existing partition's options.

## Consequences
- Positive: one noisy key/user cannot starve others; per-key tuning without
  restart; no secrets in partition keys or logs.
- Trade-off: edited overrides orphan the old partition entry (bounded — admin
  action, not attacker-controlled cardinality).

## Related SPEC
- [.specs/SPEC-20260923-rate-limiting.md](../../.specs/SPEC-20260923-rate-limiting.md)
- [.specs/SPEC-20260923-per-key-rate-limits.md](../../.specs/SPEC-20260923-per-key-rate-limits.md)
