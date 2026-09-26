# SPEC-20260924-pgvector-rag-performance

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `pgvector-rag-performance` |
| Type | `Performance / Refactor` |
| Stack | `.NET 10 (ASP.NET Core + Npgsql + Pgvector + PostgreSQL 16/17)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Antigravity-20260924-pgvector-rag-performance` |
| Ticket | [#180](https://github.com/afonsoft/LangGraph-UI/issues/180) |
| Status | `Done` |

## 1. User Story

**As a** usuário e sistema de RAG do Knowledge MCP Hub
**I want** otimizar a indexação e busca vetorial no PostgreSQL com pgvector (HNSW, pooling de conexões, índices secundários e tuning de queries)
**So that** as consultas semânticas e híbridas no acervo de documentos e fontes de dados tenham tempos de resposta ultra-rápidos (< 20ms no banco), sem table scans e com alta eficiência sob concorrência.

**Problem context:**
Atualmente, a implementação de `PostgresVectorStore`:
1. Só cria o índice HNSW uma vez durante a inicialização (`EnsureInitializedAsync`), apenas se o número de linhas já for superior a `HnswThreshold` (10.000). Quando a base começa pequena ou nova e cresce, o índice nunca é criado, forçando busca linear (exact scan) sequencial cara em todas as queries.
2. Não possui índices B-Tree em `kh_embeddings (source_id)` ou `kh_embeddings (document_id)`. Como resultado, deleções (`DELETE FROM kh_embeddings WHERE document_id = $1`) e buscas filtradas por fontes (`AND source_id = ANY($4)`) realizam table scans completos.
3. Não define `hnsw.ef_search` em tempo de query na sessão (`SET LOCAL hnsw.ef_search = ...`), o que deixa o PostgreSQL usando o default, sacrificando velocidade ou precisão.
4. O `NpgsqlDataSource` não possui tuning explícito de pool (limites de conexões, multiplexing, keep-alive e timeouts de comando).
5. Falta índice GIN na coluna `metadata jsonb`, tornando lentos filtros futuros por metadados.

## 2. Scope

**In scope:**
- **Automação do Índice HNSW:**
  - Checagem e criação inteligente do índice HNSW após lotes de upsert (se atingido o threshold configurado) e comando/método de verificação sob demanda.
  - Redução do default de `HnswThreshold` de 10.000 para 1.000 linhas (com parametrização flexível).
  - Parâmetros configuráveis: `HnswM` (default 16), `HnswEfConstruction` (default 64), `HnswEfSearch` (default 40).
- **Tuning de Query em Execução:**
  - Execução de `SET LOCAL hnsw.ef_search = {options.HnswEfSearch}` dentro de transações de busca ou bloco otimizado para maximizar throughput.
- **Índices Secundários e de Suporte:**
  - `CREATE INDEX IF NOT EXISTS kh_embeddings_source_idx ON kh_embeddings (source_id);`
  - `CREATE INDEX IF NOT EXISTS kh_embeddings_document_idx ON kh_embeddings (document_id);`
  - `CREATE INDEX IF NOT EXISTS kh_embeddings_source_model_idx ON kh_embeddings (source_id, model);`
  - `CREATE INDEX IF NOT EXISTS kh_embeddings_metadata_gin_idx ON kh_embeddings USING gin (metadata);`
- **Tuning de Conexão e DataSource:**
  - Configuração do `NpgsqlDataSourceBuilder` com connection pool otimizado (`MinPoolSize`, `MaxPoolSize`, `ConnectionLifetime`, `CommandTimeout`).
  - Suporte a TCP Keep-Alive e cancelamento cooperativo.
- **Otimizações no Batch Upsert:**
  - Ajuste do tamanho máximo do lote (`BatchMax = 500`) e suporte a parametrização binária eficiente.
- **Testes de Performance e Unidade:**
  - Validação da criação dos índices, medição de latência em mock/banco de teste e garantia de backward compatibility.

**Out of scope:**
- Migração de SQLite para Postgres na camada de documentos/chunks relacionais (esses continuam no SQLite do host principal).
- Alteração dos modelos de embedding (ex: Onnx local / OpenAI continuam idênticos).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs` — índices HNSW, índices secundários, tuning de query e pooling.
- `src/KnowledgeHub.Server/VectorStore/PostgresOptions.cs` — novos parâmetros de tuning (`HnswEfSearch`, `MaxPoolSize`, `MinPoolSize`, `ConnectionTimeoutSeconds`).
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` — instanciação e opções do `PostgresVectorStore`.
- `tests/KnowledgeHub.Tests.Unit/VectorStore/PostgresVectorStoreTests.cs` — testes unitários e de queries DDL/SQL.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs`
- `src/KnowledgeHub.Server/VectorStore/PostgresOptions.cs`
- `src/KnowledgeHub.Server/Services/SearchService.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/VectorStore/PostgresOptions.cs          (modify)
src/KnowledgeHub.Server/VectorStore/PostgresVectorStore.cs        (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modify)
tests/KnowledgeHub.Tests.Unit/VectorStore/PostgresVectorStoreTests.cs (modify)
```

## 4. Requirements

### RF-001: Índices Secundários em `kh_embeddings`
- **Description:** Na inicialização do banco, o `PostgresVectorStore` deve criar índices em `source_id`, `document_id`, `(source_id, model)` e `metadata jsonb (GIN)`.
- **Rules:** Usar DDL idempotente (`CREATE INDEX IF NOT EXISTS`).
- **Input → Output:** Inicialização do store → Todos os índices de suporte criados no Postgres.

### RF-002: Criação Reativa do Índice HNSW pós-Upsert
- **Description:** Sempre que um lote de upsert for concluído e o total de registros ultrapassar `HnswThreshold` sem que o índice exista, o índice HNSW deve ser criado em background/assíncrono.
- **Rules:** Não bloquear upsert se o índice falhar; respeitar `PostgresOptions.HnswThreshold`.

### RF-003: Tuning de Busca com `hnsw.ef_search`
- **Description:** Durante o `SearchAsync`, a sessão deve aplicar `SET LOCAL hnsw.ef_search = {options.HnswEfSearch}` (ou em nível de comando) para equilibrar recall e latência.
- **Rules:** Default = 40 (configurável entre 10 e 400).

### RF-004: Tuning de Conexão no `NpgsqlDataSource`
- **Description:** O `NpgsqlDataSourceBuilder` deve configurar `MinPoolSize`, `MaxPoolSize`, `ConnectionIdleLifetime`, `ConnectionPruningInterval` e `CommandTimeout`.
- **Rules:** Valores configuráveis via `VectorStore:Postgres` no `appsettings.json`.

## 5. API Contract

Não adiciona novas APIs REST públicas; aprimora internamente as chamadas de `IVectorStore.SearchAsync` e `UpsertBatchAsync`.

Configuração em `appsettings.json`:
```json
{
  "VectorStore": {
    "Provider": "postgres",
    "ConnectionString": "Host=localhost;Database=knowledgehub;Username=kh;Password=secret",
    "Postgres": {
      "HnswThreshold": 1000,
      "HnswM": 16,
      "HnswEfConstruction": 64,
      "HnswEfSearch": 40,
      "BatchMax": 500,
      "MinPoolSize": 5,
      "MaxPoolSize": 50,
      "CommandTimeoutSeconds": 30
    }
  }
}
```

## 6. Acceptance Criteria

- [ ] **Given** uma tabela com registros menores que o threshold **when** batches de upsert ultrapassam `HnswThreshold` **then** o índice HNSW é criado automaticamente.
- [ ] **Given** uma busca vetorial com filtro por `sourceIds` **when** `SearchAsync` é chamado **then** a query usa o índice `source_id` evitando table scan.
- [ ] **Given** a exclusão de um documento **when** `DeleteByDocumentAsync` roda **then** a deleção utiliza o índice `kh_embeddings_document_idx`.
- [ ] **Given** a execução de busca vetorial **when** `SearchAsync` é disparado **then** o parâmetro `hnsw.ef_search` é aplicado na sessão.

## 7. Task Plan

- [x] **T1:** Adicionar propriedades em `PostgresOptions.cs` (`HnswEfSearch`, `MinPoolSize`, `MaxPoolSize`, etc.).
- [x] **T2:** Atualizar `EnsureInitializedAsync` para criar índices secundários (`source_id`, `document_id`, `metadata GIN`).
- [x] **T3:** Implementar checagem pós-batch para disparar criação do índice HNSW quando atingir o threshold.
- [x] **T4:** Aplicar `hnsw.ef_search` no `SearchAsync`.
- [x] **T5:** Otimizar `NpgsqlDataSourceBuilder` com os parâmetros do pool.
- [x] **T6:** Criar e executar testes unitários em `PostgresVectorStoreTests.cs`.

## 8. Organization Guardrails

- **Branches:** não comitar diretamente em `main` ou `develop`.
- **Compatibilidade:** suportar instâncias PostgreSQL sem pgvector mais recente de forma elegante (fail-open para exact scan).
- **Segurança:** nunca expor connection string nos logs.

## 9. Definition of Done

- [x] Requisitos RF-001 a RF-004 implementados.
- [x] Testes unitários passando.
- [x] Nenhuma regressão no suite existente.
