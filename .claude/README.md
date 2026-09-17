# .claude/ — Agent Harness

Harness infrastructure for AI agents working in this repository.
`CLAUDE.md` (repo root) is the single source of truth — everything here is
either natively loaded or referenced from it. Generated per
`SPEC-20260917-harness-files` (#94).

## Structure

```text
.claude/
├── settings.json              # permissions (allow/ask/deny) + hooks — versioned
├── rules/
│   ├── global-rules.md        # always-on hard/soft rules, session ritual
│   └── dotnet.md              # path-scoped: **/*.cs, *.csproj, *.razor
├── agents/
│   ├── plan.md                # SPEC SDD writer (no implementation)
│   ├── review.md              # code & security reviewer
│   └── test.md                # verification gate runner
├── skills/                    # symlinks → ../.agents/skills/* (skills-lock.json pins)
├── memory/                    # memory.md + {YYYYMMDD}-memory.md + run reports
├── CONTEXT.md                 # context engineering (loading, budget, compaction)
├── RULES.md                   # guardrails summary
├── MEMORY.md                  # memory protocol documentation
├── TOOLS.md                   # tools/MCP inventory + risk policy
└── WORKFLOWS.md               # feature lifecycle, CI gate, deploy, skills
```

## Loading model

- **Native always-on**: `CLAUDE.md`, `rules/global-rules.md` (no `paths:`).
- **Always-on via ritual**: `memory/memory.md`, `CONTEXT.md`, `RULES.md`.
- **Path-scoped**: `rules/dotnet.md` loads for `*.cs`/`*.csproj`/`*.razor`.
- **On-demand**: `agents/*`, `skills/*`, `TOOLS.md`, `WORKFLOWS.md`,
  `MEMORY.md`, `README.md`, dated memory files.

## Skills

`.claude/skills/<name>` is a symlink into `.agents/skills/` — the canonical
install dir of the skills.sh CLI, gitignored and restored on fresh clones by
`npx skills experimental_install` using `skills-lock.json`. Add/update via
`npx skills add|update`, never by hand-editing symlinks.

## Adding to the harness

- New always-on rule → `rules/global-rules.md` (and mirror the rationale in
  `CLAUDE.md` §Convenções).
- New path-scoped rule → `rules/{domain}.md` with `paths:` frontmatter.
- New sub-agent → `agents/{name}.md`; `name:` must equal the filename.
- New permission → `settings.json`; `settings.local.json` stays local and
  gitignored.

## Platform compatibility

| Platform | Entry point | Reads `.claude/`? |
| --- | --- | --- |
| Claude Code | `CLAUDE.md` | native |
| Devin CLI/Desktop | `AGENTS.md` → `CLAUDE.md` | via `read_config_from.claude` |
| Cursor / OpenCode / Gemini | `AGENTS.md` → `CLAUDE.md` | thin reference only |
