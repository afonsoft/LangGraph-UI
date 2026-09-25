# KnowledgeHub

[![CI Build & Test](https://github.com/afonsoft/LangGraph-UI/actions/workflows/ci-build-test.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/ci-build-test.yml)
[![Code Quality](https://github.com/afonsoft/LangGraph-UI/actions/workflows/code-quality.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/code-quality.yml)
[![Security Scan](https://github.com/afonsoft/LangGraph-UI/actions/workflows/security-scan.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/security-scan.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor WASM](https://img.shields.io/badge/Blazor-WASM%20PWA-512BD4)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

All-in-one standalone knowledge platform: Blazor WebAssembly admin UI, REST API, native MCP server (Streamable HTTP + legacy SSE), SQLite persistence, pluggable embeddings and vector stores, hybrid retrieval (FTS5 + vector RRF), GraphRAG entity/relation extraction, agentic chat with HITL approvals, prompt-injection defense, partitioned rate limiting, and OpenTelemetry observability — all in a single Kestrel-hosted .NET 10 process.

## Endpoints

| Route | Purpose |
|---|---|
| `/` | Blazor WASM admin UI (`/sources`, `/mcp-monitor`, `/playground`, `/chat`, `/approvals`, `/settings`, `/api-keys`) — installable PWA, collapsible icon-rail sidebar, mobile-responsive layout |
| `/api/sources`, `/api/search`, `/api/ask`, `/api/agent`, `/api/approvals`, `/api/threads` | REST API — `POST /sources/{id}/sync` is async (`202 + jobId`; `?wait=true` for the legacy sync contract) |
| `/api/ingestion/jobs`, `/api/ingestion/jobs/{id}`, `/api/ingestion/jobs/{id}/cancel` | Background ingestion jobs — status, per-doc counters, cancellation |
| `/api/eval/baselines` | Named eval baselines for regression gates (promote a run, auto-compare future runs) |
| `/api/settings/chat`, `/api/settings/chat/test`, `/api/settings/graph`, `/api/settings/integrations*` | Persisted chat-provider config, GraphRAG runtime settings (enable, budgets), and masked integration keys (firecrawl, deepwiki, tavily, context7) |
| `/api/api-keys/{id}/settings/chat`, `/api/api-keys/{id}/settings/integrations/{provider}`, `/api/api-keys/{id}/rate-limit`, `/api/api-keys/{id}/scopes` | Per-API-key overrides: chat endpoint/model/key, integration keys, rate limits, allowed sources/tools |
| `/api/security/events`, `/api/eval/run`, `/api/eval/runs` | Prompt-injection audit feed and retrieval-quality eval harness (Recall@K/P@K/MRR/faithfulness) |
| `/api/ask/stream`, `/api/agent/stream` | REST SSE — `token`/`tool_start`/`tool_end`/`awaiting_approval`/`done`/`error` events, 15 s heartbeat, `X-Accel-Buffering: no` |
| `/mcp` | MCP — Streamable HTTP, hybrid sessions: `initialize`-handshake clients (≤2025-11-25) get full stateful sessions incl. `tools/list_changed` push; `2026-07-28` clients are served statelessly (no session, re-list on demand). `Mcp:SessionMode` knob: `Stateless`/`Stateful`/`StatefulForInitializeClients` (default) |
| `/mcp/sse` + `/mcp/message` | MCP — legacy HTTP/SSE (Cursor, Claude Desktop) |
| `/hubs/mcp` | SignalR feed for the MCP monitor |

## Authentication

All surfaces except the health probes (`/health/*`) and `POST /api/auth/login` require authentication — the SPA, the REST API, `/mcp`, `/mcp/sse` and `/hubs/mcp`.

**Browser (cookie).** The SPA signs in at `/login`; the session is an HttpOnly cookie (`SameSite=Lax`, `Secure`, 12 h sliding). On first startup an `admin` user is seeded with the password `123qwe` (override via `Auth__AdminInitialPassword`) and `mustChangePassword` forces the password change screen before any other page or API call. Password policy: ≥8 chars, different from the current one. Five consecutive failed logins lock the account for 5 minutes (`423 Locked`); wrong credentials return a generic `401` (no user enumeration).

| Auth route | Purpose |
|---|---|
| `POST /api/auth/login` | `{ username, password }` → sets the session cookie |
| `GET /api/auth/me` | `{ username, mustChangePassword }` |
| `POST /api/auth/logout` | clears the session cookie |
| `POST /api/auth/change-password` | `{ currentPassword, newPassword }` → `204`, clears the flag |
| `GET /api/apikeys` · `POST /api/apikeys` · `DELETE /api/apikeys/{id}` | manage API keys (cookie session only) |

**API keys (`aft_*`) for non-browser clients.** Create one under `/api-keys` (or `POST /api/apikeys`); the full secret `aft_<32-hex>` is shown **once** — only its SHA-256 hash is stored. Send it as a bearer token:

```bash
curl https://rag.afonsoft.dev/mcp \
  -H "Authorization: Bearer aft_..." \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}'
```

Accepted on `/mcp`, `/mcp/sse`, `/api/*` and `/hubs/mcp` (SignalR clients that cannot send headers may use `?access_token=`). API keys can call everything **except** the API-key management endpoints, which require a cookie session. Revoking a key (`DELETE /api/apikeys/{id}` or the UI) takes effect immediately. Each key can also carry its **own chat endpoint/model/key and integration keys** (firecrawl, tavily) — set them in the `/api-keys` UI, via `/api/api-keys/{id}/settings/*`, or through the `set_api_key_settings` MCP tool.

> **Breaking change for external MCP clients** (Cursor, Claude Desktop, …): they must now send `Authorization: Bearer aft_...`. Generate the key in the admin UI first.

## MCP tools

`search_knowledge`, `ask_knowledge`, `agent_chat`, `write_knowledge`, `read_document`, `write_note`, `set_api_key_settings` (per-key chat and integration settings), `query_{source_slug}` per active source, GraphRAG traversal (`find_dependencies`, `find_dependents`, `find_path`, `analyze_impact` — when `Graph:Enabled`, default on), plus upstream proxies — DeepWiki (`ask_question`, `read_wiki_structure`, `read_wiki_contents`), Firecrawl (`firecrawl_*`), Tavily (`tavily_*`), Context7 (`resolve-library-id`, `query-docs`) and arbitrary `McpProxy` sources.

## Configuration

```jsonc
{
  "Database": { "Path": "knowledgehub.db" },   // or KnowledgeHub:DatabasePath
  "Embeddings": {
    "Provider": "deterministic",               // deterministic | ollama | openai | onnx
    "Endpoint": "http://localhost:11434",
    "ApiKey": "",
    "Model": "nomic-embed-text",
    "Dimensions": 384,
    "ModelPath": "models/all-MiniLM-L6-v2"     // Provider=onnx — model.onnx + vocab.txt dir
  },
  "VectorStore": {
    "Provider": "sqlite",                      // sqlite | sqlite-vec | postgres (pgvector)
    "ConnectionString": "",
    "Postgres": {                              // VectorStore:Postgres — pgvector tuning
      "HnswThreshold": 1000,                   // rows before the HNSW index is created
      "HnswM": 16, "HnswEfConstruction": 64, "HnswEfSearch": 40,
      "IterativeScan": false,                  // pgvector ≥0.8 — filtered scans keep topK
      "StorageType": "vector",                 // vector | halfvec (pgvector ≥0.7, ≤2000 dims)
      "AllowStorageMigration": false,          // consent gate for ALTER COLUMN … TYPE halfvec
      "MinPoolSize": 5, "MaxPoolSize": 50
    }
  },
  "Cache": {
    "Provider": "memory",                      // memory | redis
    "Redis": { "ConnectionString": "" },       // host.docker.internal:6379 when using Redis
    "ToolCacheEnabled": true,                  // cache MCP tool results (mcp:tool region)
    "ToolCacheTtlMinutes": 60,
    "L1Enabled": true,                         // in-process L1 in front of redis L2
    "L1MaxTtlMinutes": 5,                      // L1 staleness bound
    "DefaultTtlMinutes": 10,                   // fallback for unknown regions
    "RegionTtlMinutes": {                      // per-key-region TTLs (longest-prefix match)
      "emb": 1440, "search": 5, "ans": 10, "mcp:tool": 60,
      "rewrite": 1440, "expand": 60, "index": 10080, "secret": 60
    },
    "AnswerCache": { "Enabled": false, "TtlSeconds": 600 }
  },
  "Search": {
    "Lexical": { "Enabled": true },            // FTS5 leg of hybrid retrieval
    "QueryRewrite": { "Enabled": false, "LexicalToo": false },
    "Rerank": { "Enabled": false, "MaxCandidates": 50 }
  },
  "RateLimiting": {                            // per-partition: api key → user → IP
    "Enabled": true,
    "LlmPermitLimit": 20, "LlmWindowSeconds": 60,
    "AnonymousLlmPermitLimit": 5,
    "SyncPermitLimit": 10, "SyncWindowSeconds": 3600,
    "GeneralPermitLimit": 300, "GeneralWindowSeconds": 60,
    "TrustForwardedHeaders": false
  },
  "Security": {
    "Injection": { "ExcludeFlagged": true }    // drop flagged chunks from results
  },
  "Graph": {                                   // GraphRAG — also editable in /settings
    "Enabled": true,
    "MaxChunksPerSync": 200,
    "MaxChunkChars": 2000,
    "MaxResults": 200
  },
  "Telemetry": {
    "Otlp": { "Endpoint": "" },                // OTLP traces/metrics exporter
    "Metrics": { "Prometheus": false }         // /metrics scrape endpoint
  },
  "Chat": {                                    // optional global default
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.2",
    "ApiKey": ""
  },
  "DeepWiki": {                                // upstream MCP proxy
    "Enabled": true,
    "Endpoint": "https://mcp.deepwiki.com/mcp",
    "PrivateEndpoint": "https://mcp.devin.ai/mcp",
    "ApiKey": ""
  },
  "Firecrawl": {
    "Enabled": true,
    "Endpoint": "https://mcp.firecrawl.dev/v2/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 300
  },
  "Tavily": {
    "Enabled": true,
    "Endpoint": "https://mcp.tavily.com/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 120
  },
  "Context7": {
    "Enabled": true,
    "Endpoint": "https://mcp.context7.com/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 60
  },
  "Mcp": {
    "SessionMode": "StatefulForInitializeClients"  // Stateless | Stateful | StatefulForInitializeClients
  },
  "Auth": {
    "AdminInitialPassword": "123qwe"           // seed password for admin user
  },
  "Serilog": {
    "MinimumLevel": {                          // runtime-adjustable via /api/settings/log-level
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    },
    "WriteTo": [
      { "Name": "Console" },
      { "Name": "File", "Args": {            // daily rolling file, 14-day retention
        "path": "logs/knowledgehub-.log",
        "rollingInterval": "Day",
        "retainedFileCountLimit": 14 } }
    ]
  }
}
```

Environment variables override `appsettings.json` (double underscore → nested key). See `.env.example` for the full list.

**Logging.** Structured request logging (Serilog): `/health` and static-asset noise logs at Debug, 5xx at Error; every request carries `x-request-id`/`RequestId`. Secrets are scrubbed by an enricher — keys named `*key*`/`*token*`/`*secret*`/`*password*`/`*connectionstring*` and `aft_*`/`ctx7sk-*`/`sk-*`/`Bearer` patterns are written as `***REDACTED***` in every property. With `Telemetry:Otlp:Endpoint` set, logs also ship to the same OTLP backend as traces/metrics. The runtime level can be raised temporarily via `PUT /api/settings/log-level` (`autoResetMinutes` 0–120). In Docker, the file sink writes to `./logs` (mounted volume — see below).

## Repository Structure

```text
KnowledgeHub/
├── src/
│   ├── KnowledgeHub.Shared/      # DTOs, JSON-RPC 2.0 / MCP contracts, enums
│   ├── KnowledgeHub.Client/      # Blazor WASM SPA (BootstrapBlazor)
│   ├── KnowledgeHub.Server/      # Kestrel host, Minimal APIs, EF Core SQLite, sync
│   └── KnowledgeHub.McpEngine/   # Native MCP: SSE sessions, JSON-RPC dispatcher
├── tests/
│   ├── KnowledgeHub.Tests.Unit/
│   └── KnowledgeHub.Tests.Integration/
├── .specs/                       # SPEC SDD — source of truth for features
├── docs/architecture/            # Architecture diagrams and ADRs
├── .claude/skills/               # versioned skills (afonsoft/skills)
├── skills-lock.json              # SHA-256 hashes of installed skills
├── backup.sh / restore.sh        # backup/restore SQLite + uploads
├── docker-compose.yml            # containerized deployment
└── LICENSE                       # MIT — Afonso Dutra Nogueira Filho, 2026
```

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Language | C# | 14 (.NET 10) |
| Framework | ASP.NET Core (Kestrel) | 10.x |
| Frontend | Blazor WebAssembly (BootstrapBlazor) | 10.x |
| Database | SQLite (EF Core) + sqlite-vec | 10.x |
| Vector Store | sqlite-vec / pgvector | 0.1.9 / 0.3.2 |
| Embeddings | ONNX Runtime (all-MiniLM-L6-v2) | 1.29.0 |
| AI SDK | Microsoft.Extensions.AI.Abstractions | 10.9.0 |
| MCP Protocol | ModelContextProtocol | 2.2.0 |
| CI/CD | GitHub Actions | — |
| Container | Docker Compose | — |

## Getting Started

### Prerequisites

- .NET SDK 10.0.x ([global.json](global.json))
- Node.js ≥ 20.x (for development tooling)
- Docker & Docker Compose (optional, for containerized deployment)

### Install

```bash
git clone https://github.com/afonsoft/LangGraph-UI.git
cd LangGraph-UI
```

### Configure

```bash
cp .env.example .env
# Edit .env with your values (API keys, endpoints, etc.)
```

### Run (development)

```bash
dotnet build KnowledgeHub.slnx
dotnet ef database update -p src/KnowledgeHub.Server
dotnet run --project src/KnowledgeHub.Server
```

Open http://localhost:5000 and sign in with `admin` / `123qwe`.

### Run (Docker)

```bash
mkdir -p data logs && chown 1654:1654 logs   # container runs as uid 1654 (app) — see note
docker compose up -d
```

Access at http://localhost:5000.

> **`./logs` permission caveat.** When the host directory doesn't exist, Docker creates it as `root`, but the container runs as `app` (uid 1654) — the Serilog file sink then fails silently (console output still works). Pre-create with `mkdir -p logs && chown 1654:1654 logs` (or `chown` it once after the first `up`). Logs persist across recreates/upgrades in the `./logs` volume — daily rolling files, 14-day retention, secrets redacted (`***REDACTED***`).

## Tests & Coverage

```bash
dotnet test                                          # unit + integration tests
dotnet test --collect:"XPlat Code Coverage"          # with Coverlet coverage
dotnet format KnowledgeHub.slnx --verify-no-changes  # formatting gate
```

| Metric | Value |
|---|---|
| **Total tests** | 231 (197 prior + 20 new `ToolCacheServiceTests` + 14 existing cache tests) |
| **Pass rate** | 100% |
| **Line coverage** | 78% (23 961 / 30 697 coverable lines) |
| **Branch coverage** | 58.1% (5 192 / 8 934 branches) |
| **Method coverage** | 79.8% (1 752 / 2 194 methods) |
| **Coverage date** | 2026-09-24 |

CI gates: Build (0 warnings), Unit Tests, Integration Tests (SQLite), Blazor WASM Client Validation, Docker Image Build, Code Quality (SonarQube), Security Scan, `dotnet format --verify-no-changes` (0 files changed of 371).

## Architecture

KnowledgeHub follows a clean architecture pattern with four layers:

- **Shared**: DTOs, MCP contracts, enums — consumed by all projects
- **Client**: Blazor WASM SPA with BootstrapBlazor components, PWA support, SignalR for real-time updates
- **Server**: Kestrel host with Minimal APIs, EF Core SQLite persistence, authentication, configuration validation
- **McpEngine**: Native MCP server implementing Streamable HTTP and legacy SSE transports, JSON-RPC 2.0 dispatcher, session management

Key architectural decisions:

- Single-process deployment: SPA + API + MCP server in one Kestrel process
- Hybrid MCP session mode: stateful for initialize-handshake clients, stateless for modern clients
- Pluggable embeddings: deterministic, Ollama, OpenAI, or local ONNX Runtime
- Multiple vector store backends: sqlite-vec (KNN) or PostgreSQL with pgvector (HNSW, batched upserts, provenance metadata)
- Hybrid retrieval: FTS5 + vector RRF with optional query rewriting and reranking; measured by the built-in eval harness
- GraphRAG on SQLite adjacency tables: LLM extraction at ingestion (per-source opt-in), provenance-tracked traversal tools
- Security: prompt-injection guard (flag → exclude → audit), per-key source/tool scoping, partitioned rate limiting with per-key overrides
- Distributed cache opt-in: in-memory (default) or Redis
- OpenTelemetry: traces + metrics with opt-in OTLP/Prometheus exporters
- Upstream MCP proxies: DeepWiki, Firecrawl, Tavily, Context7, plus arbitrary `McpProxy` sources with encrypted secrets
- Per-API-key customization: chat settings, integration keys, scopes, and rate limits scoped to each key

See [docs/architecture/](docs/architecture/README.md) for ADRs, the editable system diagram, and the interactive runtime view — and [docs/en/ARCHITECTURE.md](docs/en/ARCHITECTURE.md) for the written system design.

## Business & Technical Views

### Business Value

KnowledgeHub solves the problem of fragmented organizational knowledge by providing a unified platform that:

- Ingests knowledge from multiple sources (Obsidian vaults, web pages, documents, Notion, APIs, SQL databases)
- Enables natural language queries with hybrid retrieval (full-text search + vector similarity with RRF reranking)
- Provides agentic capabilities with tool-calling, HITL approvals, and conversation threads with summarization
- Exposes knowledge through both REST API and Model Context Protocol for AI agent integration
- Runs standalone without external dependencies (SQLite, embedded models) for easy deployment

### Technical Decisions

- **.NET 10**: Latest framework with minimal APIs, AOT compilation support, and performance improvements
- **Blazor WASM**: Type-safe frontend sharing DTOs with backend, PWA support for offline capability
- **Model Context Protocol**: Standard protocol for AI agent tool integration, enabling seamless integration with Cursor, Claude Desktop, and other MCP clients
- **SQLite + sqlite-vec**: Zero-config persistence with native KNN vector search, optional PostgreSQL/pgvector for scale
- **ONNX Runtime embeddings**: Local embedding provider (all-MiniLM-L6-v2) for privacy and zero-cost embeddings
- **Clean Architecture**: Clear separation of concerns with Shared/Client/Server/McpEngine projects

## License & Status

- **License**: MIT — see [LICENSE](LICENSE)
- **Status**: Active development
- **Author**: Afonso Dutra Nogueira Filho

## Links

- [Português](README.pt-br.md) — Portuguese translation
- [Architecture Documentation](docs/en/ARCHITECTURE.md) — Detailed system design
- [Contributing Guide](docs/en/CONTRIBUTING.md) — How to contribute
- [Installation Guide](docs/en/INSTALL.md) — Platform-specific setup
- [API Documentation](docs/en/API.md) — REST API reference
- [GitHub Issues](https://github.com/afonsoft/LangGraph-UI/issues) — Bug reports and feature requests
- [Releases](https://github.com/afonsoft/LangGraph-UI/releases) — Version history
