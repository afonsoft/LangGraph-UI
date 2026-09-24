# SPEC — Contextual chunk enrichment (Contextual Retrieval à la Anthropic)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-contextual-chunk-enrichment` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `IngestionService`, `ITextChunker`, embeddings, FTS5 |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-contextual-chunk-enrichment` |
| Status | `Draft` |
| Ticket | — |
| Origem | tabnews "RAG na Prática" (document-aware chunking: chunk herda hierarquia H1>H2>H3 como contexto); Anthropic Contextual Retrieval; beerandcode (chunk como entidade de 1ª classe com metadados) |

## 1. User Story

**As a** usuário perguntando sobre um tópico cujo chunk isolado é ambíguo (ex.: "o valor é 42" sem dizer de qual doc/seção)
**I want** que cada chunk indexado carregue no texto embedado o contexto de onde veio — título do documento, caminho de seções, nome da fonte
**So that** buscas por entidade/tópico recuperem chunks que sozinhos não mencionam o assunto mas vivem dentro da seção certa.

## 2. Contexto

`MarkdownChunker` mantém o header dentro da seção, mas um chunk de um documento grande perde o título do doc e o caminho de seções ancestrais (chunk de `### Preços` dentro de `# Manual > ## Planos` não sabe que é do "Manual"). Efeito comprovado na literatura: contextual retrieval reduz retrieval failures em ~35-49% (Anthropic, 2024).

Hoje o texto embedado == texto exibido/citado. Enriquecer direto mudaria UX e citations — então separamos: `Chunk.Text` (exibição, inalterado) vs. texto enriquecido usado para embedding e indexação FTS.

Duas camadas, custos diferentes:
- **Estrutural (barata, default):** prefixo determinístico `Fonte: X | Doc: Título | Seção: A > B` montado de metadados já existentes — zero LLM.
- **Gerada (opt-in):** 1-2 frases de contexto geradas por `IChatClient` por chunk (Anthropic-style) — custo de LLM por chunk na ingestão, cacheável por content-hash.

## 3. Requisitos Funcionais

### RF-001 — Prefixo estrutural
- `IngestionService` compõe `enrichedText = header + "\n\n" + chunk.Text` onde header = `Fonte: {source.Name}` + `Documento: {doc.Title}` + `Seção: {cadeia de headers}` quando o chunker informar `SectionPath` (estender `ChunkPiece` com `SectionPath`).
- `MarkdownTextChunker`/`MarkdownNoteParser` propagam a cadeia de headers; `CodeTextChunker` propaga `SymbolPath` (já existe) como `Seção`.
- Embedding e linha FTS indexam `enrichedText`; `Chunk.Text` exibido permanece o original.

### RF-002 — Contexto gerado (opt-in)
- `Ingestion:ContextualEnrichment` = `off|structural|llm` (default `structural` para fontes novas? — decidir: default `structural` só em reindex, ver RF-004).
- Modo `llm`: prompt curto "dado o documento {title} e a seção {path}, escreva 1 frase situando este trecho" → concatena antes do chunk. Resultado cacheado por `contentHash` do chunk (reingestão idempotente não re-gera).
- Falha de LLM → fallback structural + warning; nunca falha a ingestão.

### RF-003 — Persistência
- Coluna `Chunks.EnrichedText` (nullable) + `Chunks.ContextVersion` (int) — migração EF.
- FTS indexa `COALESCE(EnrichedText, Text)`; vector store embeda o mesmo.

### RF-004 — Backfill versionado
- Reindex incremental: apenas docs cujo `ContextVersion` < current são re-processados (reusa SPEC de strategy-versioning se aprovada; senão versionar só o campo de contexto aqui).
- `POST /api/sources/{id}/reindex?mode=context` dispara backfill por fonte.

## 4. Requisitos Não-Funcionais

- Modo `structural`: zero custo de LLM, < +5% tempo de ingestão.
- Modo `llm`: custo proporcional a chunks novos (cache por contentHash); paralelismo limitado por `Ingestion:LlmEnrichment:MaxConcurrency` (default 4).
- Display/citations nunca mostram o enriquecimento (contaminação visual zero).

## 5. Fora de escopo

- Enriquecimento com resumo do documento inteiro via map-reduce (caro; candidate futura).
- Mudança de UI para exibir SectionPath (pode vir separado).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | `ChunkPiece.SectionPath` + propagação nos chunkers |
| T2 | Migration `EnrichedText`/`ContextVersion` + composição do prefixo estrutural |
| T3 | Indexação (FTS + vector) sobre enriched; display sobre raw |
| T4 | Modo `llm` com cache por contentHash + fallback |
| T5 | Reindex endpoint + eval antes/depois no dataset existente |
| T6 | Testes: chunk sem menção ao tópico encontrado via seção; display inalterado; reingestão não re-gera contexto LLM |

## 7. Critérios de aceite

- [ ] Chunk sem o termo da query mas dentro da seção correta é recuperado em modo híbrido (teste de integração).
- [ ] `Chunk.Text` retornado por `search_knowledge` é idêntico ao atual.
- [ ] Reingestão idempotente (mesmo contentHash) não chama o LLM de enriquecimento.
- [ ] `llm` indisponível → structural + log; ingestão conclui.
- [ ] Eval harness: context_precision/recall não regridem; idealmente sobem.

## 8. Riscos

- Prefixo repetitivo pode diluir sinal do embedding em chunks curtos → mitigar com cap: só enriquece chunks > N tokens (`Ingestion:ContextualEnrichment:MinTokens`, default 40).
- Contexto LLM pode alucinar fato errado no prefixo → prefixo vai separado por delimitador e o FTS continua indexando o texto cru também via `Text`.
