# SPEC — Retrieval expandido por grafo de conhecimento (GraphRAG-lite)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-graph-expanded-retrieval` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `IKnowledgeGraphStore`, `SearchService`, RRF |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-graph-expanded-retrieval` |
| Status | `Draft` |
| Ticket | — |
| Origem | tabnews (GraphRAG); latenode (LightRAG — grafo+vetor); SPEC-20260923-graphrag (extração Done — falta a expansão no retrieval) |

## 1. User Story

**As a** usuário perguntando sobre uma entidade ("o que depende do PaymentService?", "incidentes ligados a essa API")
**I want** que a busca reconheça a entidade, expanda para entidades relacionadas no grafo e puxe chunks que as mencionam
**So that** perguntas relacionais/multi-hop encontram evidência que a similaridade vetorial isolada não alcança.

## 2. Contexto

SPEC-20260923-graphrag (Done) entregou `EntityExtractor` + `SqliteKnowledgeGraphStore` + tools de grafo (`graph_*` via `GraphToolsProvider`). Mas o **retrieval padrão não usa o grafo**: `search_knowledge` continua puramente vetorial+lexical. O valor do grafo fica restrito a quem chama as tools explicitamente.

Padrão **GraphRAG-lite** (sem community detection/summaries — versão leve):
1. Extrair entidades mencionadas na query (match léxico contra índice de entidades — barato; LLM NER opt-in depois).
2. Expandir 1-hop: vizinhos da entidade no grafo.
3. Terceiro braço de retrieval: chunks com provenance nas entidades expandidas (o grafo já guarda chunk↔entity links).
4. Fundir no RRF existente como terceiro ranking.

## 3. Requisitos Funcionais

### RF-001 — Entity linking na query
- `GraphEntityLinker`: match case-insensitive dos termos da query contra aliases/nomes de entidades indexadas (FTS5 sobre tabela de entidades, ou `LIKE` em volumes pequenos — decidir por volume; default: query SQL simples com cap).
- `Search:Graph:Enabled` (default `false` — opt-in) + `Search:Graph:MaxEntities` (default 5) + `Search:Graph:MaxNeighbors` (default 10).

### RF-002 — Braço grafo no ranking
- Chunks ligados às entidades match e seus vizinhos 1-hop formam o ranking "graph" (rank por proximidade: entidade direta > vizinho).
- `RrfFuser` aceita N listas (já genérico?) — fundir vetorial + lexical + graph com o mesmo k=60.
- `ScoreBreakdown` ganha `GraphRank` (nullable) — debug/eval enxergam de onde veio o hit.

### RF-003 — Entity boost opcional
- `Search:Graph:Boost` (default `1.0`): multiplicador no score RRF de hits com provenance em entidade direta (sutil — boost, não hard-filter).

### RF-004 — Tool arg
- `search_knowledge`/`ask_knowledge` ganham `useGraph` (bool?, default = config). Arg documentada nos contract tests.

## 4. Requisitos Não-Funcionais

- Zero chamadas LLM no caminho default (linking léxico). Sem grafo populado (fontes sem extração) → braço grafo vazio, fusão inalterada.
- Overhead: 1-2 queries SQL indexadas; < +10% latência.

## 5. Fora de escopo

- Community detection + community summaries (GraphRAG completo — pesado; reavaliar com dados reais).
- NER via LLM na query (fase 2).
- Multi-hop >1 (paths no grafo já existem via tools `graph_*` para quem precisa).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `GraphEntityLinker` (match de termos → entidades) |
| T2 | Query de chunks por entidade (direto + 1-hop) no `IKnowledgeGraphStore` |
| T3 | Terceiro braço no `SearchService` + RRF N-listas + `GraphRank` |
| T4 | Config/args + contract tests + telemetria (`search.graph.hits`) |
| T5 | Testes: pergunta sobre entidade recupera chunk via vizinho que vetorial perde; grafo vazio = inalterado; boost aplicado |

## 7. Critérios de aceite

- [ ] Query citando entidade A recupera chunk que só menciona B (vizinho de A no grafo).
- [ ] Fonte sem extração de grafo → resultados idênticos ao modo atual.
- [ ] `GraphRank` presente no breakdown quando o hit veio do braço grafo.
- [ ] `useGraph=false` por chamada desliga o braço.
- [ ] Suite verde; contract tests atualizados; eval antes/depois.

## 8. Riscos

- Entity linking léxico pode casar termos errados (entidade "API" casa tudo) → min entity-name length + stoplist + cap de vizinhos.
- Grafo espelado/ruidoso polui ranking → boost pequeno default + eval.
