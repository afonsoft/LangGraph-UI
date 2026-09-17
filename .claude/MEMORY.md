# MEMORY.md — Memory Protocol (documentation only)

Reference for the `.claude/memory/` protocol. **No state or history lives in
this file** — state lives exclusively in `.claude/memory/`.

## Tiers

| Tier | File | Lifetime | Content |
| --- | --- | --- | --- |
| Short-term | `.claude/memory/memory.md` | Current session, overwritten, ≤100 lines | Working state: branch, baseline, blockers, next action |
| Long-term | `.claude/memory/{YYYYMMDD}-memory.md` | Permanent, append-only, one per day | Prompts, decisions, lessons, debt, discoveries, checkpoints |
| Durable knowledge | `.claude/knowledge/{slug}.md` | Permanent | Reusable facts promoted out of memory |
| Reports | `.claude/memory/gap-analysis-*.md`, `orchestrator_stats.md` | Permanent | Orchestrator/gap-analysis run records (existing convention) |

## Read protocol

At session start: `memory.md`, then the 3 most recent dated files (descending
filename). Never read the whole folder. Treat long-term memory as a hint —
verify just-in-time against current code before relying on it.

## Write triggers

| Trigger | Target | What |
| --- | --- | --- |
| User prompt/instruction | Long-term | One-line summary under `## Prompts` |
| Session end / pre-compaction | Long-term | `## Session summary` — outcome + where work stopped |
| Verified checkpoint/commit | Both | Update `memory.md`; append `## Checkpoints` |
| Decision | Long-term | `## Decisions` — rationale + discarded alternatives |
| Mistake corrected | Long-term | `## Lessons learned` |
| Reusable knowledge | `.claude/knowledge/` | Promote; link under `## Discoveries` |
| Stale fact | Long-term | Append `SUPERSEDED:` entry — never rewrite history |

## Security

Zero secrets, tokens, passwords, connection strings, private keys, PII.
Reference identifiers, never values.
