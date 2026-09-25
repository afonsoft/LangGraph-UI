# SPEC — RAG corretivo (CRAG-lite): grading de retrieval, retry e abstenção honesta

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-corrective-rag` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `SearchService`, `AnswerService`, `AgentService`, `IQueryRewriter` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-corrective-rag` |
| Status | `Done` |
| Ticket | — |
| Origem | tabnews (CRAG/Self-RAG); beerandcode ("diga que não sabe" + faithfulness); fonte-rag (abstention set no eval) |

## 1. User Story

**As a** usuário do `ask_knowledge`/chat
**I want** que o sistema avalie a qualidade do retrieval antes de responder, tente de novo com query reescrita quando a evidência é fraca, e admita "não encontrei" quando nada presta
**So that** o modelo nunca preenche lacuna de retrieval com conhecimento paramétrico — a alucinação por contexto insuficiente desaparece.

## 2. Contexto

Hoje: search retorna topK sempre (sem noção de "evidência suficiente") → AnswerService sintetiza com os chunks que vieram. O prompt instrui a dizer quando insuficiente, mas sem sinal estrutural o LLM tende a responder mesmo assim.

Padrão **CRAG** (Corrective RAG): um grader avalia cada documento recuperado como `relevant|ambiguous|irrelevant`; se a maioria é irrelevante → corrige (re-escreve query e re-busca, ou desiste). Implementação pragmática sem modelo de grading dedicado:

- **Grading heurístico (default, zero custo):** score fundido do top1/top3, cobertura lexical (match ratio de termos), nº de hits acima do floor (integra SPEC-20260924-mmr-diversity).
- **Grading LLM (opt-in):** `IChatClient` julga relevância chunk-a-chunk (prompt binário, batched) — mais preciso, custo por chunk avaliado.

Fluxo: `retrieve → grade → {sufficient: responde | weak: rewrite+retry 1× | insufficient: resposta de abstenção com chunks "mais próximos" como referência}`.

## 3. Requisitos Funcionais

### RF-001 — Retrieval grader
- `IRetrievalGrader` com duas impls: `HeuristicRetrievalGrader` (default) e `LlmRetrievalGrader` (`Search:Grading:Mode=heuristic|llm|off`, default `heuristic`).
- Output: `Grade { Sufficient, Weak, Insufficient }` + `Confidence` (0-1) + por-item `IsRelevant` quando LLM.
- Heurístico: `Sufficient` se top1 fused ≥ `T1` (default 0.035 RRF ≈ top1 em ambos rankings) ou ≥3 hits acima de floor; `Insufficient` se top1 < `T3`; meio = `Weak`. Thresholds em `Search:Grading:*`.

### RF-002 — Retry corretivo
- `Weak` → `IQueryRewriter` gera reformulação orientada ("a busca anterior falhou; reformule para recuperar documentos sobre X") → 1 retry (`Search:Grading:MaxRetries`, default 1, max 2).
- Resultado final combina melhor tentativa; `AnswerDto`/`ask` expõe `RetrievalGrade` + `Retried` (transparência).

### RF-003 — Abstenção estrutural
- `Insufficient` → `AnswerService` short-circuit: resposta padrão honesta ("Não encontrei informação suficiente na base. Trechos mais próximos: …") com os top3 como `Citations` fracas — sem chamada ao LLM de síntese (economia + zero alucinação).
- `AnswerDto.InsufficientEvidence = true` para UI mostrar estado distinto.

### RF-004 — Agent loop
- `AgentService` recebe o grade no tool-result de `search_knowledge`/`ask_knowledge` (campo `grade` no payload) — o agente pode decidir reformular por conta própria (tool já sugere `suggestion="rephrase"` quando `Weak`).

## 4. Requisitos Não-Funcionais

- Modo `heuristic`: +0 chamadas LLM, < +5ms.
- Modo `llm`: +1 chamada batched por busca (grading binário, ~20 tokens/chunk).
- Abstenção nunca invoca o sintetizador → mais barata que resposta normal.

## 5. Fora de escopo

- CRAG completo com web-search fallback (integração futura possível via MCP upstreams).
- Self-RAG com reflection tokens / treino.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `IRetrievalGrader` + `HeuristicRetrievalGrader` (thresholds configuráveis) |
| T2 | `LlmRetrievalGrader` (prompt binário batched, parse resiliente) |
| T3 | Loop corretivo em `ask_knowledge`/`AnswerService` + abstenção estrutural |
| T4 | `grade`/`suggestion` no payload das tools + `InsufficientEvidence` nos DTOs |
| T5 | Eval: abstention set existente deve manter ExpectNoAnswer=1; casos Weak→retry no eval dataset |
| T6 | Testes: query fora do corpus → abstenção sem LLM; Weak → 1 retry com query reescrita; grade exposto |

## 7. Critérios de aceite

- [ ] Query sem evidência retorna `InsufficientEvidence=true` e não chama o LLM de síntese.
- [ ] Query Weak dispara exatamente `MaxRetries` re-buscas com queries reescritas.
- [ ] Resposta de abstenção lista os trechos mais próximos sem afirmar conteúdo.
- [ ] Agent loop recebe `grade` e pode agir sobre `suggestion`.
- [ ] Suite verde; eval abstention set passa.

## 8. Riscos

- Thresholds errados → abstenção excessiva (frustração) ou insuficiente (alucinação) → defaults conservadores + tunning via eval dataset; métrica `answer.abstained` para monitorar taxa.
- Grading LLM pode errar nos dois sentidos → opt-in, heuristic é o caminho padrão.
