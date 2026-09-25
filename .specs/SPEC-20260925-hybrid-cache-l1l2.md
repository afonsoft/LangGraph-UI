# SPEC — HybridCache L1+L2 (in-proc + distributed)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-hybrid-cache-l1l2` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `.NET 10`, `Microsoft.Extensions.Caching.Hybrid`, `IDistributedCache` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Origem | análise de cache 2026-09-25 |

## 1. User Story

**As a** operador com `Cache:Provider=redis`
**I want** um tier L1 in-process na frente do Redis com coalescência de leituras
**So that** hits repetidos não paguem RTT de rede e rajadas concorrentes da mesma
key não recomputem N vezes.

## 2. Contexto

Hoje todo read passa por `IDistributedCache` (memory = process-local ou redis =
RTT por hit). `SafeCache` dá fail-soft mas não single-flight: 50 requests
concorrentes da mesma query fazem 50 lookups + 50 recomputes. O pacote
`Microsoft.Extensions.Caching.Hybrid` (GA, .NET 9+) entrega exatamente isto:
L1 memória + L2 distribuído + stampede protection + serialização System.Text.Json.

## 3. Requisitos Funcionais

- **RF-001** Registrar `HybridCache` quando `Cache:Hybrid:Enabled=true` (default
  false — opt-in): L1 `MemoryCache` + L2 `IDistributedCache` existente.
- **RF-002** Pontos de leitura de alto volume migram para `GetOrCreateAsync`:
  resultados de busca (`SearchService`), respostas (`AnswerService`), tool
  results (`ToolCacheService`), embeddings.
- **RF-003** TTL por entrada via `HybridCacheEntryOptions` — herda as policies
  de `SPEC-20260925-cache-region-ttl-policies` quando presentes; fallback nos
  TTLs atuais.
- **RF-004** Manter fail-soft: L2 indisponível ⇒ L1 ainda serve; falha total ⇒
  miss. Preservar métricas `cache_hits/misses` por região.
- **RF-005** `CacheManagerService` continua autoridade do stats/keys —
  `TrackKey` chamado nas escritas do HybridCache via `IBufferingHybridCache`/
  wrapper.

## 4. Requisitos Não-Funcionais

- Zero mudança de comportamento quando desligado.
- L1 TTL ≤ L2 TTL; L1 max 60s para keys dependentes de `indexVersion`.
- Sem dependência de serializador externo — System.Text.Json nativo do pacote.

## 5. Fora de Escopo

- Invalidação pub/sub cross-replica → `SPEC-20260925-distributed-invalidation-pubsub`.
- Mudança de provider memory|redis.

## 6. Plano de Tarefas

1. `dotnet add Microsoft.Extensions.Caching.Hybrid` + wiring condicional.
2. Adapter `IHybridResultCache` fino sobre `HybridCache` com o mesmo contrato
   `SafeCache` (fail-soft + métricas + tracking).
3. Migrar call sites (search/answer/tool/embedding).
4. Testes: stampede (N concorrentes → 1 compute), L2-down → L1 serve, métricas.

## 7. Acceptance Criteria

- [ ] `Cache:Hybrid:Enabled=true` → hits repetidos não saem do processo.
- [ ] 20 chamadas concorrentes idênticas → 1 execução upstream.
- [ ] Com Redis down, comportamento degrada para L1-only sem erro visível.
- [ ] Métricas de hit/miss por região preservadas.

## 8. Riscos

- HybridCache L1 e IndexVersionToken: L1 pode servir stale por até o TTL do L1 —
  mitigado pelo TTL curto de L1 em regiões versionadas.
- `HybridCache` serializa via System.Text.Json — payloads custom (CallToolResult)
  precisam ser redondos; testar round-trip.
