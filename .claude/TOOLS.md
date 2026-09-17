# TOOLS.md — Tools & MCP Inventory

On-demand reference. Permissions policy lives in `.claude/settings.json`;
the gate model is summarized in `RULES.md`.

## Local toolchain

| Tool | Use | Command |
| --- | --- | --- |
| .NET SDK 10.0.112 | build/test/format/EF | `dotnet build|test|format|ef` |
| Node.js 24 | skills CLI (`npx skills`) | `npx skills update -p -y` |
| GitHub CLI | issues, PRs, repo settings | `gh …` (authed as `afonsoft`) |
| Docker / Compose | containerized deploy | `docker compose up -d --build` |

## Skills installed (`.claude/skills/`, pinned by `skills-lock.json`)

orchestrator, gap-analysis, write-specs, execute-specs, create-issues,
create-agent-harness, create-readme, diagnose, code-review-and-quality,
quality-test-implementation, qa-analyst, improve-codebase-architecture,
drawio-architecture, mermaid-architecture, design, building-mcp-servers,
observability-and-instrumentation, scaffold-mvp, obsidian, composio-mcp,
notebooklm-mcp, wordpress-mcp, sonarqube-autofix.

## Sub-agents (`.claude/agents/`)

`plan` (SPEC SDD writer), `review` (code/security reviewer), `test`
(verification gate). Trigger via the `description` field.

## MCP surfaces exposed BY this app

| Surface | Transport | Auth |
| --- | --- | --- |
| `/mcp` | Streamable HTTP | Bearer `aft_*` API key |
| `/mcp/sse` + `/mcp/message` | legacy HTTP/SSE | Bearer `aft_*` or `?access_token=` |

Tools served: `search_knowledge`, `ask_knowledge`, `agent_chat`,
`write_knowledge`, `read_document`, `write_note`, `set_api_key_settings`,
`query_{source_slug}` per active source, DeepWiki bypass
(`ask_question`, `read_wiki_structure`, `read_wiki_contents`).

## Upstream MCP integrations

DeepWiki (public + private `mcp.devin.ai`), Firecrawl, Tavily — secrets in the
encrypted `IIntegrationSecretStore`, managed via `/api/settings/integrations*`
or per-key via `/api/api-keys/{id}/settings/integrations/{provider}`.

## Risk policy

Read-only → free. Write → confirm. Execute (build/test) → allowed, logged.
External mutations (push, merge, `gh api` PUT/DELETE, deploy) → `ask` in
`settings.json`, Tier 3 approval for governance changes.
