# SPEC — Polish de ops e UI: log-level honesto, avisos acessíveis, monitor/eval/login corretos, diagnostics sqlite-vec, backup sem colisão

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-ops-and-ui-polish` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (LogLevelControl, SettingsEndpoints, IngestionWorker, VectorStoreDiagnostics, CatalogToolAIFunction, McpMonitorReplay feed) + `KnowledgeHub.Client` (Sources, Settings, Login, McpMonitor, Eval, EvalApiClient, app.css) + `backup.sh` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Approved` |
| Ticket | #214 ([E22] Epic #208) — devin-ai-integration review — PRs #185 (6), #188 (3), #189 (4), #191 (2), #192 (3), #193 (1), #195 (2), #196 (4), #204 (1), era antiga #43 (backup) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — clusters F, G, H |

## 1. User Story

**As a** operador usando Settings, Fontes, MCP Monitor, Eval e Login
**I want** que cada superfície mostre o estado real (avisos clicáveis, métricas certas, endpoints válidos)
**So that** não perco diagnósticos nem me engano com valores errados.

## 2. Evidências (AS-IS)

### F — Log level

| # | Achado | Evidência |
|---|---|---|
| F1 | `Debug` + `minutes:0` fica ativo para sempre — contrato diz "≤0 restaura o default", implementação aplica o nível sem timer | `LogLevelControl.Set` (`level != ConfiguredDefault && minutes > 0`) |
| F2 | Timer antigo pode matar sessão nova — `Timer.Dispose` não espera callback enfileirado; callback roda após o próximo `Set` e restaura o default prematuramente | `LogLevelControl.cs:40-49` |
| F3 | PUT `/log-level` não devolve `configuredDefault` (GET devolve) → UI mostra "volta a  em" | `SettingsEndpoints.cs:300-313` |
| F4 | Audit do log-level registra só nível + prazo — sem caller e sem nível anterior | `SettingsEndpoints.cs:311` |

### G — UI/monitor

| # | Achado | Evidência |
|---|---|---|
| G1 | Badge `completed` com warnings em `LastError` não é clicável → avisos de sync inacessíveis | `Sources.razor` (`case "completed"` Badge simples) |
| G2 | Jobs órfãos marcados `failed` no restart não atualizam a fonte → grade mostra "—" sem acesso ao erro do job | `IngestionWorker.FailOrphanedJobsAsync` + `Sources.razor` |
| G3 | Spans de status `role=button` sem ativação por teclado (Enter/Espaço não abrem modal) — a11y | `Sources.razor` |
| G4 | Race no modal de job: fechar A e abrir B antes da resposta → modal de B mostra job de A | `Sources.razor` (`_statusJob` compartilhado) |
| G5 | Falha em `JobsAsync` mostra "Nenhum job registrado" em vez do erro | `Sources.razor` |
| G6 | Coluna Ações ~120px < ~156px dos botões → transborda | `app.css:308` + `Sources`/`ApiKeys` |
| G7 | MCP Monitor: `SessionOpened`/`Closed` não entram em `activity` → filtro "sessões" vazio e CSV sem transições; `ToolCall` do agente sem `Caller`; buffer client 200 < server 500; idade da sessão congela sem re-render | `McpMonitorReplay.Apply`, `CatalogToolAIFunction.cs:74-85`, `McpMonitor.razor:55,203,355` |
| G8 | Eval: coluna P95 exibe `DurationMs` total; reabrir run não recalcula delta (sem `compare`); gate JSON malformado lança `JsonException` fora do form | `Eval.razor:41`, `EvalApiClient.cs:16,34` |
| G9 | Login: endpoint SSE anunciado incondicionalmente (inexistente em `SessionMode=Stateless`); form sai do viewport em telas baixas; instruções divergem do McpMonitor; bloco copiável com comentários `#` não é JSON válido | `Login.razor:15,51,67`, `McpMonitor.razor:355` |
| G10 | Diagnostics sqlite-vec: `rows` conta `Chunks.Embedding != null` (não `vec_chunks`) e `hnswIndex=true` para vec0 (que não é HNSW) | `VectorStoreDiagnostics.cs:27` |

### H — Scripts

| # | Achado | Evidência |
|---|---|---|
| H1 | `backup.sh` gera slug por `basename` — dois vaults `/a/docs` e `/b/docs` sobrescrevem `docs.tar.gz` | `backup.sh:62` |

## 3. Requisitos Funcionais

### RF-001 — `minutes ≤ 0` restaura o default imediatamente

- `Set(level, minutes≤0)` aplica `ConfiguredDefault` (ou interpreta como "sem prazo → restaura"): comportamento único e documentado — Debug nunca fica preso sem timer.

### RF-002 — Timer com guarda de geração

- Cada `Set` incrementa uma geração capturada no callback; callback de geração antiga é no-op — timer velho não pode derrubar sessão nova.

### RF-003 — PUT `/log-level` com payload completo + audit

- Resposta inclui `configuredDefault` (paridade com GET).
- Log de auditoria com `caller` (quando resolvível) + nível anterior → novo + prazo.

### RF-004 — Avisos de sync acessíveis na grade

- `completed` com `LastError` não-vazio → badge clicável abrindo o modal (mesmo de failed/skipped).
- `FailOrphanedJobsAsync` atualiza `LastSyncStatus="failed"`/`LastError` da fonte (ou a grade consulta último job mesmo sem status) — erro nunca fica inacessível.
- Status focáveis abrem por Enter/Espaço (ideal: `<button>` real; fallback `onkeydown`).

### RF-005 — Modal de job correto e honesto

- Resposta de `JobsAsync` é vinculada ao sourceId do modal aberto — resposta tardia de outro source é descartada.
- Falha na consulta mostra erro, não "Nenhum job registrado".

### RF-006 — Coluna Ações dimensionada

- Largura acomodando 4 botões de ≥36px + gaps (ou wrap permitido) em Sources e ApiKeys.

### RF-007 — MCP Monitor com sessões auditáveis

- Eventos `SessionOpened`/`Closed` também alimentam `activity` (ou a view "sessões" lê o dicionário de sessões com timestamps) — filtro e CSV export cobrem o ciclo de vida.
- `ToolCall` de agente carrega `Caller` resolvido do contexto de autenticação quando disponível.
- Buffer client ≥ server (500) ou nota "últimos N" explícita.
- Idade da sessão recomputa em tick periódico (ou on-demand na render a cada intervalo) — não congela.

### RF-008 — Eval honesto

- Coluna P95 usa `latency.P95` do run (não `DurationMs`).
- Reabrir run com baseline passa `compare` → delta reaparece.
- `gateJson` inválido → erro inline no formulário (try/catch no client).

### RF-009 — Login condizente com o servidor

- Bloco SSE exibido só quando o modo comporta SSE (server expõe `SessionMode` via endpoint de config já existente ou novo `GET /api/settings/mcp`); em `Stateless`, instruções apontam só o endpoint HTTP.
- Layout mantém o form acessível em telas baixas (scroll na coluna de instruções, não na página).
- Lista de clients unificada com McpMonitor (fonte única de verdade, ex.: recurso compartilhado ou mesma markup).
- Blocos copiáveis sem comentários `#` — JSON válido ou aviso explícito "JSONC".

### RF-010 — Diagnostics sqlite-vec honesto

- `rows` conta a tabela vetorial real (`vec_chunks` quando sqlite-vec; `Chunks.Embedding != null` para embeddings inline quando esse for o meio); `hnswIndex=false` para vec0 (sem índice ANN — é brute-force KNN) ou renomeia o campo para `annIndex` com valor real.

### RF-011 — `backup.sh` sem colisão de slug

- Slug do vault = `basename` + sufixo hash do path completo (ex.: `-` + 8 hex do SHA256 do path) — vaults homônimos produzem arquivos distintos.

## 4. RNFs

- Mudanças de UI mantêm BootstrapBlazor e padrões das páginas (abas Chrome, badges, modal existente).
- Nenhuma mudança de contrato REST quebradora — campos novos são aditivos.
- `backup.sh` continua POSIX/bash 5 e idempotente.

## 5. Fora de Escopo

- Redesign do monitor/export (CSV incremental, SSE replay completo).
- i18n de mensagens.
- Teste formal de shutdown gracioso×abrupto (registrado como pendência separada na SPEC sync-error-diagnostics).

## 6. Plano de Tarefas

1. `LogLevelControl`: minutes≤0 → default + generation guard; PUT com `configuredDefault` + audit caller/prev.
2. Sources: badge completed clicável, orphan→fonte com status, teclado, race do modal, erro de JobsAsync, largura Ações.
3. McpMonitor: transições no filtro/CSV, Caller do agente, buffer/idade.
4. Eval: P95 correto, compare no reopen, gate JSON seguro.
5. Login: SSE condicional por SessionMode, layout, lista unificada, JSON sem `#`.
6. `VectorStoreDiagnostics` sqlite-vec honesto.
7. `backup.sh` slug com hash.
8. Testes: log-level reset/timer race; badge abre modal com warnings; CSV inclui sessões; slug único para paths homônimos.
9. Suites + format + PR.

## 7. Critérios de Aceite

- [ ] `Debug`+`minutes:0` volta ao default imediatamente; timer velho não derruba sessão nova.
- [ ] PUT log-level devolve `configuredDefault`; log de audit tem caller e nível anterior→novo.
- [ ] Fonte `completed` com warnings abre o modal; job órfão `failed` é acessível pela grade; Enter/Espaço ativam status.
- [ ] Modal de B nunca exibe job de A; falha de consulta mostra erro real.
- [ ] Filtro "sessões" e CSV incluem abertura/fechamento; `ToolCall` de agente tem `Caller`.
- [ ] Eval mostra p95 real, restaura delta ao reabrir, e gate inválido dá erro no form (sem crash).
- [ ] Em `SessionMode=Stateless`, a tela de login não anuncia `/mcp/sse`; blocos copiáveis são JSON válido.
- [ ] Diagnostics sqlite-vec reporta rows da tabela vetorial e `annIndex:false` para vec0.
- [ ] `backup.sh` produz arquivos distintos para vaults com mesmo basename.
