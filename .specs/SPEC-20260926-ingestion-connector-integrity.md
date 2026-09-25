# SPEC — Integridade de ingestão: reindex não apaga docs inalterados, falha transitória ≠ delete, fila-cheia sem deadlock, delete-order correto

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-ingestion-connector-integrity` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Server` (IngestionService, IngestionQueue, CloudConnectorBase, AzureShareGateway, GoogleDriveGateway, DocumentFileConnector, KnowledgeSourceService, PostgresVectorStore) + `KnowledgeHub.Client` (SourceEditDialog) |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Draft` |
| Ticket | devin-ai-integration review — PRs #184 (3), #187 (4), #188 (4), #195 (3) |
| Origem | `.claude/memory/devin-review-triage-20260925.md` — cluster A (A1–A9) |

## 1. User Story

**As a** administrador com fontes remotas (Notion, cloud storage, Google Drive)
**I want** que reindexações e falhas transitórias nunca destruam conteúdo indexado
**So that** o índice reflita a fonte real e syncs não percam dados silenciosamente.

## 2. Evidências (AS-IS)

| # | Achado | Evidência |
|---|---|---|
| A1 | Reindex/forceReindex/mudança de chunking **apaga conteúdo** de docs remotos inalterados — o conector devolve `TextContent=""` para fingerprint inalterado, mas quando o doc cai no caminho de reprocessamento, `doc.RawContent` é sobrescrito com `""` e re-chunkado | `IngestionService.SyncViaConnectorAsync` (dedup por `raw.Fingerprint` cai fora quando `forceReindex`/`ChunkerVersion`/`ChunkerConfigHash` mudam); `CloudConnectorBase.FetchAsync` emite `RawDocument` com texto `""` |
| A2 | Falha transitória de download/extração → doc ausente de `fetch.Documents` → loop "unseen" o trata como **delete remoto** e purga doc + vetores. Mesma cascata quando `maxFiles` trunca a listagem do Drive | `CloudConnectorBase` (warning + `continue` sem registrar o URI como presente); `IngestionService` unseen→`DeleteByDocumentAsync`+`Remove`; `GoogleDriveGateway.ListAsync` `seen++ >= maxFiles` |
| A3 | `AzureShareGateway.ListAsync` concatena `rootDirectory/prefix` quando `prefix` já veio de `directoryPath` → lista `dir/dir`, sync não acha os arquivos | `CloudConnectorBase.cs:42` (`prefix ?? directoryPath`), `AzureShareGateway.cs:15` |
| A4 | `accountKey` Azure com padding `=` é enviado como `connectionString` → `ShareClient` ctor falha | `SourceEditDialog.razor:480` (`Contains('=')` como heurística) |
| A5 | Drive público quebrado em 3 pontos: arquivo único recebe id-como-nome sem extensão → filtrado e nunca indexado; folder público sem md5/data → fingerprint constante → conteúdo alterado nunca reprocessa; Sheets exportadas como `.csv` rejeitadas pelo filtro de extensões | `GoogleDriveGateway.cs:23/40/55/88`, `DocumentFileConnector.SupportedExtensions` (sem `.csv`) |
| A6 | Falha de documento deixa estado rastreado sujo — catch só loga; `SaveChangesAsync` posterior pode persistir doc/chunks parciais ou falhar repetidamente | `IngestionService` catch dos dois loops (vault + connector), sem detach |
| A7 | Fila cheia → job `queued` persistido **antes** de `TryWrite` → dedup encontra o job e retorna `Existing` para sempre — fonte nunca mais sincroniza até restart | `IngestionQueue.EnqueueAsync` (`SaveChangesAsync` → `TryWrite` → `QueueFullException`); `ScheduledSyncBackgroundService` catch só loga |
| A8 | Delete de fonte: vetores externos purgados **antes** do commit da fonte — falha em secrets/staging/SaveChanges deixa fonte viva sem vetores; e nenhum bump de `index:version` → busca serve docs deletados por ~5min | `KnowledgeSourceService.DeleteAsync` (ordem: `Remove` → `DeleteBySourceAsync` → secrets → `SaveChangesAsync`) |
| A9 | `UpsertBatchAsync` (pgvector): ANALYZE/HNSW dentro do mesmo `try` do commit → falha pós-commit chama `RollbackAsync` em tx já commitada → ingestão registrada como falha com dados gravados | `PostgresVectorStore.cs:95-125` |

## 3. Requisitos Funcionais

### RF-001 — Reindex nunca processa texto vazio de doc inalterado

- Quando `raw.TextContent` é vazio (stub de fingerprint inalterado) e o doc precisa reprocessar (forceReindex/chunker config/version), o pipeline **reutiliza `doc.RawContent` existente** para re-chunkar — nunca sobrescreve com `""`.
- Se `RawContent` estiver ausente/vazio no registro, o conector deve **baixar o conteúdo sob demanda** (chamar `OpenReadAsync` para aquele item) antes de reprocessar; falha nesse download segue a regra do RF-002.

### RF-002 — Falha transitória nunca é interpretada como delete remoto

- `FetchResult` ganha distinção entre "item existe mas falhou" e "item ausente": o conector registra os URIs de itens que falharam no download/extração (novo campo, ex.: `FailedUris` ou `RawDocument` marcado).
- `SyncViaConnectorAsync` trata itens falhados como **presentes** (keep doc existente + warning), nunca como deletados.
- `FetchResult` também ganha flag `Truncated` (listagem cortada por `maxFiles`/paginação); quando `Truncated`, o passe de deleção por "unseen" é **pulado** com warning explícito em `LastError`.

### RF-003 — Resolução única do diretório raiz (Azure)

- `directoryPath` e `prefix` não podem concatenar em `dir/dir`. Invariante: o gateway resolve o diretório raiz uma única vez — `prefix` filtra dentro de `directoryPath`, não o repete.
- Regressão coberta para: só `directoryPath`, só `prefix`, ambos, nenhum.

### RF-004 — Classificação correta de credencial Azure

- Valor é `connectionString` somente quando contém marcadores de connection string (`AccountName=`, `DefaultEndpointsProtocol`, `SharedAccessSignature`); caso contrário vai para `accountKey` mesmo com `=` de padding base64.

### RF-005 — Google Drive público funcional

- Arquivo único público: nome+extensão derivados do metadata público (campo `name`/`mimeType` do endpoint ou `Content-Disposition` do download); sem metadata, o item gera warning explícito — nunca sync silencioso sem docs.
- Fingerprint vazio (sem md5/data) → item tratado como **alterado** (reprocessa) — correção antes de custo.
- Sheets: `.csv` entra em `SupportedExtensions` com extração texto-plain (ou export muda para extensão já suportada) — planilhas passam a indexar.

### RF-006 — Falha de doc desfaz estado rastreado

- No catch por-documento (vault e connector), entradas do `ChangeTracker` tocadas pela iteração (doc e chunks adicionados/modificados) são `Detach`ed antes de continuar — o próximo `SaveChangesAsync` não persiste estado parcial.

### RF-007 — Fila cheia sem deadlock

- `EnqueueAsync`: quando `TryWrite` falha, o job persistido é marcado `failed` com erro `"queue full"` no mesmo fluxo — dedup não o reutiliza e o próximo ciclo enfileira normalmente.

### RF-008 — Delete de fonte com ordem segura + invalidação de busca

- Ordem invariante: (1) `SaveChangesAsync` com a fonte removida; (2) cleanup best-effort pós-commit (vetores, secrets, staging) — falha de cleanup nunca "ressuscita" a fonte nem deixa fonte sem vetores.
- Após delete bem-sucedido: bump de `index:version` + publish no invalidation bus (mesmo caminho do `BumpIndexVersionAsync`) — busca não serve docs da fonte excluída.

### RF-009 — Rollback apenas de transação ativa (pgvector)

- ANALYZE e criação de HNSW saem do `try` da transação (pós-commit, com try próprio e log), ou o catch verifica estado da tx antes de `RollbackAsync`. Falha de ANALYZE não marca ingestão como falha quando o commit ocorreu.

## 4. RNFs

- Incremental-sync preservado: fingerprint inalterado + sem reindex → zero download (caminho rápido intacto).
- Warnings de falha transitória continuam visíveis em `LastError`/job warnings.
- Zero mudança de contrato REST; `FetchResult` é contrato interno (adição de campos ok).

## 5. Fora de Escopo

- Retry com backoff para downloads (warning + keep é suficiente nesta entrega).
- Re-fetch on-demand de todos os stubs (só o caminho de reprocessamento, RF-001).
- Notion: mesmo padrão de stub (páginas inalteradas com texto vazio) é coberto pelo RF-001/RF-002 — sem conector novo.

## 6. Plano de Tarefas

1. `FetchResult` com `FailedUris` + `Truncated`; conectores emitem ambos; `SyncViaConnectorAsync` honra.
2. RF-001: reuse de `RawContent` + download sob demanda no reprocessamento.
3. Azure: resolução única do root + classificação `accountKey` vs `connectionString`.
4. Drive público: nome/extensão real, fingerprint vazio → changed, `.csv` em `SupportedExtensions`.
5. Detach no catch por-documento (vault + connector).
6. `EnqueueAsync` marca job failed quando `TryWrite` falha.
7. `DeleteAsync` reordenado + bump `index:version`.
8. `UpsertBatchAsync`: ANALYZE/HNSW pós-commit com guard de rollback.
9. Testes: reindex preserva conteúdo; falha de download não deleta; truncated não deleta; fila-cheia recupera; delete invalida cache; rollback não dispara pós-commit.
10. Suites completas + `dotnet format` + PR.

## 7. Critérios de Aceite

- [ ] Reindex forçado de fonte remota com docs inalterados preserva `RawContent` e re-chunka o texto real.
- [ ] Falha de download em 1 de N arquivos mantém o doc indexado e registra warning — sem delete.
- [ ] Listagem truncada por `maxFiles` não deleta docs além do corte.
- [ ] Fonte Azure com `directoryPath` definido lista arquivos (sem `dir/dir`); `accountKey` com `=` synca.
- [ ] Link público de arquivo Drive único indexa com nome/extensão real; edição em folder público reprocessa; Sheet indexa como `.csv`.
- [ ] Falha no doc N não impede `SaveChanges` correto do doc N+1.
- [ ] Fila cheia → job failed com causa; próximo autosync enfileira novo job sem restart.
- [ ] Delete de fonte: fonte some, vetores purgados, busca não serve docs deletados.
- [ ] Falha simulada de ANALYZE com commit ok não marca ingestão como falha.
