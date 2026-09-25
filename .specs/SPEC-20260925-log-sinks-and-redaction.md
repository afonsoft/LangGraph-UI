# SPEC — Persistência de logs + scrubbing de dados sensíveis

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-log-sinks-and-redaction` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Serilog`, `docker-compose`, sinks opcionais |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Origem | análise de logging 2026-09-25 |

## 1. User Story

**As a** operador em produção
**I want** logs sobrevivendo a redeploys e garantia de que segredos nunca
apareçam em log
**So that** possa auditar após incidentes e inspecionar arquivos sem risco de
vazar credenciais.

## 2. Contexto

Hoje o file sink escreve `/app/logs/knowledgehub-.log` **dentro do container** —
efêmero (morre no `docker compose up --force-recreate`). E não há nenhuma
proteção contra PII/segredos: um `LogDebug` descuidado numa tool-call ou um
stack trace com connection string pode vazar `ApiKey`/`Authorization` no arquivo.

## 3. Requisitos Funcionais

- **RF-001** `docker-compose.yml`: volume `./logs:/app/logs` + variável
  `Serilog__WriteTo__1__Args__path` configurável; README documenta rotação
  (rolling file diário, 14d já configurado).
- **RF-002** Enricher de scrubbing (`IDestructuringPolicy` + filter): redige
  valores de chaves `*key*`, `*token*`, `*secret*`, `*password*`,
  `*connectionstring*`, `Authorization` e padrão `aft_*`/`ctx7sk-*`/`sk-*` em
  QUALQUER propriedade → `***REDACTED***`; connection strings viram
  `Server=...;Password=***REDACTED***`.
- **RF-003** Guard no request logging: headers `Authorization`,
  `X-Api-Key`, `access_token` query param nunca logados (o enricher de RF-001
  já omite headers por padrão — garantir).
- **RF-004** Sink opcional por config: `Serilog:WriteTo` adicional OTLP
  (`Serilog.Sinks.OpenTelemetry`) quando `Telemetry:Otlp:Endpoint` setado —
  logs chegam ao mesmo backend que traces/metrics.
- **RF-005** `appsettings` expõe `Serilog:MinimumLevel:Override` para
  `Microsoft.AspNetCore`/`System` = `Warning` (hoje o request do
  `HttpClient` enche `Information`).

## 4. Requisitos Não-Funcionais

- Scrubbing não pode quebrar a mensagem — redige valores, preserva estrutura.
- Fail-soft do sink OTLP: endpoint inalcançável não deve derrubar logging.

## 5. Fora de Escopo

- Log shipping ativo (Fluent Bit) — OTLP cobre.

## 6. Plano de Tarefas

1. Compose volume + README.
2. `RedactingDestructuringPolicy` + `Serilog.Enrichers.Sensitive` ou impl própria
   (preferir própria — escopo conhecido e zero dep nova pesada).
3. OTLP sink condicional.
4. Testes: property com `ApiKey="sk-..."` → `***REDACTED***` no rendered message.

## 7. Acceptance Criteria

- [ ] `logs/` persiste no host após `compose down/up`.
- [ ] Qualquer property cujo nome/case contenha `key|token|secret|password` ou
  valor com padrão de token é redigida.
- [ ] `Telemetry:Otlp:Endpoint` setado → logs chegam no collector.

## 8. Riscos

- Scrubbing via regex tem falsos positivos (ex: `Monkey`) — lista de nomes
  exatos + prefixos de token conhecidos minimiza; documentar.
