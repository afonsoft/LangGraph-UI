# SPEC — Settings UX polish: tabs estilo terminal, badge spacing, cache per-key clear, provider de embeddings editável

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-settings-ux-embeddings` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Client` (Settings.razor, app.css), `KnowledgeHub.Server` (Settings endpoints, Embeddings, Caching), `KnowledgeHub.Shared` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | UX-requests 2026-09-26 (owner) |
| Origem | pedido direto do usuário |

## 1. User Story

**As a** administrador na página Settings
**I want** abas com visual de terminal, badges legíveis, gerenciamento granular de keys de cache e um card de provider de embeddings/indexação editável
**So that** a página fica polida e eu consigo trocar para um provider de embedding mais rápido sem editar env vars.

## 2. Contexto

- `Tab` do BootstrapBlazor suporta `TabStyle` (`Chrome`/`Capsule`) — estilo "browser/terminal" disponível nativamente.
- `.badge` global não tem respiro à esquerda — cola no texto/elemento anterior em todas as telas.
- Cache card lista keys rastreadas num `<details>` sem ações — `ICacheManagerService` já tem `RemoveKey` (tracked set) e `_cache.RemoveAsync`, falta só endpoint + UI.
- `Embeddings:*` hoje é env-only; o padrão `ChatSettingsService` (single-row `Id=1`, snapshot, secret no `IntegrationSecretStore`, aplica sem restart) já existe como blueprint. Consumers de `IEmbeddingProvider` (7 sites) recebem singleton fixo — para aplicar mudança de provider/model sem restart precisa de resolver por assinatura de config.

## 3. Requisitos Funcionais

### RF-001 — Abas estilo terminal

- `<Tab TabStyle="TabStyle.Chrome">` em Settings; `Icon` por aba (fa-solid): chat→`fa-comments`, graph→`fa-diagram-project`, integrações→`fa-plug`, cache→`fa-bolt`, banco→`fa-database`.
- Verificar que `TabStyle` existe na versão do pacote (confirmado: `Chrome`, `Capsule` no enum).

### RF-002 — Badge spacing

- `app.css`: `.badge { margin-left: .35rem; }` (respiro à esquerda em todos os usos). Sem quebrar badges que já possuem margin própria.

### RF-003 — Cache: painel "Keys salvas" + clear por key

- `ICacheManagerService.RemoveEntryAsync(string key, ct)` — remove a key real do `IDistributedCache` (L1+L2 via `L1L2Cache`) **e** do tracked set; retorna `bool found`.
- `DELETE /api/settings/cache/keys/{*key}` (mesma auth/policy do grupo).
- UI Cache: card "Keys salvas" abaixo do card de provider — lista Key/size/ttl com `PopConfirmButton` lixeira por linha (cap 1000 já existente); reload após delete.

### RF-004 — Card "Embeddings (indexação)" na aba Chat (LLM)

Backend:

- Entidade `EmbeddingSettings` (single-row `Id=1`): `Provider`, `Endpoint`, `Model`, `Dimensions`, `ModelPath`, `MaxTokens`, `OverlapTokens`, `UpdatedAt` + migration EF.
- `IEmbeddingSettingsService` (blueprint `ChatSettingsService`): `GetEffectiveOptions()` store-over-env, `GetState()` mascarado (`source`: store|env|none, `hasApiKey`, `apiKeyHint`), `SaveAsync`, `ClearAsync`, `Invalidate()`. ApiKey no `IntegrationSecretStore` slug `embeddings`.
- `GET/PUT/DELETE /api/settings/embeddings` — GET retorna estado mascarado + seletores; PUT valida provider ∈ {deterministic,ollama,openai,onnx}, dims 64–4096, maxTokens 100–4000, overlap < maxTokens; DELETE remove override (env volta a valer).
- `IEmbeddingProviderResolver` (`Current` property): cache de provider por assinatura de options (provider|endpoint|model|dims|modelPath|hash da key) — singleton; consumers trocam `IEmbeddingProvider` injetado por `resolver.Current` (SemanticTextChunker/ChunkerSelector, IngestionService, SearchService, KnowledgeHubHealthChecks, Program.cs, KnowledgeToolsProvider, registros dos vector stores). Mudança de assinatura → novo provider construído lazily + `LogWarning` "embedding provider changed — corpora antigos podem precisar de reindex".
- `Ingestion:MaxTokens`/`OverlapTokens`: `IngestionService` passa a ler dos effective embedding/index settings (store-over-env, mesmos defaults 500/50).

UI (aba Chat, card separado abaixo do card de chat):

- Select provider (deterministic/ollama/openai/onnx), endpoint, model, modelPath (visível só em onnx), api key mascarada, dimensions com aviso **"mudança de model/dims exige reindex — vetores antigos ficam incompatíveis"**, MaxTokens + OverlapTokens (chunking), badge de source (store|ambiente), Salvar + Restaurar ambiente.
- Aviso persistente quando `dimensions != storedEmbeddingDims` conhecido — informativo apenas (guard real fica no startup/migrations).

## 4. Requisitos Não-Funcionais

- Resolver não recria provider a cada chamada — cache por assinatura (ConcurrentDictionary<string,IEmbeddingProvider>, sem eviction além de último N).
- Secret nunca retorna ao client (só hint last-4).
- `PUT` idempotente; `DELETE` → 204 mesmo sem override.
- Contrato aditivo — sem quebra nos endpoints existentes.

## 5. Fora de Escopo

- Test-connection de embeddings (futuro — mesmo padrão do chat test quando pedido).
- Reindex automático ao trocar de model/dims (operador dispara manualmente via `/reindex`).
- Per-API-key embeddings override (chat já tem; embeddings fica global nesta SPEC).

## 6. Plano de Tarefas

1. `TabStyle.Chrome` + icons; `.badge` css.
2. `RemoveEntryAsync` + endpoint delete-key + painel keys.
3. `EmbeddingSettings` entity + migration (`AddEmbeddingSettings`).
4. `IEmbeddingSettingsService` impl + DTOs + endpoints + secret store.
5. `IEmbeddingProviderResolver` + swap nos 7 consumers + MaxTokens/OverlapTokens do store.
6. Card UI na aba Chat.
7. Testes: unit (resolver cache/signature, settings service snapshot, digest sem regressão), integration (GET/PUT/DELETE embeddings endpoints, delete cache key), suite verde + format.

## 7. Critérios de Aceite

- [ ] `/settings` mostra 5 abas em estilo Chrome com ícones.
- [ ] Badges com respiro à esquerda em todas as telas (diff visual).
- [ ] Card "Keys salvas" lista keys com delete individual que remove a key do cache (memory e redis).
- [ ] `GET/PUT/DELETE /api/settings/embeddings` funcionais; provider/model/dims editáveis aplicam sem restart via resolver (warning no log); key mascarada.
- [ ] MaxTokens/OverlapTokens do store alimentam o chunker.
- [ ] Suites unit+integration verdes; `dotnet format` limpo; PR via branch feature.
