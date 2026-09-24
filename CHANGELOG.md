# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `ToolCacheService` / `IToolCacheService` — MCP & agent tool result cache with minimum 1 h TTL, canonical argument hashing, and index-version invalidation (`SPEC-20260924-redis-cache-and-tool-caching RF-002`)
- 20 new BDD-style unit tests for `ToolCacheService` in `ToolCacheServiceTests.cs` covering: cacheability rules, round-trip serialization, error-result exclusion, canonical argument ordering, TTL enforcement, fail-soft behaviour
- `CacheKeys.Tool` cache-key helper for tool invocations
- `TelemetryTags.RegionFor` mapping for `mcp:tool:` prefix
- Tool cache integration in `CallToolHandler`, `ToolsEndpoints`, and `CatalogToolAIFunction`
- `GetCacheStatsAsync` / `ClearCacheAsync` on `SettingsApiClient`
- Redis `ConfigurationOptions.Parse` with fail-safe fallback (replaces non-existent `TryParse`)

### Changed
- `dotnet format --verify-no-changes`: **0 of 371 files** needed changes — code style 100% clean

### Fixed
- Build error `CS0117: 'ConfigurationOptions' does not contain a definition for 'TryParse'` — replaced with `ConfigurationOptions.Parse` inside try/catch

- Write provenance metadata on pgvector upserts
- Surface suspicion flags on kept flagged chunks
- Add per-API-key rate-limit overrides
- Runtime Graph settings editable from `/settings`
- GraphRAG entity/relation extraction, graph store, and traversal tools
- Observability metrics and OTel traces across the retrieval pipeline
- Agent runtime hardening (bounded SSE, answer cache, HTTP resilience)
- Per-API-key source/tool scoping
- Retrieval quality improvements (filters, reranker, query rewriting)
- Prompt-injection guard for retrieved context
- Structure-aware chunking for code and config sources
- Evaluation harness for retrieval quality
- Partitioned rate limiting for HTTP and MCP surfaces
- pgvector HNSW index and batched upserts
- en-US generic tool descriptions and citation path for `read_document`

## [0.0.2] - 2025-09-14

### Added
- Initial standalone release with Blazor WebAssembly admin UI, REST API, native MCP server, SQLite persistence, and Obsidian ingestion.

[Unreleased]: https://github.com/afonsoft/LangGraph-UI/compare/v0.0.2...HEAD
[0.0.2]: https://github.com/afonsoft/LangGraph-UI/releases/tag/v0.0.2
