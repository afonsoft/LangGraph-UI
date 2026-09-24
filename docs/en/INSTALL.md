# Installation

KnowledgeHub runs three ways: from source (.NET SDK), as a Docker container, or as a self-contained single-file binary.

## Docker (recommended)

```bash
cp .env.example .env   # edit values
docker compose up -d
```

- Builds `knowledgehub:latest` and serves on the mapped port (`docker-compose.yml` — `5550:8080` by default).
- `./data` is bind-mounted and persists `knowledgehub.db` + uploads.
- EF Core migrations apply automatically at startup.
- Health probe: `GET /healthz`.

## From source

```bash
dotnet build KnowledgeHub.slnx
dotnet ef database update -p src/KnowledgeHub.Server   # optional — runs at startup too
dotnet run --project src/KnowledgeHub.Server           # http://localhost:5000
```

Requires .NET SDK 10.0.x (`global.json`). First login: `admin` / `123qwe` (forced password change; override the seed via `Auth__AdminInitialPassword`).

## Standalone binary + systemd

```bash
dotnet publish src/KnowledgeHub.Server -c Release -r linux-x64   # or win-x64 / osx-arm64
sudo ./install.sh --host --systemd
```

Produces a ~120 MB self-contained binary — no .NET runtime required. The installer creates a hardened systemd unit; `knowledgehub.db` is created beside the executable. `backup.sh`/`restore.sh` cover the database and uploads.

## Configuration

`appsettings.json` + environment variables (`__` nesting) + `.env`. Runtime-editable settings (Chat, Graph, integration keys, per-API-key overrides) are stored in SQLite via `/settings` — no restart needed. See `docs/architecture/system-architecture.md` §5 for the full surface.

## Optional backends

| Option | Setting | Purpose |
|---|---|---|
| PostgreSQL + pgvector | `VectorStore:Provider=postgres` + `VectorStore:ConnectionString` | HNSW vector search at scale |
| Redis | `Cache:Provider=redis` + `Cache:Redis:ConnectionString` | Distributed cache for search/embedding/answer regions |
| Ollama / OpenAI-compat | `Embeddings:*` / `Chat:*` | Embedding and chat models |
| OTLP / Prometheus | `Telemetry:Otlp:Endpoint` / `Telemetry:Metrics:Prometheus` | Traces + `/metrics` |
