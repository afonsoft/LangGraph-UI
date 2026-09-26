# SPEC — Settings em abas + nova aba "Banco de Dados" com métricas

| Campo | Valor |
|---|---|
| Spec ID | `SPEC-20260926-settings-tabs-database-metrics` |
| Data | 2026-09-26 |
| Autor | Devin |
| Stack | `KnowledgeHub.Client` (Settings.razor, SettingsApiClient), `KnowledgeHub.Server` (SettingsEndpoints/DiagnosticsEndpoints), `KnowledgeHub.Shared` |
| Repository | `afonsoft/LangGraph-UI` |
| Status | `Done` |
| Ticket | GAP-UX-settings-tabs (user request 2026-09-26) |
| Origem | pedido direto do usuário — settings monolítica + visibilidade de banco |

## 1. User Story

**As a** administrador do Knowledge MCP Hub
**I want** a página Settings organizada em abas temáticas e uma aba "Banco de Dados" com provider e métricas
**So that** eu navego rápido entre áreas e enxergo saúde/tamanho/contagens do armazenamento sem abrir o SQLite na mão.

## 2. Contexto

`Settings.razor` (~750 linhas) hoje empilha seções `<h5>`: Chat (LLM) → GraphRAG → Integrações → Cache (+ log-level). A página cresce a cada feature e exige scroll longo. Não existe nenhuma visão consolidada do banco — provider EF (SQLite), tamanho do arquivo/WAL, contagens por entidade, estado do vector store (sqlite|sqlite-vec|postgres, dimensão, storage vector|halfvec) — só `/api/diagnostics/vectorstore` (wave 1) cobre a parte vetorial.

## 3. Requisitos Funcionais

### RF-001 — Abas na página Settings

- `BootstrapBlazor` `Tab`/`TabItem` (componente já usado no projeto — seguir convenção existente) com 5 abas na ordem:
  1. **Chat (LLM)** — seção atual de chat (endpoint/model/api key/test).
  2. **GraphRAG** — seção atual de Graph settings.
  3. **Integrações** — cards de providers (firecrawl/deepwiki/tavily/context7/gdrive/mcp proxies).
  4. **Cache** — stats card + clear + log-level (conteúdo atual).
  5. **Banco de Dados** — novo (RF-002/RF-003).
- Lazy-load dos dados por aba (cache/database só carregam quando a aba abre) ou manter load inicial simples se o padrão Tab do BootstrapBlazor renderizar tudo — decisão de implementação, registrar no código.
- Estado dos forms preservado ao trocar de aba (mesmo componente, sem remount).

### RF-002 — Backend `GET /api/settings/database`

Novo endpoint no grupo `/api/settings` (mesma auth/policy dos irmãos) retornando `DatabaseStatsDto`:

- `provider` — `sqlite` (EF provider name; postgres se um dia houver).
- `dataSource` — caminho do arquivo (mascarar senha se connstring).
- `fileSizeBytes`, `walSizeBytes` — quando arquivo SQLite local.
- `pageCount`, `pageSizeBytes`, `freelistCount` — `PRAGMA page_count/page_size/freelist_count`.
- `tables` — lista `{name, rowCount}` para as entidades do `KnowledgeHubDbContext` (Sources, Documents, Chunks, Threads, ThreadMessages, Users, ApiKeys, ApiKeyUsageEvents, IntegrationSecrets, EvalRuns, EvalBaselines, SecurityEvents, KgNodes, KgEdges, KgAliases, IngestionJobs, Approvals, Settings).
- `cache` — `PRAGMA cache_size` + `page_cache` stats quando disponíveis.
- `migrations` — última linha de `__EFMigrationsHistory` (id + applied count).
- `vectorStore` — reuso do payload de `/api/diagnostics/vectorstore` (provider, dimension, chunk count, storage vector|halfvec) para visão única.
- Fail-soft por seção: PRAGMA/stats indisponíveis → `null` + flag, nunca 500.

### RF-003 — Aba "Banco de Dados" (UI)

- Cards: Provider+path, Tamanho (db+wal formatado), Integridade (freelist %), Tabela de contagens por entidade (duas colunas, sortable), Vector store (provider/dims/chunks/storage), Migrations (última aplicada).
- Botão "Atualizar" (refetch). Loading skeleton/spinner por card.
- Sem ações destrutivas — read-only nesta SPEC.

## 4. Requisitos Não-Funcionais

- Endpoint < 300ms em DB típico; contagens via `COUNT(*)` (tabelas pequenas/médias — sem estimativas complexas).
- Layout mobile-first: tabs scrolláveis horizontalmente em telas estreitas.
- Strings em pt-BR consistentes com o resto da página.

## 5. Fora de Escopo

- Ações de manutenção (VACUUM, checkpoint WAL, backup) — futura SPEC.
- Postgres admin stats específicos (pg_stat_*) — o DTO já prevê `null` para não-SQLite.

## 6. Plano de Tarefas

1. `DatabaseStatsDto` (+ filhos) em `KnowledgeHub.Shared`.
2. `IDatabaseStatsService`/impl: EF provider, file/WAL sizes, PRAGMAs, contagens, migrations, vector info (reusar serviço do diagnostics).
3. `GET /api/settings/database` + testes de integração (auth 401, shape, contagens batem com seed).
4. `SettingsApiClient.GetDatabaseStatsAsync`.
5. Settings.razor: `Tab` com 5 itens; mover markup existente; nova seção Banco de Dados.
6. `dotnet format` + build + testes verdes; screenshot da página nas 5 abas.

## 7. Critérios de Aceite

- [ ] `/settings` mostra 5 abas na ordem pedida; conteúdo pré-existente intacto e funcional.
- [ ] Aba Cache continua exibindo provider/stats/log-level (integra com SPEC redis-stats fix).
- [ ] `GET /api/settings/database` autenticado retorna provider, fileSize, contagens por tabela e vectorStore; sem auth → 401.
- [ ] Números exibidos na UI batem com `sqlite3 knowledgehub.db "SELECT count(*)..."` para ≥3 tabelas.
- [ ] Teste de integração do endpoint + build/testes verdes.
