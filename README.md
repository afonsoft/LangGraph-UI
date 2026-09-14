# KnowledgeHub

All-in-one standalone knowledge platform: Blazor WebAssembly admin UI, REST API,
native MCP server (Streamable HTTP + legacy SSE), SQLite persistence, pluggable
embeddings and vector stores, Obsidian ingestion and a DeepWiki MCP proxy — all in
a single Kestrel-hosted .NET 10 process.

## Endpoints

| Route | Purpose |
|---|---|
| `/` | Blazor WASM admin UI (`/sources`, `/mcp-monitor`, `/playground`) |
| `/api/sources`, `/api/search` | REST API |
| `/mcp` | MCP — Streamable HTTP (modern clients) |
| `/mcp/sse` + `/mcp/message` | MCP — legacy HTTP/SSE (Cursor, Claude Desktop) |
| `/hubs/mcp` | SignalR feed for the MCP monitor |

## MCP tools

`search_knowledge`, `ask_knowledge`, `write_knowledge`, `read_document`,
`write_note`, `query_{source_slug}` per active source, plus DeepWiki bypass:
`ask_question`, `read_wiki_structure`, `read_wiki_contents`.

## Configuration

```jsonc
{
  "Database": { "Path": "knowledgehub.db" },   // or KnowledgeHub:DatabasePath
  "Embeddings": {
    "Provider": "deterministic",               // deterministic | ollama | openai
    "Endpoint": "http://localhost:11434",
    "ApiKey": "",
    "Model": "nomic-embed-text",
    "Dimensions": 384
  },
  "VectorStore": {
    "Provider": "sqlite",                      // sqlite | postgres (pgvector)
    "ConnectionString": ""
  },
  "DeepWiki": {
    "Enabled": true,
    "Endpoint": "https://mcp.deepwiki.com/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 60
  }
}
```

All settings are overridable via environment variables (`Embeddings__ApiKey`, …).

## Run from source

```bash
dotnet run --project src/KnowledgeHub.Server   # http://localhost:5009
```

## Standalone single-file publish

```bash
dotnet publish src/KnowledgeHub.Server -c Release -r linux-x64 -o publish/linux-x64
dotnet publish src/KnowledgeHub.Server -c Release -r win-x64    -o publish/win-x64
dotnet publish src/KnowledgeHub.Server -c Release -r osx-arm64  -o publish/osx-arm64
```

Produces one self-contained executable (`KnowledgeHub`) — no .NET runtime needed.
The SQLite file is created beside the executable (override with
`KnowledgeHub:DatabasePath`); `ASPNETCORE_URLS` controls the port
(default `http://localhost:5000`).

## Deploy

`./install.sh` builds, tests and deploys in one step — Docker when available,
self-contained host binary otherwise:

```bash
./install.sh                      # docker build + run → http://localhost:5000
./install.sh --docker --port 8080 # custom host port
./install.sh --host               # publish + install to /opt/knowledgehub
./install.sh --host --systemd     # + enable knowledgehub.service
./install.sh --help               # all options (--data-dir, --prefix, --skip-tests)
```

The container listens on `:8080`, published to host port `5000`. SQLite lives in
`./data` on the host, bind-mounted to `/data` (delete the container freely — data
survives). `docker compose up -d` is equivalent to `--docker` mode; override the
port with `KNOWLEDGEHUB_PORT` and providers via `EMBEDDINGS_*`, `VECTORSTORE_*`,
`DEEPWIKI_*` env vars.

## Development

```bash
dotnet build KnowledgeHub.slnx
dotnet test
dotnet format --verify-no-changes
```

Specs live in `.specs/`; architecture diagrams (Mermaid + draw.io) in
[`docs/architecture/`](docs/architecture/system-architecture.md); work is tracked
in GitHub Issues #2–#8.
