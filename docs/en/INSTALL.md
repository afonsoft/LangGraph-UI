# Installation

KnowledgeHub runs three ways: from source (.NET SDK), as a Docker container, or as a self-contained single-file binary.

## Docker (recommended)

```bash
cp .env.example .env   # edit values
mkdir -p data logs && chown -R 1654:1654 data logs   # container runs as uid 1654 (app)
docker compose up -d
```

- Builds `knowledgehub:latest` and serves on the mapped port (`docker-compose.yml` — `5550:8080` by default).
- `./data` is bind-mounted and persists `knowledgehub.db` + uploads.
- `./logs` is bind-mounted and persists the Serilog file sink — daily rolling files (`knowledgehub-YYYYMMDD.log`), 14-day retention, secrets redacted as `***REDACTED***`. **If `./logs` or `./data` don't exist Docker creates them as `root` and neither the file sink nor the SQLite migration can write** (the container runs as `app`, uid 1654) — pre-create it with the `chown` above or fix it once after the first `up`.
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
| OTLP / Prometheus | `Telemetry:Otlp:Endpoint` / `Telemetry:Metrics:Prometheus` | Traces + `/metrics` (+ OTLP log sink) |
| pgvector tuning | `VectorStore:Postgres:*` | `StorageType` vector\|halfvec (pgvector ≥0.7 + `AllowStorageMigration`), `IterativeScan` (≥0.8), `Hnsw*`, pool sizes — see README §Configuration |
| Cache tuning | `Cache:RegionTtlMinutes`, `Cache:L1*` | Per-region TTLs + in-process L1 in front of Redis (invalidation via `kh:invalidate` pub/sub) — see README §Configuration |
| Runtime log level | `GET/PUT /api/settings/log-level` | `LoggingLevelSwitch` with optional `minutes` (0–120) |
