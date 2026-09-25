# SPEC — Log level em runtime (LoggingLevelSwitch via admin API)

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-runtime-log-level` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Serilog.Core.LoggingLevelSwitch` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Origem | análise de logging 2026-09-25 |

## 1. User Story

**As a** operador debugando produção
**I want** elevar o nível de log para Debug por 15 minutos sem redeploy
**So that** capture detalhe do incidente e não esqueça o nível aberto.

## 2. Contexto

`Serilog:MinimumLevel` é estático por config — mudar nível exige editar
`appsettings`/env + restart, que destrói o estado em reprodução. Serilog tem
`LoggingLevelSwitch` exatamente para isto.

## 3. Requisitos Funcionais

- **RF-001** `LoggingLevelSwitch` singleton injetado no `LoggerConfiguration`
  (`MinimumLevel.ControlledBy(switch)`) — valor inicial do config.
- **RF-002** `PUT /api/settings/log-level { "level": "Debug", "minutes": 15 }`
  (admin): altera o switch; `minutes` (default 15, max 120) agenda auto-reset
  ao nível configurado — nunca fica Debug eterno.
- **RF-003** `GET /api/settings/log-level` → `{current, configuredAt, autoResetAt}`.
- **RF-004** Settings UI: seletor de nível com indicação "auto-reset em Xmin"
  + botão "restaurar agora".
- **RF-005** Evento de auditoria no activity feed/log quando nível muda
  (`caller`, nível antigo→novo).

## 4. Requisitos Não-Funcionais

- Reset automático via timer in-process — reboot também reseta (switch inicia
  no nível do config).
- Level inválido → 400.

## 5. Fora de Escopo

- Por-namespace switches futuros (Microsoft.EntityFrameworkCore etc.) — v2.

## 6. Plano de Tarefas

1. Switch no Program.cs + DI.
2. Endpoints + auto-reset timer.
3. UI Settings.
4. Testes: PUT Debug → requests logam Debug; timer reseta.

## 7. Acceptance Criteria

- [ ] PUT Debug → log arquivo mostra Debug; após `minutes` volta a Information.
- [ ] Sem auth admin → 401/403.

## 8. Riscos

- Esquecer em Debug enche disco — auto-reset é a mitigação.
