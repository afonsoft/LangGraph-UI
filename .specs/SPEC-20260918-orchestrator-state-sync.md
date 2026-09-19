# SPEC-20260918-orchestrator-state-sync

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `orchestrator-state-sync` |
| Type | `Docs` |
| Stack | `Markdown / harness memory` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-orchestrator-state-sync` |
| Ticket | `GAP-operation-stale-orchestrator-state` |
| Status | `Draft` |

## 1. User Story

**As a** future orchestrator or gap-analysis run
**I want** the durable harness state files to reflect delivered reality
**So that** dedup, backlog and phase tracking don't mis-propose already-shipped work or mis-mark new gaps as duplicates.

**Problem context:**
`.claude/memory/orchestrator_stats.md` is the "Orchestrator session brain"
used for backlog dedup (a prior gap-analysis run marked ONNX/sqlite-vec/
McpProxy/passthrough `DUPLICADO` solely because the backlog listed them as
`pending_approval`). It is stale by one full Epic: `current_phase` still
says "Epic #90 … PR #98 aguardando", `branch` says `main (00f1c18)` — main
is at `189f44e` with Epic #106 closed 6/6 — and Backlog #1–#6 are marked
`pending_approval` although all six were delivered (PRs #113–#116, #119,
#120, each with a Done SPEC). `.claude/memory/memory.md` has the same class
of drift ("Active task: Epic #90", last verified commit `00f1c18`), which
is partially by-design (short-term, overwritten per session) but the
backlog/ledger drift is not — it feeds future runs.

Evidence: `orchestrator_stats.md:10-13,305-310` vs
`orchestrator_sessions.md` Epic #106 entries + merged PRs + `git log` →
`189f44e`; `memory.md:3-11`.

## 2. Scope

**In scope:**
- Update `orchestrator_stats.md`: `current_phase`, `branch`, `last_updated`; mark Backlog #1–#6 `done` with their PR/SPEC references; keep append-only history sections intact.
- Refresh `memory.md` short-term state to the current verified baseline (commit, test counts, active task, next).

**Out of scope:**
- Editing `orchestrator_sessions.md` (append-only, already current).
- Rewriting gap-analysis reports (historical record).
- Changing the memory protocol itself.

## 3. Technical Context

**Where the change happens:**
`.claude/memory/orchestrator_stats.md` and `.claude/memory/memory.md` —
durable + short-term state consumed at session start per the Memory
Protocol in `CLAUDE.md`.

**Files to read before implementing:**
- `.claude/memory/orchestrator_stats.md`, `.claude/memory/memory.md`
- `.claude/memory/orchestrator_sessions.md` (source of truth for what shipped)
- `git log --oneline main`, `gh pr list --state merged`, the Epic #106 SPECs

**Files to create or modify:**
```text
.claude/memory/orchestrator_stats.md
.claude/memory/memory.md
```

## 4. Requirements

### RF-001: Reconcile orchestrator_stats.md with delivered reality
- **Description:** Set `current_phase` to post-Epic-#106 state, `branch` to `main (189f44e)` or newer, `last_updated` to execution date; mark Backlog items #1–#6 `done` with PR evidence (#115 ONNX, #116 sqlite-vec, #114 mcp-proxy-source-type, #113 passthrough, #119 systemd verify, #120 SSE E2E); update metrics counters if they track these.
- **Rules:** no history rewrites — Completed Tasks and Sessions sections stay append-only; only pointer/summary fields and the Backlog table change.
- **Input → Output:** stale stats → stats consistent with `git log` + `gh` evidence.

### RF-002: Refresh short-term memory.md
- **Description:** Update last verified commit, test baseline (271 unit + 154 integration, format exit 0 — verified this run), active task, blockers and next steps to current reality (PR #125 open; gap-analysis-20260918 in flight).
- **Input → Output:** stale memory.md → session-accurate memory.md.

**Business rules / invariants:**
- Every `done` mark cites a merged PR or Done SPEC — no unverified claims.
- Dates use the repo's `YYYYMMDD` convention.

## 5. API Contract (if applicable)

N/A — harness state files.

## 6. Acceptance Criteria

- [ ] **Given** `orchestrator_stats.md` **when** compared against `orchestrator_sessions.md` + merged PRs **then** no `pending_approval` backlog item is actually delivered.
- [ ] **Given** `memory.md` **when** read at next session start **then** commit baseline and active task match `git log`/`gh` reality.
- [ ] **Given** a future gap-analysis dedup pass **when** it reads the Backlog **then** delivered items are not re-proposed.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Item delivered but spec forgot `Done` | — | verify SPEC status first; if SPEC stale, flag it instead of silently fixing |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** reconcile each Backlog item against merged PRs/SPECs with evidence.
- [ ] **T2 — Implementation:** update stats pointers + Backlog statuses; refresh memory.md.
- [ ] **T3 — Verification:** diff review; cross-check every `done` cites a PR/SPEC.
- [ ] **T4 — Validation:** docs-only diff review.
- [ ] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

Docs: evidence-cited edits only; no code change.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`. Use `feature/Devin-20260918-orchestrator-state-sync`.
- **Scope:** the two state files only; append-only sections untouched.

## 9. Definition of Done

- [ ] Backlog items carry `done` + PR/SPEC citations.
- [ ] Pointers (phase/branch/updated) match repo reality.
- [ ] memory.md reflects current baseline.
- [ ] Guardrails respected.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-orchestrator-state-sync` referencing `GAP-operation-stale-orchestrator-state`.

## Open Questions / Pending Ambiguity

- None.
