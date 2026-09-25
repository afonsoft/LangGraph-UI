# SPEC — Coerência de cache: CacheTtlPolicy efetiva, locks podados, subscribe resiliente, clear remoto completo, degradado sem cache

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-cache-coherence-and-ttl` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (CacheTtlPolicy, L1L2Cache, RedisInvalidationBus, InvalidationSubscriber, CacheManagerService, SearchService, SafeCache, Program.cs) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | devin-ai-integration review — PRs #195 (3), #196 (1), #197 (4), #198 (3) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — cluster B (B1–B6) |

## 1. User Story

**As a** operador com Redis + múltiplas réplicas
**I want** que as TTL policies da wave 3 realmente apliquem e que invalidação distribuída cubra L1+L2
**So that** embeddings não expirem em 10min, o processo não acumule locks, e um clear valha de verdade em todos os nós.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| B1 | **`CacheTtlPolicy` nunca é resolvido** — `AddSingleton` é lazy e nenhum serviço injeta a classe → `Current` fica `null` → `SafeCache` aplica 10min default para TUDO (emb/search/ans/mcp:tool/rewrite/index). A feature de region TTL está efetivamente morta | `KnowledgeHubServiceCollectionExtensions.cs:313`; único consumidor é `SafeCache.cs:112` via `CacheTtlPolicy.Current` |
| B2 | `L1L2Cache._keyLocks` — `ConcurrentDictionary<string, SemaphoreSlim>` cresce sem bound: cada key nova de embedding cria um semáforo que nunca é removido | `L1L2Cache.cs:22,72` (`GetOrAdd` sem eviction) |
| B3 | `RedisInvalidationBus` ctor chama `sub.Subscribe` — Redis indisponível na construção lança exceção durante resolução do hosted service → risco de falha de startup | `ICacheInvalidationBus.cs:45` (subscribe no construtor) |
| B4 | `cache-clear` remoto só evita L1 das outras réplicas — keys que **elas** escreveram no Redis permanecem → próxima leitura repopula L1 com dado velho | `InvalidationSubscriber.OnReceived` (`InvalidateAllLocal` apenas); `CacheManagerService.ClearAllAsync` remove só keys rastreadas no nó de origem |
| B5 | Resultado degradado é cacheado — braço vetorial fail-soft retorna `[]`, o resultado fundido (FTS-only) entra no `search:` por até 5min como se fosse legítimo; índice recuperado não é reconsultado para aquela busca | `SearchService` (`SetJsonAsync` incondicional) + `VectorSearchAsync` catch→`[]` |
| B6 | Menores: `ServerKeys` conta o DB inteiro (`pattern:"*"`), não as keys do app; enumeração SCAN sem `ct`/timeout dedicado; `GetAsync` recarrega L1 com TTL cheio ignorando TTL restante no L2; `SetAsync` L2-first → Redis down impede até cache local | `CacheManagerService.cs:124-145`, `L1L2Cache.cs:36-58` |

## 3. Requisitos Funcionais

### RF-001 — `CacheTtlPolicy` efetiva desde o startup

- O singleton é resolvido explicitamente na inicialização (warm-up em `Program.cs` pós-`builder.Build()`, antes do `app.Run()`, ou mecanismo equivalente garantido por construção) — `CacheTtlPolicy.Current` nunca é `null` em runtime.
- Regressão: teste/hosted check provando que `SafeCache` resolve `emb:` → 1440min, `index:` → 10080min, `search:` → 5min (valores default da config), não 10min.

### RF-002 — Locks por key com bound

- `_keyLocks` substituído por **striped locks** de contagem fixa (ex.: 256 buckets, `locks[Hash(key) & 255]`) — sem crescimento por cardinalidade de key, sem lifecycle de semáforo.

### RF-003 — Subscribe resiliente no startup

- A inscrição no canal `kh:invalidate` não pode lançar na construção do hosted service.
- `RedisInvalidationBus`: subscribe com try/catch + reinscrição no evento `ConnectionRestored` do multiplexer (ou subscribe lazy no primeiro uso com retry) — Redis indisponível no boot degrada para Noop-com-log, não para crash.

### RF-004 — `cache-clear` remoto também limpa o L2 do próprio nó

- Ao receber `cache-clear`, cada réplica remove **suas próprias keys rastreadas** do L2 (além de compactar L1) — o clear administrativo vale para o cluster inteiro.
- Implementação sugerida: o subscriber delega a um método compartilhado no `CacheManagerService` (ex.: `ClearLocalTrackedAsync`) que itera `_trackedKeys` do nó chamando `RemoveAsync`, antes do `InvalidateAllLocal`.
- Fire-and-forget preservado: falha de remoção em uma réplica loga warning, não bloqueia as demais.

### RF-005 — Resultado degradado não é cacheado

- `SearchAsync` não persiste no `search:` cache um resultado produzido com braço vetorial degradado. Mecanismo: `ExecuteAsync`/pipeline sinaliza `degraded=true` (out-param, flag no resultado interno ou exceção tipada) → o `SetJsonAsync` é pulado; a resposta ao cliente permanece inalterada.

### RF-006 — Batch de honestidade do painel (menores)

- `ServerKeys` passa a ser rotulado como "total do servidor" **ou** filtra por prefixo das regiões do app — sem inflar a contagem do cache do app com keys alheias.
- Enumeração SCAN respeita `ct` e um timeout próprio — `/api/settings/cache` nunca bloqueia indefinidamente.
- `SetAsync`: falha de escrita no L2 loga warning e ainda popula L1 (degradação local honesta) — decisão registrada no código.
- L1 repopulado por leitura em voo após invalidação: aceito e documentado (bounded pelo `L1MaxTtlMinutes`); sem código novo.

## 4. RNFs

- Comportamento single-processo (provider=memory) inalterado — Noop bus continua Noop.
- Zero mudança de wire format do bus (topics existentes + payload `{instanceId}|{topic}`).
- Métricas de cache (`cache.hit`) e OTel spans intactos.

## 5. Fora de Escopo

- Invalidação por pattern/prefixo de key (só key exata e clear-all).
- Leitura do TTL restante real do Redis para L1 fill (tradeoff aceito; L1 cap basta).
- Confirm/ack de invalidação entre réplicas (fire-and-forget permanece).

## 6. Plano de Tarefas

1. Warm-up/resolução do `CacheTtlPolicy` + teste de região efetiva.
2. Striped locks em `L1L2Cache`.
3. Subscribe resiliente (`ConnectionRestored`/lazy) + teste Redis-down-no-boot.
4. `cache-clear` remoto → `ClearLocalTrackedAsync` + `InvalidateAllLocal`.
5. Flag `degraded` na pipeline de busca → skip `SetJsonAsync`.
6. Batch RF-006 (labels, ct no SCAN, L1-fill-on-L2-fail).
7. Testes: TTL por região ativa; striped lock funcional; boot sem Redis; clear propaga L1+L2; degradado não cacheia.
8. Suites completas + `dotnet format` + PR.

## 7. Critérios de Aceite

- [ ] Após startup, `SafeCache` aplica os TTLs por região configurados (não 10min genérico).
- [ ] 10k keys distintas não criam 10k semáforos (estrutura de lock tem tamanho fixo).
- [ ] App sobe healthy com Redis fora do ar no boot; ao reconectar, o subscribe funciona.
- [ ] `ClearAll` em réplica A remove também do Redis as keys escritas por réplica B.
- [ ] Busca com braço vetorial falho responde (degradado) e NÃO entra no cache `search:`.
- [ ] `ServerKeys` rotulado/filtrado honestamente; SCAN aborta por `ct`/timeout.
