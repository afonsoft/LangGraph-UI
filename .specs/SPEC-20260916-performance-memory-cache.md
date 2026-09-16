# SPEC-20260916-performance-memory-cache

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `performance-memory-cache` |
| Type | `Improvement` (performance / memory / optional Redis cache) |
| Stack | `.NET 10 / C# 14` + EF Core 10 + SQLite (+ pgvector opcional) + `IMemoryCache` / `IDistributedCache` (Redis opt-in) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-performance-cache` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` — infra Redis (RF-005 parcial: `CacheOptions`, `IDistributedCache` memory|redis, validação, compose/`.env` apontando db3 do VPS) entregue; T1–T4, T6, T7 pendentes |
| Depends on | n/a (tocar em código existente sem quebrar contratos) |

## 1. User Story

Como operador da plataforma, quero que buscas e chamadas de tools consumam menos memória e latência conforme o corpus cresce, e que exista uma camada de cache opcional (Redis) para deploys com múltiplas réplicas — sem alterar comportamento funcional nem adicionar infraestrutura obrigatória ao deploy single-process padrão.

## 2. Discovery — hotspots identificados (evidência)

| # | Local | Problema | Severidade |
|---|-------|----------|-----------|
| H1 | `src/KnowledgeHub.Server/VectorStore/SqliteVectorStore.cs:35-56` | `SearchAsync` materializa **todos** os BLOBs de embedding do modelo em memória (`ToListAsync`), decodifica cada vetor e calcula cosseno in-process. Custo O(N·dims·4B) por query — ex.: 10k chunks × 1024 dims ≈ 40MB alocados por busca. `PostgresVectorStore` já faz `<=>` server-side; o caminho SQLite é o gargalo. | **Alta** |
| H2 | `src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs` | Catálogo agregado reconstruído em **todo** `tools/list`, `tools/call` e cada iteração do loop do agente (`AgentService.cs:352`). `SourceQueryToolsProvider` faz query no SQLite a cada rebuild. Já existe `IToolCatalogChangeNotifier` para invalidação — falta o cache. | **Alta** |
| H3 | `src/KnowledgeHub.Server/Settings/IntegrationSecretStore.cs` | `GetAsync` cria scope + query EF + decrypt Data Protection por resolução de credencial — até 2× por chamada upstream (HasApiKey + resolução). | Média |
| H4 | `src/KnowledgeHub.Server/Ingestion/IngestionService.cs` | (a) `.Include(d => d.Chunks)` carrega todos os chunks + `TextContent` + BLOBs do source inteiro de uma vez; (b) `EmbedChunksAsync` é sequencial — 1 HTTP de embedding + 1 `FindAsync` + 1 `SaveChanges` por chunk (N round-trips); (c) `IEmbeddingProvider.EmbedBatchAsync` existe mas **não é usado**; (d) `SourceLocks` nunca remove semáforos (menor — limitado por nº de sources). | Média |
| H5 | `src/KnowledgeHub.Server/Search/LexicalSearchService.cs` | `IsAvailableAsync` consulta `sqlite_master` em **toda** busca — flag cacheável por processo (FTS5 é criado na migration, não muda em runtime). | Baixa |
| H6 | `KnowledgeHubServiceCollectionExtensions.cs:26` | `AddDbContext` sem pooling — `AddDbContextPool` é troca trivial com ganho em todo request EF. | Baixa |
| H7 | `src/KnowledgeHub.Server/Services/AgentService.cs` | `Channel.CreateUnbounded<SseEvent>` pode crescer sob cliente lento; schemas de tools serializados por iteração. | Baixa |

## 3. Análise Redis — onde faz e onde não faz sentido

A aplicação é **single-process standalone** (SQLite por padrão, pgvector opt-in, um container). Redis só se paga quando há ≥2 réplicas ou necessidade de cache sobreviver a restart.

**Faz sentido (via `IDistributedCache`, opt-in):**
- Resultados de busca idênticos (query+mode+topK+sources hash) — invalidável por versão de índice.
- Embeddings de query (texto→vetor, keyed por `modelId:sha256(text)`) — determinísticos, seguros.
- Metadados serializáveis do catálogo de tools (nomes/schemas — **não** `CatalogTool` inteiro, pois `Handler` é delegate não-serializável).

**NÃO faz sentido:**
- Secrets de integração — nunca fora do processo/DB criptografado. Cache apenas `IMemoryCache` de TTL curto.
- Vetores de chunks como "índice" — Redis não é vector store aqui; o fix correto de H1 é pgvector ou windowing, não despejar BLOBs no Redis.
- Delegates/handlers de runtime.

**Arquitetura proposta:** `Cache:Provider = memory|redis` — `memory` (default, zero-infra) usa `AddDistributedMemoryCache`; `redis` usa `AddStackExchangeRedisCache` com `Cache:Redis:ConnectionString`. Mesma abstração `IDistributedCache`, sem branch de código. L1 `IMemoryCache` permanece para hot paths de processo único.

## 4. Requirements

### Funcionais

- **RF-001 — Cache de catálogo de tools:** `DynamicToolCatalog` memoriza o agregado por processo e invalida via `IToolCatalogChangeNotifier` + mudanças de config upstream. Fontes ativas do `SourceQueryToolsProvider` cacheadas com a mesma invalidação.
- **RF-002 — Busca vetorial SQLite sem materialização total:** reduzir o pico de alocação de `SqliteVectorStore.SearchAsync` — streaming de rows (`AsAsyncEnumerable`), early-filter por `sourceIds`, e candidate-window via projeção mínima. Alternativa documentada: recomendar pgvector acima de threshold configurável.
- **RF-003 — Cache de resolução de secrets:** `IMemoryCache` com TTL curto (≤60s) + invalidação explícita em `Set`/`Delete`. Nunca distribuído.
- **RF-004 — Ingestão em lote:** usar `EmbedBatchAsync`, agrupar `SaveChanges`, e carregar chunks sob demanda (projeção sem BLOBs no `Include` inicial).
- **RF-005 — `IDistributedCache` opt-in:** provider `memory`/`redis` configurável; usado para resultados de busca e embeddings de query com TTL e key-versioning.
- **RF-006 — Micro-fixes:** `AddDbContextPool`, flag FTS5 cacheada, `SemaphoreSlim` cleanup.

### Não-funcionais

- **RNF-001 Compatibilidade:** zero mudança de contrato/behavior; `Cache:Provider=memory` default preserva deploy single-process.
- **RNF-002 Segurança:** secrets nunca em `IDistributedCache`/Redis; keys de cache sem dados sensíveis.
- **RNF-003 Degradação:** Redis indisponível → fallback para memory + warning, nunca falha de request.
- **RNF-004 Mensuração:** toda otimização medida — BenchmarkDotNet ou A/B com `dotnet-counters`/GC antes/depois.

## 5. Cache Keys / TTLs (proposta)

| Entrada | Store | Key | TTL | Invalidação |
|---------|-------|-----|-----|-------------|
| Catálogo agregado | `IMemoryCache` | `catalog:v1` | ∞ | `IToolCatalogChangeNotifier` + `ResetAsync` upstreams |
| Sources ativas (provider) | `IMemoryCache` | `sources:active:v1` | ∞ | notifier |
| Secret resolvido | `IMemoryCache` | `secret:{provider}` | 60s | Set/Delete no store |
| Embedding de query | `IDistributedCache` | `emb:{modelId}:{sha256(text)}` | 24h | modelo muda → key muda |
| Resultado de busca | `IDistributedCache` | `search:{mode}:{topK}:{srcHash}:{qHash}` | 5min | `indexVersion` bump no sync |
| Flag FTS5 | static/`IMemoryCache` | processo | ∞ | — |

## 6. Acceptance Criteria

- **CA-001:** busca híbrida em corpus de N chunks não aloca mais que O(window·dims) — medido com `dotnet-counters`/`GC.GetAllocatedBytesForCurrentThread` antes/depois.
- **CA-002:** `tools/list` consecutivos não re-executam queries de sources nem `tools/list` upstream (cache hit observável em log/telemetria).
- **CA-003:** sync de source com K chunks faz ≤⌈K/batch⌉ chamadas de embedding e ≤2 `SaveChanges` por documento alterado.
- **CA-004:** `Cache:Provider=redis` com Redis fora do ar → app funciona em memory-fallback (teste de integração).
- **CA-005:** suíte completa verde (unit + integration) e `dotnet format --verify-no-changes`.

## 7. Task Plan

| # | Tarefa | Arquivos principais |
|---|--------|---------------------|
| T1 | Cache in-process do catálogo + invalidação via notifier | `DynamicToolCatalog.cs`, `SourceQueryToolsProvider.cs`, `ToolCatalogChangeNotifier.cs` |
| T2 | Otimizar `SqliteVectorStore.SearchAsync` (streaming + windowing) | `SqliteVectorStore.cs` |
| T3 | Cache de secrets (L1, TTL, invalidação) | `IntegrationSecretStore.cs` |
| T4 | Ingestão: `EmbedBatchAsync`, batched saves, projeção sem BLOB | `IngestionService.cs` |
| T5 | `CacheOptions` + `IDistributedCache` (memory/redis) + cache de busca/embeddings | `KnowledgeHubServiceCollectionExtensions.cs`, `SearchService.cs`, `docker-compose.yml` (redis service opcional) |
| T6 | Micro-fixes: DbContextPool, FTS flag, semaphore cleanup | DI ext, `LexicalSearchService.cs`, `IngestionService.cs` |
| T7 | Benchmarks/evidências + testes de fallback Redis | `tests/` |

## 8. Organization Guardrails

- Não introduzir Redis como dependência obrigatória — opt-in por config.
- Não alterar contratos MCP/REST nem nomes de tools.
- Secrets nunca em cache distribuído.
- Cada item do plano entrega com medição antes/depois — sem claims de melhoria sem número.

## 9. Definition of Done

- [ ] Catálogo cacheado com invalidação correta (teste: mudança de source propaga).
- [ ] Pico de memória da busca SQLite reduzido e medido.
- [ ] Ingestão em lote implementada.
- [ ] `Cache:Provider` funcional (memory default; redis opt-in com fallback).
- [ ] Build + testes + format verdes; evidências de benchmark anexadas à SPEC.

## Open Questions / Pending Ambiguity

- Threshold para recomendar pgvector vs. otimizar SQLite path (proposta inicial: documentar ~50k chunks).
- `docker-compose.yml`: adicionar serviço `redis` comentado/opt-in ou perfil compose separado?
- BenchmarkDotNet como projeto separado em `tests/` ou medição via `dotnet-counters` na instância local?
