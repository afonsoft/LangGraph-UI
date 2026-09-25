# SPEC — Limpeza de código morto residual (NotImplementedIngestionService & cia)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-dead-code-cleanup` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `.NET 10` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | GAP-automation-dead-notimplemented-ingestion |
| Origem | gap-analysis 2026-09-25 — higiene |

## 1. User Story

**As a** mantenedor lendo a base
**I want** placeholders de specs antigas removidos quando a implementação real existe
**So that** ninguém confunde stub com caminho de produção e o código morto não acumula.

## 2. Contexto

`NotImplementedIngestionService` (`src/KnowledgeHub.Server/Services/IIngestionService.cs`) é um placeholder de SPEC-03 ("every sync reports skipped") — a implementação real (`IngestionService`) está registrada e o stub não é referenciado por nenhum call site ou teste. Mesma família de débito: outros `NotImplemented*`/`TODO` residuais de specs já Done devem ser varridos no mesmo passo.

## 3. Requisitos Funcionais

### RF-001 — Remover stubs não referenciados
- `NotImplementedIngestionService` e qualquer equivalente (`NotImplemented*`, `Fake*` em `src/`, `TODO`/`HACK`/`FIXME` apontando para spec Done).

### RF-002 — Sweep documentado
- `grep -rn "NotImplemented\|TODO\|FIXME\|HACK" src/` — cada hit classificado: remove (stub morto), mantém (justificativa no comentário) ou vira gap.

## 4. Requisitos Não-Funcionais

- Apenas remoções/comentários — zero mudança de comportamento.
- Build + suites verdes após a limpeza.

## 5. Fora de escopo

- Refatoração de código vivo (só morto).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Remover `NotImplementedIngestionService` |
| T2 | Sweep de stubs/TODOs residuais com classificação |

## 7. Critérios de aceite

- [ ] Nenhum `NotImplemented*` em `src/` sem referência de spec viva.
- [ ] Build + testes verdes.

## 8. Riscos

- Stub referenciado por reflexão/DI escondida → busca por nome antes de remover.
