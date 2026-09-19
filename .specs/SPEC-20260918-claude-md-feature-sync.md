# SPEC-20260918-claude-md-feature-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `claude-md-feature-sync` |
| Type | `Docs` |
| Stack | `Markdown` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-claude-md-sync` |
| Ticket | `GAP-documentation-claude-md-feature-drift` |
| Status | `Done` |

## 1. User Story

**As a** future agent or contributor reading `CLAUDE.md`
**I want** the "Estado Atual" feature list to reflect everything actually shipped
**So that** the declared single source of truth does not mislead about what the platform does.

**Problem context:**
`CLAUDE.md` is declared the repo's single source of truth (`AGENTS.md`,
`.claude/CONTEXT.md`), and `SPEC-20260916-claude-md-sync` established the
pattern of syncing it after feature deliveries. Its feature enumeration was
last updated 2026-09-17 and predates 7 delivered SPECs: ONNX local
embeddings (#115), sqlite-vec KNN (#116), catalog-driven McpProxy
source-type (#114), upstream tools passthrough (#113), systemd host
verification fixes (#119), SSE `?access_token=` fix (#120), and installable
PWA + icon-rail sidebar (#123/#124).

Evidence: `CLAUDE.md` §Estado Atual (ends at "scripts de backup/restore +
packaging standalone") vs `.specs/SPEC-20260917-{onnx-local-embeddings,
sqlite-vec-search,mcp-proxy-source-type,upstream-tools-passthrough,
systemd-host-verify}.md` and `.specs/SPEC-20260918-ui-layout-polish.md` —
all `Done` with merged PRs.

## 2. Scope

**In scope:**
- Update `CLAUDE.md` "Estado Atual" paragraph to include the 7 delivered features, in the same compact prose style.
- Keep every other section untouched unless factually wrong.

**Out of scope:**
- Rewriting README (already synced — mentions PWA/icon-rail).
- Changing harness rules, commands, or conventions sections.
- Translating or restructuring the file.

## 3. Technical Context

**Where the change happens:**
`CLAUDE.md` — single file, "Estado Atual" paragraph only.

**Files to read before implementing:**
- `CLAUDE.md`
- The 7 SPEC files listed above (titles + delivered scope for accurate wording)

**Files to create or modify:**
```text
CLAUDE.md
```

## 4. Requirements

### RF-001: Sync feature enumeration
- **Description:** The feature list must mention: ONNX local embeddings provider (all-MiniLM-L6-v2, opt-in, models/ gitignored), sqlite-vec native KNN vector store provider (opt-in), McpProxy as a catalog `SourceType` for upstream MCP servers, upstream `tools/list` passthrough for private DeepWiki tools when ApiKey set, `install.sh --host --systemd` verified on real host (incl. the two fixes), `?access_token=` accepted on `/mcp/sse` for header-less SSE clients, installable PWA + collapsible icon-rail sidebar.
- **Rules:** compact prose in the existing pt-BR style; no invented claims — every addition cites a Done SPEC.
- **Input → Output:** outdated paragraph → paragraph covering all Done SPECs through #124.

**Business rules / invariants:**
- Only facts backed by a `Done` SPEC may be added.
- Preserve existing wording for unchanged features.

## 5. API Contract (if applicable)

N/A — documentation.

## 6. Acceptance Criteria

- [ ] **Given** `CLAUDE.md` **when** grepped for `onnx|sqlite-vec|passthrough|SourceType|access_token|PWA|icon-rail|systemd` **then** each delivered feature has a mention.
- [ ] **Given** the updated file **when** compared to `.specs/` Done list **then** no post-2026-09-17 delivered feature is missing.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Feature delivered but not user-facing | systemd verify fixes | mention briefly under deploy/packaging clause |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** extract the delivered scope line from each of the 7 SPECs.
- [ ] **T2 — Implementation:** edit the Estado Atual paragraph.
- [ ] **T3 — Verification:** grep-check each feature name; confirm no other section needs sync.
- [ ] **T4 — Validation:** diff review only (docs change).
- [ ] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

Docs: diff review + grep evidence that every new mention maps to a Done SPEC.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`. Use `feature/Devin-20260918-claude-md-sync`.
- **Scope:** Estado Atual paragraph only; no speculative doc changes.

## 9. Definition of Done

- [ ] All 7 delivered features reflected in CLAUDE.md.
- [ ] No stale or invented claims introduced.
- [ ] Guardrails respected.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-claude-md-sync` referencing `GAP-documentation-claude-md-feature-drift`.

## Open Questions / Pending Ambiguity

- None.
