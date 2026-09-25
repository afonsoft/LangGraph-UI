# SPEC — Diagnóstico de erro de sync: unwrap de exceção + classificação de cancelamento por shutdown

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-sync-error-diagnostics` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (IngestionService, IngestionWorker), `KnowledgeHub.Shared` (SyncResultDto), `KnowledgeHub.Client` (Sources card) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | BUG-sync-generic-error-message |
| Origem | fonte "Knowledge" exibe `LastError` = "An error occurred while saving the entity changes. See the inner exception for details." — mensagem inútil persistida |

## 1. User Story

**As a** operador olhando o status de uma fonte ou job
**I want** ver a causa real da falha (ex.: `UNIQUE constraint failed: KgAliases...`)
**So that** eu diagnostico sem abrir `docker logs` nem adivinhar pela mensagem genérica do EF.

## 2. Contexto — evidências

- `Sources.LastError` = "An error occurred while saving the entity changes." (SQLite `data/knowledgehub.db`, fonte "Knowledge", sync de 24/09 — a causa raiz, UNIQUE em `KgAliases`, já foi corrigida no PR #191; o que ficou é a **má propagação da mensagem**).
- Três pontos usam `ex.Message` sem unwrap:
  - `IngestionService.cs:274` → `TryRecordSyncFailureAsync(sourceId, ex.Message)` → `source.LastError`.
  - `IngestionService.cs:275` → `Fail(sourceId, "sync failed — see server logs")` → `SyncResultDto.Reason` genérico → `job.Error`.
  - `IngestionWorker.cs:148` → `job.Error = ex.Message` no catch de job.
- O fix do incidente anterior já aplica `ex.GetBaseException().Message` nos **warnings** — os caminhos de erro top-level ficaram de fora.
- Bônus observado no DB: jobs mortos por restart/deploy caem no catch genérico → `failed: "The operation was canceled"` (5+ ocorrências hoje) — indistinguível de falha real.

## 3. Requisitos Funcionais

### RF-001 — Unwrap de exceção

Helper compartilhado `Describe(ex)` (ex.: `Ingestion/ExceptionDigest.cs` ou estático interno):

- Retorna `"{BaseExceptionType}: {GetBaseException().Message}"` — ex.: `SQLiteException: SQLite Error 19: 'UNIQUE constraint failed: KgAliases.SourceId, KgAliases.Alias'`.
- Trunca em ~500 chars; sanitiza quebras de linha (`\n` → ` | `).

Aplicar em:

- `TryRecordSyncFailureAsync` → `source.LastError` = digest real (mantém corte de 1000).
- `Fail(...)` no catch de `SyncAsync` → `Reason` = `"sync failed: {digest}"` (substitui "see server logs"; logs completos continuam no servidor).
- `IngestionWorker` catch de job → `job.Error` = digest.
- `EnrichError` — quando reason já contém digest, não duplicar.

### RF-002 — Cancelamento por shutdown vs. falha

- `IngestionWorker` catch de `OperationCanceledException`: quando `stoppingToken.IsCancellationRequested` (host parando) → `job.Status = "failed"`, `job.Error = "interrupted by shutdown/restart"` (mensagem clara, não "The operation was canceled").
- Cancelamento de usuário continua `"cancelled"` (já existe).

### RF-003 — UI

- Sources card exibe `LastError` truncado com tooltip/title completo (já existe — só garantir que o digest caiba legível).
- Jobs list mostra `job.Error` com digest real — sem alteração de markup se já renderiza `Error` (verificar truncamento).

## 4. Requisitos Não-Funcionais

- Nunca logar/retornar conteúdo de chunk/secret — só tipo+mensagem da exceção (mensagens EF/HTTP não carregam payload do documento; se conterem, truncamento já limita).
- Compat: `Reason`/`LastError`/`Error` continuam strings — sem quebra de contrato.

## 5. Fora de Escopo

- Retry automático de falhas; reclassificação de jobs históricos (ficam com mensagens antigas — aceito).
- Expor stack trace na API — só digest; stack completo segue nos logs.

## 6. Plano de Tarefas

1. Helper `Describe(ex)` + unit tests (DbUpdateException→inner, HttpRequestException, OCE).
2. Aplicar nos 3 pontos + ajuste `EnrichError`.
3. Ramificação shutdown em `IngestionWorker`.
4. Testes: unit (digest), integração job falho → `Error` contém causa real (simular exceção de save).
5. Limpar `LastError` velho? — não: próximo sync com sucesso já limpa (`source.LastError = null`).

## 7. Critérios de Aceite

- [ ] Sync falho por constraint → `LastError`/`job.Error` mostram `SQLiteException: ...UNIQUE constraint failed...`, não a mensagem genérica EF.
- [ ] Restart durante job → `job.Error = "interrupted by shutdown/restart"`.
- [ ] Cancel pelo usuário → `cancelled` (regressão coberta).
- [ ] Fonte "Knowledge": próximo autosync bem-sucedido limpa `LastError` (ou exibe digest real se falhar de novo).
- [ ] Suites verdes.
