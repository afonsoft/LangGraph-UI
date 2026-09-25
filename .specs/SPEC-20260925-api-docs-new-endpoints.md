# SPEC — Docs de API desatualizadas: ingestion jobs, reindex, baselines, args de busca

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-api-docs-new-endpoints` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `docs/en|pt/API.md`, `README.md` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | GAP-documentation-rest-api-new-endpoints |
| Origem | gap-analysis 2026-09-25 — drift doc × código |

## 1. User Story

**As a** integrador/operador lendo a API reference
**I want** que os endpoints e args novos estejam documentados
**So that** descubro o contrato assíncrono de sync, os endpoints de job e as baselines de eval sem ler o código.

## 2. Contexto

`docs/en/API.md` e `docs/pt/API.md` descrevem `POST /api/sources/{id}/sync` como síncrono ("sync + activation") — desde SPEC-20260924-async-ingestion-queue o default é `202 + {jobId}` com `?wait=true` como compat. Ausentes da doc:

- `POST /api/sources/{id}/sync` → `202 {jobId,status,existing}` + `?wait=true` legado
- `POST /api/sources/{id}/reindex` → 202 (force re-chunk)
- `GET /api/ingestion/jobs[/{id}]` · `POST /api/ingestion/jobs/{id}/cancel`
- `GET/POST /api/eval/baselines` + campos `baseline`/`gate` no `POST /api/eval/run` + `gateResult`/`latency` no report
- `expand` (off|multi|hyde|both), `contextExpand` (none|window|section), `useGraph` (bool) em `search_knowledge`/`ask_knowledge` (tools documentadas indiretamente via README §MCP tools)
- `GET /api/search` filtros novos? (expand/contextExpand/useGraph via query string — verificar se passam)

## 3. Requisitos Funcionais

### RF-001 — API.md en+pt
- Seção "Sources & ingestion": contrato async de sync (`?wait=true`), `reindex`, tabela `Ingestion jobs`.
- Seção "Security & eval": baselines + `baseline`/`gate` no run request + campos novos do report.
- Seção MCP/tools (ou nota): args `expand`, `contextExpand`, `useGraph` de `search_knowledge`/`ask_knowledge`.

### RF-002 — README
- Uma linha na tabela de endpoints para `/api/ingestion/jobs*` e `/api/eval/baselines`; menção ao async sync + `?wait=true` na seção de uso se existir exemplo com sync.

### RF-003 — CLAUDE.md
- Atualizar "todas as SPECs estão Done" → reflete estado real (specs com issues abertas #178-182 + novas) e mencionar as novas capabilities em uma linha.

## 4. Requisitos Não-Funcionais

- Sem código — docs apenas.
- en/pt em paridade (estrutura idêntica).

## 5. Fora de escopo

- OpenAPI/Swagger generation (repo não usa hoje).
- Doc de config keys completas (Settings page pode ganhar depois).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `docs/en/API.md` |
| T2 | `docs/pt/API.md` (paridade) |
| T3 | `README.md` + `CLAUDE.md` |

## 7. Critérios de aceite

- [ ] Doc explica o fluxo async: `POST /sync` → `jobId` → poll `GET /api/ingestion/jobs/{id}`.
- [ ] Baselines + gate documentados com exemplo de body.
- [ ] CLAUDE.md não afirma mais "todas Done".

## 8. Riscos

- Nenhum — documentação.
