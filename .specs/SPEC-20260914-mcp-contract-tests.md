# SPEC-20260914-mcp-contract-tests

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mcp-contract-tests` |
| Type | `Infra` |
| Stack | `.NET 10`, `xUnit`, `MCP` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-persistence-hardening` |
| Ticket | `[A DEFINIR]` |
| Status | `Approved` |

## 1. User Story

**As a** maintainer
**I want** contract tests pinning the MCP tool catalog (names, schemas, annotations) and the SignalR `McpActivityEvent` shape
**So that** a refactor can't silently break MCP clients (Cursor, Claude Desktop) or the Monitor UI — our equivalent of the `stream_events` v3 contract break in the article.

**Problem context:** The tool catalog is dynamic (`query_{slug}` per source, DeepWiki bypass tools, knowledge tools). Nothing asserts the stable surface; a renamed tool, changed `InputSchema` or a renamed JSON property in `McpActivityEvent` would only surface as a broken client in production.

## 2. Scope

**In scope:**
- Tests pinning the set of built-in tool names and their `InputSchema` JSON (normalized serialization).
- Tests pinning `McpActivityEvent` JSON shape (property names + `Kind` serialization) and `McpActivityKind` member names.
- Tests pinning JSON-RPC method surface already exercised (tools/list, tools/call, resources/list, resources/read) — via existing `TestMcp` harness.
- A "contract snapshot" approach: expected JSON embedded in the test file (readable, reviewable diff on break).

**Out of scope:**
- Snapshot testing libraries (Verify/Snapshooter) — plain embedded expected JSON keeps deps at zero.
- Dynamic `query_{slug}` tools' exact names (they depend on registered sources; test the generator pattern, not instances).
- UI-side (Blazor) contract tests.

## 3. Technical Context

**Where the change happens:** `tests/KnowledgeHub.Tests.Integration/` (tool catalog via real MCP session) and `tests/KnowledgeHub.Tests.Unit/` (`McpActivityEvent` serialization).

**Files to read:**
- `tests/KnowledgeHub.Tests.Integration/TestMcp.cs`, `McpToolsTests.cs`, `McpTransportTests.cs` — existing MCP client harness.
- `src/KnowledgeHub.Server/Mcp/ToolProviders/` — the three providers (knowledge, source-query, obsidian) + DeepWiki.
- `src/KnowledgeHub.McpEngine/Activity/McpActivityEvent.cs` — the record under contract.
- `KnowledgeHubServiceCollectionExtensions.cs` — ListTools/CallTool handlers.

**Files to create or modify:**
```text
tests/KnowledgeHub.Tests.Integration/McpContractTests.cs     (new)
tests/KnowledgeHub.Tests.Unit/McpEngine/ActivityEventContractTests.cs (new)
```

## 4. Requirements

### RF-001: Tool catalog contract
- **Description:** `tools/list` over a real in-process MCP session returns at minimum the pinned built-in set: `search_knowledge`, `ask_knowledge`, `write_knowledge`, `read_document`, `write_note`, `ask_question`, `read_wiki_structure`, `read_wiki_contents` (exact list finalized from code). Each pinned tool's `InputSchema` (normalized JSON, key-ordered) must equal the embedded expected JSON; `ReadOnlyHint` must match.
- **Input → Output:** tools/list → assert exact names present + per-tool schema equality; failure message shows the JSON diff.

### RF-002: Activity event contract
- **Description:** `JsonSerializer.Serialize(new McpActivityEvent{...all fields set})` produces a JSON object with exactly the pinned property set and `Kind` serialized as the enum member name (or current wire format — pinned as-is). `Enum.GetNames<McpActivityKind>()` equals pinned list `["SessionOpened","SessionClosed","Request","ToolCall"]`.

### RF-003: Unknown-tool error contract
- **Description:** `tools/call` with an unregistered name → JSON-RPC error with `MethodNotFound` code (already the behavior — now pinned).

### RF-004: Diff-friendly failure output
- **Description:** On contract break, the test failure prints expected vs actual JSON side by side (or unified diff string).

## 5. API Contract

The contract *is* the deliverable: pinned tool names + schemas + activity event shape.

## 6. Acceptance Criteria

- [ ] **Given** a refactor renames a tool or drops a schema property **when** tests run **then** `McpContractTests` fails with a readable diff.
- [ ] **Given** a field added/renamed on `McpActivityEvent` **when** tests run **then** `ActivityEventContractTests` fails.
- [ ] **Given** no contract change **when** `dotnet test` runs **then** all contract tests pass.
- [ ] **Given** a deliberate breaking change is needed **then** updating the embedded expected JSON is the only required test edit (documented in the test file header).

## 7. Task Plan

- [ ] **T1 — Catalog pin:** enumerate built-in tools from code; write `McpContractTests` (names + schemas + readonly hints + MethodNotFound).
- [ ] **T2 — Event pin:** `ActivityEventContractTests` (property set, Kind names).
- [ ] **T3 — Run suite:** green; document "how to update the contract" comment in test files.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-persistence-hardening`.
- **Deps:** no new test dependencies.
- **Scope:** tests only — no production code changes unless a test exposes a bug (then separate commit).

## 9. Definition of Done

- [ ] Contract tests fail on tool rename/schema change (verified by a deliberate temporary mutation, then reverted).
- [ ] Full suite green.
- [ ] Contract-update instructions live in the test file header.

## Open Questions / Pending Ambiguity

- None.
