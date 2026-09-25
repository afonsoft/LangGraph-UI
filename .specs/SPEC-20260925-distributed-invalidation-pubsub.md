# SPEC — Invalidação distribuída via Redis pub/sub

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-distributed-invalidation-pubsub` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `StackExchange.Redis` pub/sub, `IConnectionMultiplexer` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise de cache 2026-09-25 |

## 1. User Story

**As a** operador com mais de uma réplica (ou rolling deploy)
**I want** invalidação de cache propagada entre instâncias
**So that** um sync em um nó não deixe resultados stale em outro.

## 2. Contexto

`IndexVersionToken` invalida via a KEY no store compartilhado — leituras futuras
missam corretamente. Mas com o L1 da SPEC híbrida (ou o `MemoryCache` de
providers), uma réplica segue servindo stale até o TTL expirar. Pub/sub dá
propagação imediata: `kh:invalidate:indexVersion` → todos os nós bump local.

## 3. Requisitos Funcionais

- **RF-001** `ICacheInvalidationBus` — `Publish(topic, payload)` +
  `Subscribed` event; implementação Redis (`ISubscriber`) quando provider=redis,
  in-process no-op quando memory.
- **RF-002** Tópicos: `index-version` (bump), `cache-clear` (ClearAll),
  `settings-changed` (graph/chat/integrations alterados → drop entradas de
  config cacheadas no processo).
- **RF-003** Consumidores: `IndexVersionToken` re-lê do store ao receber bump;
  `CacheManagerService.ClearAll` limpa tracking local em todos os nós; services
  que seguram settings em memória (`GraphSettingsService`, `ChatSettingsService`)
  invalidam o cache local.
- **RF-004** Mensagens com `originInstanceId` — o nó publisher não precisa se
  re-invaliidar.

## 4. Requisitos Não-Funcionais

- Sem redis → tudo no-op (single replica, comportamento atual).
- Falha de subscribe/publish → warning, nunca erro de request.

## 5. Fora de Escopo

- Redis Streams / eventos duráveis — pub/sub fire-and-forget é suficiente
  (L1 stale expira pelo TTL curto mesmo se mensagem se perder).

## 6. Plano de Tarefas

1. Interface + Redis impl + no-op impl; DI por provider.
2. Integrar no bump de indexVersion e ClearAll.
3. Testes unitários (bus fake) + doc de deploy multi-réplica.

## 7. Acceptance Criteria

- [ ] Sync no nó A → nó B descarta L1 versionado imediatamente.
- [ ] ClearAll propaga.
- [ ] Sem redis → zero mudança.

## 8. Riscos

- Mensagem perdida ⇒ stale residual até TTL do L1 — aceitável e documentado.
