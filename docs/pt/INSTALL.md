# Instalação

O KnowledgeHub roda de três formas: a partir do código-fonte (.NET SDK), como container Docker, ou como binário único autocontido.

## Docker (recomendado)

```bash
cp .env.example .env   # edite os valores
mkdir -p data logs && chown 1654:1654 logs   # o container roda como uid 1654 (app)
docker compose up -d
```

- Gera a imagem `knowledgehub:latest` e serve na porta mapeada (`docker-compose.yml` — `5550:8080` por padrão).
- `./data` é bind-mount e persiste `knowledgehub.db` + uploads.
- `./logs` é bind-mount e persiste o file sink do Serilog — rolling diário (`knowledgehub-YYYYMMDD.log`), retenção de 14 dias, secrets redigidos como `***REDACTED***`. **Se `./logs` não existir o Docker o cria como `root` e o file sink não consegue escrever** (o container roda como `app`, uid 1654) — pré-crie com o `chown` acima ou corrija uma vez após o primeiro `up`.
- Migrations EF Core aplicadas automaticamente no startup.
- Health probe: `GET /healthz`.

## A partir do código-fonte

```bash
dotnet build KnowledgeHub.slnx
dotnet ef database update -p src/KnowledgeHub.Server   # opcional — roda no startup também
dotnet run --project src/KnowledgeHub.Server           # http://localhost:5000
```

Requer .NET SDK 10.0.x (`global.json`). Primeiro login: `admin` / `123qwe` (troca de senha obrigatória; sobrescreva via `Auth__AdminInitialPassword`).

## Binário standalone + systemd

```bash
dotnet publish src/KnowledgeHub.Server -c Release -r linux-x64   # ou win-x64 / osx-arm64
sudo ./install.sh --host --systemd
```

Produz um binário autocontido de ~120 MB — sem runtime .NET. O instalador cria uma unit systemd hardened; `knowledgehub.db` é criado ao lado do executável. `backup.sh`/`restore.sh` cobrem banco e uploads.

## Configuração

`appsettings.json` + variáveis de ambiente (nesting com `__`) + `.env`. Configurações editáveis em runtime (Chat, Graph, chaves de integração, overrides por API key) ficam no SQLite via `/settings` — sem restart. Veja `docs/architecture/system-architecture.md` §5 para a superfície completa.

## Backends opcionais

| Opção | Configuração | Propósito |
|---|---|---|
| PostgreSQL + pgvector | `VectorStore:Provider=postgres` + `VectorStore:ConnectionString` | Busca vetorial HNSW em escala |
| Redis | `Cache:Provider=redis` + `Cache:Redis:ConnectionString` | Cache distribuído de busca/embedding/resposta |
| Ollama / OpenAI-compat | `Embeddings:*` / `Chat:*` | Modelos de embedding e chat |
| OTLP / Prometheus | `Telemetry:Otlp:Endpoint` / `Telemetry:Metrics:Prometheus` | Traces + `/metrics` (+ sink de logs OTLP) |
| Tuning pgvector | `VectorStore:Postgres:*` | `StorageType` vector\|halfvec (pgvector ≥0.7 + `AllowStorageMigration`), `IterativeScan` (≥0.8), `Hnsw*`, pool — ver README §Configuração |
| Tuning de cache | `Cache:RegionTtlMinutes`, `Cache:L1*` | TTLs por região + L1 em processo à frente do Redis (invalidação via pub/sub `kh:invalidate`) — ver README §Configuração |
| Nível de log em runtime | `GET/PUT /api/settings/log-level` | `LoggingLevelSwitch` com `autoResetMinutes` opcional (0–120) |
