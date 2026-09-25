# SPEC — pgvector: iterative scan para queries filtradas (recall sob filtro)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-pgvector-iterative-filtered-scan` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `pgvector` ≥0.8 HNSW iterative scan |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Origem | análise de vector store 2026-09-25 |

## 1. User Story

**As a** consumidor do `search_knowledge` com `sourceIds` seletivos
**I want** recall preservado quando o filtro de fonte é restritivo
**So that** buscar "dentro de uma fonte pequena" não devolva quase nada só
porque o índice HNSW post-filtra.

## 2. Contexto

`SearchAsync` aplica `AND source_id = ANY($4)` — com HNSW, pgvector filtra
**depois** de coletar os top-k do índice: se a fonte filtrada é 1% do corpus,
top-k do scan bruto pode conter 0 hits dela → resultado vazio ou sub-recall
mesmo havendo bons matches. pgvector 0.8+ tem `iterative_scan` (strict/relaxed)
que continua o scan até juntar k linhas filtradas. Hoje `hnsw.ef_search` já é
tunado via `SET LOCAL` — mesma técnica cobre `iterative_scan` e
`max_scan_tuples`.

## 3. Requisitos Funcionais

- **RF-001** Quando `sourceIds` presente e provider pgvector ≥0.8:
  `SET LOCAL pgvector.iterative_scan = 'relaxed_order'` +
  `SET LOCAL hnsw.max_scan_tuples = Options.HnswMaxScanTuples` (default 20000).
- **RF-002** `PostgresOptions.IterativeScan = "off"|"relaxed_order"|"strict_order"`
  — default `off` (opt-in).
- **RF-003** Medição: métrica `vector_search_duration{provider,filtered}` +
  contador de hits retornados vs topK pedido (underfill) para avaliar se o
  ganho aparece — log debug quando underfill.
- **RF-004** pgvector <0.8 → tentativa de SET lança PostgresException →
  catch silencioso (mesmo padrão do ef_search atual) + warning uma vez.

## 4. Requisitos Não-Funcionais

- `strict_order` mantém ranking exato mas é mais caro — documentar trade-off;
  `relaxed_order` (default recomendado) pode retornar menos de k em filtros
  muito seletivos mas preserva recall relativo.

## 5. Fora de Escopo

- Índice parcial por fonte (explode N índices).

## 6. Plano de Tarefas

1. Options + SETs no SearchAsync.
2. Métrica de underfill/duração.
3. Testes unitários (comando emitido) + doc ops.

## 7. Acceptance Criteria

- [ ] Query com `sourceIds` restritivo em corpus misto devolve topK da fonte
  (vs vazio/sub-preenchido com scan simples).
- [ ] Sem filtro → nenhum SET extra emitido.
- [ ] pgvector antigo → fallback limpo.

## 8. Riscos

- `strict_order` pode varrer muito — `max_scan_tuples` é o freio.
