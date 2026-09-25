# SPEC — Follow-ups Devin Review: docs (data chown, campo `minutes`, pt-br parity), storageType no diagnostics, sort na tabela de entidades

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-review-docs-and-misc` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | docs + `PostgresVectorStore` + `Settings.razor` (Banco de Dados) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | devin-ai-integration review PRs #200/#201/#203 |
| Origem | reviews dos PRs fechados |

## 1. User Story

**As a** usuário seguindo o README ou lendo métricas do banco
**I want** instruções corretas de permissão e paridade de dados/documentos
**So that** instalações novas funcionam e os diagnósticos mostram o que a doc promete.

## 2. Evidências (AS-IS → TO-BE)

| # | Achado | AS-IS | TO-BE |
|---|---|---|---|
| D1 🔴 | Instalação Docker falha: `./data` sem chown | `mkdir -p data logs` + `chown 1654:1654 logs` — só logs; migration do SQLite falha numa instalação nova | `chown -R 1654:1654 data logs` documentado em README + INSTALL en/pt |
| D2 | Doc usa `autoResetMinutes`; API aceita `minutes` | `SetLogLevelRequest` field = `minutes` | docs alinhados ao campo real |
| D3 | `storageType` prometido na doc/diagnostics, ausente | `PostgresVectorStore.GetDiagnosticsAsync` retorna rows/size/hnswIndex/version/dims, sem storageType | campo `storageType` (vector\|halfvec) no payload |
| D4 | README pt-br sem os novos params de cache/pgvector | README.pt-br desatualizado | paridade en/pt |
| D5 | Tabela "Entidades" sem ordenação por coluna (RF-003 pendente) | ordem fixa desc por RowCount | ordenação clicável por coluna (ou confirmar sort desc como definitivo) |
| D6 | Memory protocol incompleto na seção nova | seção nova sem `## Prompts`/`## Session summary` | completar no próximo docs commit |

## 3. Requisitos Funcionais

- **RF-001**: README.md + INSTALL en/pt — instruir `mkdir -p data logs && sudo chown -R 1654:1654 data logs` antes do compose up (ou alternativa `user:` com ressalva).
- **RF-002**: docs/en|pt/API.md — payload do `PUT /api/settings/log-level` documenta `{ level, minutes }`.
- **RF-003**: `PostgresVectorStore.GetDiagnosticsAsync` inclui `storageType = _storageType`; DTO/UI passam a exibir (o card Vector store já lê o campo).
- **RF-004**: README.pt-br — seções Cache/pgvector/Serilog equivalentes ao README.md.
- **RF-005**: tabela "Entidades" — ordenação clicável por coluna (Nome / Linhas), default `Linhas desc`.
- **RF-006**: completar `.claude/memory/20260926-memory.md` — `## Prompts` + `## Session summary` da sessão atual (protocolo de memória).

## 4. Fora de Escopo

- `enforce_admins` (decisão do owner).
- Teste dedicado de shutdown abrupto × gracioso (sugestão — registrar como dívida se não entrar no PR).

## 5. Critérios de Aceite

- [ ] Instalação nova via README funciona sem chown manual extra.
- [ ] `PUT /api/settings/log-level` documentado com `minutes`.
- [ ] Diagnostics postgres retorna `storageType` (e card o exibe).
- [ ] README.pt-br em paridade de seções com o en.
- [ ] Tabela de entidades ordenável.
- [ ] Memory file dentro do protocolo.
