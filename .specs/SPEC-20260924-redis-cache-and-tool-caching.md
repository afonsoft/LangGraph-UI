# SPEC-20260924-redis-cache-and-tool-caching

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `redis-cache-and-tool-caching` |
| Type | `Feature / Performance` |
| Stack | `.NET 10 (StackExchange.Redis + ASP.NET Core + Blazor WASM + BootstrapBlazor)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Antigravity-20260924-redis-cache-and-tool-caching` |
| Ticket | [#181](https://github.com/afonsoft/LangGraph-UI/issues/181) |
| Status | `Approved` |

## 1. User Story

**As a** operador ou cliente conectado às ferramentas (MCP) e RAG do KnowledgeHub
**I want** cache distribuído resiliente com Redis, cache automático de ferramentas de leitura/perguntas com TTL mínimo de 1 hora, e uma aba de gerenciamento na tela Settings para visualizar chaves, tamanho e botão de limpeza total
**So that** perguntas ou chamadas repetidas sejam respondidas instantaneamente (< 5ms) sem gastar tokens nem latência de LLM/busca vetorial, com controle operacional transparente do cache pela interface.

**Problem context:**
Atualmente:
1. O suporte a Redis existe como opção em `Cache:Provider = "redis"`, mas faltava validação resiliente com fallback suave para memória se o Redis cair ou estiver indisponível.
2. Chamadas de ferramentas MCP (`CallToolHandler`) não possuem cache automático de respostas: se o usuário ou agente perguntar a mesma coisa repetidamente (ex.: `knowledge_ask`, `knowledge_search`), a LLM e o pipeline de busca rodam do zero todas as vezes, elevando custo e latência.
3. Não há controle ou visibilidade do cache na interface: o operador não consegue saber quantas chaves existem, quais são, o tamanho em memória ou forçar a limpeza imediata em caso de testes ou atualizações manuais.

## 2. Scope

**In scope:**
- **Resiliência e Fallback do Redis:**
  - Validação e conexão com `StackExchange.Redis` / `Microsoft.Extensions.Caching.StackExchangeRedis`.
  - Fallback gracioso para cache em memória caso a conexão com Redis falhe ou caia (fail-soft sem derrubar requisições).
- **Cache de Chamadas de Tools (Tool Invocations) e Perguntas RAG:**
  - Interceptação em `CallToolHandler` para ferramentas somente leitura (`readOnly: true` ou lista de tools idempotentes: `knowledge_search`, `knowledge_ask`, `find_*`, `read_*`).
  - Cache chaveado por SHA-256 do par `(ToolName, CanonicalArgumentsJson, IndexVersionToken)`.
  - **TTL mínimo estrito de 1 hora** (`TimeSpan.FromHours(1)` padrão, configurável com piso de 60 minutos).
  - Invalidação automática quando o `indexVersion` muda (após novo sync de dados).
- **Gerenciamento do Cache na Interface (`Settings.razor`):**
  - Nova aba "Cache do Sistema" no Settings.
  - Indicador de status do backend ativo (`Memory` ou `Redis`, latência/status da conexão, total de chaves e tamanho total aproximado em bytes/KB).
  - Tabela listando as chaves ativas (prefixo/região, chave, tamanho aproximado em bytes, TTL restante).
  - Botão de ação: **"Limpar todo o cache"** (Flush) com diálogo de confirmação (`PopConfirmButton`) e notificação toast.
- **Novos Endpoints REST em `SettingsEndpoints.cs`:**
  - `GET /api/settings/cache` — retorna métricas do cache, provedor ativo, contagem de chaves e lista detalhada com tamanho.
  - `POST /api/settings/cache/clear` — purga todas as chaves do cache no provedor ativo.
- **Serviço de Gerenciamento de Cache (`ICacheManagerService`):**
  - Abstração para inspecionar chaves, obter tamanhos e realizar `ClearAllAsync()`.

**Out of scope:**
- Cache de ferramentas com efeitos colaterais (`readOnly: false` como `write_obsidian_note`, etc.).
- Persistência permanente em disco do Redis gerenciada pelo KnowledgeHub (o Redis é considerado volátil/cache).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/Caching/` — novos `ICacheManagerService.cs`, `CacheManagerService.cs`, suporte a inspeção e limpeza.
- `src/KnowledgeHub.Server/Configuration/CacheOptions.cs` — novas opções de cache de tools (`ToolCacheEnabled`, `ToolCacheTtlMinutes = 60`).
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` — registro do cache resiliente e interceptação com cache no `CallToolHandler`.
- `src/KnowledgeHub.Server/Api/SettingsEndpoints.cs` — endpoints `GET /api/settings/cache` e `POST /api/settings/cache/clear`.
- `src/KnowledgeHub.Shared/Contracts/CacheSettingsDtos.cs` — DTOs `CacheStatsDto`, `CacheKeyItemDto`.
- `src/KnowledgeHub.Client/Services/SettingsApiClient.cs` — métodos de cliente para consulta e limpeza de cache.
- `src/KnowledgeHub.Client/Pages/Settings.razor` — aba de visualização das chaves, tamanhos e botão de limpeza.
- `tests/KnowledgeHub.Tests.Unit/` — testes de resiliência, cálculo de TTL >= 1h, serialização e inspeção de chaves.

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/CacheSettingsDtos.cs           (create)
src/KnowledgeHub.Server/Caching/ICacheManagerService.cs         (create)
src/KnowledgeHub.Server/Caching/CacheManagerService.cs          (create)
src/KnowledgeHub.Server/Configuration/CacheOptions.cs           (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modify)
src/KnowledgeHub.Server/Api/SettingsEndpoints.cs                (modify)
src/KnowledgeHub.Client/Services/SettingsApiClient.cs           (modify)
src/KnowledgeHub.Client/Pages/Settings.razor                    (modify)
tests/KnowledgeHub.Tests.Unit/Server/CacheManagerServiceTests.cs (create)
tests/KnowledgeHub.Tests.Unit/Server/ToolCachingTests.cs        (create)
```

## 4. Requirements

### RF-001: Resiliência de Conexão Redis
- **Description:** A inicialização e execução do Redis devem utilizar fallback seguro para memória se o servidor Redis estiver inacessível.
- **Rules:** Se o Redis cair durante a execução, o `SafeCache` registra log de aviso e opera em modo degradação sem travar requisições.

### RF-002: Cache de Respostas de Ferramentas (Tool Caching)
- **Description:** Ferramentas MCP de leitura devem consultar o cache antes de executar o handler.
- **Rules:**
  - Argumentos canônicos normalizados e hasheados com SHA-256 junto com o nome da tool e o `indexVersion`.
  - TTL mínimo de **1 hora** (`Math.Max(config.ToolCacheTtlMinutes, 60)`).
  - Se houver hit no cache, retorna `CallToolResult` em < 5ms marcando métrica de cache hit.
  - Apenas tools com anotação `ReadOnlyHint = true` (ou na lista de queries) são cacheadas.

### RF-003: Endpoint e Operação de Limpeza Total do Cache
- **Description:** `POST /api/settings/cache/clear` deve expurgar todas as chaves do cache no provedor ativo (Memory ou Redis).
- **Rules:** Autenticado com `AuthPolicies.Operational`. Retorna status 200 com confirmação de chaves removidas.

### RF-004: Aba de Cache na Tela Settings
- **Description:** Aba "Cache do Sistema" em `Settings.razor` contendo:
  - Card com resumo: Provider (`Memory` ou `Redis`), Total de Chaves, Tamanho Estimado Total, Hits e Misses.
  - Tabela com lista de chaves: Nome da Chave, Tamanho (Bytes/KB), Expiração/TTL restante.
  - Botão de ação "Limpar todo o cache" com PopConfirm.

## 5. API Contract

### Obter Estatísticas e Chaves do Cache
**Endpoint:** `GET /api/settings/cache`  
**Auth:** Cookie Session / Operational  
**Response 200 OK:**
```json
{
  "provider": "redis",
  "isConnected": true,
  "totalKeys": 42,
  "totalSizeBytes": 128450,
  "hits": 310,
  "misses": 45,
  "keys": [
    {
      "key": "mcp:tool:knowledge_ask:a1b2c3d4",
      "prefix": "mcp:tool",
      "sizeBytes": 2450,
      "expiresInSeconds": 3420
    },
    {
      "key": "search:hybrid:10:topk:hash123",
      "prefix": "search",
      "sizeBytes": 15200,
      "expiresInSeconds": 2100
    }
  ]
}
```

### Limpar Todo o Cache
**Endpoint:** `POST /api/settings/cache/clear`  
**Auth:** Cookie Session / Operational  
**Response 200 OK:**
```json
{
  "success": true,
  "clearedKeys": 42,
  "message": "Cache completamente limpo com sucesso."
}
```

## 6. Acceptance Criteria

- [ ] **Given** uma chamada à tool `knowledge_ask` com argumentos idênticos dentro de 1 hora **when** a mesma chamada é executada **then** a resposta é retornada do cache em menos de 10ms sem invocar o LLM.
- [ ] **Given** a configuração de TTL para o cache de tools **when** o valor configurado for inferior a 60 minutos **then** o sistema força o piso mínimo de 1 hora.
- [ ] **Given** um novo sync de documentos finalizado com sucesso **when** a versão do índice (`indexVersion`) é atualizada **then** os caches de busca e tools associados à versão anterior expiram automaticamente.
- [ ] **Given** a tela Settings aberta na aba Cache **when** o usuário visualiza a seção **then** as chaves ativas e seus respectivos tamanhos estimados são exibidos.
- [ ] **Given** o usuário clicando em "Limpar todo o cache" e confirmando **when** a API responde **then** todas as chaves são purgadas e a lista fica zerada com notificação toast de sucesso.

## 7. Task Plan

- [ ] **T1:** Criar DTOs `CacheSettingsDtos.cs` com contratos de estatísticas e chaves.
- [ ] **T2:** Implementar `ICacheManagerService` e `CacheManagerService` com suporte a listagem de chaves e `ClearAllAsync` para Memory e Redis.
- [ ] **T3:** Implementar interceptação de cache no `CallToolHandler` garantindo TTL mínimo de 1 hora.
- [ ] **T4:** Implementar endpoints em `SettingsEndpoints.cs` (`GET /api/settings/cache`, `POST /api/settings/cache/clear`).
- [ ] **T5:** Atualizar `SettingsApiClient.cs` e adicionar aba Cache em `Settings.razor`.
- [ ] **T6:** Criar testes unitários para o `CacheManagerService` e para o interceptador de tools.

## 8. Organization Guardrails

- **Branches:** nunca comitar em `main` ou `develop`.
- **Segurança:** nunca expor senhas ou tokens do Redis na interface ou DTOs de configuração.
- **Fail-soft:** a falha do cache nunca deve quebrar a execução de buscas ou de tools.

## 9. Definition of Done

- [ ] Requisitos RF-001 a RF-004 implementados e testados.
- [ ] TTL mínimo de 1 hora garantido por teste unitário.
- [ ] Tela Settings atualizada com botão de limpeza e aba de visualização.
- [ ] Build e testes passando sem warnings.
