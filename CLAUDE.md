# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado Atual

Este repositório contém o **KnowledgeHub** — plataforma standalone .NET 10: SPA Blazor WebAssembly + REST management API + MCP server nativo (Streamable HTTP + SSE legado) num único processo Kestrel. RAG agêntico sobre fontes de conhecimento registradas pelo usuário (Obsidian vaults, web pages, documentos, APIs, SQL).

Features implementadas (todas as SPECs em `.specs/` estão `Done`): catálogo dinâmico de tools MCP, ingestão com conectores (Obsidian — vault local ou via WebDAV, WebPage, DocumentFile), retrieval híbrido FTS5+RRF, síntese de respostas via `IChatClient` (Ollama/OpenAI), loop de agente com tool-calling, aprovações HITL, threads de conversação com sumarização, endpoints SSE de streaming, auth (login por cookie + API keys `aft_*` com auditoria de uso e policies), proxies MCP upstream (DeepWiki público/privado, Firecrawl, Tavily) com secrets por integração, cache distribuído opt-in `IDistributedCache` (memory|redis) para busca/embeddings, health checks, ProblemDetails, validação de config no startup, graceful shutdown, migrations EF Core com guard de dimensão de embeddings, UI admin com monitor MCP em tempo real (SignalR), edição de sources e playground, e scripts de backup/restore + packaging standalone.

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

As skills são gerenciadas pelo CLI `skills` (skills.sh): o store canônico fica em `.agents/skills/<skill-name>/` (gitignored) e `.claude/skills/<skill-name>` é um symlink para ele. O `skills-lock.json` fixa cada skill com `computedHash` SHA-256 (origem: `https://github.com/afonsoft/skills`, branch `skills/...`). Para atualizar: `npx skills update -p -y` (existentes) e `npx skills add afonsoft/skills -s <nome> -y` (novas); para restaurar após clone: `npx skills experimental_install`.

## Workflow Recomendado

O fluxo do agent-loop definido em `.claude/skills/write-specs/` → `execute-specs/` → `create-issues/` é o caminho canônico para novas funcionalidades:

1. `/write-specs` — estabelecer linguagem compartilhada, domínio e SPEC SDD.
2. `/execute-specs` — executar tarefas a partir do SPEC aprovado.
3. `/create-issues` — quando o trabalho precisa ser fragmentado em tickets.

Para revisão: `/code-review`, `/simplify` ou invoque `code-review-and-quality` / `quality-test-implementation` / `qa-analyst` diretamente. Para arquitetura: `drawio-architecture` ou `mermaid-architecture`.

## Memory Protocol

- **State** (short-term): `.claude/memory/memory.md` — overwritten every session, max 100 lines.
- **History** (long-term): `.claude/memory/{YYYYMMDD}-memory.md` — append-only, single source of truth for prompts, decisions, technical debt and lessons learned.
- **Knowledge** (durable): `.claude/knowledge/{slug}.md` — reusable facts and patterns promoted out of memory.
- **Protocol docs** (on-demand): `.claude/MEMORY.md` — reference only, no state or history.

Save everything, always. Read `memory.md` and the 3 most recent long-term files at session start. Log a one-line summary of every user prompt or instruction under `## Prompts`, each verified checkpoint, decision, mistake or discovery under its section, and a `## Session summary` — outcome and where work stopped — before compaction, context reset or any possible end of session. Promote reusable knowledge to `.claude/knowledge/`. Nothing survives only in context.

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
