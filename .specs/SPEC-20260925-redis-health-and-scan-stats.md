# SPEC — Redis health check e stats reais via SCAN

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-redis-health-and-scan-stats` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `StackExchange.Redis`, health checks |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Origem | análise de cache 2026-09-25 |

## 1. User Story

**As a** operador com provider redis
**I want** `/health/ready` cobrindo a conexão Redis e stats de cache vindos do
servidor (não só do processo)
**So that** detecte Redis fora do ar e veja o estado real do cache compartilhado.

## 2. Contexto

`CacheManagerService` mantém keys/hits/misses **em memória do processo** — com
provider redis isso mostra apenas o que ESTE processo escreveu/foi buscando, e
zera no restart. `/health/ready` não testa a conexão Redis. `AbortOnConnectFail=false`
mascara indisponibilidade até a primeira operação.

## 3. Requisitos Funcionais

- **RF-001** `RedisHealthCheck` registrado quando `Cache:Provider=redis`:
  `PING` com timeout 2s → `Unhealthy`/`Degraded` conforme latência (>500ms →
  degraded). Tag `ready` — falha não deve matar readiness (cache é fail-soft):
  resultado `Degraded`, nunca `Unhealthy`.
- **RF-002** `CacheStatsDto` ganha contadores do servidor quando provider=redis:
  `Keys` via `SCAN` bounded (até 500 keys com prefixo `kh:*`), `used_memory`,
  `connected_clients` via `INFO` parseado — marcado `ServerReported=true`.
- **RF-003** GET `/api/settings/cache` continua <500ms: SCAN com cursor limit
  e `COUNT` 200; resultado marcado `Partial=true` quando truncado.
- **RF-004** Sem redis → comportamento atual intacto (stats do processo).

## 4. Requisitos Não-Funcionais

- SCAN nunca bloqueia (`COUNT` pequeno, sem KEYS *).
- Timeout agressivo — o endpoint não pode travar com Redis lento.

## 5. Fora de Escopo

- Eviction policy tuning do Redis (documentação apenas).
- Pub/sub → spec separada.

## 6. Plano de Tarefas

1. `IConnectionMultiplexer` singleton quando provider=redis + health check.
2. `CacheManagerService` branch redis: SCAN/INFO no GetStatsAsync.
3. Testes de unidade com multiplexer fake; integration opcional (skip se sem redis).

## 7. Acceptance Criteria

- [ ] Redis derrubado → `/health/ready` degraded + UI mostra `desconectado`.
- [ ] Stats do servidor visíveis na aba Cache (diferenciais de `ServerReported`).
- [ ] Sem redis → comportamento atual.

## 8. Riscos

- `INFO`/`SCAN` pesados em instâncias enormes — mitigado por COUNT/limit e
  cache de stats por 30s.
