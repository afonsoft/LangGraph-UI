# SPEC — Desflake dos testes de ingestion jobs (WaitTerminalJobAsync)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-ingestion-jobs-test-deflake` |
| Data | 2026-09-26 |
| Autor | Devin |
| Tipo | `Bugfix` (testes) |
| Stack | `xUnit`, `WebApplicationFactory`, SQLite in-memory |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | `GAP-tests-ingestion-jobs-flaky` |
| Origem | gap-analysis 2026-09-26 — 2 flakes no CI em `IngestionJobsApiTests` |

## 1. User Story

**As a** mantenedor que mergeia PRs protegidos por status checks
**I want** `IngestionJobsApiTests` determinístico no CI
**So that** merges não bloqueiam aleatoriamente por flakes de timing.

## 2. Contexto

AS-IS: `tests/KnowledgeHub.Tests.Integration/IngestionJobsApiTests.cs` tem `WaitTerminalJobAsync` com polling fixo `120×500ms` (60s). No run 36192973074 (CI push, PR #228) **dois testes distintos falharam**:

- `Sync_EnqueuesJob_AndJobReachesTerminal` — ~61s (justo acima do budget)
- `Cancel_TerminalJob_Returns409` — ~116s (outro teste, mesma classe)

O comentário no helper já documenta flake anterior (PR #205 — budget já foi elevado de 30s→60s). Os runs `pull_request` e local passam 245/245. Provável causa raiz: `IngestionWorker` compartilhado por fixture processa jobs de classes xUnit paralelas serialmente — sob carga de CI, jobs ficam na fila além do budget.

TO-BE: testes com determinismo real — não apenas subir timeout de novo (mascarou uma vez, voltou a falhar).

## 3. Requisitos Funcionais

### RF-001 — Diagnóstico da causa raiz
- Investigar se `IngestionJobsApiTests` compartilha `IngestionWorker`/fila com classes paralelas (mesma collection? factory reutilizada? SQLite file compartilhado?).
- Confirmar se jobs de outras classes enfileiram na mesma fila e serializam o processamento.
- Documentar a causa no teste (comentário) e na SPEC antes do fix.

### RF-002 — Fix determinístico
- Escolher e aplicar a estratégia conforme o diagnóstico:
  - se fila compartilhada: collection isolada ou fixture por classe com worker dedicado;
  - se worker lento: budget adaptativo (poll com exponential backoff até Ns, ou latch/sinal do worker) em vez de polling fixo;
  - se xUnit parallelização: `[Collection("ingestion")]` dedicada.
- O fix não pode só aumentar timeout sem remover a contensão que o causa.

### RF-003 — Não regredir comportamento
- Os 4 testes da classe continuam cobrindo o mesmo comportamento (enqueue→terminal, cancel, 409, filter).
- Suíte completa 961+ passa local e no CI.

## 4. Requisitos Não-Funcionais

- Fix não deve inflar tempo de CI em mais que ~10s por run.
- Se o worker real for o gargalo, preferir fixture isolada a serialização global (preserva paralelismo do resto da suíte).
- Nenhuma mudança em código de produção — só testes/fixtures.

## 5. Restrições / Não-fazer

- Não subir o timeout acima de 60s como única mudança.
- Não mexer em `IngestionWorker` de produção.
- Não desabilitar paralelismo global do xUnit (mataria throughput da suíte toda).

## 6. Critérios de Aceite

- `IngestionJobsApiTests` passa em 5 runs consecutivos de CI sem flake (ou evidência equivalente de determinismo).
- Causa raiz documentada em comentário no helper/teste.
- Tempo da suíte não aumenta significativamente.

## 7. Definição de Pronto

- [ ] Diagnóstico da fila compartilhada concluído e documentado
- [ ] Fix aplicado conforme RF-002
- [ ] 5 runs de CI verdes consecutivos
- [ ] Comentário atualizado referenciando esta SPEC
