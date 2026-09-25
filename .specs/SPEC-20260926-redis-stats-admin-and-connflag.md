# SPEC — Fix: Cache card mostra "desconectado" com Redis saudável (AllowAdmin + semântica IsConnected)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-redis-stats-admin-and-connflag` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (CacheManagerService, DI), `KnowledgeHub.Shared` (CacheStatsDto), `KnowledgeHub.Client` (Settings cache card) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` — aprovada pelo owner 2026-09-26 |
| Ticket | BUG-cache-card-disconnected |
| Origem | report do usuário + reprodução com teste real (root cause confirmado) |

## 1. User Story

**As a** administrador olhando a aba Cache
**I want** que "conectado" reflita exclusivamente a conectividade Redis e que falhas de stats sejam exibidas separadamente
**So that** um Redis saudável não aparece como desconectado por causa de um comando de telemetria.

## 2. Contexto — root cause (reproduzido)

`CacheManagerService.EnrichFromRedisAsync` chama `server.InfoAsync("memory"|"clients")` — `IServer.Info*` exige `AllowAdmin=true` no `ConfigurationOptions` do SE.Redis. O multiplexer registrado em `KnowledgeHubServiceCollectionExtensions` (~linha 320) não seta `AllowAdmin` → `RedisCommandException: This operation is not available unless admin mode is enabled: INFO`.

Prova (teste real contra o Redis do host): `PING ok 0.7ms`, `Keys ok: 2`, `InfoAsync threw: RedisCommandException ... unless admin mode is enabled`. O catch de `EnrichFromRedisAsync` então seta `stats.IsConnected=false` e loga em **Debug** (invisível no nível Information) → UI exibe "desconectado" apesar de PING+SCAN+cache funcionarem (`client list` mostra a conexão SE.Redis ativa, db 3 com keys `emb:*`/`index:version`).

Bugs:

1. `AllowAdmin` ausente → INFO sempre falha (server stats nunca chegam).
2. Falha de enriquecimento (não-crítica) corrompe `IsConnected` — conectividade deveria ser decidida só pelo PING.
3. Erro real engolido em `LogDebug` — operador não vê a causa.

## 3. Requisitos Funcionais

### RF-001 — `AllowAdmin = true` no multiplexer compartilhado

- `KnowledgeHubServiceCollectionExtensions`: `parsed.AllowAdmin = true` no `IConnectionMultiplexer` (e espelhar no `ConfigurationOptions` do `RedisCache` por consistência — sem efeito funcional, mas evita surpresa futura).
- Comentário no código citando o motivo (INFO/SCAN stats).

### RF-002 — Semântica de `IsConnected`

- `IsConnected` = resultado exclusivo do PING (com timeout atual de 2s).
- Falhas posteriores (SCAN/INFO/qualquer enrichment) **não** alteram `IsConnected` — populam novo campo `StatsError` (`string?`) no `CacheStatsDto` com `ex.GetBaseException().Message` truncado (≤200 chars, sem stack).
- Catch do enrichment: `LogWarning` (não Debug), mensagem com `ex.GetBaseException().Message`.

### RF-003 — UI

- Card Cache: badge conectado/desconectado segue `IsConnected`.
- Quando `StatsError` presente: badge/alert secundário (`Warning`) "stats parciais: {StatsError}".
- Linha "servidor" do card exibe `—` nos campos quando `ServerReported=false` (já é o comportamento — manter).

### RF-004 — Fallback sem admin (defesa)

- Se `INFO` continuar falhando mesmo com `AllowAdmin` (ex.: Redis gerenciado com comandos renomeados), `ServerReported=false` + `StatsError` preenchido — nunca `IsConnected=false`. (Coberto por RF-002; RF-004 é só o critério explícito.)

## 4. Requisitos Não-Funcionais

- Endpoint `/api/settings/cache` não pode demorar mais que o ping timeout + SCAN cap atuais.
- Sem quebra de contrato: `StatsError` é campo aditivo (pinned contract tests atualizados).

## 5. Fora de Escopo

- `db.ExecuteAsync("INFO")` como alternativa a `AllowAdmin` — opção rejeitada: parsing manual do texto do INFO é frágil; `AllowAdmin` é o mecanismo oficial. Se um dia o Redis for managed-sem-admin, RF-004 cobre o degrade gracioso.
- Auto-retry/backoff — chamada única por request basta.

## 6. Plano de Tarefas

1. `parsed.AllowAdmin = true` nos dois pontos de DI (mux + RedisCache options).
2. `EnrichFromRedisAsync`: separar ping-try de enrichment-try; `StatsError`; `LogWarning`.
3. `CacheStatsDto.StatsError` + client card.
4. Testes: (a) unit — InfoAsync falhando → `IsConnected=true` + `StatsError` set (mock via interface ou refactor p/ isolar); (b) pinned contract test do DTO atualizado; (c) teste manual real contra o Redis do host (`PING`/`INFO` ok → conectado + ServerReported).
5. Build/testes verdes; redeploy opcional junto com a próxima entrega.

## 7. Critérios de Aceite

- [ ] Com Redis saudável, card mostra **conectado** + ServerKeys/memória/clientes populados.
- [ ] Com `INFO` indisponível (simular via mux sem AllowAdmin): `IsConnected=true`, `StatsError` preenchido, badge conectado + aviso parcial.
- [ ] Com Redis fora: `IsConnected=false`, badge desconectado.
- [ ] `docker logs` mostra o motivo do erro em `WRN` quando enrichment falha.
- [ ] Suites unit+integration verdes.
