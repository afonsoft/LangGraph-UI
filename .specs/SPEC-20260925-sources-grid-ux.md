# SPEC — Fontes: alinhamento das ações, coluna de sync compacta e popup de erro no Status

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260925-sources-grid-ux` |
| Data | 2026-09-25 |
| Autor | Devin |
| Stack | `Blazor WASM`, `BootstrapBlazor`, `app.css`, `IngestionJobs` API |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260924-sources-grid-ux` |
| Status | `In implementation` |
| Ticket | — |
| Origem | feedback de UX do operador — grid "Fontes de conhecimento" |

## 1. User Story

**As a** operador olhando o grid de fontes
**I want** ações com ícones alinhados, coluna "Último sync" compacta e poder clicar no Status para ver o detalhe do erro/log
**So that** a tabela fica visualmente limpa e eu diagnostico falhas de sync sem abrir o editar nem ir ao servidor.

## 2. Contexto

Em `src/KnowledgeHub.Client/Pages/Sources.razor`:

- A coluna **Ações** usa `div.kh-actions` com `Button`/`PopConfirmButton` icon-only (SPEC anterior). O `PopConfirmButton` do BootstrapBlazor renderiza um wrapper (`div.popover-confirm`) em torno do botão — ele vira o flex item, não o `.btn`, e a linha base vertical fica desalinhada em relação aos 3 `Button` diretos. A coluna tem `Width="150"` fixo.
- A coluna **Último sync** usa formato `g` (data+hora longos) sem `Width` — consome espaço demais.
- A coluna **Status** mostra badge (`ok`/`falhou`/`skip`) com o `LastError` só no `title` — tooltip nativo, não clicável, texto truncado e ilegível quando longo (`LastError` pode conter warnings agregados `"; "`-separados).
- O backend agora expõe `GET /api/ingestion/jobs?sourceId=` (SPEC-20260924-async-ingestion-queue) — o popup pode mostrar o último job com contadores e erro persistido, não apenas `LastError` da fonte.

## 3. Requisitos Funcionais

### RF-001 — Ações alinhadas
- `.kh-actions` trata wrappers de confirmação como transparentes ao layout (`display: contents` no wrapper do PopConfirmButton) e centraliza verticalmente todos os botões com tamanho uniforme (`ExtraSmall`, ~28px).
- Largura da coluna `Ações` fixa e mínima (~120px), sem quebra de linha dos botões (`flex-wrap: nowrap`).

### RF-002 — Último sync compacto
- Largura fixa reduzida (~120px) e formato curto `dd/MM/yy HH:mm` em vez de `g`.
- `—` para nunca sincronizado (inalterado).

### RF-003 — Status clicável com detalhe
- Badge de status vira clicável (`cursor: pointer` + affordance visual sutil) quando houver `LastError` ou job recente — abre `Modal` de detalhe.
- O modal mostra: nome da fonte, status, `LastSyncAt`, `LastError` completo (wrapping, warnings `"; "` renderizados como lista) e o último `IngestionJob` (kind, contadores docs/chunks, erro, timestamps) via `GET /api/ingestion/jobs?sourceId={id}&limit=1`.
- Fonte sem erro e sem jobs → badge inerte (sem modal).

### RF-004 — Cliente
- `SourceApiClient` ganha `JobsAsync(sourceId, limit)` retornando lista de `IngestionJobDto` (DTO já existente no client ganha `Kind`, `CreatedAt`, `StartedAt`, `FinishedAt`).

## 4. Requisitos Não-Funcionais

- Zero mudança de backend — reuso de `LastError` + endpoint de jobs já existente.
- Modal não-bloqueante para leitura (sem save); ESC/fechar normais.
- Nenhum texto de erro em telemetry/logs do client além do que já vem do servidor.

## 5. Fora de escopo

- Streaming de logs em tempo real / SignalR de progresso no grid.
- Retry de job a partir do popup (fica na coluna Ações).
- Histórico completo de jobs — o popup mostra só o mais recente; paginação futura se precisar.

## 6. Plano de tarefas

| # | Tarefa |
|---|---|
| T1 | CSS `.kh-actions`: wrappers transparentes + nowrap + alinhamento vertical |
| T2 | `Sources.razor`: width/formato da coluna sync, badge clicável, modal de detalhe |
| T3 | `SourceApiClient.JobsAsync` + campos novos no `IngestionJobDto` |
| T4 | Build client + verificação visual manual |

## 7. Critérios de aceite

- [ ] Os 4 ícones de Ações ficam na mesma linha de base, sem desnível vertical.
- [ ] "Último sync" ocupa ~120px com formato curto.
- [ ] Clicar em `falhou`/`skip` abre modal com erro completo + dados do último job.
- [ ] Fonte ok/sem erro não abre modal (badge inerte).
- [ ] Build verde; nenhuma quebra visual nas outras telas que usam `.kh-actions` (o fix é localizado).

## 8. Riscos

- `display: contents` no wrapper do PopConfirm pode quebrar o posicionamento do popover — fallback: `.kh-actions .popover-confirm { display: inline-flex; align-items: center; }` mantendo a âncora.
- Erros muito longos → modal com scroll interno (`max-height` no body).
