# SPEC-20260915-apikey-usage-audit

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `apikey-usage-audit` |
| Type | `Feature` |
| Stack | `.NET 10 (ASP.NET Core + EF Core SQLite + Blazor WASM)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Devin-20260915-apikey-usage-audit` |
| Ticket | — |
| Status | `Done` |

## 1. User Story

**As a** administrador do KnowledgeHub
**I want** ver os detalhes de cada API key e um resumo de uso com auditoria das chamadas feitas por ela
**So that** eu saiba qual client está usando qual chave, detecte uso anômalo e possa revogar com segurança.

**Problem context:**
Hoje `/api-keys` mostra apenas prefixo, datas e status. Não há nenhuma auditoria de chamadas autenticadas via `Bearer aft_*` — apenas `LastUsedAt` (throttled a 1 escrita/min). Não é possível saber quantas chamadas uma chave fez, quais endpoints ela bateu, latência ou erros. O segredo completo é irrecuperável por design (só SHA-256 é persistido) — "visualizar a chave" significa um dialog de detalhes com o prefixo copiável.

## 2. Scope

**In scope:**
- Novo claim `key_id` no principal de `ApiKeyAuthenticationHandler` para correlacionar chamadas à chave.
- Middleware de auditoria que persiste um evento por request autenticada via API key (`/mcp`, `/api/*`, `/hubs/mcp` e qualquer outra rota `auth_method=apikey`).
- Nova entidade `ApiKeyUsageEvent` + migration EF Core.
- Endpoint `GET /api/apikeys/{id}/usage` (cookie session) com agregados + eventos recentes.
- UI em `/api-keys`: ícone de detalhes/métricas por linha abrindo dialog com cards de resumo + tabela de auditoria.
- Retenção: 90 dias OU máx. 10.000 eventos por chave (o que expirar primeiro), prune no insert.

**Out of scope:**
- Corpo de request/response (tamanho + privacidade).
- Métricas de sessões cookie (a auditoria é por chave de API).
- Dashboards gráficos/charts; export CSV; alertas.
- Alterar o esquema de armazenamento do segredo (re-exibição do segredo é impossível por design e permanece assim).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs` — adiciona claim `key_id`.
- `src/KnowledgeHub.Server/` — novo middleware `ApiKeyUsageMiddleware` registrado após `UseAuthentication`/`UseAuthorization` (precisa do principal autenticado); registra evento quando `auth_method == "apikey"`.
- `src/KnowledgeHub.Server/Domain/Entities/ApiKeyUsageEvent.cs` — nova entidade.
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` — `DbSet<ApiKeyUsageEvent>` + configuração.
- `src/KnowledgeHub.Server/Migrations/` — nova migration.
- `src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs` — endpoint de usage.
- `src/KnowledgeHub.Shared/Contracts/AuthDtos.cs` — `ApiKeyUsageEventDto`, `ApiKeyUsageDto`.
- `src/KnowledgeHub.Client/Services/AuthApiClient.cs` — `GetKeyUsageAsync`.
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` — ícone + dialog.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs` (claims)
- `src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs` (padrão de endpoints + CurrentUserId)
- `src/KnowledgeHub.Server/Auth/AuthPolicies.cs` (`CookieSession`)
- `src/KnowledgeHub.Server/Data/DatabaseMigrator.cs` (convenção de migrations)
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` (UI atual)
- `tests/KnowledgeHub.Tests.Integration/AuthFlowTests.cs` (padrão de testes de auth)

## 4. Requirements

### RF-001: Claim de identificação da chave
- **Description:** `ApiKeyAuthenticationHandler` deve emitir claim `key_id` = `ApiKey.Id` no `ClaimsIdentity`.
- **Rules:** claim adicional, sem alterar claims existentes.
- **Input → Output:** request com `Bearer aft_*` válido → principal contém `key_id`.

### RF-002: Persistência de evento de uso
- **Description:** Middleware registra `ApiKeyUsageEvent` para toda request cujo principal tem `auth_method == "apikey"`, após a execução do pipeline (status conhecido).
- **Rules:** campos: `Id (Guid)`, `ApiKeyId (Guid)`, `Timestamp`, `HttpMethod`, `Path` (máx. 256 chars, truncado), `StatusCode (int)`, `DurationMs (double)`, `UserAgent` (máx. 200 chars, truncado, nullable). Falha na gravação NÃO pode derrubar a request (try/catch + log warning). Não bloquear o response: registrar após `next()` com `await` simples (volume é baixo — write per request aceitável, consistente com o throttle atual de LastUsedAt que passa a ser redundante mas permanece).
- **Input → Output:** request autenticada por chave → 1 linha em `ApiKeyUsageEvents`.

### RF-003: Migration EF Core
- **Description:** `dotnet ef migrations add` criando tabela `ApiKeyUsageEvents` com índice em `(ApiKeyId, Timestamp)` e FK para `ApiKeys` (cascade delete).
- **Rules:** seguir `DatabaseMigrator`/`Migrations` existentes; aplicada no startup via `MigrateAsync` já existente.

### RF-004: Endpoint de usage
- **Description:** `GET /api/apikeys/{id}/usage` — cookie session apenas (mesmo `RequireAuthorization(AuthPolicies.CookieSession)` do grupo). Dono-only: 404 se a chave não pertencer ao usuário.
- **Rules:** retorna `ApiKeyUsageDto { TotalCalls, CallsLast24h, CallsLast7d, AvgDurationMs, ErrorCount, ErrorRate, RecentEvents: ApiKeyUsageEventDto[100] }`. `ErrorCount` = status >= 400. `ApiKeyUsageEventDto(Id, Timestamp, HttpMethod, Path, StatusCode, DurationMs, UserAgent)`.
- **Input → Output:** `GET /api/apikeys/{id}/usage` → 200 + `ApiKeyUsageDto` | 404.

### RF-005: Retenção
- **Description:** Ao inserir evento, prune: remover eventos da chave com `Timestamp < utcnow - 90d` e manter no máx. 10.000 por chave (remover os mais antigos excedentes).
- **Rules:** prune no mesmo `SaveChanges` do insert; custo limitado por índice `(ApiKeyId, Timestamp)`.

### RF-006: UI — detalhes + métricas
- **Description:** Na tabela de `/api-keys`, coluna Ações ganha ícone `fa-solid fa-eye`/`fa-chart-line` por linha abrindo `Modal` "Detalhes da chave" contendo:
  - Info: nome, prefixo `<code>aft_xxxx…</code>` com botão copiar, criada em, último uso, status, id;
  - Aviso: "o segredo completo só é exibido na criação";
  - Cards de resumo: total de chamadas, últimas 24h, últimas 7d, duração média, taxa de erro;
  - Tabela de auditoria: hora, método, path, status (badge ok/erro), ms, user-agent — últimos 100 eventos.
- **Rules:** loading state no dialog; erro na API → toast; seguir padrões do `ApiKeys.razor` (Modal/ModalDialog, `ToastService`, `AuthApiClient`). Chave revogada continua exibindo histórico.
- **Input → Output:** clique no ícone → dialog com dados de `GET /api/apikeys/{id}/usage`.

## 5. API Contract

**Endpoint:** `GET /api/apikeys/{id:guid}/usage`
**Auth:** Cookie session (`AuthPolicies.CookieSession`) — API keys não podem consultar auditoria de API keys.

**Response 200:**
```json
{
  "totalCalls": 1234,
  "callsLast24h": 42,
  "callsLast7d": 380,
  "avgDurationMs": 87.4,
  "errorCount": 12,
  "errorRate": 0.0097,
  "recentEvents": [
    {
      "id": "guid",
      "timestamp": "2026-09-15T18:00:00Z",
      "httpMethod": "POST",
      "path": "/mcp",
      "statusCode": 200,
      "durationMs": 45.2,
      "userAgent": "claude-desktop/1.x"
    }
  ]
}
```

**Response 404:** `{ "error": "api key não encontrada" }`

## 6. Acceptance Criteria

- **Given** uma request `GET /api/sources` com `Bearer aft_válida`, **when** completa, **then** existe um `ApiKeyUsageEvent` com path `/api/sources`, status e duração corretos, ligado à chave certa.
- **Given** request com cookie session, **when** completa, **then** nenhum evento é criado.
- **Given** request com `Bearer aft_` de chave revogada, **then** autenticação falha e nenhum evento é criado.
- **Given** 2 usuários com chaves, **when** usuário A chama `GET /api/apikeys/{id_de_B}/usage`, **then** 404.
- **Given** chave com 10.000 eventos, **when** evento 10.001 é gravado, **then** o mais antigo é removido.
- **Given** evento com `Timestamp` > 90d, **when** novo evento da chave é gravado, **then** o antigo é removido.
- **Given** UI `/api-keys`, **when** clico no ícone de uma chave, **then** dialog abre com detalhes + resumo + auditoria.
- **Edge:** `UserAgent` ausente → null; `Path` > 256 → truncado; falha de escrita no evento → request continua (log warning).

## 7. Task Plan

| # | Task | Validation |
|---|------|-----------|
| T1 | `key_id` claim + entidade `ApiKeyUsageEvent` + DbSet + migration | `dotnet build`; teste de integração RED |
| T2 | `ApiKeyUsageMiddleware` + registro no pipeline + prune (RF-002/005) | testes de integração verdes |
| T3 | Endpoint `GET /api/apikeys/{id}/usage` + DTOs (RF-004) | testes: agregados, 404 cross-user, 100-event cap |
| T4 | `AuthApiClient.GetKeyUsageAsync` + UI dialog em `ApiKeys.razor` (RF-006) | `dotnet build` client |
| T5 | `dotnet format` + suíte completa + smoke local | gates verdes |

## 8. Organization Guardrails

- Branch `feature/Devin-20260915-apikey-usage-audit` — nunca commitar em `main`.
- `.github/workflows/` protegido.
- Nunca logar ou persistir o segredo `aft_*`; eventos gravam `ApiKeyId` apenas.
- `UserAgent`/`Path` truncados — sem PII além do necessário para auditoria.

## 9. Definition of Done

- [ ] Claim `key_id` emitido
- [ ] `ApiKeyUsageEvent` + migration aplicada
- [ ] Middleware gravando eventos apenas para `auth_method=apikey`
- [ ] Retenção 90d/10k implementada
- [ ] `GET /api/apikeys/{id}/usage` com agregados + 100 eventos, owner-only
- [ ] Dialog na UI com detalhes + métricas + auditoria
- [ ] Testes de integração cobrindo gravação, isolamento por usuário, retenção, endpoint
- [ ] Build + format + suíte verdes; PR; merge; redeploy; smoke `/api-keys`
