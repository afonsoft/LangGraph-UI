# SPEC — Consistência do cache por key: replicação de delete, honestidade de falha, ServerReported, log sanitization

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-cache-key-consistency` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (CacheManagerService, InvalidationSubscriber, L1L2Cache, SettingsEndpoints) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | devin-ai-integration review PR #203 (2 findings 🟡) + PR #200/#201 (ServerReported) + CodeQL log-forging |
| Origem | [Devin Review PR #203](https://github.com/afonsoft/LangGraph-UI/pull/203) |

## 1. User Story

**As a** administrador removendo uma key de cache
**I want** que a remoção valha em todas as réplicas e que falhas sejam reportadas como erro
**So that** o painel não minta e réplicas não sirvam dados obsoletos.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| C1 | Delete remove só L1 local + Redis — L1 de outras réplicas sobrevive ao TTL | `CacheManagerService.RemoveEntryAsync` + `InvalidationSubscriber` (só topics `index-version`/`cache-clear`) |
| C2 | Falha no delete → `NoContent` + untrack — key some do painel mas persiste | `RemoveEntryAsync` catch swallow + untrack incondicional; endpoint sempre 204/404 |
| C3 | `ServerReported=true` mesmo quando INFO falha | `EnrichFromRedisAsync` seta após SCAN, antes do INFO — review #200/#201: SPEC exigia false quando stats indisponíveis |
| C4 | CodeQL log-forging: `key`/`provider` user-controlled em LogWarning | `CacheManagerService.cs:179`, `EmbeddingSettingsService.cs:156` — strip newlines antes de logar |

## 3. Requisitos Funcionais

### RF-001 — Invalidação por key entre réplicas

- Novo bus topic `cache-key:{key}` (formato prefixado — assinatura `PublishAsync(string topic)` preservada).
- `RemoveEntryAsync` publica o topic após remoção bem-sucedida.
- `InvalidationSubscriber`: `case var t when t.StartsWith("cache-key:")` → `l1.InvalidateLocal(key)` (Remove no L1 só; L2 Redis já foi removido no publisher).

### RF-002 — Honestidade na falha de exclusão

- `RemoveEntryAsync` retorna resultado `{ bool Tracked, bool Removed, string? Error }` (record `CacheKeyRemovalResult`).
- Untrack **somente** quando `Removed` (ou quando a key não existia no backend — verificar existência prévia se barato; senão: falha no remove mantém tracking).
- Endpoint: removed → 204; !tracked → 404; error → 502 `ProblemDetails` com digest curto (estilo `ExceptionDigest`).

### RF-003 — Semântica de `ServerReported`

- `ServerReported` só `true` quando **INFO** responder (stats de servidor completos).
- SCAN parcial continua populando `ServerKeys`/`Partial`; falha de INFO segue em `StatsError` sem rebaixar `IsConnected`.

### RF-004 — Log sanitization (CodeQL)

- Helper `LogSafe(string)` (strip `\r`/`\n`, cap 200) aplicado a `key`/`provider`/`model` user-controlled nos novos logs e nos dois pontos flagrados.
- CodeQL "useless upcast" em `VectorStoreDiagnostics.cs` (L28/L30) e `EmbeddingSettingsService.cs` (L106): remover casts implícitos.

## 4. RNFs

- Bus topic retrocompatível — subscribers antigos ignoram prefixo desconhecido.
- Nenhuma regressão no fail-soft do painel de cache.

## 5. Fora de Escopo

- Invalidação por pattern/prefixo (só key exata).
- Confirm de delete de réplicas (fire-and-forget como o resto do bus).

## 6. Plano de Tarefas

1. Topic `cache-key:` + publish + subscriber.
2. `CacheKeyRemovalResult` + endpoint honesto + untrack condicional.
3. `ServerReported` após INFO.
4. `LogSafe` + fixes CodeQL upcast.
5. Testes: replica-behavior simulado (L1L2Cache + subscriber), falha de remove → 502 + tracked, ServerReported sem INFO.
6. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] Delete por key propaga para L1 de outras réplicas via bus.
- [ ] Falha de remove retorna erro e mantém key no painel.
- [ ] `ServerReported=false` quando INFO indisponível, `StatsError` preenchido.
- [ ] Zero ocorrências de log-forging nos valores user-controlled citados.
