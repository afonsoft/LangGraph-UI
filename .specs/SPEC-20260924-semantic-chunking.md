# SPEC — Semantic chunking: quebra por distância de embeddings

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-semantic-chunking` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `ITextChunker`, `IEmbeddingProvider` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-semantic-chunking` |
| Status | `Done` |
| Ticket | — |
| Origem | tabnews "RAG na Prática" (Estratégia 2 — SemanticChunker, breakpoint por percentil); pedrolealdino (separador semântico) |

## 1. User Story

**As a** operador indexando prosa densa sem estrutura de headers (artigos, transcrições, docs corridos)
**I want** uma estratégia de chunking que corte onde o assunto muda — medindo distância semântica entre sentenças consecutivas
**So that** chunks têm coerência temática real em vez de cortes arbitrários no meio de uma ideia.

## 2. Contexto

`ChunkerSelector` hoje escolhe entre `Markdown`/`Code`/`Config`/`Prose` — todos estruturais (headers, símbolos, separadores, tamanho). Prosa sem markup cai no fallback por parágrafo/tamanho, que corta ideias ao meio.

**Semantic chunking:** embed cada sentença → distância cosseno entre sentenças consecutivas → quebra quando a distância supera um threshold (percentil do próprio documento). Custo: N embeddings de sentença por documento na ingestão — reusando `EmbedBatchAsync` já existente. Chunks resultantes respeitam `maxTokens` como teto duro (merge de sentenças até o limite, quebra no breakpoint ou no teto — o que vier antes).

## 3. Requisitos Funcionais

### RF-001 — `SemanticTextChunker`
- Novo `ITextChunker` (`ChunkKind.Semantic` ou ativado via config por fonte — `Source.ChunkingStrategy` já existe? verificar; se não, `Ingestion:Chunking:Strategy` global `auto|semantic`).
- Pipeline: split em sentenças (regex pt/en robusto: `.`, `!`, `?`, `;` + quebras de linha) → `EmbedBatchAsync(sentenças)` → distâncias consecutivas → breakpoints no percentil `Ingestion:Semantic:BreakpointPercentile` (default 95) → merge respeitando `maxTokens`/`overlapTokens` (overlap reusa as últimas sentenças do chunk anterior).
- Piso de tamanho: sentenças são mescladas até `minTokens` (default 100) mesmo com breakpoint — evita micro-chunks.

### RF-002 — Custo controlado
- `Ingestion:Semantic:MaxSentencesPerDoc` (default 2000) — doc maior cai para o chunker estrutural + warning.
- Sentença-embeddings NÃO são persistidos (só servem para o corte) — descartados após o chunking.
- Falha do provider → fallback para o chunker estrutural da fonte + warning; ingestão nunca aborta por isso.

### RF-003 — Seleção
- `ConnectorConfig`/`Source` ganham `Chunking` override por fonte (`null` = auto atual). UI de edição de fonte ganha o seletor.

## 4. Requisitos Não-Funcionais

- Custo: ~1 embedding por sentença na ingestão (amortizado — só em docs novos/alterados graças ao content-hash).
- Determinístico: mesmo texto + mesmo provider → mesmos cortes (necessário para reingestão idempotente).

## 5. Fora de escopo

- Agentic chunking (LLM decide cortes — custo muito maior).
- Tuning automático de percentil por fonte.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Sentence splitter + `SemanticTextChunker` puro (vetores injetados → fácil de testar com provider fake) |
| T2 | Integração no `ChunkerSelector` + config por fonte |
| T3 | Fallback e caps |
| T4 | Testes: quebra em mudança de assunto; teto de tokens respeitado; fallback sem provider; determinismo |
| T5 | Eval antes/depois numa fonte de prosa real |

## 7. Critérios de aceite

- [ ] Documento com mudança clara de tópico corta exatamente na transição (teste com embeddings fake controlados).
- [ ] Nenhum chunk excede `maxTokens`; nenhum fica abaixo de `minTokens` salvo doc curto.
- [ ] Provider indisponível → chunking estrutural + warning; sync conclui.
- [ ] Reingestão do mesmo doc produz os mesmos chunks.
- [ ] Suite verde.

## 8. Riscos

- Percentil global do doc pode criar chunks gigantes em texto homogêneo → teto duro de `maxTokens` sempre aplicado.
- Custo de embeddings em vaults grandes → cap por doc + só em docs alterados.
