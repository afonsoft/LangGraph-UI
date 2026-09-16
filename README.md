# Knowledge

All-in-one standalone knowledge platform: Blazor WebAssembly admin UI, REST API,
native MCP server (Streamable HTTP + legacy SSE), SQLite persistence, pluggable
embeddings and vector stores, Obsidian ingestion and a DeepWiki MCP proxy — all in
a single Kestrel-hosted .NET 10 process.

## Endpoints

| Route | Purpose |
|---|---|
| `/` | Blazor WASM admin UI (`/sources`, `/mcp-monitor`, `/playground`) |
| `/api/sources`, `/api/search`, `/api/ask`, `/api/agent`, `/api/approvals`, `/api/threads` | REST API |
| `/api/ask/stream`, `/api/agent/stream` | REST SSE — `token`/`tool_start`/`tool_end`/`awaiting_approval`/`done`/`error` events, 15 s heartbeat, `X-Accel-Buffering: no` |
| `/mcp` | MCP — Streamable HTTP (modern clients) |
| `/mcp/sse` + `/mcp/message` | MCP — legacy HTTP/SSE (Cursor, Claude Desktop) |
| `/hubs/mcp` | SignalR feed for the MCP monitor |

## Authentication

All surfaces except the health probes (`/health/*`) and `POST /api/auth/login`
require authentication — the SPA, the REST API, `/mcp`, `/mcp/sse` and
`/hubs/mcp`.

**Browser (cookie).** The SPA signs in at `/login`; the session is an HttpOnly
cookie (`SameSite=Lax`, `Secure`, 12 h sliding). On first startup an `admin`
user is seeded with the password `123qwe` (override via
`Auth__AdminInitialPassword`) and `mustChangePassword` forces the password
change screen before any other page or API call. Password policy: ≥8 chars,
different from the current one. Five consecutive failed logins lock the
account for 5 minutes (`423 Locked`); wrong credentials return a generic `401`
(no user enumeration).

| Auth route | Purpose |
|---|---|
| `POST /api/auth/login` | `{ username, password }` → sets the session cookie |
| `GET /api/auth/me` | `{ username, mustChangePassword }` |
| `POST /api/auth/logout` | clears the session cookie |
| `POST /api/auth/change-password` | `{ currentPassword, newPassword }` → `204`, clears the flag |
| `GET /api/apikeys` · `POST /api/apikeys` · `DELETE /api/apikeys/{id}` | manage API keys (cookie session only) |

**API keys (`aft_*`) for non-browser clients.** Create one under `/api-keys`
(or `POST /api/apikeys`); the full secret `aft_<32-hex>` is shown **once** —
only its SHA-256 hash is stored. Send it as a bearer token:

```bash
curl https://rag.afonsoft.dev/mcp \
  -H "Authorization: Bearer aft_..." \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}'
```

Accepted on `/mcp`, `/mcp/sse`, `/api/*` and `/hubs/mcp` (SignalR clients that
cannot send headers may use `?access_token=`). API keys can call everything
**except** the API-key management endpoints, which require a cookie session.
Revoking a key (`DELETE /api/apikeys/{id}` or the UI) takes effect
immediately.

> **Breaking change for external MCP clients** (Cursor, Claude Desktop, …):
> they must now send `Authorization: Bearer aft_...`. Generate the key in the
> admin UI first.

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
    "ApprovalTimeoutMinutes": 30,                 // pending approvals expire after this
    "MaxContextTokens": 8000                      // thread history window (chars/4) — older turns are summarized
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
container freely — data survives).

### Docker Compose

`docker-compose.yml` is the declarative equivalent of `./install.sh
--docker` — same image, same port mapping, same `/data` volume.

```bash
# 1. Environment — Compose reads .env automatically for ${VAR} substitution
cp .env.example .env            # then edit values (see the table below)

# 2. Optional — host-specific mounts (local Obsidian vaults, WebDAV mounts)
cp docker-compose.override.yml.example docker-compose.override.yml

# 3. Build and run → http://localhost:5000
docker compose up -d --build
```

Day-to-day:

```bash
docker compose logs -f   # follow logs
docker compose ps        # status — the image HEALTHCHECKs GET / every 30 s
docker compose up -d     # recreate after changing .env or the override file
docker compose down      # stop + remove the container (./data survives)
```

`docker compose restart` does **not** re-read `.env` or the override file —
environment is baked in at container-creation time, so use `up -d` to pick
up changes.

How configuration reaches the container:

- `.env` feeds `${VAR:-default}` substitution in `docker-compose.yml`.
  Shell env vars take precedence over `.env`, and `./install.sh` reads the
  same file (`--port` still wins over `KNOWLEDGEHUB_PORT`).
- Only variables declared under `environment:` reach the container.
  Settings not wired in the base file — `Chat__*`, or any other
  `Section__Key` appsettings override — go under `environment:` in
  `docker-compose.override.yml`:

  ```yaml
  services:
    knowledgehub:
      environment:
        Chat__Provider: ollama
        Chat__Endpoint: http://host.docker.internal:11434
        Chat__Model: llama3.1
        Chat__ApiKey: ${CHAT__APIKEY:-}
  ```

- `docker-compose.override.yml` is gitignored and merged automatically on
  top of the base file. A clean checkout without it deploys fine — sources
  whose `configuration.path` points at `/vaults/*` just report "mount
  unavailable". The example mounts a local vault `rw` (so `write_note` can
  persist) and shows a commented `ro` WebDAV mount — see
  "Obsidian via WebDAV" below.

### Environment variables

Every `UPPERCASE` variable below maps to a `${VAR:-default}` substitution in
`docker-compose.yml`, resolved from `.env` or the shell; `Section__Key`
entries are ASP.NET Core-style overrides injected via `environment:`.
Placeholders only — never commit a real `.env`.

| Variable | Purpose | Default | Required |
|---|---|---|---|
| `KNOWLEDGEHUB_PORT` | Host port published for the container | `5000` | no |
| `ALLOWED_HOSTS` | Kestrel `AllowedHosts` — pin the public hostname or `*` behind a tunnel | `*` | no |
| `EMBEDDINGS_PROVIDER` | `deterministic` \| `ollama` \| `openai` | `deterministic` | no |
| `EMBEDDINGS_ENDPOINT` | Embedding endpoint — required for `ollama`/`openai` | `http://host.docker.internal:11434` | provider-dependent |
| `EMBEDDINGS_MODEL` | Embedding model name | `nomic-embed-text` | provider-dependent |
| `EMBEDDINGS_APIKEY` | Embedding API key (env only — never committed) | empty | no |
| `VECTORSTORE_PROVIDER` | `sqlite` \| `postgres` (pgvector) | `sqlite` | no |
| `VECTORSTORE_CONNECTIONSTRING` | Postgres connection string | empty | provider-dependent |
| `DEEPWIKI_ENABLED` | DeepWiki upstream MCP proxy | `true` | no |
| `DEEPWIKI_ENDPOINT` | Public DeepWiki endpoint | `https://mcp.deepwiki.com/mcp` | no |
| `DEEPWIKI_PRIVATE_ENDPOINT` | Private DeepWiki endpoint (authenticated orgs) | `https://mcp.devin.ai/mcp` | no |
| `DEEPWIKI_APIKEY` | DeepWiki API key | empty | no |
| `FIRECRAWL_ENABLED` | Firecrawl upstream MCP proxy | `true` | no |
| `FIRECRAWL_ENDPOINT` | Firecrawl MCP endpoint | `https://mcp.firecrawl.dev/v2/mcp` | no |
| `FIRECRAWL_APIKEY` | Firecrawl API key | empty | no |
| `FIRECRAWL_TIMEOUT_SECONDS` | Upstream call timeout | `300` | no |
| `TAVILY_ENABLED` | Tavily upstream MCP proxy | `true` | no |
| `TAVILY_ENDPOINT` | Tavily MCP endpoint | `https://mcp.tavily.com/mcp` | no |
| `TAVILY_APIKEY` | Tavily API key | empty | no |
| `TAVILY_TIMEOUT_SECONDS` | Upstream call timeout | `120` | no |
| `CACHE_PROVIDER` | `IDistributedCache` backend: `memory` \| `redis` | `memory` | no |
| `REDIS_CONNECTIONSTRING` | StackExchange.Redis conn string — required when `CACHE_PROVIDER=redis`; use `defaultDatabase=N` | empty | provider-dependent |
| `AUTH_ADMIN_INITIAL_PASSWORD` | Seed password for `admin` (forced change on first login) | `123qwe` | no |
| `CHAT__PROVIDER` | `none` \| `ollama` \| `openai` — server-side answer synthesis / agent loop. Not wired in the base compose — set via `environment:` in the override | `none` | no |
| `CHAT__ENDPOINT` / `CHAT__MODEL` / `CHAT__APIKEY` | Chat provider endpoint, model and key (same override note; use `host.docker.internal` from the container) | `http://localhost:11434` / `llama3.1` / empty | provider-dependent |
| `KnowledgeHub__DatabasePath` | SQLite file path — Dockerfile pins `/data/knowledgehub.db` (the bind-mounted volume) | `/data/knowledgehub.db` | no |

Any other `appsettings.json` key can be injected the same way —
`Section__Subsection__Key` (double underscore) per ASP.NET Core conventions.

### Redis security

When `CACHE_PROVIDER=redis`, search results and query embeddings are written
to the configured Redis. A deployment observed in the wild ran the host Redis
on `0.0.0.0:6379` with `protected-mode no` and no `requirepass`: **any process
or host that can reach the port can read — or poison — the cache.** The app
works fine without auth, but it logs a startup warning in that case.

Hardening options, ordered by effort:

1. **Firewall / security group** — deny inbound `6379` from outside the host
   (or the Docker network). Cheapest fix; the container reaches the host via
   `host.docker.internal` → `host-gateway`, unaffected.
2. **`bind 127.0.0.1`** in the host `redis.conf` — same reachability for the
   container (`host-gateway` resolves to the host gateway interface; if the
   bind is strictly loopback, point `REDIS_CONNECTIONSTRING` at a Docker-side
   address or run Redis as a compose service instead).
3. **`requirepass <secret>`** in `redis.conf` + `,password=<secret>` appended
   to `REDIS_CONNECTIONSTRING` — defense in depth; also protects against
   other containers on the same host.

TLS (`rediss://`) is supported by the connection string but out of scope for
this doc.

## Obsidian via WebDAV

A remote Obsidian vault can be indexed without an in-app WebDAV client: mount
it on the **host** as a local folder and register it as an `ObsidianVault`
source. Knowledge consumes it like any local vault (SPEC-20260914-obsidian-webdav).

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

Then bind the mount into the container via `docker-compose.override.yml`
(see `docker-compose.override.yml.example`) and create the source with
`configuration.path` set to the **container** path — e.g. `/vaults/obsidian`,
not the host path.

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
