# SPEC-20260924-api-key-reveal-and-copy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `api-key-reveal-and-copy` |
| Type | `Feature` |
| Stack | `.NET 10 (ASP.NET Core + EF Core SQLite + Blazor WASM)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Antigravity-20260924-api-key-reveal-and-copy` |
| Ticket | [#176](https://github.com/afonsoft/LangGraph-UI/issues/176) |
| Status | `Done` |

## 1. User Story

**As a** desenvolvedor ou administrador utilizando o Knowledge MCP Hub
**I want** visualizar e copiar a chave de API completa diretamente a partir do popup "Detalhes e uso da chave"
**So that** eu possa integrar facilmente ferramentas externas (Cursor, Claude Desktop, scripts) sem a necessidade de recriar a chave caso ela não tenha sido copiada no momento da criação inicial.

**Problem context:**
Atualmente, no modal "Detalhes e uso da chave" (`_usageModal` em `ApiKeys.razor`), o botão de cópia executa `CopyText(_usageKey.Prefix)`, copiando apenas o prefixo de 12 caracteres (ex.: `aft_1a2b3c4d`), dando a impressão de que a chave está quebrada ou truncada.
Além disso, a entidade `ApiKey` armazena apenas o `KeyHash` (SHA-256) e o `Prefix`. Como o SHA-256 é uma função criptográfica unidirecional (one-way), o backend não armazena a chave original após o momento da criação.
Para permitir a recuperação e cópia da chave completa a qualquer momento com segurança, a entidade `ApiKey` precisa persistir o segredo criptografado de forma reversível utilizando o `IDataProtectionProvider` da aplicação (padrão já adotado em `IntegrationSecretStore`), disponibilizando um endpoint sob demanda para recuperação pelo dono da chave e uma interface intuitiva no Blazor com máscara, alternância de visibilidade e botão de cópia integral.

## 2. Scope

**In scope:**
- Adição da propriedade `ProtectedKey` (string nullable) na entidade `ApiKey` e mapeamento no `KnowledgeHubDbContext`.
- Criação de migration EF Core para adicionar a coluna `ProtectedKey` na tabela `ApiKeys`.
- Criptografia do segredo gerado no momento da criação da chave (`ApiKeyEndpoints.CreateAsync`) usando `IDataProtectionProvider` com finalidade (`purpose`) `"api-keys"`.
- Criação do DTO `ApiKeySecretDto(Guid Id, string? Secret, bool IsAvailable)` em `KnowledgeHub.Shared`.
- Criação do endpoint seguro `GET /api/apikeys/{id:guid}/secret` no `ApiKeyEndpoints`:
  - Protegido por autenticação de sessão cookie (`AuthPolicies.CookieSession`).
  - Restrito ao proprietário da chave (`k.UserId == CurrentUserId(http)`).
  - Retorna o segredo descriptografado (`IsAvailable: true`) ou indica indisponibilidade (`IsAvailable: false`) caso seja uma chave legada sem `ProtectedKey`.
- Adição do método `GetKeySecretAsync` no cliente `AuthApiClient` do Blazor.
- Atualização da UI em `ApiKeys.razor`:
  - No modal "Detalhes e uso da chave", exibir um componente visual com máscara (ex.: `aft_••••••••••••••••`), botão de alternar revelação (olho) e botão de copiar chave completa.
  - Carregamento sob demanda do segredo via `GetKeySecretAsync`.
  - Cópia da chave completa para o clipboard via JavaScript interoperability com toast de feedback ("Chave copiada com sucesso!").
  - Tratamento visual para chaves legadas (alerta de segredo indisponível criado na versão anterior).
- Testes unitários e de integração validando o ciclo de vida, persistência protegida, restrição de acesso e comportamento com chaves legadas.

**Out of scope:**
- Recuperação retroativa de segredos de chaves legadas criadas antes desta migração (matematicamente impossível a partir do SHA-256).
- Inclusão do segredo descriptografado na listagem geral `GET /api/apikeys` (para evitar tráfego de dados sensíveis em massa).
- Exposição do endpoint `/secret` para autenticação via Bearer token (somente sessões de usuário no painel administrativo podem acessar).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs` — inclusão da propriedade `ProtectedKey`.
- `src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs` — configuração da coluna `ProtectedKey`.
- `src/KnowledgeHub.Server/Migrations/` — nova migration EF Core.
- `src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs` — gravação do `ProtectedKey` no create e novo endpoint `GET /api/apikeys/{id:guid}/secret`.
- `src/KnowledgeHub.Shared/Contracts/AuthDtos.cs` — novo contrato `ApiKeySecretDto`.
- `src/KnowledgeHub.Client/Services/AuthApiClient.cs` — método `GetKeySecretAsync`.
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` — atualização visual do modal `_usageModal`.
- `tests/KnowledgeHub.Tests.Integration/AuthFlowTests.cs` — cenários de teste automatizado.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs`
- `src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs`
- `src/KnowledgeHub.Server/Auth/ApiKeyService.cs`
- `src/KnowledgeHub.Server/Settings/IntegrationSecretStore.cs`
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor`
- `tests/KnowledgeHub.Tests.Integration/AuthFlowTests.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs
src/KnowledgeHub.Server/Data/KnowledgeHubDbContext.cs
src/KnowledgeHub.Server/Migrations/YYYYMMDDHHMMSS_AddApiKeyProtectedKey.cs
src/KnowledgeHub.Server/Auth/ApiKeyEndpoints.cs
src/KnowledgeHub.Shared/Contracts/AuthDtos.cs
src/KnowledgeHub.Client/Services/AuthApiClient.cs
src/KnowledgeHub.Client/Pages/ApiKeys.razor
tests/KnowledgeHub.Tests.Integration/AuthFlowTests.cs
```

## 4. Requirements

### RF-001: Armazenamento Criptografado da Chave de API
- **Description:** No fluxo de criação de chaves (`POST /api/apikeys`), o segredo gerado deve ser persistido de forma reversível utilizando o serviço de Data Protection do ASP.NET Core (`IDataProtectionProvider`), mantendo o hash SHA-256 e o prefixo intactos para a autenticação.
- **Rules:**
  - A propriedade `ProtectedKey` em `ApiKey` é uma string nullable no banco de dados.
  - A criptografia deve utilizar `IDataProtector` criado com o escopo `"api-keys"`.
  - O segredo completo gerado continua seguindo o formato `aft_` + 32 caracteres hexadecimais minúsculos (total de 36 caracteres).
- **Input → Output:** `POST /api/apikeys` com `{ "name": "cursor-laptop" }` → Registro gravado com `KeyHash`, `Prefix` e `ProtectedKey`.

### RF-002: Endpoint de Recuperação do Segredo (`/secret`)
- **Description:** Disponibilizar o endpoint `GET /api/apikeys/{id:guid}/secret` para permitir a recuperação do segredo pelo proprietário da chave.
- **Rules:**
  - Requer política de autorização de sessão cookie (`AuthPolicies.CookieSession`).
  - Deve validar o proprietário: se a chave com o `id` informado não pertencer ao `CurrentUserId`, retornar `404 Not Found` (`{ error = "api key não encontrada" }`).
  - Se a chave for legada (`ProtectedKey == null`), retornar `200 OK` com `ApiKeySecretDto(id, Secret: null, IsAvailable: false)`.
  - Se `ProtectedKey` estiver preenchido, descriptografar com `IDataProtector` ("api-keys") e retornar `200 OK` com `ApiKeySecretDto(id, Secret: secret, IsAvailable: true)`.
  - Caso ocorra exceção ao descriptografar, logar aviso e retornar `200 OK` com `IsAvailable: false` e `Secret: null`.
- **Input → Output:** `GET /api/apikeys/{id}/secret` (autenticado) → `200 OK` com `ApiKeySecretDto`.

### RF-003: Interface do Modal "Detalhes e uso da chave"
- **Description:** Atualizar o modal `_usageModal` em `ApiKeys.razor` para exibir a chave completa protegida por máscara, controle de visibilidade e botão de cópia integral.
- **Rules:**
  - Ao carregar os detalhes da chave ou ao interagir, buscar os dados via `AuthApiClient.GetKeySecretAsync(key.Id)`.
  - Enquanto carrega, exibir indicador discreto ou manter a máscara com estado de carregamento.
  - Quando `IsAvailable == true`:
    - Campo de entrada ou código monospaçado com valor mascarado (ex.: `aft_••••••••••••••••••••••••••••••••`) por padrão.
    - Botão de alternar visibilidade (ícone `fa-solid fa-eye` / `fa-solid fa-eye-slash`) para revelar/ocultar o segredo em tela.
    - Botão "Copiar chave completa" (ícone `fa-solid fa-copy`). Ao clicar, envia a chave completa para a área de transferência via `JS.InvokeVoidAsync("navigator.clipboard.writeText", secret)` e exibe `Toast.Success("Copiado", "Chave completa copiada para a área de transferência.")`.
  - Quando `IsAvailable == false` (chave legada):
    - Exibir banner/badge informativo avisando que a chave foi criada anteriormente sem armazenamento seguro e que o segredo completo é irrecuperável.
    - Oferecer orientação de criar uma nova chave caso necessário.
- **Input → Output:** Clique no botão de cópia → Área de transferência preenchida com a chave completa de 36 caracteres.

### RF-004: Migration de Banco de Dados
- **Description:** Criar migration EF Core compatível com SQLite adicionando a coluna `ProtectedKey` (texto, nullable) na tabela `ApiKeys`.
- **Rules:** A migration deve ser executada automaticamente na inicialização da aplicação pelo `DatabaseMigrator`.

### RF-005: Cobertura de Testes Automatizados
- **Description:** Incluir testes de integração no projeto `KnowledgeHub.Tests.Integration` validando os fluxos da funcionalidade.
- **Rules:**
  - Teste de criação e leitura do segredo via `GET /api/apikeys/{id}/secret`.
  - Teste garantindo que um usuário não pode acessar o segredo da chave de outro usuário (retorno 404).
  - Teste de requisição não autenticada no endpoint `/secret` (retorno 401).
  - Teste de chave legada sem `ProtectedKey` retornando `IsAvailable: false` e `Secret: null`.

## 5. API Contract

### Endpoint: `GET /api/apikeys/{id:guid}/secret`
- **Auth:** Cookie Session (`AuthPolicies.CookieSession`)

**Response 200 OK (Chave disponível):**
```json
{
  "id": "c1a2b3c4-d5e6-7f8a-9b0c-1d2e3f4a5b6c",
  "secret": "aft_4f89d38c20164cfa9e31d4e0e49bb410",
  "isAvailable": true
}
```

**Response 200 OK (Chave legada / segredo indisponível):**
```json
{
  "id": "c1a2b3c4-d5e6-7f8a-9b0c-1d2e3f4a5b6c",
  "secret": null,
  "isAvailable": false
}
```

**Response 404 Not Found:**
```json
{
  "error": "api key não encontrada"
}
```

**Response 401 Unauthorized:** (quando chamada sem cookie de sessão válido).

## 6. Acceptance Criteria

- [x] **Given** um usuário autenticado que cria uma nova API key **when** a chave é salva **then** a propriedade `ProtectedKey` é gravada no banco criptografada e o segredo completo é exibido na modal de criação inicial.
- [x] **Given** um usuário que abre o modal "Detalhes e uso da chave" de uma chave recém-criada **when** ele visualiza os dados **then** a chave é exibida mascarada por padrão com opção de desmascarar.
- [x] **Given** um usuário no modal "Detalhes e uso da chave" **when** ele clica em "Copiar chave" **then** a chave completa (36 caracteres, formato `aft_<32hex>`) é copiada para a área de transferência e uma notificação de sucesso é exibida.
- [x] **Given** um usuário com uma chave legada (sem `ProtectedKey`) **when** ele abre o modal "Detalhes e uso da chave" **then** a interface exibe que o segredo completo está indisponível para chaves legadas.
- [x] **Given** um usuário autenticado A **when** ele tenta chamar `GET /api/apikeys/{id}/secret` de uma chave pertencente ao usuário B **then** a resposta é 404 Not Found.
- [x] **Given** uma requisição sem cookie de sessão **when** bate em `GET /api/apikeys/{id}/secret` **then** a resposta é 401 Unauthorized.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Falha na descriptografia da chave | `ProtectedKey` corrompido ou chave de proteção rotacionada | Retornar `IsAvailable: false` e logar warning no servidor sem estourar 500 para o cliente |
| Clipboard API indisponível no browser (contexto HTTP sem SSL) | Clique no botão Copiar | Tratar exceção no JSInterop graciosamente sem travar a interface Blazor |
| ID inexistente | `GET /api/apikeys/{random-guid}/secret` | Retornar `404 Not Found` |

## 7. Task Plan (agent execution)

- [x] **T1 — Discovery:** Ler arquivos de contexto (`ApiKey.cs`, `ApiKeyEndpoints.cs`, `IntegrationSecretStore.cs`, `ApiKeys.razor`, `AuthFlowTests.cs`) e validar contratos.
- [x] **T2 — Backend & Migration:**
  - Adicionar `ProtectedKey` à entidade `ApiKey` e mapeamento no `KnowledgeHubDbContext`.
  - Criar e aplicar migration EF Core.
  - Atualizar `ApiKeyEndpoints.CreateAsync` para proteger e persistir o segredo via `IDataProtectionProvider`.
  - Criar contrato `ApiKeySecretDto` em `KnowledgeHub.Shared`.
  - Implementar endpoint `GET /api/apikeys/{id:guid}/secret` no `ApiKeyEndpoints`.
- [x] **T3 — Frontend Blazor:**
  - Adicionar método `GetKeySecretAsync` no `AuthApiClient`.
  - Atualizar o modal `_usageModal` em `ApiKeys.razor` com máscara, alternância de visibilidade, cópia integral e tratamento de chaves legadas.
- [x] **T4 — Testes Automatizados:**
  - Adicionar testes de integração no `AuthFlowTests.cs` cobrindo o novo endpoint e cenários de sucesso, autorização, 404 e chaves legadas.
- [x] **T5 — Validação & DoD:**
  - Executar `dotnet build KnowledgeHub.slnx` e `dotnet test`.
  - Validar DoD (seção 9) e atualizar status para `Done`.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
|---|---|
| **.NET 10** | Testes de integração para os endpoints `/api/apikeys` e `/api/apikeys/{id}/secret`; build completo da solution sem erros ou warnings novos. |

## 8. Organization Guardrails

- **Branches:** nunca comitar em `main`, `master` ou `develop`. Usar `feature/Antigravity-20260924-api-key-reveal-and-copy`.
- **Workflows:** não modificar `.github/workflows/`.
- **Segurança:** nunca logar a chave em texto puro nos logs da aplicação; utilizar sempre `IDataProtectionProvider` com finalidade específica.
- **Escopo:** não expor a chave descriptografada em endpoints de listagem em massa (`GET /api/apikeys`).
- **Arquitetura:** manter regras de negócio e de persistência no backend; manter componentes Blazor desacoplados chamando o client de API.

## 9. Definition of Done

- [x] Todos os requisitos (seção 4) implementados.
- [x] Critérios de aceitação (seção 6) validados com testes automatizados passando.
- [x] Casos de borda (falha de descriptografia, chaves legadas, clipboard inacessível) tratados.
- [x] Build e testes da solution (`dotnet test`) passando sem falhas.
- [x] Guardrails da seção 8 respeitados.
- [x] Nenhuma chave ou segredo vazando em logs ou exceções.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/...` referencing the ticket.

## Open Questions / Pending Ambiguity

- Nenhuma pendência bloqueante identificada.
