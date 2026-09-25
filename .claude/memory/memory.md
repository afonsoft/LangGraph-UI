# Short-term memory — session state (overwritten each session, ≤100 lines)

- **Last verified commit on `main`**: `05571ea` (PR #206 — memory log).
- **Baseline**: build 0 warnings · 665 unit + 243 integration green (última sessão) · prod container healthy :5550 pós-#205 redeploy.
- **Done hoje (2026-09-25, sessão 4)**: triage completo devin-review (~90 findings em #184–#206 + #9–#43) → ~45 CONFIRMADOS abertos, relatório em `.claude/memory/devin-review-triage-20260925.md`; 4 branches obsoletas deletadas; redeploy pós-#205 healthy.
- **Blockers**: agrupamento dos findings em SPECs aguarda aprovação do owner.
- **Next**: owner aprova SPEC grouping → write-specs → execute; `enforce_admins` decisão pendente; teste shutdown gracioso×abrupto em aberto.

## Session summary (2026-09-25 — devin-review triage + housekeeping)

- Análise dos comentários devin-ai-integration em todos os PRs com findings (155 comments, #184–#206 + era antiga): cada finding verificado contra main — clusters A (ingestão/conectores, 9), B (cache, 6), C (embeddings/settings, 5), D (search/RAG, 4), E (pgvector, 3), F (log-level, 4), G (UI/monitor, 10), H (era antiga: backup.sh vault-collision válido), I (bookkeeping).
- Cluster mais severo: integridade de ingestão (wipe de docs no reindex por texto vazio; falha transitória → delete; fila-cheia deadlock) e CacheTtlPolicy morta (nunca resolvida no DI).
- Housekeeping: 4 branches locais deletadas (Antigravity×2, flagged-chunk-badge, quality-test — conteúdo em main); redeploy pós-#205 (healthz+ready 200).
- Aguardando: aprovação do agrupamento em SPECs; enforce_admins; shutdown test.

## 2026-09-26 — Epic E22 completo (6 SPECs → 6 PRs)

PRs #215-#220 abertos (issues #209-#214 → in_pullrequest). Checks verdes/rodando.
Sessão executou as 6 SPECs aprovadas em branches independentes off main@9b52213.
Decisões/notas de implementação no log datado (20260926-memory.md).

Pendente: revisão/merge dos PRs, redeploy, enforce_admins, shutdown test.

## 2026-09-26 — Postgres vector store ativado (host PG18)

- Prompt: conectar no Postgres (rag_db/rag_user no host :5432, aaPanel `/www/server/pgsql`), credenciais em `.env`, compose lendo env — padrão proxyLLM-AI.
- Instalado pgvector 0.8.6 no PG18 do host (build /tmp/pgvector, PG_CONFIG=/www/server/pgsql/bin/pg_config); `CREATE EXTENSION vector` em rag_db via postgres@127.0.0.1 (trust).
- pg_hba.conf: adicionado `host rag_db rag_user 172.22.0.0/16 md5` (subnet langgraph-ui_default; host-gateway resolve 172.17.0.1 mas client_ip é do container).
- Branch `feature/Devin-20260925-postgres-vectorstore` (commit 3d072c3): compose `env_file: .env` (required:false) + `VectorStore__ConnectionString` composta de POSTGRES_*; install.sh idem; .env.example documenta.
- **Bug real achado**: `PostgresVectorStore.SearchAsync` commitava tx com reader aberto → Npgsql 10 `OperationInProgress` em TODA busca. Fix: `await using` no reader antes do Commit. `/health/ready` Healthy pós-redeploy.
- Resolvido: 2136 embeddings migrados do SQLite (`Chunks`, float32 LE) para `kh_embeddings` via TSV — model `deterministic:hash384`, dims 384, metadata no formato do IngestionService. HNSW auto-criado (2136 > threshold 1000, `vector_cosine_ops` m=16/ef=64). PR #227 squash-merged (`2edd0ae`).
- Pós-verificado (sessão seguinte): `/health/ready` Healthy, "Embedding store OK (2136 chunks)", 961 testes verdes, counts rag_db == SQLite (2136/2136), 7 índices presentes.

## 2026-09-26 — Docs refresh bilíngue (EN+PT)

- Continuação da sessão adaptable-candytuft: verificado .env (rag_db/rag_user, gitignored), 961 testes verdes, container healthy, rag_db com 2136 chunks == SQLite, pgvector 0.8.6 + 7 índices (HNSW cosine m=16/ef=64).
- memory.md fechou o loop da sessão pgvector (PR #228).
- Docs refresh: README EN+PT (conectores cloud, fila de ingestão, endpoints faltantes, "Redis security" — seção que o warning de startup citava e não existia), docs/{en,pt} (API.md +endpoints: embeddings/database/cache-keys/mcp-capabilities/agent-resume/threads/apikey usage+secret; INSTALL.md +checklist pgvector POSTGRES_*), ARCHITECTURE.md pipeline atualizada, CONTRIBUTING.md +paridade bilíngue, CHANGELOG.md reorganizado ([0.0.3]).
