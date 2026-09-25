# SPEC — Correção de busca: expansão sem concorrência em DbContext, stream com retrieval corretivo, grading consistente, eval por dataset

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-search-correctness-and-stream` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (SearchService, LexicalSearchService, SqliteVecVectorStore, StreamingEndpoints, CorrectiveRetrievalService, EvalRunner) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | devin-ai-integration review — PR #184 (4 findings: 1🔴 + 3🔍) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — cluster D (D1–D4) |

## 1. User Story

**As a** usuário da busca com expansão multi-query/HyDE e do endpoint de streaming
**I want** que as variantes paralelas não derrubem a consulta e que `/api/ask/stream` siga a mesma política corretiva do `/api/ask`
**So that** expansão funcione em produção e as respostas sejam consistentes entre endpoints.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| D1 | Expansão `multi`/`both` dispara `Task.WhenAll` de variantes que compartilham o **mesmo** `KnowledgeHubDbContext` scoped e a mesma `DbConnection` — EF Core rejeita operação concorrente ("A second operation started") e/ou leitores simultâneos na mesma conexão SQLite → busca expandida falha | `SearchService.cs:500-520`; `LexicalSearchService` injeta `KnowledgeHubDbContext` e usa `db.Database.GetDbConnection()`; `SqliteVecVectorStore` usa `_db`/`_db.Database.GetDbConnection()` cast |
| D2 | `/api/ask/stream` chama `search.SearchAsync` direto — ignora `CorrectiveRetrievalService` (grading → retry → abstenção). Perguntas iguais recebem políticas diferentes conforme o endpoint | `StreamingEndpoints.cs:53` vs `AskEndpoints` (via corrective) |
| D3 | Retry pior mantém resultados antigos mas **adota o grading novo** — `grading = retryGrading` é incondicional → resposta pode abster-se indevidamente com bons resultados | `CorrectiveRetrievalService.cs:63` |
| D4 | `baseline` resolve o run por nome mas não confere `DatasetHash` — delta entre datasets diferentes aparece como regressão válida | `EvalRunner.cs:43-50` (`DatasetHash` gravado em :98 mas não comparado) |

## 3. Requisitos Funcionais

### RF-001 — Expansão sem operações concorrentes no mesmo contexto/conexão

- Variantes vetoriais e lexicais não executam operações simultâneas sobre a mesma instância de `KnowledgeHubDbContext`/`DbConnection` scoped.
- Implementação recomendada (menor risco): os braços de busca passam a usar **conexão dedicada por chamada concorrente** — `LexicalSearchService` abre `SqliteConnection` própria por `SearchAsync` (SQLite read-only permite N leitores) em vez da conexão do `DbContext`; `SqliteVecVectorStore.SearchAsync` idem para o caminho vetorial. Serialização das variantes (await sequencial) é aceitável como alternativa se mais simples, mas dedicada por chamada é o alvo — expansion existe para paralelizar.
- Invariante: busca `mode=hybrid` + `expand=multi|both|hyde` com ≥2 variantes conclui sem erro de concorrência em todos os providers (sqlite-vec, pgvector, in-memory EF fallback).

### RF-002 — Paridade corretiva no streaming

- `/api/ask/stream` usa o mesmo caminho de retrieval corretivo do `/api/ask` (grading → retry → abstention).
- O stream emite um evento `meta` (ou campo no primeiro evento) com `corrected:bool`, `retries:int`, `grade` e a query efetiva — o cliente pode exibir "pergunta reescrita/abstida" antes dos chunks.
- Abstenção no stream: emite evento de abstinência com citações (mesmo shape do `/api/ask`) em vez de sintetizar resposta fraca.

### RF-003 — Grading consistente com os resultados escolhidos

- `CorrectiveRetrievalService`: `grading = retryGrading` somente dentro do ramo `IsBetter` — retry pior mantém **results antigos + grading antigo**; o log registra a decisão (retry kept/rejected).

### RF-004 — Eval delta só dentro do mesmo dataset

- Comparação com baseline verifica `baseline.DatasetHash == run.DatasetHash`; mismatch → campo `datasetMismatch:true` no delta + warning (não erro — permite inspeção), e o gate não marca regressão por diferença de conjunto.

## 4. RNFs

- Latência da expansão não pode regredir para além do caso serial atual (a correção preserva paralelismo, só isola recursos).
- `eval-gate.sh` comportamento `gate=none` documentado (não altera — decisão registrada).
- Zero mudança de contrato REST para clients que não leem `meta` (evento aditivo).

## 5. Fora de Escopo

- Pool de conexões ou reestruturação do `DbContext` scoped para "por operação" — só os braços de busca.
- Corrective grading customizável por fonte.
- Comparação cross-dataset explícita (feature nova, não este fix).

## 6. Plano de Tarefas

1. Conexão dedicada por chamada em `LexicalSearchService` + `SqliteVecVectorStore.SearchAsync` (ou serialização documentada se dedicada inviável em algum braço).
2. `/api/ask/stream` via `CorrectiveRetrievalService` + evento `meta` + caminho de abstenção.
3. `grading` só no ramo `IsBetter`.
4. `datasetMismatch` no delta de eval.
5. Testes: expansão multi 3-variantes passa em sqlite-vec; stream reescreve/abstém igual ao endpoint síncrono; retry pior preserva grading antigo; delta com dataset diferente sinaliza mismatch.
6. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] `expand=multi` com 3 variantes em busca híbrida completa sem `InvalidOperationException` de concorrência (sqlite-vec + pgvector).
- [ ] `/api/ask/stream` aplica retry/abstenção do corrective e emite `meta` com query efetiva + retries + grade.
- [ ] Retry com nota pior mantém resultados E grading do attempt anterior.
- [ ] Delta de eval entre datasets diferentes exibe `datasetMismatch:true` sem marcar regressão.
