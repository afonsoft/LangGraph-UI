# Short-term memory — session state (overwritten each session, ≤100 lines)

- **Last verified commit on `main`**: `48f1edc` (PR #247 — ingestion orphan-job fix).
- **Baseline**: build 0 warnings · 719 unit + 6/6 IngestionJobsApiTests integration green (local) · prod container healthy :5550 pós-#247 redeploy.
- **Done hoje (2026-09-26, sessão ingestion-deflake)**: root cause real do flake `Jobs_List_FiltersBySource` encontrado e corrigido (#247); 6 branches locais mergeadas deletadas; redeploy via compose healthy.
- **Blockers**: nenhum.
- **Next**: enforce_admins decisão pendente; teste shutdown gracioso×abrupto em aberto; watchdog de jobs `queued` stale continua como follow-up opcional.

## Session summary (2026-09-26 — ingestion orphan-job root cause + fix)

- Erro CI: `Jobs_List_FiltersBySource` timeout 300s — `last status: queued, running: [], queued count: 1`.
- Análise: `eb` (enfileirado depois de `ea`) completou → canal FIFO single-reader implica que `ea` FOI dequeued mas descartado antes da transição `running` persistir — crash no gap entre dequeue e `SaveChangesAsync` (fora do try/finally de terminal). Provável SQLITE_BUSY (sem WAL/busy_timeout na connstring).
- Por que fixes anteriores (#235/#239/#241/#244) não bastaram: atacavam o mecanismo de espera e a race de canal duplo — nunca o stranding de job dequeued.
- Fix `48f1edc`: `FailStrandedJobAsync` no catch do ExecuteAsync persiste `failed` + publica evento terminal; warning no early-return silencioso (exceto `cancelled`); `PRAGMA journal_mode=WAL` no startup SQLite.
- Testes: `IngestionWorkerTests` (crash determinístico via IIngestionService ausente → failed+evento; cancelled-skip com sentinel FIFO). 719 unit verdes.
- CI #247 todos os checks verdes; squash-merge; branch remota deletada.
- Redeploy: rebuild compose, container healthy, healthz 200, autosync ok (Postgres catalog).
- Detalhe aprendido: `FailOrphanedJobsAsync` varre queued/running no start — jobs enfileirados antes do worker subir morrem como "interrupted by restart" sem evento terminal (edge case, não corrigido).
