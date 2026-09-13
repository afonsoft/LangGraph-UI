# AGENTS.md

Thin reference — the single source of truth for this repository is [`CLAUDE.md`](CLAUDE.md).

## Project

**KnowledgeHub** — all-in-one standalone .NET 10 platform: Blazor WebAssembly admin SPA + REST management API + native MCP server (HTTP/SSE, JSON-RPC 2.0) in a single Kestrel process. Agentic RAG over user-registered knowledge sources (Obsidian vaults, web pages, documents, APIs, SQL).

## Structure

```
KnowledgeHub.slnx
├── src/
│   ├── KnowledgeHub.Shared/      # DTOs, JSON-RPC 2.0 / MCP contracts, enums
│   ├── KnowledgeHub.Client/      # Blazor WebAssembly SPA (BootstrapBlazor)
│   ├── KnowledgeHub.Server/      # Kestrel host, Minimal APIs, EF Core SQLite, sync
│   └── KnowledgeHub.McpEngine/   # Native MCP: SSE sessions, JSON-RPC dispatcher
└── tests/
    ├── KnowledgeHub.Tests.Unit/
    └── tests/KnowledgeHub.Tests.Integration/
```

## Commands

```bash
dotnet build KnowledgeHub.slnx                    # build all
dotnet test                                       # run tests
dotnet run --project src/KnowledgeHub.Server      # serve http://localhost:5000
```

## Rules

- Never commit to `main`, `master` or `develop` — use `feature/{AgentLLM}-{YYYYMMDD}-{slug}`.
- `.github/workflows/` is protected.
- Never commit `.env`, `*.key`, `*.pem` or secrets.
- Specs live in `.specs/`; approved SPEC is the source of truth for implementation.
