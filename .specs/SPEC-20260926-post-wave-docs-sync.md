# SPEC — Sincronização de documentação pós-waves de infra (endpoints, config, logs, CLAUDE.md)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-post-wave-docs-sync` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `docs/en|pt/API.md`, `docs/en|pt/INSTALL.md`, `docs/architecture/system-architecture.md`, `README.md`, `CLAUDE.md`, `docker-compose.yml` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-documentation-post-wave-sync |
| Origem | gap-analysis 2026-09-26 — drift doc × código das waves 1–4 (PRs #195–198) |

## 1. User Story

**As a** operador/integrador do KnowledgeHub
**I want** que a documentação reflita os endpoints, knobs de config e operação de logs entregues nas waves de infra
**So that** eu descubra e configure log-level em runtime, cache L1/L2+TTLs, pgvector halfvec/iterative scan e o volume de logs sem ler o código — e sem repetir o incidente de permissão do `./logs`.

## 2. Contexto

As waves 1–4 (`aa08857`, `64ac287`, `14f5464`, `077328e`) entregaram endpoints e config novos que não chegaram aos docs. Divergências confirmadas no gap-analysis-20260926:

- `GET/POST /api/settings/cache` + `POST /api/settings/cache/clear`, `GET/PUT /api/settings/log-level` (`src/KnowledgeHub.Server/Api/SettingsEndpoints.cs:157-178`) e `GET /api/diagnostics/vectorstore` (`DiagnosticsEndpoints.cs:16`) sem entrada em `docs/en|pt/API.md`.
- SPEC-20260925-log-sinks-and-redaction **RF-001** exigia "README documenta rotação" — não entregue: nenhum doc menciona `./logs`, rolling file diário/14d, nem o caveat de permissão (Docker cria o host dir como `root`; o container roda como `app` uid 1654 → file sink falha silenciosamente até `chown 1654:1654 logs/`). Incidente real em 2026-09-26.
- `README.md:76-80` e `docs/architecture/system-architecture.md:252-253` documentam `Cache` sem `RegionTtlMinutes`/`DefaultTtlMinutes`/`L1MaxTtlMinutes`/`ToolCache*`; `VectorStore:Postgres.*` (`StorageType` vector|halfvec, `AllowStorageMigration`, `IterativeScan`, `Hnsw*`, `MinPoolSize`/`MaxPoolSize`) e a seção `Serilog` ausentes de todos os docs.
- CLAUDE.md "Estado Atual" não cobre as waves 1–4.

## 3. Requisitos Funcionais

### RF-001 — API.md en+pt
- Seção Settings: `GET/POST /api/settings/cache` (stats servidor/L1/L2, `serverReported`/`partial`) + `POST /api/settings/cache/clear`; `GET/PUT /api/settings/log-level` (nível + `autoResetMinutes` 0–120).
- Nova linha Diagnostics: `GET /api/diagnostics/vectorstore` (provider, dimensão, contagem, storage vector|halfvec).

### RF-002 — Logs: volume, rotação e permissão
- `README.md`/`INSTALL.md` (en+pt): `./logs:/app/logs`, rolling file diário com retenção 14d, `Serilog__WriteTo__1__Args__path` configurável, redaction de secrets (`***REDACTED***`), sink OTLP opcional via `Telemetry:Otlp:Endpoint`, runtime level via `/api/settings/log-level`.
- **Caveat de permissão**: host dir criado pelo Docker como `root` quebra o file sink — documentar `mkdir -p logs && chown 1654:1654 logs` (ou equivalente) no passo de deploy; adicionar comentário no `docker-compose.yml` ao lado do volume.

### RF-003 — Config surface (README + system-architecture.md §5)
- `Cache`: `Provider`, `ToolCacheEnabled`/`ToolCacheTtlMinutes`, `RegionTtlMinutes` (emb/search/ans/mcp:tool/rewrite/expand/index/secret), `DefaultTtlMinutes`, `L1MaxTtlMinutes`, invalidation pub/sub quando `redis`.
- `VectorStore:Postgres`: `StorageType` (vector|halfvec, requer pgvector ≥0.7 + `AllowStorageMigration`), `IterativeScan` (pgvector ≥0.8), `HnswThreshold`/`M`/`EfConstruction`/`EfSearch`, `MinPoolSize`/`MaxPoolSize`, `BatchMax`, timeouts.
- `Serilog`: MinimumLevel/Overrides (Microsoft/System→Warning), sinks console+file+OTLP, request logging (health/static→Debug, 5xx→Error, `x-request-id`).

### RF-004 — CLAUDE.md "Estado Atual"
- Uma linha/bloco cobrindo: request logging+redaction+file sink, runtime log-level com auto-reset, cache L1/L2+TTL por região+invalidation pub/sub, pgvector iterative scan+halfvec+pooling, vectorstore metrics/health check, pipeline spans OTel.

## 4. Requisitos Não-Funcionais

- Docs en e pt em paridade (mesma informação, mesmas tabelas).
- Não documentar valores de secrets — apenas nomes de chaves/env vars.
- `dotnet build`/`dotnet test` não afetados (docs only).

## 5. Fora de Escopo

- Mudanças de código, novos endpoints, `docs/architecture/` novos ADRs.
- Deleção de branches obsoletas (housekeeping separado, fora desta SPEC).
- Publicação/translation além de en+pt existentes.

## 6. Plano de Tarefas

1. API.md en+pt (RF-001).
2. README + INSTALL en+pt: seção Logs (RF-002) + atualizar bloco Cache/VectorStore/Serilog (RF-003 parcial).
3. Comentário de permissão no `docker-compose.yml` (RF-002).
4. `system-architecture.md` §5 config surface (RF-003).
5. CLAUDE.md "Estado Atual" (RF-004).
6. Revisão de paridade en/pt + grep final dos paths/keys citados.

## 7. Critérios de Aceite

- [ ] `grep "log-level\|diagnostics/vectorstore\|settings/cache" docs/en/API.md docs/pt/API.md` → matches nos dois idiomas.
- [ ] `grep -i "logs\|1654" README.md docs/en/INSTALL.md docs/pt/INSTALL.md` → volume, rotação 14d e caveat de permissão presentes.
- [ ] `grep "RegionTtlMinutes\|StorageType\|IterativeScan" README.md docs/architecture/system-architecture.md` → matches.
- [ ] `grep -i "serilog\|redact\|halfvec\|log-level" CLAUDE.md` → matches.
- [ ] Paridade en/pt verificada por diff visual das seções alteradas.
- [ ] PR via `feature/{Agent}-{YYYYMMDD}-docs-sync` — **sem push direto em main**.
