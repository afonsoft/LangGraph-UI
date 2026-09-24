# Arquitetura do KnowledgeHub

## Visão Geral

O KnowledgeHub é uma plataforma de conhecimento all-in-one standalone construída em .NET 10. Ela combina uma SPA administrativa Blazor WebAssembly, uma API REST de gerenciamento e um servidor nativo do Model Context Protocol (MCP) em um único processo hospedado no Kestrel.

## Camadas do Sistema

```
                                  +---------------------------+
                                  |    Clientes MCP Externos  |
                                  | (Cursor, Claude Desktop)  |
                                  +--------------+------------+
                                                 | Streamable HTTP / SSE
                                                 v
+------------------+             +---------------+-----------+
|    Blazor SPA    |             |  KnowledgeHub.Server      |
| (Admin PWA / UI) |             |  (Kestrel, Minimal APIs)  |
+--------+---------+             +---------------+-----------+
         | REST / SignalR                        |
         v                                       v
+------------------+             +---------------+-----------+
| KnowledgeHub.    |             | KnowledgeHub.McpEngine    |
|   Client         |             | (Dispatcher JSON-RPC 2.0) |
+------------------+             +---------------+-----------+
         |                                       |
         +-------------------+-------------------+
                             |
                             v
                 +-----------+-----------+
                 | KnowledgeHub.Shared   |
                 | (Contratos, DTOs)     |
                 +-----------+-----------+
                             |
                             v
                 +-----------+-----------+
                 |    EF Core SQLite     |
                 | (sqlite-vec / pgvector) |
                 +-----------------------+
```

## Projetos Principais

1. **KnowledgeHub.Shared**: Contém DTOs compartilhados, contratos JSON-RPC 2.0 / MCP e enums.
2. **KnowledgeHub.Client**: SPA Blazor WebAssembly utilizando componentes BootstrapBlazor, fornecendo administração, monitoramento MCP, playgrounds e gerenciamento de fontes.
3. **KnowledgeHub.Server**: Host Kestrel executando Minimal APIs, Entity Framework Core com SQLite (ou PostgreSQL/pgvector), motor de sincronização de conectores, autenticação (cookie + chaves de API) e proxies MCP upstream.
4. **KnowledgeHub.McpEngine**: Servidor MCP nativo gerenciando sessões SSE, transporte Streamable HTTP e despacho JSON-RPC.

## Fluxo de Dados & Retrieval

- **Ingestão**: Conectores ingerem de vaults Obsidian (local ou WebDAV), páginas web, documentos, Notion e bancos de dados SQL.
- **Chunking & Indexação**: Chunking sensível à estrutura para código e configuração, com embeddings locais via ONNX Runtime (all-MiniLM-L6-v2) ou provedores externos.
- **Retrieval Híbrido**: Combina busca full-text FTS5 do SQLite com busca de similaridade vetorial (sqlite-vec ou pgvector HNSW), fundidas via Reciprocal Rank Fusion (RRF).
- **Síntese & Loop de Agente**: Respostas sintetizadas via `IChatClient` (Ollama/OpenAI), suportando conversações multi-turno, tool calling e aprovações Human-in-the-Loop (HITL).
