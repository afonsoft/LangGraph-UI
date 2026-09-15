# SPEC-20260915-app-rename-knowledge

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `app-rename-knowledge` |
| Type | `Feature` |
| Stack | `.NET 10`, `Blazor WASM`, `MCP (ModelContextProtocol)` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-rename-mcp-monitor` |
| Ticket | `—` |
| Status | `Approved` |

## 1. User Story

**As a** platform user and MCP client operator
**I want** the application to present itself as **Knowledge** everywhere a human sees the name — the SPA (tab title, navbar, login, home, page titles) and the MCP server identity (`serverInfo.name`, client config snippets)
**So that** the product name is consistent with the intended branding instead of the legacy "KnowledgeHub".

**Problem context:**
The app was scaffolded as "KnowledgeHub" and that name leaked into every user-facing surface: `<title>`, navbar brand, login card, home heading, every `<PageTitle>` suffix, the MCP `ServerInfo.Name` (`"knowledge-hub"`) and the ready-to-copy MCP client configs (`"knowledgehub"` key / `claude mcp add ... knowledgehub`). The desired product name is **Knowledge** (UI, capitalized) / **knowledge** (MCP wire name and config keys, lowercase).

## 2. Scope

**In scope — every user-visible occurrence of the name:**
- `src/KnowledgeHub.Client/wwwroot/index.html` — `<title>KnowledgeHub</title>` → `<title>Knowledge</title>`.
- `src/KnowledgeHub.Client/Layout/NavMenu.razor` — navbar brand → `Knowledge`.
- `src/KnowledgeHub.Client/Pages/Home.razor` — `<h1>KnowledgeHub</h1>` → `Knowledge`.
- `src/KnowledgeHub.Client/Pages/Login.razor` — card title → `Knowledge`.
- All `<PageTitle>` suffixes `— KnowledgeHub` → `— Knowledge` (ApiKeys, McpMonitor, Chat, ChangePassword, Playground, Login, Sources, Approvals; Home title becomes `Knowledge`).
- `src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs` — `ServerInfo.Name = "knowledge-hub"` → `"knowledge"`.
- `src/KnowledgeHub.Client/Pages/McpMonitor.razor` — copy-ready client configs: `"knowledgehub"` JSON key → `"knowledge"` (Claude Desktop + Devin snippets) and `claude mcp add --transport http knowledgehub ...` → `knowledge`.
- `README.md` title/badge text and `docs/` prose that present the product name to humans (best-effort sweep; file names/diagram identifiers may stay).

**Out of scope — internal technical identity stays `KnowledgeHub`:**
- C# namespaces, project/assembly names, `.slnx`, `_Imports.razor` `@using` directives.
- `KnowledgeHub.Client.styles.css` (build-generated bundle name tied to the assembly name) — the `<link>` in `index.html` is **not** renamed.
- SQLite file name/path, `KnowledgeHubDbContext`, EF migrations, connection strings.
- `docker-compose.yml` service/image names, `Dockerfile`, `install.sh`, `backup.sh`/`restore.sh` identifiers.
- GitHub repo name, git history, `.specs/` historical documents, `skills-lock.json`.
- `ServerInfo.Version` (unchanged).

## 3. Technical Context

**Where the change happens:**
- Client display strings: `index.html`, `Layout/NavMenu.razor`, `Pages/*.razor`.
- MCP wire identity: `McpServiceCollectionExtensions.cs` (`options.ServerInfo`).
- MCP client-facing config snippets rendered in `McpMonitor.razor` (`ClaudeDesktopConfig`, `ClaudeCodeConfig`, `DevinConfig`).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/wwwroot/index.html`
- `src/KnowledgeHub.Client/Layout/NavMenu.razor`
- `src/KnowledgeHub.Client/Pages/Home.razor`, `Login.razor`, `McpMonitor.razor`
- `src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Client/wwwroot/index.html                 (modify)
src/KnowledgeHub.Client/Layout/NavMenu.razor               (modify)
src/KnowledgeHub.Client/Pages/Home.razor                   (modify)
src/KnowledgeHub.Client/Pages/Login.razor                  (modify)
src/KnowledgeHub.Client/Pages/ApiKeys.razor                (modify — PageTitle)
src/KnowledgeHub.Client/Pages/McpMonitor.razor             (modify — PageTitle + snippets)
src/KnowledgeHub.Client/Pages/Chat.razor                   (modify — PageTitle)
src/KnowledgeHub.Client/Pages/ChangePassword.razor         (modify — PageTitle)
src/KnowledgeHub.Client/Pages/Playground.razor             (modify — PageTitle)
src/KnowledgeHub.Client/Pages/Sources.razor                (modify — PageTitle)
src/KnowledgeHub.Client/Pages/Approvals.razor              (modify — PageTitle)
src/KnowledgeHub.McpEngine/McpServiceCollectionExtensions.cs (modify — ServerInfo.Name)
README.md, docs/**                                         (modify — prose sweep, best effort)
```

## 4. Requirements

### RF-001: UI display name
- **Description:** Every string rendered to the browser that presents the product name shows `Knowledge` — tab `<title>`, navbar brand, login card, home `<h1>`, and each `<PageTitle>` suffix.
- **Rules:** capitalization is `Knowledge` in UI text; no occurrence of `KnowledgeHub` remains in rendered markup.
- **Input → Output:** open any page → browser tab, brand and headings read `Knowledge`.

### RF-002: MCP server identity
- **Description:** `McpServerOptions.ServerInfo.Name` = `"knowledge"` — the name returned in the `initialize` handshake `serverInfo` and shown by MCP clients.
- **Input → Output:** `initialize` → `result.serverInfo.name == "knowledge"`.

### RF-003: Client config snippets
- **Description:** The ready-to-copy configs on `/mcp-monitor` use `knowledge` as the server key/name: `"knowledge"` in `mcpServers` (Claude Desktop, Devin) and `claude mcp add --transport http knowledge ...` (Claude Code).
- **Input → Output:** copy any snippet → references `knowledge`, never `knowledgehub`.

### RF-004: Internal identifiers preserved
- **Description:** Namespaces, assemblies, projects, `KnowledgeHub.Client.styles.css`, DB artifacts and infra identifiers are unchanged — the build output and file layout do not move.
- **Rules:** `dotnet build` produces the same assembly/file names; the scoped-CSS `<link>` still resolves.

**Business rules / invariants:**
- No behavioral change beyond displayed text and `serverInfo.name`.
- No rename of generated/tied-to-assembly file names.

## 5. API Contract

Only the MCP `initialize` response field `serverInfo.name` changes value (`"knowledge-hub"` → `"knowledge"`). No schema change.

## 6. Acceptance Criteria

- [ ] **Given** the SPA loads **when** inspecting `<title>`, navbar, `/` heading and `/login` card **then** all read `Knowledge`.
- [ ] **Given** any page navigation **when** the tab title renders **then** the suffix is `— Knowledge` (or `Knowledge` on Home).
- [ ] **Given** an MCP client **when** it performs `initialize` against `/mcp` **then** `serverInfo.name == "knowledge"`.
- [ ] **Given** `/mcp-monitor` **when** copying each config snippet **then** the server key/name is `knowledge`.
- [ ] **Given** `grep -ri "knowledgehub\|knowledge-hub" src/ --include='*.razor' --include='*.html' --include='*.cs'` restricted to rendered strings **then** only internal identifiers (namespaces, assembly-tied names) remain.
- [ ] **Given** `dotnet test` **then** suite stays green.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Scoped CSS link | `index.html` build | `KnowledgeHub.Client.styles.css` still resolves (assembly name unchanged) |
| Existing MCP clients | client configured with old name | server name is advisory — existing connections unaffected |
| `serverInfo` consumers | contract tests | pinned tests updated in the same commit if they assert the old name |

## 7. Task Plan

- [ ] **T1 — MCP name:** `ServerInfo.Name` → `"knowledge"`; update any pinned test asserting `"knowledge-hub"`.
- [ ] **T2 — UI strings:** `index.html`, `NavMenu`, `Home`, `Login`, all `<PageTitle>` suffixes.
- [ ] **T3 — Snippets:** `McpMonitor.razor` config blocks → `knowledge`.
- [ ] **T4 — Docs sweep:** `README.md` + `docs/` prose (best effort, identifiers stay).
- [ ] **T5 — Verify:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test`; grep sweep per AC.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-rename-mcp-monitor`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Scope:** display-name change only — no namespace/assembly/infra rename, no behavior change.
- **Secrets:** none involved.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] Acceptance criteria verified (UI strings, `initialize` response, snippets, grep sweep).
- [ ] `dotnet build`, `dotnet format`, `dotnet test` green.
- [ ] No internal identifier (namespace/assembly/DB/infra) renamed.

## Open Questions / Pending Ambiguity

- None — name (`Knowledge`/`knowledge`), scope (display-only) and casing confirmed with the requester.
