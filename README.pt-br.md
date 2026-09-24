# KnowledgeHub

[![CI Build & Test](https://github.com/afonsoft/LangGraph-UI/actions/workflows/ci-build-test.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/ci-build-test.yml)
[![Code Quality](https://github.com/afonsoft/LangGraph-UI/actions/workflows/code-quality.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/code-quality.yml)
[![Security Scan](https://github.com/afonsoft/LangGraph-UI/actions/workflows/security-scan.yml/badge.svg?branch=main)](https://github.com/afonsoft/LangGraph-UI/actions/workflows/security-scan.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor WASM](https://img.shields.io/badge/Blazor-WASM%20PWA-512BD4)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Licença: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)

Plataforma de conhecimento standalone tudo-em-um: UI administrativa Blazor WebAssembly, API REST, servidor MCP nativo (Streamable HTTP + SSE legado), persistência SQLite, provedores de embeddings e vector stores plugáveis, ingestão do Obsidian e proxy MCP DeepWiki — tudo em um único processo Kestrel hospedado .NET 10.

## Endpoints

| Rota | Propósito |
|---|---|
| `/` | UI administrativa Blazor WASM (`/sources`, `/mcp-monitor`, `/playground`, `/settings`, `/api-keys`) — PWA instalável, sidebar colapsável com icon-rail, layout responsivo para mobile |
| `/api/sources`, `/api/search`, `/api/ask`, `/api/agent`, `/api/approvals`, `/api/threads` | API REST |
| `/api/settings/chat`, `/api/settings/chat/test`, `/api/settings/integrations*` | Configuração persistente de provedor de chat (endpoint/modelo/key, teste de conexão) e chaves de integração mascaradas (firecrawl, deepwiki, tavily, context7) |
| `/api/api-keys/{id}/settings/chat`, `/api/api-keys/{id}/settings/integrations/{provider}` | Sobrescrições por chave de API: endpoint/modelo/chave de chat e chaves de integração |
| `/api/ask/stream`, `/api/agent/stream` | REST SSE — eventos `token`/`tool_start`/`tool_end`/`awaiting_approval`/`done`/`error`, heartbeat 15s, `X-Accel-Buffering: no` |
| `/mcp` | MCP — Streamable HTTP, sessões híbridas: clients com handshake `initialize` (≤2025-11-25) obtêm sessões stateful completas incluindo push `tools/list_changed`; clients `2026-07-28` são atendidos statelessly (sem sessão, re-list sob demanda). Configuração `Mcp:SessionMode`: `Stateless`/`Stateful`/`StatefulForInitializeClients` (padrão) |
| `/mcp/sse` + `/mcp/message` | MCP — HTTP/SSE legado (Cursor, Claude Desktop) |
| `/hubs/mcp` | Feed SignalR para o monitor MCP |

## Autenticação

Todas as superfícies exceto as sondas de health (`/health/*`) e `POST /api/auth/login` requerem autenticação — a SPA, a API REST, `/mcp`, `/mcp/sse` e `/hubs/mcp`.

**Navegador (cookie).** A SPA faz login em `/login`; a sessão é um cookie HttpOnly (`SameSite=Lax`, `Secure`, 12h sliding). Na primeira inicialização, um usuário `admin` é criado com a senha `123qwe` (sobrescreva via `Auth__AdminInitialPassword`) e `mustChangePassword` força a tela de troca de senha antes de qualquer outra página ou chamada de API. Política de senha: ≥8 caracteres, diferente da atual. Cinco tentativas consecutivas falhas bloqueiam a conta por 5 minutos (`423 Locked`); credenciais erradas retornam `401` genérico (sem enumeração de usuário).

| Rota de auth | Propósito |
|---|---|
| `POST /api/auth/login` | `{ username, password }` → define o cookie de sessão |
| `GET /api/auth/me` | `{ username, mustChangePassword }` |
| `POST /api/auth/logout` | limpa o cookie de sessão |
| `POST /api/auth/change-password` | `{ currentPassword, newPassword }` → `204`, limpa a flag |
| `GET /api/apikeys` · `POST /api/apikeys` · `DELETE /api/apikeys/{id}` | gerenciar chaves de API (sessão por cookie apenas) |

**Chaves de API (`aft_*`) para clientes não-navegador.** Crie uma em `/api-keys` (ou `POST /api/apikeys`); o segredo completo `aft_<32-hex>` é mostrado **uma vez** — apenas seu hash SHA-256 é armazenado. Envie como bearer token:

```bash
curl https://rag.afonsoft.dev/mcp \
  -H "Authorization: Bearer aft_..." \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{...}}'
```

Aceito em `/mcp`, `/mcp/sse`, `/api/*` e `/hubs/mcp` (clients SignalR que não podem enviar headers podem usar `?access_token=`). Chaves de API podem chamar tudo **exceto** os endpoints de gerenciamento de chaves de API, que requerem sessão por cookie. Revogar uma chave (`DELETE /api/apikeys/{id}` ou pela UI) tem efeito imediato. Cada chave pode também ter suas **próprias configurações de endpoint/modelo/chave de chat e chaves de integração** (firecrawl, tavily) — configure na UI `/api-keys`, via `/api/api-keys/{id}/settings/*`, ou através da ferramenta MCP `set_api_key_settings`.

> **Mudança quebrante para clients MCP externos** (Cursor, Claude Desktop, …): agora devem enviar `Authorization: Bearer aft_...`. Gere a chave na UI administrativa primeiro.

## Ferramentas MCP

`search_knowledge`, `ask_knowledge`, `agent_chat`, `write_knowledge`, `read_document`, `write_note`, `set_api_key_settings` (configurações de chat e integração por chave), `query_{source_slug}` por fonte ativa, mais proxies upstream — DeepWiki (`ask_question`, `read_wiki_structure`, `read_wiki_contents`), Firecrawl (`firecrawl_*`), Tavily (`tavily_*`) e Context7 (`resolve-library-id`, `query-docs`).

## Configuração

```jsonc
{
  "Database": { "Path": "knowledgehub.db" },   // ou KnowledgeHub:DatabasePath
  "Embeddings": {
    "Provider": "deterministic",               // deterministic | ollama | openai | onnx
    "Endpoint": "http://localhost:11434",
    "ApiKey": "",
    "Model": "nomic-embed-text",
    "Dimensions": 384,
    "ModelPath": "models/all-MiniLM-L6-v2"     // Provider=onnx — model.onnx + vocab.txt dir
  },
  "VectorStore": {
    "Provider": "sqlite",                      // sqlite | sqlite-vec | postgres (pgvector)
    "ConnectionString": ""
  },
  "Cache": {
    "Provider": "memory",                      // memory | redis
    "Redis": { "ConnectionString": "" }        // host.docker.internal:6379 ao usar Redis
  },
  "Chat": {                                    // opcional global default
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.2",
    "ApiKey": ""
  },
  "DeepWiki": {                                // proxy MCP upstream
    "Enabled": true,
    "Endpoint": "https://mcp.deepwiki.com/mcp",
    "PrivateEndpoint": "https://mcp.devin.ai/mcp",
    "ApiKey": ""
  },
  "Firecrawl": {
    "Enabled": true,
    "Endpoint": "https://mcp.firecrawl.dev/v2/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 300
  },
  "Tavily": {
    "Enabled": true,
    "Endpoint": "https://mcp.tavily.com/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 120
  },
  "Context7": {
    "Enabled": true,
    "Endpoint": "https://mcp.context7.com/mcp",
    "ApiKey": "",
    "TimeoutSeconds": 60
  },
  "Mcp": {
    "SessionMode": "StatefulForInitializeClients"  // Stateless | Stateful | StatefulForInitializeClients
  },
  "Auth": {
    "AdminInitialPassword": "123qwe"           // senha seed para usuário admin
  }
}
```

Variáveis de ambiente sobrescrevem `appsettings.json` (duplo underscore → chave aninhada). Veja `.env.example` para a lista completa.

## Estrutura do Repositório

```text
KnowledgeHub/
├── src/
│   ├── KnowledgeHub.Shared/      # DTOs, contratos JSON-RPC 2.0 / MCP, enums
│   ├── KnowledgeHub.Client/      # SPA Blazor WASM (BootstrapBlazor)
│   ├── KnowledgeHub.Server/      # host Kestrel, Minimal APIs, EF Core SQLite, sync
│   └── KnowledgeHub.McpEngine/   # MCP nativo: sessões SSE, dispatcher JSON-RPC
├── tests/
│   ├── KnowledgeHub.Tests.Unit/
│   └── KnowledgeHub.Tests.Integration/
├── .specs/                       # SPECs SDD — fonte da verdade para features
├── docs/architecture/            # Diagramas e ADRs de arquitetura
├── .claude/skills/               # skills versionadas (afonsoft/skills)
├── skills-lock.json              # hashes SHA-256 das skills instaladas
├── backup.sh / restore.sh        # backup/restore SQLite + uploads
├── docker-compose.yml            # deploy containerizado
└── LICENSE                       # MIT — Afonso Dutra Nogueira Filho, 2026
```

## Stack Tecnológica

| Camada | Tecnologia | Versão |
|-------|-----------|---------|
| Linguagem | C# | 12 (.NET 10) |
| Framework | ASP.NET Core (Kestrel) | 10.x |
| Frontend | Blazor WebAssembly (BootstrapBlazor) | 10.x |
| Banco de Dados | SQLite (EF Core) + sqlite-vec | 10.x |
| Vector Store | sqlite-vec / pgvector | 0.1.9 / 0.3.2 |
| Embeddings | ONNX Runtime (all-MiniLM-L6-v2) | 1.29.0 |
| AI SDK | Microsoft.Extensions.AI.Abstractions | 10.9.0 |
| Protocolo MCP | ModelContextProtocol | 2.2.0 |
| CI/CD | GitHub Actions | — |
| Container | Docker Compose | — |

## Começando

### Pré-requisitos

- .NET SDK 10.0.x ([global.json](global.json))
- Node.js ≥ 20.x (para ferramentas de desenvolvimento)
- Docker & Docker Compose (opcional, para deploy containerizado)

### Instalar

```bash
git clone https://github.com/afonsoft/LangGraph-UI.git
cd LangGraph-UI
```

### Configurar

```bash
cp .env.example .env
# Edite .env com seus valores (chaves de API, endpoints, etc.)
```

### Executar (desenvolvimento)

```bash
dotnet build KnowledgeHub.slnx
dotnet ef database update -p src/KnowledgeHub.Server
dotnet run --project src/KnowledgeHub.Server
```

Abra http://localhost:5000 e faça login com `admin` / `123qwe`.

### Executar (Docker)

```bash
docker compose up -d
```

Acesse em http://localhost:5000.

## Testes & Cobertura

```bash
dotnet test                                       # testes unitários + integração
dotnet format KnowledgeHub.slnx --verify-no-changes  # gate de formatação
```

Gates do CI: Build, Testes Unitários, Testes de Integração (SQLite), Validação Cliente Blazor WASM, Build Imagem Docker, Qualidade de Código (SonarQube), Security Scan.

## Arquitetura

KnowledgeHub segue um padrão de arquitetura limpa com quatro camadas:

- **Shared**: DTOs, contratos MCP, enums — consumido por todos os projetos
- **Client**: SPA Blazor WASM com componentes BootstrapBlazor, suporte PWA, SignalR para atualizações em tempo real
- **Server**: Host Kestrel com Minimal APIs, persistência EF Core SQLite, autenticação, validação de configuração
- **McpEngine**: Servidor MCP nativo implementando transportes Streamable HTTP e SSE legado, dispatcher JSON-RPC 2.0, gerenciamento de sessões

Decisões arquiteturais chave:

- Deploy single-process: SPA + API + servidor MCP em um único processo Kestrel
- Modo de sessão MCP híbrido: stateful para clients com handshake initialize, stateless para clients modernos
- Embeddings plugáveis: determinístico, Ollama, OpenAI, ou ONNX Runtime local
- Múltiplos backends de vector store: sqlite-vec (KNN) ou PostgreSQL com pgvector (HNSW)
- Cache distribuído opt-in: in-memory (padrão) ou Redis
- Proxies MCP upstream: DeepWiki, Firecrawl, Tavily, Context7 com secrets criptografados
- Customização por chave de API: configurações de chat e chaves de integração escopadas por chave

Veja [docs/pt/ARCHITECTURE.md](docs/pt/ARCHITECTURE.md) para design detalhado do sistema e [docs/architecture/system-architecture.md](docs/architecture/system-architecture.md) para diagramas.

## Visões de Negócio e Técnica

### Valor de Negócio

KnowledgeHub resolve o problema de conhecimento organizacional fragmentado fornecendo uma plataforma unificada que:

- Ingera conhecimento de múltiplas fontes (vaults Obsidian, páginas web, documentos, Notion, APIs, bancos de dados SQL)
- Permite consultas em linguagem natural com retrieval híbrido (busca full-text + similaridade vetorial com reranking RRF)
- Fornece capacidades agênticas com tool-calling, aprovações HITL e threads de conversação com sumarização
- Expõe conhecimento tanto via API REST quanto via Model Context Protocol para integração com agentes de IA
- Roda standalone sem dependências externas (SQLite, modelos embarcados) para fácil deploy

### Decisões Técnicas

- **.NET 10**: Framework mais recente com minimal APIs, suporte AOT compilation e melhorias de performance
- **Blazor WASM**: Frontend type-safe compartilhando DTOs com backend, suporte PWA para capacidade offline
- **Model Context Protocol**: Protocolo padrão para integração de ferramentas de agentes de IA, permitindo integração seamless com Cursor, Claude Desktop e outros clients MCP
- **SQLite + sqlite-vec**: Persistência zero-config com busca vetorial KNN nativa, PostgreSQL/pgvector opcional para escala
- **ONNX Runtime embeddings**: Provedor de embeddings local (all-MiniLM-L6-v2) para privacidade e embeddings sem custo
- **Clean Architecture**: Separação clara de responsabilidades com projetos Shared/Client/Server/McpEngine

## Licença & Status

- **Licença**: MIT — veja [LICENSE](LICENSE)
- **Status**: Desenvolvimento ativo
- **Autor**: Afonso Dutra Nogueira Filho

## Links

- [English](README.md) — versão em inglês
- [Documentação de Arquitetura](docs/pt/ARCHITECTURE.md) — Design detalhado do sistema
- [Guia de Contribuição](docs/pt/CONTRIBUTING.md) — Como contribuir
- [Guia de Instalação](docs/pt/INSTALL.md) — Setup específico por plataforma
- [Documentação da API](docs/pt/API.md) — Referência da API REST
- [Issues no GitHub](https://github.com/afonsoft/LangGraph-UI/issues) — Relatórios de bugs e solicitações de features
- [Releases](https://github.com/afonsoft/LangGraph-UI/releases) — Histórico de versões
