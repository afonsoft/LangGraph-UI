# SPEC — Eval contínuo: regression gate, execução agendada e latência p95

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-eval-regression-gate` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `EvalRunner` existente, BackgroundService, GitHub Actions |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-eval-regression-gate` |
| Status | `Draft` |
| Ticket | — |
| Origem | beerandcode ("RAG sem evals é fé" — recall@5/faithfulness/p95 medidos semanalmente); fonte-rag (golden set + RAGAS-like + diff entre runs); withnocode (monitoramento contínuo de qualidade) |

## 1. User Story

**As a** mantenedor evoluindo chunker/embeddings/prompts
**I want** que cada mudança relevante no pipeline rode o golden set e compare contra o baseline aprovado, com percentis de latência, e que eu seja alertado quando a qualidade regride
**So that** melhorias de retrieval sejam decididas por métrica e não por intuição — e regressões sejam pegas antes do merge/deploy.

## 2. Contexto

`EvalRunner` (SPEC-20260923, Done) já executa datasets com `compareTo` (diff vs. run de referência) e métricas recall@K/precision/faithfulness/abstention. Falta o **fechamento do ciclo**:

- Ninguém roda o eval a menos que lembre — sem agendamento nem gatilho em mudança de pipeline.
- `compareTo` reporta diff mas não **falha** — não existe gate ("faithfulness caiu >0.05 → reprovado").
- Latência não é agregada em percentis no relatório (p50/p95/p99 por estágio).
- Baseline ("golden run aprovado") não é um conceito persistido — comparação é por ID de run arbitrário.

## 3. Requisitos Funcionais

### RF-001 — Baseline e gates
- `POST /api/eval/baselines` marca um run como baseline nomeado (`name`, dataset fingerprint). `GET /api/eval/baselines`.
- Eval run ganha `Gate` opcional: `{ metric, threshold, direction }[]` (ex.: `{"context_recall", "gte", 0.80}`, `{"p95_ms", "lte", 1500}`). Resultado `gateResult: pass|fail` + violações detalhadas no relatório persistido.
- Endpoint `POST /api/eval/runs` aceita `baseline` (nome) → auto-compare + gate no mesmo passo.

### RF-002 — Percentis de latência
- `EvalRunner` cronometra por caso: tempo de retrieval e (quando `ask`) de geração. Report agrega `latency: { p50, p95, p99, mean }` por estágio — alinhado às métricas OTel existentes.

### RF-003 — Execução agendada
- `EvalScheduleService` (BackgroundService): `Eval:Schedule:Cron` (simples: intervalo em minutos ou cron-lite `@daily`/`@weekly`), `Eval:Schedule:Dataset`, `Eval:Schedule:Enabled` (default false).
- Resultado persistido como qualquer run; se `gateResult=fail` → log Warning + evento SignalR `eval.gate.failed` + (opt) webhook `Eval:Schedule:NotifyUrl` (POST do relatório-resumo).

### RF-004 — CI hook
- `POST /api/eval/runs` utilizável em workflow: script `scripts/eval-gate.sh` (ou step) que roda dataset versionado em `eval/datasets/` contra o server do CI e sai `exit 1` em fail. Workflow novo/arquivo separado — **`.github/workflows/` é protegido**: entregar apenas o script + instrução doc, dono adiciona o step.

### RF-005 — UI mínima
- Seção em Settings ou página própria simples: lista de runs com métricas, badge pass/fail, botão "promover a baseline", diff view vs baseline.

## 4. Requisitos Não-Funcionais

- Eval agendado nunca muta o índice (EvalRunner já é read-only — manter).
- Faithfulness (LLM-judge) é opt-in por run (custo) — gates sobre métricas LLM só quando `faithfulness!=none`.
- Falha do eval agendado não pode derrubar o servidor — try/catch + log, como os demais hosted services.

## 5. Fora de escopo

- LLM-judge multi-métrica completo (RAGAS full) — faithfulness atual basta para o gate.
- A/B testing online de pipelines.
- Dashboard de tendência temporal rico (lista de runs + diff já permite ver evolução).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Entidade `EvalBaseline` + endpoints + fingerprint de dataset |
| T2 | Percentis de latência no `EvalRunner`/report |
| T3 | `Gate` no run request + avaliação + persistência do resultado |
| T4 | `EvalScheduleService` + notificação SignalR/webhook |
| T5 | `scripts/eval-gate.sh` + doc (sem tocar `.github/workflows/`) |
| T6 | UI mínima (runs, gate badge, promote baseline) |
| T7 | Testes: gate falha/passa corretamente; baseline resolve por nome; scheduler dispara e registra; p95 presente no report |

## 7. Critérios de aceite

- [ ] Run com `baseline` + `gate` retorna `gateResult` e violações específicas.
- [ ] Latência p50/p95/p99 por estágio no relatório persistido.
- [ ] Scheduler executa run no intervalo configurado e notifica em fail.
- [ ] `eval-gate.sh` sai 0/1 conforme gate — utilizável em CI.
- [ ] Promover run a baseline torna-o referência das próximas comparações.
- [ ] Suite verde.

## 8. Riscos

- LLM-judge não-determinista causa flapping de gate → thresholds com margem (histerese) + faithfulness opt-in.
- Eval agendado em produção consome LLM → default desligado, doc clara sobre custo.
