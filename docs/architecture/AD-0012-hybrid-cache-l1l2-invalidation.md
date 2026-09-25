# AD-0012 — Hybrid L1/L2 cache with pub/sub invalidation

## Context

The opt-in Redis `IDistributedCache` gave every read a network hop, one global TTL could not express region-appropriate freshness (embeddings are stable for days, search results for minutes), and mutating a source or settings on one node left stale entries in other nodes' Redis — there was no distributed invalidation. Per-key admin (clear one pattern) and honest stats also didn't exist.

## Decision

Layer an in-process L1 in front of the Redis L2 (`Cache:L1Enabled`, `Cache:L1MaxTtlMinutes` as the staleness bound). TTLs resolve per key **region** via longest-prefix matching (`Cache:RegionTtlMinutes` — `emb` 24 h, `search` 5 min, `ans` 10 min, `mcp:tool` 1 h, `rewrite` 24 h, `expand` 1 h, `index` 7 d, `secret` 1 h). Mutations publish invalidations on the `kh:invalidate` pub/sub channel so every node evicts its L1/L2 copies. `CacheTtlPolicy` is resolved inside the cache service (not injected per call site), `Striped` locks guard single-flight recomputation, and admin endpoints (`GET /api/settings/cache`, `POST .../clear`, `DELETE .../keys/{*key}`) report per-process tracked keys overlaid with Redis SCAN/INFO.

## Consequences

- Positive: hot reads stay in-process; region-appropriate TTLs cut both staleness bugs and Redis pressure; invalidation propagates across nodes without a sweep; tool results (`mcp:tool:`) are cached with canonical argument hashing and index-version invalidation.
- Trade-off: L1 staleness is bounded by `L1MaxTtlMinutes` for keys that miss the pub/sub; pub/sub depends on Redis being reachable (falls back to bounded TTL staleness when down).

## Related SPEC

- [.specs/SPEC-20260925-hybrid-cache-l1l2.md](../../.specs/SPEC-20260925-hybrid-cache-l1l2.md)
- [.specs/SPEC-20260925-distributed-invalidation-pubsub.md](../../.specs/SPEC-20260925-distributed-invalidation-pubsub.md)
- [.specs/SPEC-20260925-cache-region-ttl-policies.md](../../.specs/SPEC-20260925-cache-region-ttl-policies.md)
- [.specs/SPEC-20260926-cache-coherence-and-ttl.md](../../.specs/SPEC-20260926-cache-coherence-and-ttl.md)
