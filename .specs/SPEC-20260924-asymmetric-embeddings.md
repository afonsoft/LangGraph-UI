# SPEC — Embeddings assimétricos: input_type query vs. document

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260924-asymmetric-embeddings` |
| Data | 2026-09-24 |
| Autor | Devin |
| Stack | `.NET 10`, `IEmbeddingProvider`, Ollama/OpenAI/ONNX providers |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-asymmetric-embeddings` |
| Status | `Done` |
| Ticket | — |
| Origem | beerandcode (Voyage `input_type: document|query` — esquecer de alternar derruba recall ~10pp); convenção nomic/Ollama (`search_query:`/`search_document:`) |

## 1. User Story

**As a** plataforma usando provider de embedding com treino assimétrico (nomic-embed-text, voyage, e5, bge)
**I want** que queries e documentos passem pelo provider com o papel correto (`query` vs `document`)
**So that** o retrieval usa os vetores como o modelo foi treinado — recuperando os pontos de recall perdidos por embedding simétrico.

## 2. Contexto

`IEmbeddingProvider` hoje tem `EmbedAsync(text)`/`EmbedBatchAsync(texts)` — simétricos. Modelos assimétricos geram vetores diferentes para o mesmo texto dependendo do papel:

- **nomic-embed-text (Ollama):** prefixes `search_query: ` / `search_document: `.
- **e5/bge:** `query: `/`passage: ` prefixes.
- **Voyage/OpenAI v3+:** `input_type` no request (OpenAI ignora; Voyage usa).
- **ONNX all-MiniLM-L6-v2:** simétrico (sem prefixo).

Solução: adicionar `EmbedQueryAsync`/`EmbedDocumentBatchAsync` ao provider interface com default simétrico (delega ao atual — backward compatible); cada provider implementa prefixo/input_type conforme sua convenção, configurável.

## 3. Requisitos Funcionais

### RF-001 — Interface assimétrica
- `IEmbeddingProvider` ganha `EmbedQueryAsync(text)` e `EmbedDocumentBatchAsync(texts)` com **default implementations** delegando ao simétrico (nenhum provider quebra).
- Config por provider: `Embeddings:QueryPrefix`, `Embeddings:DocumentPrefix` (default vazio) — prefixo concatenado ao texto antes de embedar. Nunca duplicar prefixo se o texto já começar com ele.

### RF-002 — Convenções por provider
- `OllamaEmbeddingProvider`: auto-detecta modelo nomic/e5/bge por nome (`Embeddings:Asymmetric:Auto`, default true) e aplica o prefixo padrão do modelo; overrides via config.
- `OpenAiEmbeddingProvider`: envia `input_type` quando configurado (`Embeddings:OpenAi:InputType` — providers compatíveis tipo Voyage via baseURL customizado).
- `OnnxEmbeddingProvider`/`DeterministicEmbeddingProvider`: simétricos — no-op.

### RF-003 — Pontos de chamada
- `SearchService`: embed de query via `EmbedQueryAsync` (cache key inclui papel — query e doc com mesmo texto têm vetores diferentes!).
- `IngestionService`/`write_knowledge`: docs via `EmbedDocumentBatchAsync`.
- `EmbeddingCompatibilityCheck`/dimension guard inalterados (dimensão é a mesma; só o vetor muda).

### RF-004 — Migração de corpus
- Ativar prefixo de documento muda todos os vetores indexados → exige reindex. `Embeddings:Asymmetric:Enabled` (default `false`); ao ligar, `IndexVersionToken` bump + warning no startup se embeddings existentes foram gerados com config diferente (persistir `embedding_config_fingerprint` na metadata do índice).

## 4. Requisitos Não-Funcionais

- Zero custo adicional de API — prefixo é texto, input_type é campo.
- Cache de query-embedding existente ganha sufixo `:q` na key (não colide com doc-embeddings).

## 5. Fora de escopo

- Matryoshka/dimension reduction, quantização de vetores.
- Troca de modelo de embedding (SPEC separada se necessário — o guard de dimensão já cobre).

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | Interface + defaults simétricos |
| T2 | Prefixos configuráveis + auto-detecção por modelo no Ollama provider |
| T3 | `input_type` no OpenAI-compatible provider |
| T4 | `SearchService`/`IngestionService` migram para os métodos por papel + cache key com papel |
| T5 | Fingerprint de config + warning de reindex |
| T6 | Testes: prefixo aplicado uma vez; query≠doc no mesmo texto; provider simétrico inalterado; cache separado |

## 7. Critérios de aceite

- [ ] Com `nomic-embed-text`, query sai com `search_query:` e doc com `search_document:` (verificável via fake HTTP).
- [ ] Provider sem suporte → vetores idênticos aos atuais.
- [ ] Ativar assimétrico loga warning de reindex quando fingerprint difere.
- [ ] Mesmo texto em papel query e documento produz cache entries distintas.
- [ ] Suite verde.

## 8. Riscos

- Ligar em corpus já indexado sem reindex → recall piora silenciosamente → mitigado pelo fingerprint/warning + doc no README.
- Auto-detecção por nome erra em modelos custom (`my-finetune`) → override manual sempre disponível.
