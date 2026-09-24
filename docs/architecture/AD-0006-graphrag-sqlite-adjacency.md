# AD-0006 — GraphRAG on SQLite adjacency tables (dedicated graph DB deferred)

## Context
The staged architecture plan made a graph database conditional: add GraphRAG
only with measurable need, and prefer the existing store for the MVP.

## Decision
Entities/relations are extracted at ingestion (opt-in per source via
`"graph": true` in `ConfigurationJson`, plus the global `Graph:Enabled` flag —
default **on**, editable at `/settings` without restart) by
`EntityExtractor` (LLM, strict prompt + lenient parse, fail-open). The graph
persists as adjacency tables (`KgNodes`, `KgEdges`, `KgAliases`) in the same
SQLite DB — deterministic entity resolution (`NormalizedName+Type` unique,
alias `merge`/`conflict` semantics), full provenance (edge → evidence chunk →
document → source → prompt version). Traversal tools: `find_dependencies`,
`find_dependents`, `find_path`, `analyze_impact` (BFS, depth ≤ 3, bounded
fan-out via `Graph:MaxResults`).

## Consequences
- Positive: no new infrastructure; graph rows join cleanly with chunks;
  consistent backup story.
- Trade-off: multi-hop analytics on very large graphs will eventually want a
  dedicated engine (Neo4j) — the `IKnowledgeGraphStore` seam keeps that
  migration isolated; re-evaluate when eval data shows relational-query need.

## Related SPEC
- [.specs/SPEC-20260923-graphrag.md](../../.specs/SPEC-20260923-graphrag.md)
- [.specs/SPEC-20260923-graph-settings-ui.md](../../.specs/SPEC-20260923-graph-settings-ui.md)
