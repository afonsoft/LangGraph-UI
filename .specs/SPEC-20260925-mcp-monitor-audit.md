# SPEC-20260925-mcp-monitor-audit

| Campo | Valor |
|---|---|
| Status | `Done` |
| Branch | `feature/Devin-20260925-monitor-audit` |
| Prioridade | média |

## 1. User Story

Como operador, quero auditoria rica no Monitor MCP — quem chamou cada tool,
filtros e exportação — para investigar uso indevido e incidentes.

## 2. Contexto

O feed de atividade (`IMcpActivityFeed`) já registrava método, sessão, latência
e resultado — mas não **quem** chamou. Os claims de auth (`auth_method`,
`key_id`, `Name`) existem no `HttpContext` ambiente; os filtros MCP alcançam-no
via `request.Server.Services` → `IHttpContextAccessor` (AsyncLocal).

## 3. Requirements

- **RF-001 Caller nos eventos**: `McpActivityEvent.Caller` + `CallerResolver`
  (`{user} (apikey:{keyId8})` / `{user} (cookie)`), em tool-calls, requests e
  eventos de sessão. Nunca lança — anônimo → null.
- **RF-002 Wire shape**: `Caller` em `McpMonitorEventDto`, snapshot e broadcasts
  `SessionOpened`/`SessionClosed`.
- **RF-003 UI**: coluna Quem + Sessão (id curto com tooltip), filtros por
  tipo/resultado/texto, resumo agregado (eventos/ok/erros/média ms/top tools),
  exportação CSV do feed filtrado via `khDownload`.
- **RF-004 Sessões**: caller + idade da sessão na lista.

## 4. Acceptance

- Eventos carregam caller quando autenticado; contratos pinados atualizados.
- Filtros combinam tipo + resultado + texto; CSV reflete o filtro ativo.

## 5. Riscos

- Caller depende de `IHttpContextAccessor` — chamadas fora de HTTP (testes,
  hosted services) simplesmente ficam null.
