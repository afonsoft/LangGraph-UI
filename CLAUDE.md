# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado Atual

Este repositório contém o **KnowledgeHub** — plataforma standalone .NET 10: SPA Blazor WebAssembly + REST management API + MCP server nativo (Streamable HTTP + SSE legado) num único processo Kestrel. RAG agêntico sobre fontes de conhecimento registradas pelo usuário (Obsidian vaults, web pages, documentos, APIs, SQL).

Features implementadas (todas as SPECs em `.specs/` estão `Done`): catálogo dinâmico de tools MCP, ingestão com conectores (Obsidian, WebPage, DocumentFile), retrieval híbrido FTS5+RRF, síntese de respostas via `IChatClient` (Ollama/OpenAI), loop de agente com tool-calling, aprovações HITL, threads de conversação com sumarização, endpoints SSE de streaming, health checks, ProblemDetails, validação de config no startup, graceful shutdown e scripts de backup/restore.

## Estrutura

```
.
├── KnowledgeHub.slnx          # solution
├── src/
│   ├── KnowledgeHub.Shared/      # DTOs, contratos JSON-RPC 2.0 / MCP, enums
│   ├── KnowledgeHub.Client/      # Blazor WASM SPA (BootstrapBlazor)
│   ├── KnowledgeHub.Server/      # host Kestrel, Minimal APIs, EF Core SQLite, sync
│   └── KnowledgeHub.McpEngine/   # MCP nativo: sessões SSE, dispatcher JSON-RPC
├── tests/
│   ├── KnowledgeHub.Tests.Unit/
│   └── KnowledgeHub.Tests.Integration/
├── .specs/                     # SPECs SDD — fonte da verdade para features
├── .claude/skills/             # skills versionadas (afonsoft/skills)
├── skills-lock.json            # hash SHA-256 de cada skill
├── backup.sh / restore.sh      # backup/restore do SQLite + uploads
├── docker-compose.yml          # deploy containerizado
└── LICENSE                     # MIT — Afonso Dutra Nogueira Filho, 2026
```

## Skills Registradas

As skills ficam em `.claude/skills/<skill-name>/SKILL.md` e são fixadas via `skills-lock.json` com `computedHash` SHA-256 (origem: `https://github.com/afonsoft/skills`, branch `skills/...`). Para atualizar uma skill, regere o hash e atualize o lockfile mantendo a mesma estrutura.

## Workflow Recomendado

O fluxo do agent-loop definido em `.claude/skills/write-specs/` → `execute-spec/` → `create-issues/` é o caminho canônico para novas funcionalidades:

1. `/write-specs` — estabelecer linguagem compartilhada, domínio e SPEC SDD.
2. `/execute-spec` — executar tarefas a partir do SPEC aprovado.
3. `/create-issues` — quando o trabalho precisa ser fragmentado em tickets.

Para revisão: `/code-review`, `/simplify` ou invoque `code-review-and-quality` / `quality-test-implementation` / `qa-analyst` diretamente. Para arquitetura: `drawio-architecture` ou `mermaid-architecture`.

## Convenções

- **Branches**: `feature/{AgentLLM}-{YYYYMMDD}-{descricao-curta}` baseada em `main`. Nunca commitar em `main`, `master` ou `develop`.
- **Workflows**: `.github/workflows` é protegido — qualquer alteração é bloqueada pela proteção de branch.
- **Specs**: `.specs/SPEC-*.md` aprovadas são a fonte da verdade; manter `Status`/`Ticket` sincronizados com a implementação.
- **Secrets**: nunca commitar `.env`, `*.key`, `*.pem`. API keys via variáveis de ambiente (`Chat__ApiKey`, `Embeddings__ApiKey`).
- **Commits**: Conventional Commits (`feat:`, `fix:`, `docs:`, `refactor:`).

## Comandos

```bash
dotnet build KnowledgeHub.slnx                    # build all
dotnet test                                       # unit + integration tests
dotnet format KnowledgeHub.slnx --verify-no-changes  # formatting gate
dotnet run --project src/KnowledgeHub.Server      # serve http://localhost:5000
dotnet ef database update -p src/KnowledgeHub.Server  # apply EF migrations
```

Para inspecionar/atualizar o lockfile de skills manualmente, use ferramentas Git padrão (ex.: `git diff skills-lock.json` para auditar mudanças de hash).

## Licença

MIT — ver `LICENSE`. Atribuição a terceiros (skills de `afonsoft/skills`) deve respeitar a licença de cada skill; cada `SKILL.md` declara a própria licença no frontmatter.
