# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Enable GraphRAG by default and clean settings toggle
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
