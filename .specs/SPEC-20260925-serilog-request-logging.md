# SPEC — Serilog request logging + correlation enrichers

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-serilog-request-logging` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Serilog.AspNetCore`, `LogContext` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise de logging 2026-09-25 |

## 1. User Story

**As a** operador investigando um incidente
**I want** um log de request HTTP por request (método, path, status, elapsed,
caller) com correlation id propagado aos logs de negócio
**So that** reconstrua "o que aconteceu nessa chamada" sem juntar pistas manuais.

## 2. Contexto

Serilog foi adicionado com sinks console+arquivo — mas falta
`UseSerilogRequestLogging`: requests HTTP não aparecem como evento único
estruturado, não há `RequestId`/`TraceId` enriquecendo os eventos, e o caller
autenticado (resolvido pelo `CallerResolver` do MCP audit) não entra no
LogContext fora do caminho MCP.

## 3. Requisitos Funcionais

- **RF-001** `app.UseSerilogRequestLogging()` com `GetLevel` por path:
  `/health/*` e static WASM → `Debug` (silencia ruído), `/api/*` → `Information`,
  erro 5xx → `Error`. `EnrichDiagnosticContext` adiciona `Elapsed`,
  `ContentLength`, `Caller` (mesma resolução do CallerResolver — user+auth_method),
  `ClientIp` (respectando `ForwardedHeaders` configurados).
- **RF-002** Correlation: `TraceId`/`RequestId` no `LogContext` via middleware
  inicial; exposto como response header `x-request-id` para o client citar em
  bug reports. SSE/MCP long-lived: correlation no evento de abertura.
- **RF-003** Template de arquivo inclui `[{Level:u3}] {SourceContext} {RequestId}`
  — grepável.
- **RF-004** Logs do bootstrap (`Log.Logger = ...` two-stage) para capturar
  falha de startup antes do host subir.

## 4. Requisitos Não-Funcionais

- Health checks no path de log não devem encher o arquivo (GetLevel filtra).
- Nenhum PII extra: `Caller` é user-id/label já exibido no monitor, não IP bruto
  (exceto `ClientIp` que já é registrável por padrão de auditoria).

## 5. Fora de Escopo

- Sinks externos → `SPEC-20260925-log-sinks-and-redaction`.
- Toggle de nível → `SPEC-20260925-runtime-log-level`.

## 6. Plano de Tarefas

1. `UseSerilogRequestLogging` + enrichers + forwarded-headers aware.
2. RequestId middleware + header.
3. Two-stage bootstrap logger.
4. Testes de integração (log via TestSink ou captura de output template).

## 7. Acceptance Criteria

- [ ] Um request `/api/sources` gera 1 evento `HTTP GET ... responded 200 in N ms`
  com caller quando autenticado.
- [ ] `x-request-id` presente na resposta e repetido nos logs internos.
- [ ] `/health/*` não polui em `Information`.

## 8. Riscos

- `ForwardedHeaders` mal configurado loga IP do proxy — já existe config no repo?
  Validar antes de enriquecer ClientIp.
