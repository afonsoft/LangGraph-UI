# Gap Analysis — 20260925 (pós-merge das 11 SPECs RAG + PRs Antigravity)

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: main (clean)
- Driver: auditoria pós-delivery — specs implementadas × código × docs × issues
- Mode: fresh run. Prior runs: gap-analysis-20260914/16/17/17-r2/22/23 (todas ENTREGUE)
- gh: OK (afonsoft). Sibling skills: write-specs/create-issues/orchestrator presentes.

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 115 SPECs — 111 Done; 4 abertas cobertas por Issues #178–182 |
| docs/, docs/architecture/ | present | API.md en+pt desatualizado vs. novos endpoints |
| .claude/ harness + memory | present | — |
| CLAUDE.md, AGENTS.md, README.md | present | CLAUDE.md afirma "todas as SPECs Done" — drift |
| GitHub | gh OK | Issues abertas: #178, #179, #181 (in_progress), #182 (todo) |
| Working tree | clean | — |

## 2. Candidates and verdicts

| Key | Category | Verdict | Priority | Evidence |
| --- | --- | --- | --- | --- |
| GAP-requirements-semantic-chunking-source-ui | requirements | CONFIRMADO | média | SPEC-20260924-semantic-chunking RF-003 exige seletor; `SourceEditDialog.razor` sem campo `chunking` — só `{"chunking":"semantic"}` via JSON cru. `ChunkerSelector.StrategyFor` já lê. |
| GAP-requirements-eval-ui | requirements | CONFIRMADO | média | RF-005 deferido (nota na spec Done); sem página `/eval`; backend completo (`/api/eval/runs`, `/baselines`, gate). |
| GAP-documentation-rest-api-new-endpoints | documentation | CONFIRMADO | média | `docs/en|pt/API.md` sem `?wait=true`/`202+jobId`, `/reindex`, `/api/ingestion/jobs*`, `/api/eval/baselines`, args `expand`/`contextExpand`/`useGraph`; CLAUDE.md "todas Done" falso. |
| GAP-operation-autosync-no-job-record | operation | CONFIRMADO | baixa | `VaultWatcherService` chama `SyncAsync` inline — auto-syncs invisíveis em `/api/ingestion/jobs` (auditabilidade da RF-001 incompleta). |
| GAP-implementation-signalr-job-progress | implementation | CONFIRMADO | baixa | RF-002 pedia `ingestion.progress` no hub; entregue polling+flush DB (sem hub genérico). Desvio documentado parcialmente. |
| GAP-automation-dead-notimplemented-ingestion | automation | CONFIRMADO | baixa | `NotImplementedIngestionService` sem referências (stub de SPEC-03, impl real registrada). |
| GAP-tests-ingestion-eval-endpoints | tests | CONFIRMADO | média | `/api/ingestion/jobs*`, `cancel`, `reindex`, `/api/eval/baselines` sem teste de integração — contrato async quebrou 25 testes só detectado no CI. |
| 4 specs não-Done (cloud, gdrive, serilog, redis) | requirements | DUPLICADO | — | Cobertas por Issues #178/#179/#181/#182 — trabalho já rastreado. |
| `Search:*` knobs sem settings runtime | operation | REJEITADO | — | Sem TO-BE documentado — specs definem config-only. |
| Semantic chunker sem SectionPath | requirements | REJEITADO | — | Spec prevê fallback para window quando sem seção. |
| `enforce_admins` escape hatch | security | INCONCLUSIVO (carried) | — | Carregado de 20260917-r2 — decisão do dono pendente. |

## 3. SPECs Draft produzidas

| SPEC | Gap | Prioridade |
| --- | --- | --- |
| `.specs/SPEC-20260925-semantic-chunking-source-ui.md` | seletor de chunking por fonte | média |
| `.specs/SPEC-20260925-eval-ui.md` | página de eval + promote baseline | média |
| `.specs/SPEC-20260925-api-docs-new-endpoints.md` | docs drift | média |
| `.specs/SPEC-20260925-autosync-through-queue.md` | auto-sync via fila | baixa |
| `.specs/SPEC-20260925-job-progress-feed.md` | push de progresso | baixa |
| `.specs/SPEC-20260925-dead-code-cleanup.md` | stubs mortos | baixa |
| `.specs/SPEC-20260925-new-endpoints-integration-tests.md` | cobertura de integração | média |

## 4. Gate

Aguardando aprovação do usuário antes de criar Issues/commitar (hard gate da skill).

## Execution (2026-09-25, Devin)

All 7 gap SPECs + 3 remaining Approved SPECs implemented on `feature/Devin-20260925-remaining-specs`:
- dead-code: NotImplementedIngestionService stub removed
- autosync: moved to dedicated ScheduledSyncBackgroundService → enqueues "autosync" jobs
- semantic-chunking-source-ui: chunking select in SourceEditDialog (all doc-producing types)
- api-docs: API.md en/pt + README + CLAUDE.md updated (async sync, jobs, baselines, new args)
- integration tests: IngestionJobsApiTests (6) + EvalBaselinesApiTests (3) + gdrive cases (3)
- job-progress-feed: IIngestionProgressFeed (1s throttle) → "IngestionProgress" on /hubs/mcp; Sources.razor races push vs poll fallback
- eval-ui: /eval page + EvalApiClient + nav item; runs list now exposes gate+baselineName
- redis RF-004: Cache section in Settings (stats card + clear)
- serilog: Serilog.AspNetCore 10.0.0, console+rolling-file sinks (logs/, 14d), ScheduledSync + Maintenance (orphan staging purge, 6h) services, structured job logs in worker
- gdrive: SourceType.GoogleDrive=11, GoogleDriveApiClient (v3 + public fallbacks), GoogleDriveGateway (natives→txt/csv export, maxFiles cap, md5|modifiedTime fingerprint), GoogleDriveSharedConnector (gdrive:{id} secret), UI fields, 400 on bad link

Gotchas: Drive natives report no size → sentinel 1 so base loop doesn't skip. DateTimeOffset "O" → Z suffix. Staging subdirs need CreateDirectory before File.Create (found earlier).

Suite: 613 unit + 229 integration green.

## Follow-up (2026-09-25): monitor audit + api-keys icons + deploy

- PR #189 merged (4f25a5f): CallerResolver (IHttpContextAccessor ambient) → Caller em todos os eventos MCP; monitor ganhou colunas Quem/Sessão, filtros kind/outcome/texto, resumo agregado, export CSV; ApiKeys ícone-only.
- Pinned contract tests updated para o campo aditivo `caller`.
- Issue #181 fechada — todas as specs Done, zero issues abertas.
- Deploys: 6c313ee (specs batch) e 4f25a5f (audit) ambos healthy :5550.

## Follow-up 2 (2026-09-25): infra specs — cache/pgvector/logging/otel

- PR #190 aberto (feature/Devin-20260925-infra-specs): 12 Draft specs.
- Bug confirmado documentado: `DeleteAsync` de fonte não apaga kh_embeddings
  (só DeleteByDocumentAsync existe) → SPEC-pgvector-source-cascade.
- CacheManagerService stats são process-local (enganosos com redis) → SCAN/INFO.
- L1/stampede ausentes → HybridCache. Invalidação single-replica → pub/sub.
- Serilog: sem request logging, sem redaction, logs/ não é volume, sem level
  switch. OTel: pipeline RAG sem spans.
- Prioridade sugerida: pgvector-source-cascade (bug) > request-logging >
  redis-health > hybrid-cache > region-ttl > resto.
