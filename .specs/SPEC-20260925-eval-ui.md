# SPEC — UI mínima de avaliação (runs, gates, baselines)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-eval-ui` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Blazor WASM`, `BootstrapBlazor`, `/api/eval/*` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-requirements-eval-ui |
| Origem | gap-analysis 2026-09-25 — RF-005 deferido de SPEC-20260924-eval-regression-gate |

## 1. User Story

**As a** mantenedor ajustando o pipeline de retrieval
**I want** ver runs de eval com métricas, resultado de gate e promover um run a baseline pela UI
**So that** não precise chamar a API manualmente para comparar runs.

## 2. Contexto

SPEC-20260924-eval-regression-gate entregou todo o backend (`GET/POST /api/eval/runs`, `/api/eval/baselines`, gate pass/fail, latências p50/p95/p99) mas deferiu a UI — não existe página de eval no client. Esta spec fecha o RF-005.

## 3. Requisitos Funcionais

### RF-001 — Página `/eval`
- Lista de runs: data, dataset hash (curto), recall@k, precision, MRR, faithfulness, p95, badge gate `pass`/`fail`, baseline usado.
- Clique num run → detalhe: métricas completas + violações do gate + top regressions vs baseline.

### RF-002 — Executar run
- Form: dataset (nome ou JSON inline), mode, topK, faithfulness (none|keyword|llm), baseline (select dos nomes existentes), gate rules (JSON textarea simples).
- Submit → `POST /api/eval/run` → mostra resultado + gate badge; entra na lista.

### RF-003 — Baselines
- Lista de baselines (nome, run id, dataset hash, data) + botão "Promover run a baseline" no detalhe do run (input nome → `POST /api/eval/baselines`).

### RF-004 — Navegação
- Item "Eval" no menu (ícone fa-flask), grupo Operacional; página `[Authorize]` como as demais.

## 4. Requisitos Não-Funcionais

- Reuso dos endpoints existentes — zero backend novo.
- `EvalApiClient` no client seguindo padrão `SourceApiClient` (`ApiResult<T>` + erros).

## 5. Fora de escopo

- Editor visual de gate rules (textarea JSON basta na v1).
- Gráficos de tendência entre runs.
- Agendamento via UI (`Eval:Schedule:*` continua config-only).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `EvalApiClient` + DTOs (report, metrics, gate, baseline) |
| T2 | Página `/eval` lista + detalhe + badge gate |
| T3 | Form de run + promote baseline |
| T4 | Menu/nav + build |

## 7. Critérios de aceite

- [ ] Lista mostra runs com gate badge correto.
- [ ] Rodar dataset com baseline+gate retorna resultado visível na página.
- [ ] Promover run → nome aparece em `GET /api/eval/baselines`.
- [ ] Erros da API viram mensagens legíveis (não stack trace).

## 8. Riscos

- Runs grandes (muitos cases) → detalhe lista só top regressions + métricas; per-case fica no JSON da API.
