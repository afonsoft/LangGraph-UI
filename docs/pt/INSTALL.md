# Instalação

O KnowledgeHub roda de três formas: a partir do código-fonte (.NET SDK), como container Docker, ou como binário único autocontido.

## Docker (recomendado)

```bash
cp .env.example .env   # edite os valores
docker compose up -d
```

- Gera a imagem `knowledgehub:latest` e serve na porta mapeada (`docker-compose.yml` — `5550:8080` por padrão).
- `./data` é bind-mount e persiste `knowledgehub.db` + uploads.
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
| OTLP / Prometheus | `Telemetry:Otlp:Endpoint` / `Telemetry:Metrics:Prometheus` | Traces + `/metrics` |
