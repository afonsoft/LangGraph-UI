# SPEC — Coerência de runtime do provider de embeddings (Devin Review #203)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-embeddings-runtime-coherence` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (Embeddings resolver, IVectorStore, SearchService, CacheKeys, Settings) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | devin-ai-integration review PR #203 — 4 findings 🔴 + 1 analysis |
| Origem | [Devin Review PR #203](https://github.com/afonsoft/LangGraph-UI/pull/203) |

## 1. User Story

**As a** administrador que trocou o provider/modelo de embeddings via /settings
**I want** que busca, ingestão e caches acompanhem a mudança e que configs inválidas não travem a tela
**So that** não sirvo resultados de outro provider nem quebro a ingestão com vetores de dimensão incompatível.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| R1 | Dims novas quebram busca+ingestão | `KnowledgeHubServiceCollectionExtensions.cs:189-205` — `IVectorStore` scoped com `Embeddings:Dimensions` env; `SqliteVecVectorStore.cs:178-180` rejeita vetor com dims ≠ schema |
| R2 | ONNX inválido trava o GET | `OnnxEmbeddingProvider.Load` lança; `SettingsEndpoints` GET chama `resolver.Current` → 500; `Settings.razor` fica em "Carregando" sem botão de restore |
| R3 | Busca serve resultados do provider antigo | `SearchService.cs:53-75` — cache key leva `indexVersion`, não o fingerprint do provider; mudança de config não bumpa versão |
| R4 | Cache `emb:` reusa vetor de outro endpoint/key | `CacheKeys.Embedding(modelId, text)` — ModelId não cobre endpoint/key/input_type |
| R5 | Sessões ONNX nativas vazam a cada swap | `EmbeddingProviderResolver` substitui o provider sem dispose (`OnnxEmbeddingProvider` segura `InferenceSession`) |
| R6 | Limite overlap 2000 × maxTokens 4000 | consistente entre camadas — apenas confirmar/documentar |

## 3. Requisitos Funcionais

### RF-001 — Coordenação de dimensões com o vector store

- `IVectorStore` ganha `int? Dimensions` (null = irrestrito — `SqliteVectorStore` blob; `SqliteVecVectorStore`/`PostgresVectorStore` retornam a dim compilada no ctor).
- `GET /api/settings/embeddings` retorna `storeDimensions` + `dimsMismatch` (bool).
- `PUT /api/settings/embeddings` rejeita `dimensions != store.Dimensions` com 400: *"dims X != índice Y — altere `Embeddings:Dimensions` no ambiente e reinicie, depois rode reindex"*. Troca de provider/model mantendo dims permanece permitida (reindex necessário já avisado).
- UI: campo Dimensions mostra o valor do índice e alerta visual quando difere.

### RF-002 — GET fail-soft + validação ONNX no PUT

- GET nunca falha por provider quebrado: `try/catch` em `resolver.Current` → `stampedModelId=null` + novo campo `providerError` (digest curto).
- PUT com `provider=onnx` valida `Directory.Exists(modelPath)` + `model.onnx`/`vocab.txt` → 400 antes de persistir.
- UI renderiza formulário + "Restaurar ambiente" mesmo com `providerError` (badge de erro no header).

### RF-003 — Invalidação de busca/answer/tool na troca de provider

- `Save`/`Clear`/`RemoveKey` que altere a assinatura efetiva: bump `index:version` (mesmo mecanismo do ClearAll) + `ICacheInvalidationBus.PublishAsync("index-version")` → réplicas evitam token L1 (subscriber já trata).
- `EmbeddingSettingsService` injeta `ICacheInvalidationBus` + `IDistributedCache` (opcionais, noop default).

### RF-004 — Fingerprint nas keys `emb:`

- `IEmbeddingProviderResolver` expõe `string Fingerprint` (8 hex do hash da assinatura já calculada).
- `CacheKeys.Embedding(modelId, fingerprint, text)` → `emb:{modelId}:{fp}:{hash}`; callers (`SearchService.cs:605` e demais) passam `resolver.Fingerprint`.
- Entries antigas expiram naturalmente por TTL (sem varredura/evict).

### RF-005 — Dispose do provider anterior no swap

- `IEmbeddingProvider` implementations que seguram recursos nativos implementam `IDisposable`/`IAsyncDisposable` (`OnnxEmbeddingProvider` dispose `InferenceSession`); `AsymmetricEmbeddingProvider` encaminha o dispose ao inner.
- Resolver: dentro do lock, após publicar o novo provider, descarta o anterior via `Task.Run` com try/catch + LogWarning. Grace não-obrigatória (janela de ms documentada no comentário).

### RF-006 — Overlap (confirmar/documentar)

- Manter cap 2000 + regra `overlap < maxTokens`; adicionar comentário na validação (overlap >~40% degrada coerência dos chunks). Nenhuma mudança de comportamento.

## 4. RNFs

- GET `/embeddings` nunca retorna 5xx por config inválida persistida.
- Dispose é best-effort: nunca lança no caminho do swap.
- Fingerprint não expõe segredo (hash já é SHA-256).

## 5. Fora de Escopo

- Migração real de schema de índice (drop/recreate vec_chunks ao mudar dims) — o caminho documentado é env+restart+reindex.
- Eviction ativa de keys `emb:` antigas (orphan + TTL é suficiente).
- Test-connection de embeddings (pendente da SPEC anterior).

## 6. Plano de Tarefas

1. `IVectorStore.Dimensions` + GET `storeDimensions/dimsMismatch` + PUT block + UI warning.
2. GET try/catch + `providerError` + validação onnx path + UI error-state.
3. Bump `index:version` + publish bus em Save/Clear/RemoveKey.
4. `Fingerprint` no resolver + `CacheKeys.Embedding` + callers.
5. `IDisposable` em providers nativos + dispose no resolver.
6. Testes: PUT dims-block, GET com provider quebrado, fingerprint muda por signature, dispose chamado, bump index-version.
7. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] PUT dims≠store → 400 explicativo; GET expõe `dimsMismatch`.
- [ ] GET `/embeddings` retorna 200 com `providerError` quando o modelo não carrega; UI mostra botão de restore.
- [ ] Busca repetida após troca de provider reflete o novo provider (cache invalidado).
- [ ] Cache `emb:` não reusa vetores quando endpoint/key muda com mesmo ModelId.
- [ ] Swaps ONNX consecutivos não acumulam sessões nativas.
- [ ] Suites verdes + format.
