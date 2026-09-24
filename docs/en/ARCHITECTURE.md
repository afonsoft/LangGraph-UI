# KnowledgeHub Architecture

## Overview

KnowledgeHub is an all-in-one standalone knowledge platform built on .NET 10. It combines a Blazor WebAssembly admin SPA, a REST management API, and a native Model Context Protocol (MCP) server into a single Kestrel-hosted process.

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

1. **KnowledgeHub.Shared**: Contains shared DTOs, JSON-RPC 2.0 / MCP contracts, and enums.
2. **KnowledgeHub.Client**: Blazor WebAssembly single-page application using BootstrapBlazor components, providing administration, MCP monitoring, playgrounds, and source management.
3. **KnowledgeHub.Server**: Kestrel host running Minimal APIs, Entity Framework Core with SQLite (or PostgreSQL/pgvector), connector sync engine, authentication (cookie + API keys), and upstream MCP proxies.
4. **KnowledgeHub.McpEngine**: Native MCP server handling SSE sessions, Streamable HTTP transport, and JSON-RPC dispatching.

## Data Flow & Retrieval

- **Ingestion**: Connectors ingest from Obsidian vaults (local or WebDAV), web pages, documents, Notion, and SQL databases.
- **Chunking & Indexing**: Structure-aware chunking for code and config, with local ONNX Runtime embeddings (all-MiniLM-L6-v2) or external embedding providers.
- **Hybrid Retrieval**: Combines SQLite FTS5 full-text search with vector similarity search (sqlite-vec or pgvector HNSW), fused via Reciprocal Rank Fusion (RRF).
- **Synthesis & Agent Loop**: Responses synthesized via `IChatClient` (Ollama/OpenAI), supporting multi-turn conversations, tool calling, and Human-in-the-Loop (HITL) approvals.
