# KnowledgeHub

All-in-one standalone knowledge platform: Blazor WebAssembly admin UI, REST API,
native MCP server (Streamable HTTP + legacy SSE), SQLite persistence, pluggable
embeddings and vector stores, Obsidian ingestion and a DeepWiki MCP proxy — all in
a single Kestrel-hosted .NET 10 process.

## Endpoints

| Route | Purpose |
|---|---|
| `/` | Blazor WASM admin UI (`/sources`, `/mcp-monitor`, `/playground`) |
| `/api/sources`, `/api/search`, `/api/ask`, `/api/agent`, `/api/approvals` | REST API |
| `/mcp` | MCP — Streamable HTTP (modern clients) |
| `/mcp/sse` + `/mcp/message` | MCP — legacy HTTP/SSE (Cursor, Claude Desktop) |
| `/hubs/mcp` | SignalR feed for the MCP monitor |

## MCP tools

`search_knowledge`, `ask_knowledge`, `agent_chat`, `write_knowledge`,
`read_document`, `write_note`, `query_{source_slug}` per active source,
plus DeepWiki bypass:
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
  "Chat": {
    "Provider": "none",                        // none | ollama | openai — server-side answer synthesis
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.1",
    "ApiKey": "",                              // env var only: Chat__ApiKey — never committed
    "Temperature": 0.2,
    "MaxTokens": 512,
    "TimeoutSeconds": 120
  },
  "Agent": {
    "MaxIterations": 10,                          // model→tools→model rounds cap (agent_chat / POST /api/agent)
    "MaxToolCalls": 20,                           // total tool invocations cap
    "MaxToolResultChars": 4000,                   // truncation before results re-enter the model
    "RequireApprovalFor": ["*"],                  // mutating tools need human approval inside the agent loop
    "ApprovalTimeoutMinutes": 30                  // pending approvals expire after this
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

The container listens on `:8080`, published to host port `5000` by default.
SQLite lives in `./data` on the host, bind-mounted to `/data` (delete the
container freely — data survives). `docker compose up -d` is equivalent to
`--docker` mode. Port and providers can be overridden via a local `.env`
(`KNOWLEDGEHUB_PORT=5550`, `EMBEDDINGS_*`, `VECTORSTORE_*`, `DEEPWIKI_*`) —
both `docker compose` and `install.sh` read it; `--port` still wins.

## Obsidian via WebDAV

A remote Obsidian vault can be indexed without an in-app WebDAV client: mount
it on the **host** as a local folder and register it as an `ObsidianVault`
source. KnowledgeHub consumes it like any local vault (SPEC-20260914-obsidian-webdav).

Host mount with `davfs2`:

```bash
sudo apt install davfs2
sudo mkdir -p /srv/webdav/obsidian
# credentials live in davfs2 secrets — never in the repo or in configuration.json
echo '/srv/webdav/obsidian  user  <password>' | sudo tee -a /etc/davfs2/secrets
sudo mount -t davfs2 https://<webdav-host>/remote.php/dav/files/<user>/ /srv/webdav/obsidian
```

Persist across reboots via `/etc/fstab` (note `_netdev` — waits for the network):

```fstab
https://<webdav-host>/remote.php/dav/files/<user>/  /srv/webdav/obsidian  davfs  _netdev,rw,uid=<user>,gid=<user>  0  0
```

Then bind the mount into the container (see the commented volume in
`docker-compose.yml`) and create the source with `configuration.path` set to
the **container** path — e.g. `/vaults/obsidian`, not the host path.

Operational notes:

- **Use `autoSync` + `syncIntervalMinutes`.** davfs2/FUSE mounts do not
  propagate inotify for *remote* changes — the file watcher only sees writes
  made through this mount. Polling (`autoSync`) is the update mechanism.
- **Mount `ro` for read-only vaults** — `write_note` needs `rw` and fails
  clearly otherwise.
- **Cache tuning:** davfs2 `cache_size`/`file_refresh` in `davfs2.conf` trade
  sync latency vs. bandwidth.
- **Mount down** → sync fails per-source with `LastSyncStatus=failed` and a
  `mount unavailable?` hint on the source; other sources stay healthy and the
  watcher re-arms automatically once the mount is back (≤10 s refresh).

## Development

```bash
dotnet build KnowledgeHub.slnx
dotnet test
dotnet format --verify-no-changes
```

Schema changes require an EF Core migration — the app applies pending
migrations at startup (`DatabaseMigrator`, which also baselines databases
created before migrations existed). After editing entities or
`KnowledgeHubDbContext`, generate and commit one:

```bash
dotnet ef migrations add <Name> -p src/KnowledgeHub.Server
```

NuGet resolves are locked via `packages.lock.json` (one per project,
generated by `RestorePackagesWithLockFile` in `Directory.Build.props`).
After changing any `PackageReference`, run `dotnet restore` and commit the
updated lock files. Enforce with `LOCKED_RESTORE=1 ./install.sh` or
`dotnet restore --locked-mode` — both fail on stale locks. Note: lock files
capture SDK-workload implicit refs, so locked mode requires the same SDK
band that generated them (the Dockerfile intentionally restores unlocked).

Specs live in `.specs/`; architecture diagrams (Mermaid + draw.io) in
[`docs/architecture/`](docs/architecture/system-architecture.md); work is tracked
in GitHub Issues #2–#8.
