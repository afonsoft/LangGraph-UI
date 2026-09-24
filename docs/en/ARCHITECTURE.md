# KnowledgeHub Architecture

## Overview

KnowledgeHub is an all-in-one standalone knowledge platform built on .NET 10. It combines a Blazor WebAssembly admin SPA, a REST management API, a native Model Context Protocol (MCP) server, and a SignalR activity hub into a single Kestrel-hosted process.

> Full diagrams, ADRs, and the interactive runtime view live in [`docs/architecture/`](../architecture/) — see `system-architecture.md` and `README.md` there.

## System Layers

```
                                  +---------------------------+
                                  |    External MCP Clients   |
                                  | (Cursor, Claude Desktop)  |
                                  +--------------+------------+
                                                 | Streamable HTTP / SSE
                                                 v
+------------------+             +---------------+-----------+
|    Blazor SPA    |             |  KnowledgeHub.Server      |
| (Admin PWA / UI) |             |  (Kestrel, Minimal APIs)  |
+--------+---------+             +---------------+-----------+
         | REST / SignalR                        |
         v                                       v
+------------------+             +---------------+-----------+
| KnowledgeHub.    |             | KnowledgeHub.McpEngine    |
|   Client         |             | (JSON-RPC 2.0 Dispatcher) |
+------------------+             +---------------+-----------+
         |                                       |
         +-------------------+-------------------+
                             |
                             v
                 +-----------+-----------+
                 | KnowledgeHub.Shared   |
                 | (Contracts, DTOs)     |
                 +-----------+-----------+
                             |
                             v
                 +-----------+-----------+
                 |    EF Core SQLite     |
                 | (sqlite-vec / pgvector) |
                 +-----------------------+
```

## Core Projects

1. **KnowledgeHub.Shared**: shared DTOs, JSON-RPC 2.0 / MCP contracts, enums.
2. **KnowledgeHub.Client**: Blazor WebAssembly SPA (BootstrapBlazor) — administration, MCP monitor, playground, chat, approvals, eval, settings.
3. **KnowledgeHub.Server**: Kestrel host — Minimal APIs, EF Core SQLite, connectors/sync engine, hybrid search, answer/agent loops, GraphRAG, auth (cookie + API keys), rate limiting, upstream MCP proxies, OpenTelemetry.
4. **KnowledgeHub.McpEngine**: native MCP server — SSE sessions, Streamable HTTP transport, JSON-RPC dispatch.

## Data Flow & Retrieval

- **Ingestion**: connectors ingest Obsidian vaults (local/WebDAV), web pages, document files, Notion, REST APIs, and SQL databases; structure-aware chunking for markdown/code/config; every chunk passes the prompt-injection sanitizer (`SuspicionFlags` + `security_events` audit) before embedding.
- **GraphRAG**: when `Graph:Enabled` (default **on**) and the source opts in (`"graph": true`), an LLM extracts entities/relations into `KgNodes`/`KgEdges`/`KgAliases` with full provenance — traversed by `find_dependencies`, `find_dependents`, `find_path`, `analyze_impact`.
- **Hybrid retrieval**: SQLite FTS5 lexical + vector KNN (sqlite-vec or pgvector HNSW) fused via RRF (k=60); optional LLM query rewriting and reranking; metadata filters; per-API-key source authorization; flagged-chunk exclusion.
- **Synthesis & agent**: `IChatClient` (Ollama/OpenAI) answers with `[n]` citations; agent loop supports tool-calling, HITL approvals, bounded SSE streaming, and conversation threads with summarization.
- **Observability**: `Meter`/`ActivitySource` instrument search stages, LLM calls, agent iterations, sync runs, and cache regions — exported via opt-in OTLP or Prometheus (`/metrics`).

## Decision records

Key decisions are recorded as ADRs in [`docs/architecture/`](../architecture/README.md) — single-process monolith, SQLite-first storage, hybrid MCP sessions, dual auth, hybrid retrieval, GraphRAG on adjacency tables, partitioned rate limiting, injection guard, OTel observability, upstream proxies.
