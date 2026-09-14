# KnowledgeHub — System Architecture

All-in-one standalone .NET 10 platform: a **single Kestrel process** serves the Blazor WebAssembly admin SPA, the REST management API, a native **MCP server** (Streamable HTTP + legacy HTTP/SSE) and a SignalR monitor hub. Agentic RAG over user-registered knowledge sources (Obsidian vaults, web pages, documents, APIs, SQL).

> Source of truth: `.specs/` (SPEC-20260913-*, approved). Diagram sources: `*.mmd` (Mermaid) + `knowledge-hub-architecture.drawio` (editable XML).

## 1. Context & Container view

```mermaid
%%{init: {'theme':'neutral'}}%%
flowchart TB
    subgraph Clients["Consumers"]
        UI["Browser Admin<br/>(Blazor WASM SPA)"]
        CUR["Cursor / Claude Desktop<br/>(legacy SSE)"]
        AG["LangGraph / Python agents<br/>(Streamable HTTP)"]
    end

    subgraph KH["KnowledgeHub — single Kestrel process (.NET 10)"]
        direction TB
        SPA["Static host<br/>index.html + WASM assets"]
        API["REST API<br/>/api/sources · /api/search"]
        MCP["MCP Server<br/>/mcp (Streamable HTTP)<br/>/mcp/sse + /mcp/message (legacy)"]
        HUB["SignalR Hub<br/>/hubs/mcp"]
        CAT["DynamicToolCatalog<br/>IToolProviders"]
        ING["IngestionService<br/>parser · chunker · watcher"]
        SRCH["SearchService<br/>cosine ranking"]
        EMB["IEmbeddingProvider<br/>deterministic · ollama · openai"]
        VEC["IVectorStore<br/>sqlite | postgres/pgvector"]
        FEED["IMcpActivityFeed<br/>ring buffer 500"]
    end

    subgraph Stores["Storage"]
        SQL[("SQLite<br/>knowledgehub.db<br/>catalog + docs + vectors")]
        PG[("PostgreSQL + pgvector<br/>(optional vectors)")]
        VAULT["Obsidian vault<br/>(.md on disk)"]
    end

    DW["DeepWiki<br/>mcp.deepwiki.com/mcp"]
    LLM["Embedding endpoints<br/>Ollama · OpenAI-compat"]

    UI -->|HTTPS+WS| SPA
    UI -->|REST| API
    UI -->|SignalR| HUB
    CUR -->|SSE+POST| MCP
    AG -->|JSON-RPC| MCP

    MCP --> CAT
    MCP --> FEED
    FEED --> HUB
    CAT --> SRCH
    CAT --> ING
    API --> SRCH
    API --> ING
    ING --> EMB
    ING --> VAULT
    ING --> VEC
    SRCH --> VEC
    VEC --> SQL
    VEC --> PG
    EMB --> LLM
    CAT -.->|bypass: ask_question etc.| DW

    classDef client fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px,color:darkblue
    classDef core fill:#d5e8d4,stroke:#82b366,stroke-width:2px,color:darkgreen
    classDef store fill:#f5f5f5,stroke:#666666,stroke-width:2px,color:black
    classDef ext fill:#ffe6cc,stroke:#d79b00,stroke-width:2px,color:#7f4d00
    class UI,CUR,AG client
    class SPA,API,MCP,HUB,CAT,ING,SRCH,EMB,VEC,FEED core
    class SQL,PG,VAULT store
    class DW,LLM ext
```

| Layer | Component | Responsibility |
|---|---|---|
| Consumers | Browser (Blazor WASM) | `/sources` CRUD, `/mcp-monitor` (live), `/playground` (search) |
| | Cursor / Claude Desktop | legacy SSE `GET /mcp/sse` + `POST /mcp/message` |
| | LangGraph / Python agents | Streamable HTTP `POST /mcp` |
| Edge | Static host | `index.html` + WASM assets, SPA fallback for `/sources` etc. |
| | REST API | `/api/sources`, `/api/search` (Minimal APIs) |
| | MCP transport | `MapMcp("/mcp")`, `EnableLegacySse` — official `ModelContextProtocol.AspNetCore` |
| | SignalR hub | `/hubs/mcp` — live sessions + call log |
| Core | `DynamicToolCatalog` | Resolves tools live per `tools/list` from `IToolProviders` |
| | `IngestionService` | Markdown parse → chunk (500/50 tok) → embed → upsert |
| | `VaultWatcherService` | `FileSystemWatcher` → debounce → re-index |
| | `SearchService` | Cosine similarity over model-matched chunks |
| | `IEmbeddingProvider` | `deterministic` (default) · `ollama` · `openai` — endpoint/key/model/dimensions via config |
| | `IVectorStore` | `sqlite` (default) · `postgres`/pgvector |
| | `IMcpActivityFeed` | Ring buffer (500) → SignalR broadcast |
| Storage | SQLite `knowledgehub.db` | Catalog (sources/docs/chunks) + vectors — beside the executable |
| | PostgreSQL + pgvector | Optional vector backend (catalog stays in SQLite) |
| | Obsidian vault | `.md` files on disk — `SafePath` confines reads/writes to vault root |
| External | `mcp.deepwiki.com/mcp` | Upstream bypass via `McpClient` (`HttpClientTransport AutoDetect`) |
| | Ollama / OpenAI-compat | Embedding generation endpoints |

## 2. MCP request flow (tools/call)

```mermaid
%%{init: {'theme':'neutral'}}%%
sequenceDiagram
    autonumber
    participant C as MCP Client
    participant K as /mcp (SDK transport)
    participant F as Activity filters
    participant CAT as DynamicToolCatalog
    participant S as SearchService
    participant V as IVectorStore
    participant D as DeepWikiUpstreamClient
    participant W as mcp.deepwiki.com

    C->>K: POST initialize
    K-->>C: 200 + Mcp-Session-Id
    C->>K: notifications/initialized
    C->>K: tools/list
    K->>CAT: GetToolsAsync (live DB)
    CAT-->>K: 9 tools (local + query_* + deepwiki)
    K-->>C: tools[]

    alt Local tool (search_knowledge)
        C->>K: tools/call search_knowledge
        K->>F: gate + telemetry start
        K->>CAT: resolve + invoke
        CAT->>S: SearchAsync(query, topK)
        S->>V: cosine similarity (model-matched)
        V-->>S: ranked chunks
        S-->>CAT: hits
        CAT-->>K: CallToolResult(text)
        K-->>C: result
    else DeepWiki bypass (read_wiki_structure)
        C->>K: tools/call read_wiki_structure
        K->>F: gate + telemetry start
        K->>CAT: resolve + validate repoName
        CAT->>D: CallAsync(tool, args)
        D->>W: McpClient → tools/call (Streamable HTTP)
        W-->>D: CallToolResult
        D-->>CAT: pass-through (retry once on failure)
        CAT-->>K: CallToolResult
        K-->>C: result
    end
```

- `tools/list` resolves **live** from the DB per call; source mutations broadcast `notifications/tools/list_changed` to connected sessions.
- Invalid params → `McpProtocolException` → JSON-RPC `-32602` (handled by the SDK).
- Execution failures → `CallToolResult { isError: true }` — never break the SSE stream.
- DeepWiki calls pass through with the **same tool names** as upstream (`ask_question`, `read_wiki_structure`, `read_wiki_contents`); `DeepWiki:ApiKey` is only sent as the upstream `Authorization` header.

## 3. Tool catalog

| Tool | Type | Availability |
|---|---|---|
| `search_knowledge` | local — ranked semantic search, all active sources | always |
| `ask_knowledge` | local — retrieval aggregate (ranked passages + citations) | always |
| `write_knowledge` | local — persist & index content (vault `.md` or DB doc) | always |
| `query_{slug}` | local — per-source filtered search | per active source |
| `read_document` | local — read vault note (`SafePath`) | ≥1 active ObsidianVault |
| `write_note` | local — write vault `.md` + re-index | ≥1 active ObsidianVault |
| `ask_question` · `read_wiki_structure` · `read_wiki_contents` | DeepWiki bypass | `DeepWiki:Enabled` |

Resources: `knowledge://sources` (catalog JSON) + `obsidian://{slug}/{path}` per indexed document.

## 4. Deployment

```mermaid
%%{init: {'theme':'neutral'}}%%
flowchart LR
    subgraph Host["User machine (no .NET runtime needed)"]
        EXE["KnowledgeHub<br/>single-file binary<br/>(self-contained, per-RID)"]
        DB[("knowledgehub.db<br/>beside the executable")]
        EXT["%TMP%/.net extraction<br/>native libs (e_sqlite3)"]
        CFG["appsettings.json + env vars<br/>DeepWiki__* Embeddings__* ..."]
        EXE --> DB
        EXE -.->|self-extract| EXT
        CFG -.->|config| EXE
    end

    DEV["dotnet publish -r linux-x64|win-x64|osx-arm64"] -->|produces| EXE

    subgraph Net["Same port (ASPNETCORE_URLS, default :5000)"]
        R1["/            → SPA + deep links"]
        R2["/api/*       → REST"]
        R3["/mcp         → Streamable HTTP"]
        R4["/mcp/sse|msg → legacy SSE"]
        R5["/hubs/mcp    → SignalR"]
    end
    EXE --- Net

    classDef bin fill:#d5e8d4,stroke:#82b366,stroke-width:2px,color:darkgreen
    classDef data fill:#f5f5f5,stroke:#666666,stroke-width:2px,color:black
    classDef net fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px,color:darkblue
    class EXE bin
    class DB,EXT,CFG data
    class R1,R2,R3,R4,R5 net
```

`dotnet publish -r <RID>` produces a **single-file self-contained binary** (~120 MB) — no .NET runtime, Python, container or external UI service required. `knowledgehub.db` is created beside the executable; native libs self-extract to `%TMP%/.net`. All endpoints share one port (`ASPNETCORE_URLS`, `AllowedHosts` restricted to localhost).

## 5. Configuration surface

```jsonc
{
  "Embeddings":  { "Provider": "deterministic|ollama|openai", "Endpoint": "...", "ApiKey": "...", "Model": "...", "Dimensions": 384 },
  "VectorStore": { "Provider": "sqlite|postgres", "ConnectionString": "Host=...;Database=knowledgehub" },
  "DeepWiki":    { "Enabled": true, "Endpoint": "https://mcp.deepwiki.com/mcp", "ApiKey": "", "TimeoutSeconds": 60 }
}
```

Security boundaries: source configs are **redacted** (`connectionString`, `apiKey`, `key`, `headers`, `token`, `password`, `secret`) in every API response; vault paths reject `..` and canonical escapes; embeddings record `EmbeddingModel` (`{provider}:{model}`) so stale vectors are ignored after a model switch.

## Editable diagram

`knowledge-hub-architecture.drawio` mirrors section 1 — open in [draw.io / diagrams.net](https://app.diagrams.net) to edit.
