# SPEC — Retrieval hierárquico: small-to-big com expansão de contexto pai

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-hierarchical-retrieval` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `SearchService`, `IngestionService`, EF Core |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-hierarchical-retrieval` |
| Status | `Done` |
| Ticket | — |
| Origem | pedrolealdino (chunking hierárquico: chunk pequeno para precisão + contexto pai para coerência); tabnews (document-aware chunking) |

## 1. User Story

**As a** consumidor de `search_knowledge`/`ask_knowledge`
**I want** que um chunk pequeno recuperado traga opcionalmente o contexto da seção pai (ou chunks vizinhos)
**So that** respostas não fiquem órfãs de contexto — o trecho exato vem com a moldura que dá sentido a ele.

## 2. Contexto

Chunks pequenos maximizam precisão de retrieval mas minimizam contexto para a geração. A literatura (parent-document retriever do LangChain, chunking hierárquico) resolve com dois níveis: indexa-se o filho (preciso), retorna-se o pai (coeso).

Hoje `Chunks` é flat — não há noção de parentesco. O `MarkdownChunker` já trabalha por seções: o "pai" natural é a seção (conteúdo sob o header) ou a janela de chunks adjacentes do mesmo documento.

Design mínimo sem re-chunking: **expansão pós-retrieval** — para cada hit, opcionalmente anexar `±N` chunks adjacentes do mesmo `DocumentId` (window expansion) ou a seção-pai inteira quando identificável via `SectionPath` (depende/integra SPEC-20260924-contextual-chunk-enrichment). Sem reindex obrigatório: parentesco deriva de `DocumentId` + `ChunkIndex` já existentes.

## 3. Requisitos Funcionais

### RF-001 — Window expansion
- Arg `expand` (`none|window|section`, default `none`) em `search_knowledge`/`ask_knowledge`; `Search:Expansion:WindowSize` (default `1` = ±1 chunk).
- `window`: para cada hit, merge de chunks `ChunkIndex±N` do mesmo documento — marcados como `role=context` no item (não competem no ranking).
- Deduplicação: dois hits vizinhos fundem janelas sobrepostas; cada chunk aparece uma vez.

### RF-002 — Section expansion
- `section`: se `SectionPath` disponível (SPEC contextual aplicada), retorna o texto completo da seção-pai (cap `Search:Expansion:MaxParentTokens`, default 1500) — senão degrada para `window`.
- Result item expõe `ExpandedContext` separado do `ChunkText` — o hit original continua identificável para citations.

### RF-003 — Controle de orçamento
- Expansão respeita `Search:Expansion:MaxTotalTokens` (default 6000): expansões são adicionadas em ordem de rank até estourar o orçamento; o resto vai sem expansão.
- Expansão nunca aumenta `topK` — ela enriquece itens já selecionados.

### RF-004 — Contratos
- `SearchResultItem`/`CitationDto` ganham campo opcional `Context` (chunks vizinhos/seção); schemas MCP atualizados + contract tests.

## 4. Requisitos Não-Funcionais

- Uma query extra por documento-hit (ou 1 query agrupada por doc): fetch de chunks adjacentes por `(DocumentId, ChunkIndex range)` — indexável, sem full scan.
- Overhead alvo < +15% latência de busca; expansão desligada = zero custo.

## 5. Fora de escopo

- Re-chunking físico em dois níveis (parent rows separados) — evitável enquanto window/section expansion cobre o caso.
- Summarization de seção-pai via LLM.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `ExpandHitsAsync`: fetch de vizinhos por doc + merge/dedup |
| T2 | Args `expand` nas tools + DTOs + contract tests |
| T3 | Section expansion quando `SectionPath` existir (integra SPEC contextual; degrada para window) |
| T4 | Orçamento de tokens + telemetria (`search.expansion.chunks`) |
| T5 | Testes: vizinhos anexados e marcados; dedup de janelas sobrepostas; orçamento respeitado; `expand=none` inalterado |

## 7. Critérios de aceite

- [ ] Hit no meio de um doc retorna `Context` com os ±N vizinhos corretos, sem duplicatas.
- [ ] `expand=none` (default) produz resultado byte-idêntico ao atual.
- [ ] Expansão respeita `MaxTotalTokens` e nunca altera o ranking dos hits.
- [ ] Citations continuam apontando para o chunk-hit, não para o contexto expandido.
- [ ] Suite verde.

## 8. Riscos

- Contexto expandido pode reintroduzir chunk flagged por injection → expansão respeita `ExcludeFlagged` (vizinho flagged não entra).
- Janela grande em doc enorme estoura prompt → cap de tokens (RF-003).
