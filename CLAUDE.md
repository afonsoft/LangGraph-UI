# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Estado Atual

Este repositório (`LangGraph-UI`) **ainda não contém código-fonte da aplicação**. Ele está atualmente configurado como um **agent-harness / skills registry** — apenas o catálogo de skills em `.claude/skills/` e o lockfile `skills-lock.json` estão versionados. Antes de implementar features, confirme com o usuário se a aplicação propriamente dita já foi scaffoldada ou se ele espera que você a inicialize (use `/scaffold-mvp`).

## Estrutura

```
.
├── .claude/skills/      # 22 skills versionadas, sourced de afonsoft/skills
├── skills-lock.json     # Lockfile com hash SHA-256 de cada skill
├── .gitignore           # Perfil .NET (Debug/Release/bin/obj/.env) — ainda sem .csproj/.sln
└── LICENSE              # MIT — Afonso Dutra Nogueira Filho, 2026
```

## Skills Registradas

As skills ficam em `.claude/skills/<skill-name>/SKILL.md` e são fixadas via `skills-lock.json` com `computedHash` SHA-256 (origem: `https://github.com/afonsoft/skills`, branch `skills/...`). Para atualizar uma skill, regere o hash e atualize o lockfile mantendo a mesma estrutura.

Skills atualmente bloqueadas:
`building-mcp-servers`, `code-review-and-quality`, `composio-mcp`, `create-agent-harness`, `create-issues`, `create-readme`, `design`, `diagnose`, `drawio-architecture`, `execute-spec`, `improve-codebase-architecture`, `mermaid-architecture`, `notebooklm-mcp`, `observability-and-instrumentation`, `obsidian`, `orchestrator`, `qa-analyst`, `quality-test-implementation`, `scaffold-mvp`, `sonarqube-autofix`, `wordpress-mcp`, `write-specs`.

## Workflow Recomendado

O fluxo do agent-loop definido em `.claude/skills/write-specs/` → `execute-spec/` → `create-issues/` é o caminho canônico para novas funcionalidades:

1. `/write-specs` — estabelecer linguagem compartilhada, domínio e SPEC SDD.
2. `/scaffold-mvp` — bootstrap da aplicação (só em repo vazio).
3. `/execute-spec` — executar tarefas a partir do SPEC aprovado.
4. `/create-issues` — quando o trabalho precisa ser fragmentado em tickets.

Para revisão: `/code-review`, `/simplify` ou invoque `code-review-and-quality` / `quality-test-implementation` / `qa-analyst` diretamente. Para arquitetura: `drawio-architecture` ou `mermaid-architecture`.

## Convenções Herdadas das Skills

- **Branches**: `feature/{AgentLLM}-{YYYYMMDD}-{descricao-curta}` baseada em `main`. Nunca commitar em `main`, `master` ou `develop`.
- **Workflows**: `.github/workflows` é protegido — qualquer alteração é bloqueada pela proteção de branch.
- **Stack inferida pelo `.gitignore`**: .NET (Debug/Release/bin/obj). O `scaffold-mvp` permite escolher entre Blazor (MudBlazor, Radzen, Fluent UI Blazor, Bootstrap Blazor) ou Angular (Angular Material, PrimeNG, NG-ZORRO) — confirme a escolha com o usuário antes de scaffold.
- **Secrets**: nunca commitar `.env`, `*.key`, `*.pem`.

## Comandos

Atualmente **não há build, lint ou test configurados** (nenhum `package.json`, `.csproj`, `.sln`, `pyproject.toml`, etc.). Quando o scaffold for executado, os comandos serão adicionados pelo template escolhido — não invente comandos que não existem.

Para inspecionar/atualizar o lockfile de skills manualmente, use ferramentas Git padrão (ex.: `git diff skills-lock.json` para auditar mudanças de hash).

## Licença

MIT — ver `LICENSE`. Atribuição a terceiros (skills de `afonsoft/skills`) deve respeitar a licença de cada skill; cada `SKILL.md` declara a própria licença no frontmatter.
