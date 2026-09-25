# SPEC — Detalhe de falhas no job + popup de Status enriquecido

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-job-error-details` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `EF Core`, `Minimal API`, `Blazor`, `BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | pedido do usuário 2026-09-25 — popup "Erro" mostra só
  "sync failed — see server logs" |

## 1. User Story

**As a** operador vendo `failed` na grid de fontes
**I want** o popup com a causa real (inner exception), quais documentos
falharam e agrupamento por erro
**So that** diagnostique sem abrir `docker logs`.

## 2. Contexto

Hoje `job.Error` recebe `result.Reason` ("sync failed — see server logs") e a
lista `result.Warnings` (falhas por doc) é descartada pelo worker — o DTO do job
nem tem campo para ela. O popup mostra apenas `source.LastError` (digest de 5
warnings) + `job.Error`. Resultado: zero informação acionável.

## 3. Requisitos Funcionais

- **RF-001** `IngestionJob.WarningsJson` (migration, TEXT NULL): worker persiste
  `SyncResultDto.Warnings` serializado (cap de 100 entradas — além disso guarda
  só o digest).
- **RF-002** `IngestionJobDto.Warnings` (string[] | null) + `Error` — expostos
  em `GET /api/ingestion/jobs/{id}` e no item de listagem do `?sourceId=`.
- **RF-003** Mensagens por doc carregam a inner exception real
  (`GetBaseException()` no ponto de captura — o envelope EF é inútil).
- **RF-004** `job.Error`: quando `result.Reason` for o genérico "sync failed",
  concatenar digest dos warnings (`{n} docs falharam — {primeiro erro real}`).
- **RF-005** Popup `Sources.razor`:
  - Seção "Falhas por documento" — agrupa warnings por mensagem de erro
    (`texto depois de ': '`), mostra contagem + até 5 docs por grupo + resto
    colapsável, scroll.
  - `Erro` do job em blockquote destacado; `LastError` da fonte como segundo
    bloco ("Último erro registrado").
  - Seção Stats: docs por status com badges coloridas.

## 4. Requisitos Não-Funcionais

- `WarningsJson` limitado (≤ 100 entradas / ~32KB) — jobs gigantes não incham a
  tabela.
- Migração aditiva (coluna nullable).

## 5. Fora de Escopo

- Download do log completo / persistência de warnings por documento em tabela
  separada (futuro se o digest não bastar).

## 6. Plano de Tarefas

1. Coluna + migration `AddIngestionJobWarnings`.
2. Worker popula warnings + digest em Error.
3. DTO/mapper/endpoints.
4. Popup: render de grupos + stats.
5. Testes: job falho expõe warnings via API; digest no Error.

## 7. Acceptance Criteria

- [ ] Job com 465 fails → popup mostra erro real + docs agrupados.
- [ ] `GET /api/ingestion/jobs/{id}` retorna `warnings` array.
- [ ] Sem warnings → popup igual ao atual.

## 8. Riscos

- Warnings podem conter caminhos de arquivo (PII leve) — aceitável: popup já é
  área admin autenticada.
