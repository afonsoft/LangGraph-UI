# SPEC-20260924-cloud-storage-connectors

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `cloud-storage-connectors` |
| Type | `Feature` |
| Stack | `.NET 10 (ASP.NET Core + EF Core SQLite + Blazor WASM + Cloud Storage Clients)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Devin-20260925-cloud-storage-connectors` |
| Ticket | [#178](https://github.com/afonsoft/LangGraph-UI/issues/178) |
| Status | `Done` |

## 1. User Story

**As a** administrador ou analista de dados utilizando o KnowledgeHub
**I want** cadastrar e sincronizar fontes de dados armazenadas no AWS S3, Azure Files e OCI Storage na tela de Fontes
**So that** os documentos da nuvem corporativa (.pdf, .docx, .md, .txt, código) sejam baixados para uma pasta temporária de staging local, tratados, vetorizados no banco de dados e utilizados para responder perguntas e buscas no RAG.

**Problem context:**
Atualmente, o KnowledgeHub suporta fontes locais (`ObsidianVault`, `DocumentFile`), páginas web (`WebPage`), APIs externas (`RestApi`), bancos SQL (`SqlDatabase`), proxies MCP (`McpProxy`) e workspaces Notion (`Notion`).
Entretanto, a maior parte do acervo documental de empresas e equipes técnicas reside em serviços de armazenamento em nuvem: buckets S3 na AWS, compartilhamentos de arquivos no Azure Files e buckets/file storage na Oracle Cloud Infrastructure (OCI).
Não havia até o momento conectores para esses serviços. É necessário que o KnowledgeHub sincronize esses arquivos para uma pasta temporária de staging local, realize a extração textual multiformato, faça o chunking semântico e persista os vetores no banco de dados para alimentar o pipeline de RAG com sincronização incremental eficiente.

## 2. Scope

**In scope:**
- Novos membros no enum `SourceType`:
  - `AwsS3 = 8`
  - `AzureFiles = 9`
  - `OciStorage = 10`
- Criação do serviço de gerenciamento de staging local (`IStagingStorageService`):
  - Diretório isolado por fonte em `data/staging/{sourceId}/`.
  - Controle de ciclo de vida: download de arquivos novos/modificados e limpeza automática do diretório local quando a fonte for excluída.
- Implementação dos conectores de ingestão (`IIncrementalSourceConnector`):
  - `AwsS3Connector`: integração com buckets AWS S3 via AWS SDK (S3 Client), com suporte a prefixos, paginação e autenticação por `accessKeyId`/`secretAccessKey` e `region`.
  - `AzureFilesConnector`: integração com compartilhamentos de arquivo Azure Files via Azure Storage Files Shares SDK, com autenticação por connection string ou account name + key, com navegação recursiva de diretórios.
  - `OciStorageConnector`: integração com a API S3-Compatible da Oracle Cloud Infrastructure (OCI Object Storage), parametrizando namespace, region, bucket, customer secret key e endpoint customizado.
- Sincronização incremental:
  - Verificação de metadados remotos (ETag, LastModified, tamanho) contra o fingerprint armazenado no banco e arquivos no staging local, pulando downloads desnecessários.
- Tratamento e extração de texto multiformato:
  - Reutilização dos extratores consolidados de `DocumentFileConnector` para `.pdf`, `.docx`, `.md`, `.txt`, `.cs`, `.py`, `.json`, `.yaml`, etc.
  - Geração de `RawDocument` com URI representativa (ex.: `s3://bucket/prefix/doc.pdf`, `azure://share/path/doc.docx`, `oci://bucket/path/doc.pdf`).
  - Chunking semântico/código, geração de embeddings, indexação vetorial (`sqlite-vec`/`pgvector`), FTS e KnowledgeGraph (GraphRAG).
- Armazenamento seguro de credenciais sensíveis:
  - Segredos (chaves AWS, connection strings Azure, chaves OCI) armazenados de forma criptografada via `IIntegrationSecretStore` (`s3:{sourceId}`, `azure:{sourceId}`, `oci:{sourceId}`).
  - Máscara e proteção em `SourceEditDialog.razor`.
- Atualização da UI `SourceEditDialog.razor`:
  - Campos dinâmicos específicos para cada provedor (Bucket, Region, ShareName, Diretório/Prefixo, Filtro Glob de extensões, Credenciais).
- Auto-sync em `VaultWatcherService`:
  - Inclusão dos tipos `AwsS3`, `AzureFiles` e `OciStorage` no loop de auto-sync periódico.
- Testes unitários e de integração cobrindo conectividade, download de staging, extração e indexação vetorial.

**Out of scope:**
- Escrita ou upload de arquivos para a nuvem (pipeline estritamente read-only).
- Webhooks de eventos de nuvem em tempo real (EventBridge/Azure Event Grid) — sincronização é realizada por polling (manual ou auto-sync configurável).
- Autenticação federada complexa OIDC/OAuth para provedores de nuvem (usa chaves de acesso/conexão padrão de serviço).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Shared/Contracts/SourceType.cs` — inclusão de `AwsS3`, `AzureFiles`, `OciStorage`.
- `src/KnowledgeHub.Server/Ingestion/Staging/` — novo serviço `IStagingStorageService` e `StagingStorageService`.
- `src/KnowledgeHub.Server/Ingestion/Connectors/` — novos conectores `AwsS3Connector.cs`, `AzureFilesConnector.cs`, `OciStorageConnector.cs`.
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs` — validação de parâmetros, chaves obrigatórias e persistência de credenciais no `IIntegrationSecretStore`.
- `src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs` — inclusão dos novos tipos no loop de auto-sync.
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` — injeção dos conectores e serviços de storage.
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor` — formulários de configuração específicos para AWS S3, Azure Files e OCI.
- `tests/KnowledgeHub.Tests.Unit/` e `tests/KnowledgeHub.Tests.Integration/` — testes de unidade e integração.

**Files to read before implementing:**
- `src/KnowledgeHub.Shared/Contracts/SourceType.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/ISourceConnector.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/DocumentFileConnector.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/NotionConnector.cs`
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs`
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs`
- `src/KnowledgeHub.Server/Settings/IIntegrationSecretStore.cs`
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor`

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/SourceType.cs                               (modify)
src/KnowledgeHub.Server/Ingestion/Staging/IStagingStorageService.cs          (create)
src/KnowledgeHub.Server/Ingestion/Staging/StagingStorageService.cs           (create)
src/KnowledgeHub.Server/Ingestion/Connectors/AwsS3Connector.cs               (create)
src/KnowledgeHub.Server/Ingestion/Connectors/AzureFilesConnector.cs          (create)
src/KnowledgeHub.Server/Ingestion/Connectors/OciStorageConnector.cs          (create)
src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs                   (modify)
src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs            (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs           (modify)
src/KnowledgeHub.Client/Pages/SourceEditDialog.razor                         (modify)
tests/KnowledgeHub.Tests.Unit/Server/Ingestion/CloudStorageConnectorsTests.cs (create)
tests/KnowledgeHub.Tests.Integration/CloudStorageIngestionTests.cs           (create)
```

## 4. Requirements

### RF-001: Extensão do Enum `SourceType`
- **Description:** O enum `SourceType` deve conter `AwsS3 = 8`, `AzureFiles = 9` e `OciStorage = 10`.
- **Rules:** Serializado como string em contratos JSON e compatível com as regras de filtro existentes do `SearchService`.
- **Input → Output:** `SourceType.AwsS3` serializa como `"AwsS3"`.

### RF-002: Gerenciador de Pasta de Staging Local (`IStagingStorageService`)
- **Description:** Serviço responsável por isolar, criar e gerenciar a pasta local temporária de staging para sincronização de arquivos de fontes remotas.
- **Rules:**
  - Diretório padrão: `Path.Combine(AppContext.BaseDirectory, "data", "staging", sourceId.ToString())`.
  - Método `GetStagingPath(Guid sourceId)`: retorna o caminho absoluto e assegura a criação do diretório.
  - Método `CleanupStagingAsync(Guid sourceId)`: remove recursivamente o diretório local quando a fonte for deletada ou quando solicitado expurgo.
  - Preservação entre syncs: os arquivos locais permanecem na pasta como cache para permitir comparações de modificação (ETag / LastModified) no sync incremental.
- **Input → Output:** `sourceId` → Diretório `data/staging/{sourceId}/` pronto para receber downloads.

### RF-003: Conector AWS S3 (`AwsS3Connector`)
- **Description:** Conector `IIncrementalSourceConnector` para listar e sincronizar objetos de um bucket AWS S3.
- **Rules:**
  - Parâmetros em `ConfigurationJson`: `bucketName` (obrigatório), `region` (obrigatório, ex.: `us-east-1`), `prefix` (opcional), `glob` (opcional, default: `**/*`), `maxFileSizeMB` (default: 20MB, clamp 1-512MB), `accessKeyId` e `secretAccessKey` (armazenado de forma segura no secret store).
  - Paginação transparente via `ListObjectsV2Async`.
  - Sync incremental: compara `ETag` e `LastModified` do S3 com os metadados existentes; objetos não modificados são emitidos com `Fingerprint` anterior sem re-baixar o arquivo.
  - Download do arquivo novo/modificado para `data/staging/{sourceId}/{s3Key}`.
  - Extração de texto usando os parsers multiformato (PdfPig para `.pdf`, OpenXML para `.docx`, etc.).
  - Geração de `RawDocument` com URI `s3://{bucketName}/{s3Key}` e texto extraído.
- **Input → Output:** Bucket S3 com arquivos `.pdf`, `.docx`, `.md` → `FetchResult` com `RawDocument`s e avisos de arquivos ignorados.

### RF-004: Conector Azure Files (`AzureFilesConnector`)
- **Description:** Conector `IIncrementalSourceConnector` para sincronizar compartilhamentos de arquivos do Azure Storage Files Shares.
- **Rules:**
  - Parâmetros: `shareName` (obrigatório), `directoryPath` (opcional, raiz do share se omitido), `glob` (opcional), `maxFileSizeMB` (default 20MB), e credencial segura: `connectionString` OU `accountName` + `accountKey` (no secret store).
  - Varredura recursiva de diretórios e listagem de arquivos.
  - Sync incremental por `ETag` / `LastModified` do Azure File.
  - Download para `data/staging/{sourceId}/{relativePath}` e extração de texto.
  - Geração de `RawDocument` com URI `azure://{shareName}/{relativePath}`.
- **Input → Output:** Azure File Share → `FetchResult` com documentos prontos para chunking.

### RF-005: Conector OCI Storage (`OciStorageConnector`)
- **Description:** Conector `IIncrementalSourceConnector` para buckets do Oracle Cloud Infrastructure (OCI Object Storage) via S3-Compatible API.
- **Rules:**
  - Parâmetros: `namespace` (obrigatório), `region` (obrigatório, ex.: `sa-saopaulo-1`), `bucketName` (obrigatório), `prefix` (opcional), `glob` (opcional), `accessKeyId` e `secretAccessKey` (Customer Secret Key da OCI, no secret store).
  - Utiliza o S3 Client apontando para o ServiceURL customizado da OCI: `https://{namespace}.compat.objectstorage.{region}.oraclecloud.com`.
  - Sync incremental e download para `data/staging/{sourceId}/{key}`.
  - Geração de `RawDocument` com URI `oci://{bucketName}/{key}`.
- **Input → Output:** Bucket OCI → `FetchResult` com documentos indexados.

### RF-006: Armazenamento Seguro de Credenciais
- **Description:** Chaves secretas da AWS, Azure e OCI nunca são expostas em texto puro na tabela de fontes (`KnowledgeSources.ConfigurationJson`).
- **Rules:**
  - Ao salvar fonte do tipo `AwsS3`, o `secretAccessKey` é gravado em `IIntegrationSecretStore` sob a chave `s3:{sourceId}`; a configuração JSON armazena apenas `hasKey: true`.
  - Ao salvar fonte `AzureFiles`, a `connectionString` ou `accountKey` é gravada sob `azure:{sourceId}`; a configuração JSON armazena `hasKey: true`.
  - Ao salvar fonte `OciStorage`, o `secretAccessKey` é gravado sob `oci:{sourceId}`; a configuração JSON armazena `hasKey: true`.
  - Ao excluir a fonte (`KnowledgeSourceService.DeleteAsync`), o segredo é removido de `IIntegrationSecretStore` e os arquivos de staging são excluídos por `IStagingStorageService.CleanupStagingAsync`.
- **Input → Output:** POST `/api/sources` com segredo → Secret protegido, config persistido apenas com `hasKey: true`.

### RF-007: Interface de Configuração (`SourceEditDialog.razor`)
- **Description:** Interface dinâmica adaptada para configurar cada tipo de fonte de nuvem.
- **Rules:**
  - Seleção de `AwsS3`: exibe campos para `Bucket *`, `Região *` (ex.: `us-east-1`), `Access Key ID *`, campo de senha para `Secret Access Key` (com placeholder quando `hasKey: true`), `Prefixo / Pasta` e `Filtro Glob` (ex.: `**/*.{pdf,docx,md,txt}`).
  - Seleção de `AzureFiles`: exibe campos para `Share Name *`, `Diretório raiz`, `Connection String / Account Key *` (campo protegido), `Filtro Glob`.
  - Seleção de `OciStorage`: exibe campos para `Namespace *`, `Região *`, `Bucket *`, `Access Key ID *`, campo de senha para `Secret Key` (protegido), `Prefixo` e `Filtro Glob`.
  - Validações no cliente antes do envio: campos obrigatórios sinalizados.
- **Input → Output:** Usuário preenche dados e salva → Fonte criada e configurada com sucesso.

### RF-008: Auto-sync e Tratamento Vetorial no RAG
- **Description:** Os novos tipos de fonte devem ser processados pelo ciclo de auto-sync em `VaultWatcherService` e disponibilizados nas respostas do RAG.
- **Rules:**
  - `VaultWatcherService.RunDueAutoSyncsAsync` inclui `AwsS3`, `AzureFiles` e `OciStorage` no filtro de tipos com polling automático.
  - Documentos ingeridos geram citações no RAG com links identificadores da origem (`s3://...`, `azure://...`, `oci://...`).
- **Input → Output:** Auto-sync acionado → Documentos atualizados na pasta de staging local, reindexados no vetor e disponíveis para busca.

## 5. API Contract

### Criação / Atualização de Fonte AWS S3
**Endpoint:** `POST /api/sources` / `PUT /api/sources/{id}`
**Auth:** Cookie Session (`AuthPolicies.CookieSession`)

**Payload (AwsS3):**
```json
{
  "name": "Manual e Políticas (S3)",
  "type": "AwsS3",
  "description": "Bucket de documentos internos da empresa",
  "configuration": {
    "bucketName": "empresa-documentos",
    "region": "us-east-1",
    "prefix": "politicas/",
    "glob": "**/*.{pdf,docx,md,txt}",
    "maxFileSizeMB": 20,
    "accessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "secretAccessKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY"
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 60
}
```

**Payload (AzureFiles):**
```json
{
  "name": "Projetos Compartilhados (Azure)",
  "type": "AzureFiles",
  "description": "Azure File Share corporativo",
  "configuration": {
    "shareName": "arquivos-projetos",
    "directoryPath": "engenharia/especificacoes",
    "glob": "**/*",
    "maxFileSizeMB": 30,
    "connectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 120
}
```

**Payload (OciStorage):**
```json
{
  "name": "Manuais Técnicos (OCI)",
  "type": "OciStorage",
  "description": "Object Storage na Oracle Cloud",
  "configuration": {
    "namespace": "grb483920",
    "region": "sa-saopaulo-1",
    "bucketName": "docs-tecnicos",
    "prefix": "v1/",
    "glob": "**/*.pdf",
    "maxFileSizeMB": 50,
    "accessKeyId": "0982348092384029384",
    "secretAccessKey": "kjahsd892374haskdjh29384"
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 60
}
```

**Retorno 200 OK (GET /api/sources/{id}):**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Manual e Políticas (S3)",
  "type": "AwsS3",
  "description": "Bucket de documentos internos da empresa",
  "configuration": {
    "bucketName": "empresa-documentos",
    "region": "us-east-1",
    "prefix": "politicas/",
    "glob": "**/*.{pdf,docx,md,txt}",
    "maxFileSizeMB": 20,
    "accessKeyId": "AKIAIOSFODNN7EXAMPLE",
    "hasKey": true
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 60,
  "lastSyncStatus": "completed",
  "lastSyncAt": "2026-09-24T18:00:00Z"
}
```

## 6. Acceptance Criteria

- [ ] **Given** uma fonte `AwsS3` configurada com credenciais válidas **when** o sync é executado **then** os arquivos do bucket correspondentes ao filtro glob são baixados para `data/staging/{sourceId}/`, o texto é extraído, os chunks são vetorizados no banco e indexados para RAG.
- [ ] **Given** uma fonte `AzureFiles` com connection string **when** o sync é disparado **then** o conector navega a árvore de diretórios, baixa arquivos alterados para o staging e os vetoriza no KnowledgeHub.
- [ ] **Given** uma fonte `OciStorage` configurada com a S3-Compatible API da OCI **when** o sync é iniciado **then** os arquivos do bucket da OCI são sincronizados localmente e inseridos no índice vetorial.
- [ ] **Given** arquivos já sincronizados que não sofreram alteração no storage remoto (mesmo ETag/LastModified) **when** novo sync roda **then** o download é pulado e os chunks existentes são preservados (sync incremental rápido).
- [ ] **Given** um arquivo apagado ou renomeado no storage remoto **when** novo sync roda **then** o documento correspondente é removido do índice vetorial e do staging local.
- [ ] **Given** uma fonte de nuvem cadastrada com chaves secretas **when** a fonte é consultada via GET `/api/sources` **then** o campo do segredo nunca é retornado (retorna apenas `hasKey: true`).
- [ ] **Given** a exclusão de uma fonte de nuvem **when** `DeleteAsync` é chamado **then** o diretório local `data/staging/{sourceId}/` é limpo e as credenciais protegidas são excluídas do secret store.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Falha de autenticação / credenciais inválidas (403/Forbidden) | Chave secreta errada | Marcar sync como `failed` e gravar `LastError` detalhado no banco sem derrubar a aplicação |
| Bucket ou File Share inexistente (404/NotFound) | Nome incorreto de bucket | Marcar sync como `failed` com mensagem `"bucket ou compartilhamento não encontrado"` |
| Arquivo acima de `maxFileSizeMB` | Arquivo de 200MB quando o limite é 20MB | Pular o arquivo e registrar nos `Warnings` do sync |
| Extensão de arquivo não suportada (ex.: `.exe`, `.mp4`) | Arquivo binário sem extrator | Pular o arquivo com warning nos metadados do sync |
| Falha transitória de rede durante download | Perda de conexão no meio de um arquivo | Limpar arquivo parcial local e registrar aviso |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery & Modelagem:**
  - Validar pacotes de clientes para S3 e Azure Files no `.NET 10` (`AWSSDK.S3` ou cliente REST S3 leve, `Azure.Storage.Files.Shares`).
  - Adicionar membros no enum `SourceType` em `KnowledgeHub.Shared`.
- [ ] **T2 — Staging & Armazenamento Seguro:**
  - Implementar `IStagingStorageService` / `StagingStorageService`.
  - Integrar segredos de `AwsS3`, `AzureFiles` e `OciStorage` no `KnowledgeSourceService` com `IIntegrationSecretStore`.
- [ ] **T3 — Implementação dos Conectores:**
  - Implementar `AwsS3Connector` com sync incremental e download em staging.
  - Implementar `AzureFilesConnector` com varredura de diretórios e staging.
  - Implementar `OciStorageConnector` reutilizando a interface S3-compatible da OCI.
  - Registrar conectores em `IngestionService` e `KnowledgeHubServiceCollectionExtensions`.
  - Atualizar `VaultWatcherService` para incluir os novos tipos no auto-sync.
- [ ] **T4 — Frontend Blazor:**
  - Atualizar `SourceEditDialog.razor` e `SourceEditModel` com campos específicos para AWS S3, Azure Files e OCI.
- [ ] **T5 — Testes e Validação:**
  - Criar testes de unidade e integração cobrindo os conectores, staging, validação de configuração e fluxo de ingestão vetorial.
  - Executar `dotnet build KnowledgeHub.slnx` e `dotnet test`.
  - Validar DoD (seção 9) e atualizar status para `Done`.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
|---|---|
| **.NET 10** | Testes de integração para `IIncrementalSourceConnector` emulando S3, Azure Files e OCI; testes de unidade para `StagingStorageService`; build sem warnings ou erros. |

## 8. Organization Guardrails

- **Branches:** nunca comitar em `main`, `master` ou `develop`. Usar `feature/Devin-20260925-cloud-storage-connectors`.
- **Workflows:** não modificar `.github/workflows/`.
- **Segurança:** nunca gravar segredos de nuvem (secret access keys, connection strings) em texto puro no banco de dados ou logs; utilizar sempre `IIntegrationSecretStore`.
- **Performance:** garantir sync incremental baseado em ETag/LastModified para não baixar massivamente arquivos inalterados a cada execução.
- **Limpeza:** diretórios locais de staging devem ser purgados na exclusão da fonte.

## 9. Definition of Done

- [ ] Requisitos RF-001 a RF-008 implementados.
- [ ] Critérios de aceitação da seção 6 validados por testes automatizados.
- [ ] Casos de borda (arquivo grande, falha de auth, credencial nula) tratados.
- [ ] Build e testes da solution (`dotnet test`) passando sem falhas.
- [ ] Guardrails da seção 8 rigorosamente atendidos.
- [ ] Nenhuma credencial de nuvem registrada em logs.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/...` referencing the ticket.

## Open Questions / Pending Ambiguity

- Nenhuma pendência bloqueante.
