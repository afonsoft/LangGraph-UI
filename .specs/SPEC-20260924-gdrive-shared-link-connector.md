# SPEC-20260924-gdrive-shared-link-connector

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `gdrive-shared-link-connector` |
| Type | `Feature` |
| Stack | `.NET 10 (ASP.NET Core + EF Core SQLite + Blazor WASM + Google Drive REST API)` |
| Repository | `LangGraph-UI` |
| Branch | `feature/Antigravity-20260924-gdrive-shared-link-connector` |
| Ticket | [#179](https://github.com/afonsoft/LangGraph-UI/issues/179) |
| Status | `Done` |

## 1. User Story

**As a** usuário ou pesquisador utilizando o Knowledge MCP Hub
**I want** cadastrar uma fonte de dados informando apenas um link compartilhado de uma pasta ou arquivo do Google Drive
**So that** os arquivos sejam sincronizados em uma pasta temporária de staging local, os documentos nativos (Google Docs, Sheets, Slides) e binários (PDF, DOCX, TXT, MD) sejam tratados e vetorizados no banco de dados para alimentar respostas completas no RAG.

**Problem context:**
O Google Drive é uma das ferramentas de colaboração documental mais populares do mundo. Muitas equipes compartilham pastas inteiras ou documentos de projeto por meio de links de compartilhamento ("Qualquer pessoa com o link pode visualizar").
Atualmente, para ingerir documentos do Google Drive no Knowledge MCP Hub, o usuário precisa baixar manualmente todos os arquivos para o seu computador e configurá-los como `DocumentFile`, perdendo atualizações e rastreabilidade da origem.
Com este conector, o usuário precisa apenas colar a URL compartilhada do Drive. O Knowledge MCP Hub resolve os arquivos contidos na pasta compartilhada, sincroniza-os em uma pasta de staging local (`data/staging/{sourceId}/`), exporta formatos nativos do Google para texto/csv legível, extrai o conteúdo de PDFs e DOCXs, e gera os embeddings vetoriais com suporte a sync incremental periódico.

## 2. Scope

**In scope:**
- Novo membro no enum `SourceType`:
  - `GoogleDrive = 11`
- Parser robusto de URLs do Google Drive para extração de identificadores:
  - Pastas: `https://drive.google.com/drive/folders/{folderId}` e variantes com parâmetros.
  - Arquivos individuais: `https://drive.google.com/file/d/{fileId}/view` ou `https://drive.google.com/open?id={fileId}`.
- Conector `GoogleDriveSharedConnector` implementando `IIncrementalSourceConnector`:
  - Descoberta e enumeração recursiva de arquivos dentro da pasta compartilhada.
  - Suporte a links compartilhados públicos e suporte opcional a `apiKey` ou credencial de `serviceAccount` (armazenada de forma segura no `IIntegrationSecretStore` sob `gdrive:{sourceId}`) para evitar limites de taxa (rate limits).
  - Download dos arquivos para a pasta temporária de staging local (`data/staging/{sourceId}/`).
- Conversão transparente de documentos nativos do Google Workspace:
  - Google Docs (`application/vnd.google-apps.document`) → exportado via API como Texto/Markdown (`text/plain`).
  - Google Sheets (`application/vnd.google-apps.spreadsheet`) → exportado como CSV (`text/csv`).
  - Google Slides (`application/vnd.google-apps.presentation`) → exportado como Texto (`text/plain`).
- Extração de texto de arquivos convencionais baixados:
  - Reutilização dos extratores existentes para `.pdf`, `.docx`, `.md`, `.txt`, `.csv` e arquivos de código.
- Sincronização incremental:
  - Compara `modifiedTime` e `md5Checksum` remotos com o fingerprint armazenado no Knowledge MCP Hub, baixando apenas itens novos ou atualizados.
- Indexação e RAG:
  - Geração de `RawDocument` com URI `gdrive://{folderOrFileId}/{relativePath}` e link para visualização original.
  - Chunking semântico, geração de embeddings, indexação vetorial (`sqlite-vec`/`pgvector`), FTS e citações no RAG.
- Interface em `SourceEditDialog.razor`:
  - Campo `Link compartilhado do Google Drive *` com validação de formato.
  - Campo opcional de `Google API Key` (com máscara e suporte a `hasKey`).
  - Campos de filtro glob e limite máximo de arquivos (`maxFiles`).
- Auto-sync em `VaultWatcherService` e limpeza de staging na exclusão da fonte.

**Out of scope:**
- Fluxo de autorização interativo OAuth 2.0 (popup de login do Google com conta pessoal) — opera sobre links compartilhados ou chaves de serviço/API.
- Criação, edição ou exclusão de arquivos no Google Drive (read-only ingestion).
- Arquivos binários muito grandes (acima de `maxFileSizeMB`) ou mídias de vídeo/áudio (ignorados com warning).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Shared/Contracts/SourceType.cs` — novo membro `GoogleDrive = 11`.
- `src/KnowledgeHub.Server/Ingestion/Connectors/GoogleDriveApiClient.cs` — cliente HTTP para resolução de metadados, listagem de pastas e download/exportação do Google Drive.
- `src/KnowledgeHub.Server/Ingestion/Connectors/GoogleDriveSharedConnector.cs` — conector de ingestão incremental.
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs` — validação de URL e persistência de credencial opcional no secret store.
- `src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs` — inclusão de `GoogleDrive` no auto-sync.
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs` — registro do conector e do cliente HTTP.
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor` — formulário de cadastro para fontes Google Drive.
- `tests/KnowledgeHub.Tests.Unit/` e `tests/KnowledgeHub.Tests.Integration/` — testes de parsing de URL, conversão de docs e sync.

**Files to read before implementing:**
- `src/KnowledgeHub.Shared/Contracts/SourceType.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/ISourceConnector.cs`
- `src/KnowledgeHub.Server/Ingestion/Connectors/DocumentFileConnector.cs`
- `src/KnowledgeHub.Server/Ingestion/Staging/IStagingStorageService.cs` (criado na SPEC 1)
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs`
- `src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs`
- `src/KnowledgeHub.Client/Pages/SourceEditDialog.razor`

**Files to create or modify:**
```text
src/KnowledgeHub.Shared/Contracts/SourceType.cs                               (modify)
src/KnowledgeHub.Server/Ingestion/Connectors/GoogleDriveApiClient.cs         (create)
src/KnowledgeHub.Server/Ingestion/Connectors/GoogleDriveSharedConnector.cs  (create)
src/KnowledgeHub.Server/Services/KnowledgeSourceService.cs                   (modify)
src/KnowledgeHub.Server/BackgroundServices/VaultWatcherService.cs            (modify)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs           (modify)
src/KnowledgeHub.Client/Pages/SourceEditDialog.razor                         (modify)
tests/KnowledgeHub.Tests.Unit/Server/Ingestion/GoogleDriveConnectorTests.cs (create)
tests/KnowledgeHub.Tests.Integration/GoogleDriveIngestionTests.cs            (create)
```

## 4. Requirements

### RF-001: Extensão do Enum `SourceType.GoogleDrive`
- **Description:** O enum `SourceType` deve conter `GoogleDrive = 11`.
- **Rules:** Serializado como `"GoogleDrive"` e suportado nos filtros de busca e RAG.
- **Input → Output:** `SourceType.GoogleDrive` serializa como string `"GoogleDrive"`.

### RF-002: Resolução e Validação de Link Compartilhado
- **Description:** O conector deve validar a URL fornecida e extrair o identificador do recurso (seja pasta ou arquivo individual).
- **Rules:**
  - Padrões aceitos:
    - Pasta: `^https?:\/\/drive\.google\.com\/(?:drive\/)?(?:u\/\d+\/)?folders\/([a-zA-Z0-9_-]+)`
    - Arquivo: `^https?:\/\/drive\.google\.com\/file\/d\/([a-zA-Z0-9_-]+)`
    - Formato com id explícito: `^https?:\/\/drive\.google\.com\/.*[?&]id=([a-zA-Z0-9_-]+)`
  - URL inválida ou não reconhecida gera erro `400 Bad Request` com mensagem amigável no diálogo.
- **Input → Output:** `https://drive.google.com/drive/folders/1a2b3c4d_xyz` → Identificador `1a2b3c4d_xyz`, tipo `Folder`.

### RF-003: Enumeração e Download em Staging Local
- **Description:** A sincronização deve listar os arquivos contidos na pasta compartilhada (recursivamente para subpastas) e baixá-los para a pasta de staging `data/staging/{sourceId}/`.
- **Rules:**
  - Utiliza `IStagingStorageService.GetStagingPath(sourceId)` para obter o diretório local.
  - Para chamadas com API Key fornecida: utiliza a Google Drive API v3 (`files.list`).
  - Para chamadas públicas sem API Key: utiliza a interface pública de exportação/download direto do Drive com tolerância a paginação.
  - Respeita o limite `maxFiles` (default: 200, clamp 1–1000) e `maxFileSizeMB` (default: 20MB).
- **Input → Output:** Pasta remota com 10 arquivos → 10 arquivos baixados/atualizados no staging local.

### RF-004: Conversão Transparente de Documentos Nativos do Google Workspace
- **Description:** Arquivos nativos do Google Docs, Sheets e Slides não possuem download binário convencional e devem ser convertidos automaticamente via endpoint de exportação.
- **Rules:**
  - Google Docs (`application/vnd.google-apps.document`) → exportar com mimeType `text/plain`.
  - Google Sheets (`application/vnd.google-apps.spreadsheet`) → exportar como `text/csv`.
  - Google Slides (`application/vnd.google-apps.presentation`) → exportar como `text/plain`.
  - O conteúdo exportado é salvo no staging local com extensão correspondente (`.txt` ou `.csv`).
- **Input → Output:** Arquivo nativo Google Docs "Relatório.gdoc" → Arquivo texto "Relatório.txt" salvo no staging e pronto para extração.

### RF-005: Sincronização Incremental por Fingerprint
- **Description:** A sincronização não deve reprocessar arquivos que não sofreram alteração no Google Drive.
- **Rules:**
  - O conector monta o `Fingerprint` combinando `modifiedTime` e `md5Checksum` (ou tamanho para docs nativos).
  - Se o fingerprint coincidir com o armazenado no banco, o conector emite `RawDocument` com conteúdo vazio e o fingerprint anterior, preservando o documento no índice sem custo de download e re-vetorização.
- **Input → Output:** Sync disparado com 50 arquivos onde apenas 2 foram alterados → 48 mantidos instantaneamente, 2 rebaixados e reindexados.

### RF-006: Armazenamento Seguro de Chave de API Opcional
- **Description:** Quando o usuário fornecer uma Google API Key ou credencial de Service Account para evitar limitações de cota pública, ela deve ser protegida via `IIntegrationSecretStore`.
- **Rules:**
  - Chave gravada sob `gdrive:{sourceId}`.
  - Configuração JSON armazena apenas `hasKey: true` e nunca a chave em texto puro.
  - Chave e staging são purgados ao excluir a fonte.
- **Input → Output:** Chave de API informada → Criptografada e protegida.

### RF-007: Interface de Configuração (`SourceEditDialog.razor`)
- **Description:** Formulário dedicado para fontes do tipo `GoogleDrive`.
- **Rules:**
  - Campo `Link Compartilhado do Google Drive *` (placeholder: `https://drive.google.com/drive/folders/...`).
  - Campo opcional `API Key do Google` (campo de senha, mascarado).
  - Campo `Filtro Glob` (opcional, default `**/*`).
  - Campo `Máximo de arquivos` (opcional, default 200).
- **Input → Output:** Usuário cola link compartilhado e salva → Fonte cadastrada com sucesso.

### RF-008: Auto-sync e Limpeza de Staging
- **Description:** Fontes `GoogleDrive` devem ser integradas ao agendamento de auto-sync em `VaultWatcherService` e ter seus arquivos temporários excluídos na remoção da fonte.
- **Rules:**
  - Auto-sync roda conforme `SyncIntervalMinutes`.
  - `IStagingStorageService.CleanupStagingAsync` é chamado na exclusão da fonte.
- **Input → Output:** Exclusão da fonte → Pasta `data/staging/{sourceId}/` apagada completamente.

## 5. API Contract

### Criação / Atualização de Fonte Google Drive
**Endpoint:** `POST /api/sources` / `PUT /api/sources/{id}`
**Auth:** Cookie Session (`AuthPolicies.CookieSession`)

**Payload:**
```json
{
  "name": "Manuais de Treinamento (Google Drive)",
  "type": "GoogleDrive",
  "description": "Pasta compartilhada com manuais e procedimentos",
  "configuration": {
    "sharedUrl": "https://drive.google.com/drive/folders/1A2b3C4d5E6f7G8h9I0jKlMnOpQrStUvW",
    "apiKey": "AIzaSyD-EXAMPLE-API-KEY-12345",
    "glob": "**/*.{pdf,docx,txt,md}",
    "maxFiles": 200,
    "maxFileSizeMB": 20
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 60
}
```

**Retorno 200 OK (GET /api/sources/{id}):**
```json
{
  "id": "e7b89c01-2345-6789-abcd-ef0123456789",
  "name": "Manuais de Treinamento (Google Drive)",
  "type": "GoogleDrive",
  "description": "Pasta compartilhada com manuais e procedimentos",
  "configuration": {
    "sharedUrl": "https://drive.google.com/drive/folders/1A2b3C4d5E6f7G8h9I0jKlMnOpQrStUvW",
    "glob": "**/*.{pdf,docx,txt,md}",
    "maxFiles": 200,
    "maxFileSizeMB": 20,
    "hasKey": true
  },
  "isActive": true,
  "autoSyncEnabled": true,
  "syncIntervalMinutes": 60,
  "lastSyncStatus": "completed",
  "lastSyncAt": "2026-09-24T18:15:00Z"
}
```

## 6. Acceptance Criteria

- [ ] **Given** uma URL de pasta compartilhada do Google Drive válida **when** o sync é disparado **then** os arquivos da pasta são baixados para a pasta de staging local, convertidos para texto e vetorizados no banco para o RAG.
- [ ] **Given** arquivos nativos do Google Docs e Google Sheets na pasta compartilhada **when** a sincronização é executada **then** os documentos são exportados transparentemente como texto/csv, extraídos e indexados.
- [ ] **Given** arquivos já sincronizados que não sofreram edição no Drive **when** um novo ciclo de sync roda **then** o download é pulado e os chunks no banco de dados são preservados.
- [ ] **Given** uma URL inválida do Google Drive que não corresponda a pasta ou arquivo **when** o usuário tenta salvar **then** a validação rejeita com 400 Bad Request e mensagem clara.
- [ ] **Given** uma pasta sem permissão pública de visualização ("Acesso negado" / 403) **when** o sync tenta ler **then** o status da fonte marca `failed` com mensagem `"acesso não autorizado ao link compartilhado"`.
- [ ] **Given** a exclusão de uma fonte Google Drive **when** `DeleteAsync` é chamado **then** o diretório local `data/staging/{sourceId}/` é limpo do disco.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Pasta compartilhada vazia | URL válida sem arquivos | Concluir sync com 0 documentos sem erro |
| Arquivo nativo não exportável | Tipo de arquivo desconhecido do Google | Pular o arquivo e emitir aviso em `Warnings` |
| Link com múltiplos subdiretórios aninhados | Pastas aninhadas | Percorrer recursivamente respeitando o limite `maxFiles` |
| Quota da API do Google excedida (HTTP 429) | Muitas requisições sem API key | Respeitar backoff/Retry-After e registrar aviso sem crash |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery & Modelagem:**
  - Adicionar membro `GoogleDrive = 11` no enum `SourceType`.
  - Implementar expressões regulares para parsing de URLs do Google Drive.
- [ ] **T2 — Cliente de Conexão com Google Drive:**
  - Implementar `GoogleDriveApiClient` para listagem de pastas, exportação de docs/sheets e download de binários.
  - Integrar tratamento de Rate Limit e timeouts.
- [ ] **T3 — Conector de Ingestão & Staging:**
  - Implementar `GoogleDriveSharedConnector` com suporte a staging local e sync incremental.
  - Registrar conector em `IngestionService` e `KnowledgeHubServiceCollectionExtensions`.
  - Atualizar `VaultWatcherService` para auto-sync de fontes `GoogleDrive`.
- [ ] **T4 — Frontend Blazor:**
  - Atualizar `SourceEditDialog.razor` e `SourceEditModel` com campos específicos para Google Drive.
- [ ] **T5 — Testes e Validação:**
  - Criar testes de unidade para validação de links, extração de IDs e exportação de documentos.
  - Criar testes de integração simulando o sync e a indexação vetorial.
  - Executar `dotnet build KnowledgeHub.slnx` e `dotnet test`.
  - Validar DoD (seção 9) e atualizar status para `Done`.

**7.1 Validation strategy by type/stack**

| Type / Stack | Required evidence |
|---|---|
| **.NET 10** | Testes de integração para `GoogleDriveSharedConnector` com API emulada; testes de unidade do parser de URLs e exportador de docs; build limpo sem warnings. |

## 8. Organization Guardrails

- **Branches:** nunca comitar em `main`, `master` ou `develop`. Usar `feature/Antigravity-20260924-gdrive-shared-link-connector`.
- **Workflows:** não modificar `.github/workflows/`.
- **Segurança:** chaves de API opcionais devem ser armazenadas exclusivamente em `IIntegrationSecretStore`.
- **Performance:** respeitar `maxFiles` e `maxFileSizeMB` para evitar esgotamento de memória e disco local.

## 9. Definition of Done

- [ ] Requisitos RF-001 a RF-008 implementados.
- [ ] Critérios de aceitação da seção 6 validados por testes automatizados.
- [ ] Casos de borda (link inválido, pasta vazia, arquivos nativos) tratados.
- [ ] Build e testes da solution (`dotnet test`) passando com sucesso.
- [ ] Guardrails da seção 8 rigorosamente atendidos.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/...` referencing the ticket.

## Open Questions / Pending Ambiguity

- Nenhuma pendência bloqueante.
