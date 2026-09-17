# CONTEXT.md — Context Engineering

How context reaches the agent in this repository. `CLAUDE.md` is the single
source of truth; every other artifact is referenced, never duplicated.

## Loading priority

| Tier | Mechanism | Artifacts |
| --- | --- | --- |
| 1. Native always-on | Loaded by the harness automatically | `CLAUDE.md`, `.claude/rules/global-rules.md` |
| 2. Always-on via ritual | Read at session start (see `global-rules.md` §Session Start Ritual) | `.claude/memory/memory.md`, last 3 `.claude/memory/*-memory.md`, `CONTEXT.md`, `RULES.md` |
| 3. Pattern-matched | `paths:` frontmatter in `.claude/rules/` | `dotnet.md` (`**/*.cs`, `**/*.razor`, …) |
| 4. On-demand | Loaded when the task references them | `.claude/skills/*/SKILL.md`, `TOOLS.md`, `WORKFLOWS.md`, `MEMORY.md`, `docs/architecture/`, `.specs/` |
| 5. Progressive disclosure | Large files/dirs | Directory map → headers → full content |

## Token budget

- Reserve ~20% of the context window for output.
- Chunk files over ~500 lines — read by `offset`/`limit`, not whole-file.
- Prefer `grep`/`glob` hits over speculative reads.

## Compaction ladder

budget reduction → snip → microcompact → collapse → auto-compact.
Before any compaction or session end: append `## Session summary` to today's
`.claude/memory/{YYYYMMDD}-memory.md` (protocol in `.claude/MEMORY.md`).

## Repo-specific context sources

- `.specs/` — 50+ SPEC SDD files; the source of truth for implemented features.
- `.claude/memory/` — orchestrator stats + gap-analysis reports (durable state).
- `docs/architecture/` — Mermaid + draw.io diagrams.
- `skills-lock.json` — pinned SHA-256 per installed skill.
