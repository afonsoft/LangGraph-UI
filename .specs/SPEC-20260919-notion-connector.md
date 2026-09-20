# SPEC-20260919-notion-connector

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `notion-connector` |
| Type | `Feature` |
| Stack | `.NET 10 / HttpClient / Notion REST API` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260919-notion-connector` |
| Ticket | `#143` |
| Status | `Approved` |

## 1. User Story

**As a** administrador do KnowledgeHub
**I want** registrar um workspace Notion como fonte de conhecimento (`SourceType = Notion`) com um internal integration token
**So that** o RAG indexe páginas e databases do Notion e responda perguntas sobre eles — cobrindo a maior fonte de conhecimento corporativa que hoje não tem conector.

**Problem context:**
O pipeline de ingestão (`ISourceConnector` → `FetchResult` → dedup por hash → `MarkdownChunker` → embed) já suporta `WebPage` e `DocumentFile`; `RestApi`/`SqlDatabase` seguem não implementados e `McpProxy` não é ingerível. O Notion expõe uma REST API estável (`https://api.notion.com/v1`, header `Notion-Version`, Bearer `ntn_*`/`secret_*`) que permite listar tudo que foi compartilhado com a integração (`POST /search`), ler árvores de blocos (`GET /blocks/{id}/children`) e consultar databases (`POST /databases/{id}/query`). Páginas precisam ser compartilhadas com a integração no Notion — o conector só enxerga esse subconjunto, o que é a feature de segurança desejada.

Nota de escopo sobre a documentação consultada: as páginas "Notion AI Connectors" e "Using Notion AI" descrevem o produto do Notion puxando apps de terceiros *para dentro* do Notion (Slack, Drive, Jira) — direção inversa da nossa. Nossa integração usa apenas a **Notion REST API** (internal integration, read-only).

## 2. Scope

**In scope:**
- `SourceType.Notion = 7` no enum `src/KnowledgeHub.Shared/Contracts/SourceType.cs`.
- `NotionConnector` (`ISourceConnector`) que descobre conteúdo via `POST /search` (paginado, filtro `page`+`database`) ou, quando configurado, restringe a `rootPageIds`/`rootDatabaseIds`; renderiza blocos em texto/markdown-ish e emite um `RawDocument` por página/row.
- Config da source: `token` (movido ao `IIntegrationSecretStore`, chave `notion:{sourceId}`, config persiste só `hasKey`), `rootPageIds`, `rootDatabaseIds` (arrays de strings, opcionais), `maxPages` (default 200, clamp 1–1000), `apiBaseUrl` (default `https://api.notion.com`, override para testes), `apiVersion` (default `2022-06-28`).
- Sync incremental por fingerprint: `RawDocument` ganha `Fingerprint` opcional; documentos Notion usam `notion:{last_edited_time}` como `ContentHash`, e o conector recebe o mapa `UriReference → fingerprint armazenado` para pular o fetch de blocos de páginas não alteradas.
- Rate limiting Notion (~3 req/s): politeness ≥350 ms entre requests + respeito a `Retry-After` em 429; falha por item → `Warnings`, nunca aborta o sync.
- `KnowledgeSourceService`: `RequiredKeys[Notion]`, validação de token (`token` presente **ou** `hasKey=true` com segredo já armazenado), persistência do segredo no encrypted store (padrão `PersistProxySecretAsync`, generalizado ou ramificado para Notion), remoção do segredo no delete.
- Auto-sync: `SourceType.Notion` entra no whitelist de `VaultWatcherService.RunDueAutoSyncsAsync` (polling por `SyncIntervalMinutes`; sem `FileSystemWatcher`).
- UI `SourceEditDialog.razor`: seção Notion — campo senha para token (placeholder quando `hasKey`), textareas para `rootPageIds`/`rootDatabaseIds` (um ID por linha ou vírgula), `maxPages`; `ManagedKeys`, `Validate()` e `SourceEditModel` atualizados.
- Validação de conectividade: primeiro fetch chama `GET /users/me`; 401 → sync `failed` com `LastError` claro ("token inválido").

**Out of scope:**
- Webhooks de conexão do Notion (sync em tempo real) — SPEC futura.
- Integração pública / OAuth flow — só internal token.
- Tools MCP `notion_*` para consulta em tempo real pelo agente — o `McpProxy` genérico cobre o cenário, mas o MCP hospedado do Notion exige OAuth, não suportado hoje.
- Write-back (criar/editar páginas Notion) — read-only.
- Comentários, usuários/menções expandidas, e conteúdo binário de blocos `image`/`file`/`pdf`/`video` (emitidos como nota textual `[imagem]`/`[arquivo]` ou skip com warning).
- Sync incremental dentro de uma página (diff parcial) — fingerprint é por página inteira.
- "Notion AI Connectors" como produto — direção inversa, não integrável por nós.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Shared/Contracts/SourceType.cs` — novo membro do enum.
- `src/KnowledgeHub.Server/Ingestion/Connectors/` — `NotionConnector.cs` + `NotionBlockRenderer.cs` (flatten bloco→texto) + `NotionApiClient.cs` (HttpClient fino: search, block children, database query, users/me, throttle + Retry-After).
- `src/KnowledgeHub.Server/Ingestion/Connectors/ISourceConnector.cs` — `RawDocument` ganha `Fingerprint`; contrato incremental (ver RF-007).
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs` — monta o mapa `UriReference→ContentHash` existente e passa ao conector; compara `raw.Fingerprint ?? SHA256(text)`.
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs` — `RequiredKeys`, validação, segredo `notion:{sourceId}`, remoção no `DeleteAsync`.
- `src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs` — whitelist de auto-sync.
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` — `AddHttpClient("notion")` (timeout 30 s) + registro do conector.
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor` — campos Notion.
- `tests/KnowledgeHub.Tests.Unit` / `tests/KnowledgeHub.Tests.Integration` — renderer, validação, segredo, sync com API fake.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Ingestion/Connectors/{ISourceConnector,ConnectorConfig,WebPageConnector,DocumentFileConnector}.cs`
- `src/KnowledgeHub.Server/Ingestion/{IngestionService,MarkdownChunker,MarkdownNoteParser}.cs`
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs` (RequiredKeys, `PersistProxySecretAsync`, `Redact`)
- `src/KnowledgeHub.Server/Settings/{IIntegrationSecretStore,IntegrationSecretStore}.cs` + `Mcp/Upstream/McpProxySession.cs` (`SecretKey`)
- `src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs` (`RunDueAutoSyncsAsync`)
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor`
- `src/KnowledgeHub.Server/Domain/Entities/{KnowledgeSource,KnowledgeDocument}.cs`
- `.specs/SPEC-20260914-webpage-docfile-connectors.md`, `.specs/SPEC-20260917-mcp-proxy-source-type.md`

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/SourceType.cs                          (modify)
src/KnowledgeHub.Server/Ingestion/Connectors/ISourceConnector.cs         (modify — Fingerprint + incremental)
src/KnowledgeHub.Server/Ingestion/Connectors/NotionApiClient.cs          (create)
src/KnowledgeHub.Server/Ingestion/Connectors/NotionBlockRenderer.cs      (create)
src/KnowledgeHub.Server/Ingestion/Connectors/NotionConnector.cs          (create)
src/KnowledgeHub.Server/Ingestion/IngestionService.cs                    (modify)
src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs               (modify)
src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs        (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs       (modify)
src/KnowledgeHub.Client/Pages/SourceEditDialog.razor                     (modify)
tests/KnowledgeHub.Tests.Unit/…                                          (create)
tests/KnowledgeHub.Tests.Integration/…                                   (create)
```

## 4. Requirements

### RF-001: `SourceType.Notion` e configuração
- **Description:** enum `Notion = 7`; `configuration` aceita `token`, `rootPageIds`/`rootDatabaseIds` (string[]), `maxPages` (default 200, clamp 1–1000), `apiBaseUrl` (default `https://api.notion.com`), `apiVersion` (default `2022-06-28`).
- **Rules:** `RequiredKeys[Notion]` = `["token"]` com exceção documentada: update com `hasKey=true` e segredo já armazenado passa sem reenviar o token; `hasKey=true` sem segredo no store → 400. `apiBaseUrl` deve ser http(s) absoluto quando presente.
- **Input → Output:** POST `/api/sources` type=Notion + token → source criada, GET retorna `hasKey:true` e nunca o token.

### RF-002: `NotionApiClient` (HttpClient fino)
- **Description:** client sobre `IHttpClientFactory` ("notion") que encapsula `GET /v1/users/me`, `POST /v1/search`, `GET /v1/blocks/{id}/children`, `POST /v1/databases/{id}/query` com headers `Authorization: Bearer {token}` e `Notion-Version: {apiVersion}`.
- **Rules:** paginação via `start_cursor`/`has_more`/`next_cursor` (`page_size=100`); throttle interno ≥350 ms entre chamadas; em HTTP 429 lê `Retry-After` e aguarda (máx 3 retries por chamada); timeout 30 s; token nunca em log.
- **Input → Output:** chamadas tipadas → `JsonElement`/DTOs internos; erros HTTP mapeados (ver §5).

### RF-003: Descoberta e fetch de conteúdo
- **Description:** sem roots → `POST /search` paginado (`filter: page` e `database`); com `rootPageIds`/`rootDatabaseIds` → atravessa essas raízes (`child_page`/`child_database` recursivos, database→query de rows).
- **Rules:** cada página vira `RawDocument(UriReference: "notion://page/{id}", Title, TextContent)`; `Title` vem da propriedade title da página (fallback `id`); dedup/purge por URI já existente no pipeline (`seen`) cobre páginas descompartilhadas/deletadas; hard bound `maxPages` — ao atingir, warning "truncated at maxPages".
- **Input → Output:** `FetchResult` com documentos + warnings por item.

### RF-004: Renderização de blocos → texto
- **Description:** `NotionBlockRenderer` converte a árvore de blocos em texto/markdown-ish: `heading_1..3` → `#`/`##`/`###`, `paragraph`, `bulleted_list_item` → `- `, `numbered_list_item` → `1. `, `to_do` → `- [ ]`/`- [x]`, `quote`/`callout` → `> `, `code` → fenced block, `divider` → `---`, `table`+`table_row` → linhas `a | b | c`, `child_page`/`child_database` → descendem; `image`/`file`/`pdf`/`video`/`embed`/`bookmark` → `[tipo: caption-ou-url]`; tipos desconhecidos → skip silencioso.
- **Rules:** `rich_text` concatenado por `plain_text` (annotations ignoradas); recursão com `maxBlockDepth` (default 10) e `maxBlocksPerPage` (default 500) como hard bounds; blocos filhos paginados.
- **Input → Output:** `JsonElement` block children → `string` texto.

### RF-005: Databases
- **Description:** database → `POST /databases/{id}/query` paginado; cada row (que é uma page) vira documento com as **propriedades serializadas** como linhas `Nome: valor` no início do texto, seguidas dos blocos da row-page.
- **Rules:** tipos de propriedade comuns serializados (title, rich_text, number, select, multi_select, date, checkbox, url, email, people→name, relation→id, formula→valor); tipos não suportados → `key: (unsupported)`; row vazia de blocos ainda indexa pelas propriedades.
- **Input → Output:** rows → `RawDocument` com `UriReference: "notion://page/{rowId}"`.

### RF-006: Segredo do token
- **Description:** `configuration.token` é movido ao `IIntegrationSecretStore` na chave `notion:{sourceId}` (DataProtection); config persistida mantém só `hasKey`. Mesmas regras do `apiKey` McpProxy: `"***"`/ausente mantém, vazio remove. `DeleteAsync` remove o segredo.
- **Rules:** `SensitiveKeys` já cobre `token` na redação; GET nunca ecoa o valor; o conector resolve o segredo via `IIntegrationSecretStore` + `IServiceScopeFactory` (conector é singleton) — ausência de segredo → `InvalidOperationException` clara ("token Notion não configurado").
- **Input → Output:** POST com token → `hasKey:true`; POST sem token mas com segredo → sync funciona.

### RF-007: Sync incremental por fingerprint
- **Description:** `RawDocument` ganha `Fingerprint` opcional; `IngestionService` compara `raw.Fingerprint ?? SHA256(TextContent)` com `doc.ContentHash`. Conector Notion usa `notion:{last_edited_time}` (campo gratuito do `/search`/`query`) como fingerprint e **pula o fetch de blocos** quando o fingerprint bate — emite `RawDocument` com `TextContent=""` + `Fingerprint` para manter o URI em `seen`.
- **Rules:** implementação via interface opcional `IIncrementalSourceConnector` (método recebe `IReadOnlyDictionary<string,string>` `UriReference→ContentHash`) ou parâmetro extra — decisão de implementação, sem quebrar `WebPage`/`DocumentFile`; docs novos/alterados carregam `TextContent` completo e `ContentHash=fingerprint`; `RawContent`/`IndexedAt` de docs inalterados não são tocados.
- **Input → Output:** re-sync sem mudanças → 0 chamadas `blocks/children`, `DocumentsSkipped=N`.

### RF-008: Auto-sync, DI e UI
- **Description:** `SourceType.Notion` no whitelist de `RunDueAutoSyncsAsync`; `services.AddHttpClient("notion", 30s)` + registro do conector; `SourceEditDialog` com campos da seção Notion.
- **Rules:** `ManagedKeys` += `token`, `rootPageIds`, `rootDatabaseIds`, `maxPages`, `apiBaseUrl`, `apiVersion`; `Validate()` exige `token` ou `hasKey`; model ganha `Token`, `RootPageIds`, `RootDatabaseIds`, `MaxPages` (e `hasKey` já existe via `HasStoredKey`).
- **Input → Output:** dialog Notion salva config válida; `GET` subsequente popula campos (token nunca).

## 5. API Contract (outbound — Notion REST API)

**Base:** `{apiBaseUrl}/v1` (default `https://api.notion.com/v1`)
**Auth:** `Authorization: Bearer {token}` + `Notion-Version: {apiVersion}` (default `2022-06-28`)

| Endpoint | Uso |
| --- | --- |
| `GET /users/me` | probe de token no início do fetch |
| `POST /search` `{ query:"", filter:{property:"object",value:"page"|"database"}, page_size:100, start_cursor }` | descoberta workspace |
| `GET /blocks/{id}/children?page_size=100&start_cursor=` | árvore de blocos da página |
| `POST /databases/{id}/query` `{ page_size:100, start_cursor }` | rows de database |

**Erros esperados → comportamento:**
- `401 unauthorized` → sync `failed`, `LastError="token Notion inválido"`.
- `403 restricted_resource` / `404 object_not_found` num item → warning por item ("não compartilhado com a integração").
- `429 rate_limited` → honrar `Retry-After`, máx 3 retries; esgotado → warning por item.
- `5xx`/timeout → warning por item; falha no probe inicial (`users/me`) → sync `failed`.
- Sem novos endpoints inbound — reuso total de `/api/sources`.

## 6. Acceptance Criteria

- [ ] **CA-001:** **Given** source Notion com token válido e workspace com páginas compartilhadas, **when** `POST /api/sources/{id}/sync`, **then** páginas aparecem como documentos e respondem em `search_knowledge`/`query_{slug}`.
- [ ] **CA-002:** **Given** re-sync sem alterações no Notion, **when** sync roda, **then** `DocumentsSkipped=N`, `DocumentsProcessed=0` e nenhuma chamada `blocks/children` é feita (verificável no handler fake).
- [ ] **CA-003:** **Given** página editada no Notion (novo `last_edited_time`), **when** sync roda, **then** só ela é re-fetchada, re-chunked e re-embedada.
- [ ] **CA-004:** **Given** página descompartilhada/deletada, **when** sync roda, **then** documento e chunks/vetores são removidos.
- [ ] **CA-005:** **Given** database com rows, **when** sync roda, **then** cada row indexa com propriedades serializadas + blocos.
- [ ] **CA-006:** **Given** token inválido, **when** sync roda, **then** `LastSyncStatus=failed` + `LastError` indica token inválido; nenhum documento criado.
- [ ] **CA-007:** **Given** resposta 429 com `Retry-After`, **when** fetch, **then** o client aguarda e retenta (máx 3) antes de virar warning.
- [ ] **CA-008:** **Given** `GET /api/sources/{id}`, **then** `configuration` nunca contém `token` — só `hasKey`.
- [ ] **CA-009:** **Given** workspace maior que `maxPages`, **when** sync roda, **then** fetch trunca no bound e registra warning.
- [ ] **CA-010:** **Given** `AutoSyncEnabled=true`, **then** `RunDueAutoSyncsAsync` dispara sync periódico para a source Notion.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| token ausente no create | config sem `token`/`hasKey` | 400 "Configuration key 'token' is required for Notion" |
| `hasKey:true` sem segredo | update malicioso/stale | 400 com mensagem clara |
| página vazia (sem blocos) | blocks children = [] | documento indexado só com título, ou skipped com warning (decisão: indexar — título é sinal) |
| bloco de tipo desconhecido | `unsupported`/`file` | nota textual ou skip sem falhar |
| `apiBaseUrl` inválida | `not-a-url` | 400 na validação |
| recursão profunda | aninhamento > `maxBlockDepth` | trunca com warning por página |
| segredo corrompido/indisponível | DataProtection falha | sync `failed` com erro claro, sem token em log |

## 7. Task Plan

- [ ] **T1 — Contratos:** `SourceType.Notion`, `RawDocument.Fingerprint`, contrato incremental no pipeline (`IngestionService` monta mapa e compara fingerprint). **Validação:** unit tests de hash/fingerprint.
- [ ] **T2 — `NotionApiClient`:** endpoints, paginação, throttle, `Retry-After`, mapeamento de erros. **Validação:** unit tests com `HttpMessageHandler` fake (429, 401, paginação).
- [ ] **T3 — `NotionBlockRenderer`:** flatten de todos os tipos suportados + bounds. **Validação:** unit tests com fixtures JSON de blocos.
- [ ] **T4 — `NotionConnector`:** discovery (search/roots), páginas, databases (RF-005), warnings por item, fingerprint skip. **Validação:** unit + integração com API fake.
- [ ] **T5 — Serviço/segredo/DI:** `RequiredKeys`, validação Notion, persistência do token no secret store, remoção no delete, `AddHttpClient("notion")`, registro, whitelist auto-sync. **Validação:** integration tests de CRUD/sync.
- [ ] **T6 — UI:** seção Notion no `SourceEditDialog` + `ManagedKeys`/`Validate`/model. **Validação:** build WASM + smoke manual.
- [ ] **T7 — Docs:** `CLAUDE.md`/`README` atualizados (novo conector, config keys, como criar a integração no Notion + compartilhar páginas).

**7.1 Validation strategy (.NET):** unit tests para renderer/client/validação; integration tests do pipeline com Notion API fake; `dotnet build` 0 warnings, `dotnet test` verde, `dotnet format --verify-no-changes`; cobertura ≥80% nos arquivos novos.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260919-notion-connector` a partir de `main`; nunca commitar em `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` protegido — sem alterações.
- **Secrets:** token Notion **somente** via `IIntegrationSecretStore`; nunca em `ConfigurationJson`, logs, `LastError` ou respostas; sem `.env`/secrets em commit.
- **Scope:** não implementar webhooks, OAuth, write-back ou tools `notion_*` — fora de escopo §2.
- **Architecture:** lógica de fetch no conector (Ingestion), sem regra de negócio em endpoints/componentes; conector não acessa `DbContext` diretamente — mapa de fingerprints vem do `IngestionService`.
- **Deps:** nenhuma dependência nova prevista — `System.Text.Json` + `HttpClient` bastam; se um SDK for proposto, justificar na revisão.

## 9. Definition of Done

- [ ] Todos os RFs (§4) implementados.
- [ ] CA-001..CA-010 cobertos por testes passando (unit + integration conforme §7.1).
- [ ] Edge cases da tabela tratados.
- [ ] `dotnet build` 0 warnings · `dotnet test` verde · `dotnet format --verify-no-changes` exit 0.
- [ ] Guardrails §8 respeitados — sem token em logs/respostas/persistência plana.
- [ ] Documentação (`CLAUDE.md`/README) menciona o conector Notion e o fluxo de compartilhamento no Notion.

**Next action after DoD:** `Status = Done` + PR na branch `feature/Devin-20260919-notion-connector` referenciando o ticket.

## Open Questions / Pending Ambiguity

- Nenhuma — design tree fechada na rodada de entrevista (2026-09-19).
