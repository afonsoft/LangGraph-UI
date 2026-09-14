# Gap Analysis — 20260914

- Repository: /home/ubuntu/repos/LangGraph-UI | Branch: main | Commit: cae2a48
- Phase reached: gate
- Mode: full
- Build: `dotnet build KnowledgeHub.slnx -c Release` green (1 xUnit2012 warning). Tests: 89/89 green (51 unit + 38 integration).

## 1. Source inventory

| Source | Status | Notes |
| --- | --- | --- |
| .specs/ | present | 27 SPECs — 7 Done, 20 Approved |
| docs/ | present | architecture docs present; docs/adr/ and docs/specs/ empty |
| docs/architecture/ | present | system-architecture.md + mermaid + drawio |
| .claude/CONTEXT.md | absent | |
| .claude/memory/ | present | orchestrator_stats.md only; no prior gap-analysis |
| CLAUDE.md / AGENTS.md / README.md | present | CLAUDE.md stale (claims no source code) |
| tests / linters / CI | present | 4 workflows; suite green |
| gh auth + remote | ok | afonsoft/LangGraph-UI; issues #2–#8 closed epics |

## 2. AS-IS × TO-BE matrix

| Topic | AS-IS | TO-BE | Sources |
| --- | --- | --- | --- |
| Base platform (sources, ingestion, MCP, UI, packaging) | Implemented: entities, DbContext, REST APIs, DynamicToolCatalog, McpEngine, Blazor pages, single-file publish | SPECs 20260913-* | src/**, tests/**, issues #2–#8 closed |
| LLM answer synthesis | Absent — no IChatClient/Chat: config/POST /api/ask; ask_knowledge returns raw context | SPEC-20260914-llm-answer-synthesis (Approved) | src/ grep: no IChatClient |
| Agent chat loop | Absent — no AgentService, agent_chat tool, POST /api/agent | SPEC-20260914-agent-chat-loop (Approved; DependsOn llm-answer-synthesis) | grep: no AgentService/agent_chat |
| Conversation threads | Absent — no ConversationThread/Message entities or /api/threads | SPEC-20260914-conversation-threads (Approved; DependsOn agent-chat-loop) | Domain/Entities has 3 entities only |
| HITL tool approval | Absent — no ToolApproval entity, /api/approvals, resume | SPEC-20260914-hitl-tool-approval (Approved; DependsOn agent-chat-loop) | grep: no ToolApproval |
| Streaming answers | Absent — no /api/ask/stream, /api/agent/stream | SPEC-20260914-streaming-answers (Approved; DependsOn llm+agent) | grep: no stream endpoints |
| Hybrid retrieval (FTS5+RRF) | Absent — SearchService is pure vector cosine; no chunks_fts, no mode/source args | SPEC-20260914-hybrid-retrieval (Approved) | SearchService.cs, no fts5 in Migrations |
| WebPage/DocumentFile connectors | Absent — IngestionService returns "connector X not implemented" for non-vault types | SPEC-20260914-webpage-docfile-connectors (Approved) | IngestionService.cs:46-47 |
| Obsidian-over-WebDAV | Partial — mount-as-vault works via existing connector; UI exposes autoSync/syncInterval (SourceEditDialog.razor:51-54). Missing: LastSyncStatus/LastError on source + mount-specific error (RF-002), compose volume doc (RF-006), README WebDAV section (RF-007) | SPEC-20260914-obsidian-webdav (Approved) | KnowledgeSource.cs (no status fields), README.md (no WebDAV), docker-compose.yml (no vault volume) |
| Backup/restore | Absent — no backup.sh/restore.sh at root | SPEC-20260914-backup-restore (Approved) | ls *.sh → install.sh only |
| Health checks | Absent — no MapHealthChecks, no /health/live|/ready | SPEC-20260914-health-checks (Approved) | Program.cs, grep: no HealthCheck |
| Error handling (ProblemDetails) | Absent — no AddProblemDetails/IExceptionHandler | SPEC-20260914-error-handling (Approved) | grep: no ProblemDetails |
| Config validation | Absent — no startup validator (EmbeddingCompatibilityCheck covers dims only) | SPEC-20260914-config-validation (Approved) | Program.cs:20-40 |
| Graceful shutdown | Partial — CancellationToken honored in VaultWatcherService/IngestionService; ShutdownTimeout not configured; no MCP session drain | SPEC-20260914-graceful-shutdown (Approved) | grep: no ShutdownTimeout/HostOptions |
| Doc/status sync | Stale — 7 base SPECs say Approved but are implemented; Ticket fields "[A DEFINIR]" though issues #2–#8 exist; CLAUDE.md outdated | SPECs are source of truth (CLAUDE.md/AGENTS.md) | spec metadata vs src/** |

## 3. Candidates and verdicts

| Key | Category | Verdict | Priority | Spec | Issue | Evidence |
| --- | --- | --- | --- | --- | --- | --- |
| GAP-implementation-llm-answer-synthesis | implementation | CONFIRMADO | high | SPEC-20260914-llm-answer-synthesis.md | — | no IChatClient//api/ask in src |
| GAP-implementation-agent-chat-loop | implementation | CONFIRMADO | high | SPEC-20260914-agent-chat-loop.md | — | no AgentService/agent_chat |
| GAP-implementation-webpage-docfile-connectors | implementation | CONFIRMADO | high | SPEC-20260914-webpage-docfile-connectors.md | — | IngestionService.cs:46-47 skips non-vault |
| GAP-implementation-hybrid-retrieval | implementation | CONFIRMADO | medium-high | SPEC-20260914-hybrid-retrieval.md | — | no FTS5/RRF/mode args |
| GAP-implementation-hitl-tool-approval | implementation | CONFIRMADO | medium-high | SPEC-20260914-hitl-tool-approval.md | — | no ToolApproval |
| GAP-implementation-conversation-threads | implementation | CONFIRMADO | medium | SPEC-20260914-conversation-threads.md | — | no thread entities |
| GAP-operation-backup-restore | operation | CONFIRMADO | medium | SPEC-20260914-backup-restore.md | — | no backup.sh/restore.sh |
| GAP-operation-health-checks | operation | CONFIRMADO | medium | SPEC-20260914-health-checks.md | — | no health endpoints |
| GAP-implementation-obsidian-webdav | implementation | CONFIRMADO | medium | SPEC-20260914-obsidian-webdav.md | — | partial: RF-002/006/007 missing |
| GAP-implementation-config-validation | implementation | CONFIRMADO | medium | SPEC-20260914-config-validation.md | — | no validator |
| GAP-implementation-error-handling | implementation | CONFIRMADO | medium | SPEC-20260914-error-handling.md | — | no ProblemDetails |
| GAP-implementation-streaming-answers | implementation | CONFIRMADO | medium | SPEC-20260914-streaming-answers.md | — | no SSE endpoints |
| GAP-implementation-graceful-shutdown | implementation | CONFIRMADO | low-medium | SPEC-20260914-graceful-shutdown.md | — | ShutdownTimeout absent; CT already honored |
| GAP-documentation-spec-status-sync | documentation | CONFIRMADO | low | (meta — update existing SPECs/CLAUDE.md) | — | spec Status vs code/issues |
| GAP-security-authn-api-mcp | security | REJEITADO | — | — | — | out-of-scope documented (SPEC knowledge-sources §2, mcp-sse-engine §2); localhost-first + AllowedHosts pinned |
| GAP-security-env-file | security | REJEITADO | — | — | — | .env untracked + gitignored; contains only KNOWLEDGEHUB_PORT |
| GAP-tests-write-tool-confirm | security | DUPLICADO | — | SPEC-20260914-hitl-tool-approval.md | — | tracked by HITL spec + agent allowWrite |
| GAP-docs-empty-adr-dirs | documentation | REJEITADO | — | — | — | no TO-BE source requires ADR content |
| GAP-operation-systemd-unverified | operation | INCONCLUSIVO | — | SPEC-20260913-install-deploy.md | — | AC marked done with honest "não verificado" note — needs host with sudo/systemd to verify |

## 4. Approval gate

- Decision: pending — awaiting user approval to create Issues (Phase 6) and/or hand off to orchestrator (Phase 7).
- Note: every CONFIRMADO gap already maps to an existing Approved SPEC — no new Draft SPECs were generated (dedup against .specs/).

## 5. Issues

- Epic: not created (gate pending)
- Slices: not created

## 6. Orchestrator handoff

- Not reached (gate pending). Pre-conditions ready: tree clean, gh authed, specs Approved.

## 7. Pendencies

- INCONCLUSIVO: `--systemd` install path never verified on a real host.
- Execution order implied by DependsOn: llm-answer-synthesis → agent-chat-loop → {conversation-threads, hitl-tool-approval, streaming-answers}; hybrid-retrieval and webpage-docfile-connectors independent; infra specs independent.
