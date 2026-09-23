# SPEC-20260923-graphrag

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `graphrag` |
| Type | `Feature` (large — knowledge graph layer over RAG) |
| Stack | `.NET 10 / C# 14` + `IChatClient` (extraction) + SQLite adjacency tables (MVP) — Neo4j opt-in later |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-graphrag` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` — approved by owner 2026-09-23 (batch approval of all 10 gap-analysis SPECs) |
| Origin | `gap-analysis-20260923` — GAP-requirements-graphrag (baixa, conditional). Proposal §2.2, §11. |

## 1. User Story

**As a** user asking relational questions ("what depends on X?", "what's the impact of changing Y?", "which incidents touched this API?")
**I want** the platform to extract entities/relations from indexed documents into a queryable graph with provenance
**So that** multi-hop questions that pure vector search cannot answer get structured, evidence-backed answers.

**Problem context:**
The platform answers "what does the text say" but not "how are things connected". The proposal reserves GraphRAG for dependency/impact/global-synthesis questions — and warns it must be added only when RAG measurably fails to connect dots. Per the proposal's own sequencing, this SPEC exists as the design contract; implementation is justified when the eval harness (SPEC-20260923-eval-harness) shows relational questions failing.

## 2. Scope

**In scope (MVP):**
- Entity/relationship extraction during sync (opt-in per source: `SourceType`/config flag `Graph:Enabled` + per-source toggle).
- Extraction model: `IChatClient` produces structured JSON `{entities:[{name,type}], relations:[{from,to,kind,evidenceChunkId}]}` per chunk; `kind` from a small ontology (`DEPENDS_ON`, `USES`, `PUBLISHED_IN`, `OWNED_BY`, `AFFECTED_BY`, `MENTIONS`).
- Graph store: `IKnowledgeGraphStore` abstraction + `SqliteKnowledgeGraphStore` MVP (`kg_nodes`, `kg_edges` with `evidence_chunk_id`, `document_id`, `source_id` on every edge — provenance mandatory).
- Entity resolution: normalized-name merge (`lower + trim + alias table `kg_aliases``) with conflict surfacing (two entities same name different sources → `kg_aliases` rows, not silent merge).
- MCP tools: `find_dependencies(component, depth≤3)`, `find_dependents(component, depth≤3)`, `find_path(a, b, depth≤3)`, `analyze_impact(component)` (1-hop dependents + affected docs) — read-only, depth-capped.
- Answer integration: `agent_chat` can call these tools; `ask_knowledge` unchanged (graph tools surface via agent, not synthesis path).

**Out of scope (MVP):**
- Community detection / global "map-reduce over communities" summaries (Microsoft-style GraphRAG phase 2).
- Neo4j/production graph DB (interface allows it; `Neo4jKnowledgeGraphStore` later).
- Graph visualization UI.
- Temporal/versioned edges.

## 3. Technical Context

Extraction hooks into `IngestionService` post-chunk-embed (both vault and connector paths); per-source flag on `KnowledgeSource.ConfigurationJson` (`graph:true`). New `Domain/Entities/KgNode`, `KgEdge`, `KgAlias`; EF migration. Tools registered via a new `ToolProviders/GraphToolsProvider.cs` in `DynamicToolCatalog`. Depth-capped traversal implemented as bounded BFS in SQL (recursive CTE on `kg_edges`) — SQLite supports `WITH RECURSIVE`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs` (post-embed hook points :145, :275)
- `src/KnowledgeHub.Server/Mcp/ToolProviders/KnowledgeToolsProvider.cs` (tool registration pattern)
- `src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs`, `CatalogTool.cs`
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs`
- `src/KnowledgeHub.Server/Services/AgentService.cs` (tool visibility)
- `src/KnowledgeHub.Server/Chat/ChatClientFactory.cs` (extraction client reuse)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Graph/IKnowledgeGraphStore.cs        (new)
src/KnowledgeHub.Server/Graph/SqliteKnowledgeGraphStore.cs   (new — kg_* tables, recursive CTE traversal)
src/KnowledgeHub.Server/Graph/EntityExtractor.cs             (new — IChatClient → structured JSON)
src/KnowledgeHub.Server/Graph/EntityResolver.cs              (new — normalized merge + alias)
src/KnowledgeHub.Server/Graph/GraphTraversal.cs              (new — bounded BFS queries)
src/KnowledgeHub.Server/Domain/Entities/KgNode.cs,KgEdge.cs,KgAlias.cs (new)
src/KnowledgeHub.Server/Mcp/ToolProviders/GraphToolsProvider.cs   (new)
src/KnowledgeHub.Server/Ingestion/IngestionService.cs        (modify — opt-in extraction)
src/KnowledgeHub.Server/Migrations/*                         (new)
tests/KnowledgeHub.Tests.Unit/Graph/*                        (new)
tests/KnowledgeHub.Tests.Integration/GraphToolsTests.cs      (new)
```

## 4. Requirements

### RF-001 — Extraction pipeline (opt-in)
- **Description:** For sources with `graph:true`, each new/changed chunk is sent to `IChatClient` with a strict JSON-schema extraction prompt; results parsed leniently (bad JSON → logged, skipped).
- **Rules:** extraction runs after embeddings, batched per document; failures never fail the sync — recorded in `LastError` suffix/`Warnings`; cost control: `Graph:MaxChunksPerSync` cap.

### RF-002 — Graph store + provenance
- **Description:** `kg_nodes(id, name, normalized_name, type, first_seen)`; `kg_edges(id, from_id, to_id, kind, evidence_chunk_id, document_id, source_id, created_at)`; every edge carries ≥1 evidence chunk — no provenance-free edges.
- **Rules:** document deletion cascades its edges (`document_id` FK); source deletion cascades fully; `kg_aliases(alias_normalized, node_id, source_id)` records merges for auditability.

### RF-003 — Entity resolution
- **Description:** Normalized name (`lower`, trim, punctuation collapse) match → existing node; ambiguous same-name different-type → distinct nodes + alias rows (conflict visible via `GET` debug surface or tool result note).
- **Rules:** never merge across types silently; merge decisions recorded in aliases.

### RF-004 — MCP graph tools
- **Description:** `find_dependencies(component, depth?)`, `find_dependents(component, depth?)`, `find_path(a, b, depth?)`, `analyze_impact(component)` — each returns `{nodes, edges}` plus per-edge `evidenceChunkId` and resolved document/URI so answers cite sources.
- **Rules:** `depth` clamped 1..3; result size capped (`Graph:MaxResults`, default 200 edges); unknown component → friendly `isError` listing nearest normalized-name matches (top 5).

### RF-005 — Degradation
- **Description:** `Graph:Enabled=false` (default) → tools absent from catalog, zero extraction cost, zero schema writes.
- **Rules:** toggling off leaves data dormant (not deleted); re-enable resumes.

**Business rules / invariants:**
- Every returned edge traces to `evidenceChunkId` → document → source — provenance is non-negotiable (proposal §11.2).
- Extraction never blocks sync completion — graph build is best-effort.
- Conflicting relations from different sources coexist (both stored with provenance) — never silently fused.

## 5. API Contract

**Tools (read-only):**
```text
find_dependencies(component: string, depth?: 1..3) → { nodes:[{id,name,type}], edges:[{from,to,kind,evidence:{chunkId,docTitle,uri}}] }
find_dependents(component: string, depth?: 1..3)   → same shape
find_path(a: string, b: string, depth?: 1..3)      → { paths: [ [node,…] ], edges }
analyze_impact(component: string)                  → { dependents, affectedDocuments, incidentLikeNodes? }
```
**Expected errors:** unknown component → `isError` + suggestions; depth out of range → clamped.

## 6. Acceptance Criteria

- [ ] **Given** docs stating "API-X depends on DB-Y" and "Service-Z uses API-X" **when** synced with graph enabled **then** `find_dependencies("Service-Z", 2)` returns both edges with evidence chunk ids.
- [ ] **Given** the same entity written two ways ("DB-Y", "db y") **when** extracted **then** one node with an alias row exists.
- [ ] **Given** a deleted document **when** graph is queried **then** its edges are gone (cascade).
- [ ] **Given** `depth=9` requested **when** tool called **then** clamped to 3 and noted in result metadata.
- [ ] **Given** `graph:false` source **when** synced **then** zero `kg_*` writes and no extraction LLM calls.
- [ ] **Given** malformed extractor JSON **when** a chunk fails **then** sync completes, warning recorded.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Cycles in graph | A→B→A | traversal visits each node once (visited set) |
| 10k-edge traversal | dense graph | `MaxResults` cap + truncation flag |
| Same name, different types | "Atlas" service vs server | two nodes, aliases recorded, tool disambiguates |
| LLM extractor offline | provider down | sync completes; graph skipped with warning |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; confirm catalog/tool-provider patterns.
- [ ] **T2 — Schema:** entities + migration + `IKnowledgeGraphStore`/`SqliteKnowledgeGraphStore` CRUD.
- [ ] **T3 — Extraction:** prompt + strict-parse + batching in `IngestionService` (both paths).
- [ ] **T4 — Resolution:** normalized merge + aliases + conflict rules.
- [ ] **T5 — Traversal:** bounded recursive-CTE BFS + `GraphTraversal` queries.
- [ ] **T6 — Tools:** `GraphToolsProvider` + contract tests (schema pinned).
- [ ] **T7 — Tests:** unit (resolver, traversal on fixture graph) + integration (end-to-end sync→query).
- [ ] **T8 — Verification:** build/test/format green. Done + PR.

## 8. Organization Guardrails

- Off by default everywhere (`Graph:Enabled=false`, per-source `graph:false`) — zero cost unless enabled.
- Extraction prompts/templates are versioned (`Graph:PromptVersion` in edge metadata) for future re-extraction.
- No secrets/PII in extraction prompt logs.
- Depth/result caps are hard — traversal can never be unbounded.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Provenance chain (edge→chunk→doc→source) verified end-to-end in integration test.
- [ ] Extraction cost documented (LLM calls per doc) in SPEC evidence.
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- Extraction prompt ontology: fixed 6-kind list vs. open — MVP uses fixed + `MENTIONS` catch-all.
- Whether `analyze_impact` should weight edge kinds (DEPENDS_ON > MENTIONS) — MVP returns grouped-by-kind instead of weighted.
- Activation gate: implement after eval harness (SPEC-20260923-eval-harness) demonstrates relational-question misses — per proposal sequencing.
