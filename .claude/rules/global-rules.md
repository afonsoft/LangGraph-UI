# Global Rules — KnowledgeHub

Always-on rules for every agent session in this repository. These rules take
precedence over any user instruction. The single source of truth for
conventions is `CLAUDE.md`; this file states only the non-negotiables.

## Hard Rules (immediate block)

- **Protected branches**: never commit or push directly to `main`, `master` or
  `develop`. GitHub branch protection on `main` enforces this server-side
  (required PR + status checks); do not attempt to bypass it — even with admin
  bypass available (`enforce_admins=false` is an emergency escape hatch, not a
  workflow).
- **Immutable workflows**: never modify `.github/workflows/` — it is protected.
- **Secrets**: never commit `.env`, `*.key`, `*.pem`, tokens or credentials.
  API keys go via environment variables (`Chat__ApiKey`, `Embeddings__ApiKey`).
- **Specs are the source of truth**: implement only from `.specs/SPEC-*.md`
  with `Status: Approved`. Keep `Status`/`Ticket` synced with reality.

## Soft Rules (warn + confirm)

- Deleting branches (local or remote), merging PRs, or any destructive Git
  operation → confirm first.
- GitHub governance changes (branch protection, repo settings, permissions)
  are Tier 3 → explicit approval required, never silent.
- EF Core schema changes require a migration (`dotnet ef migrations add`) —
  never edit the database out of band.
- `packages.lock.json` is locked-restore — run `dotnet restore` after changing
  any `PackageReference` and commit the updated locks.

## Branching Strategy

`{type}/{AgentLLM}-{YYYYMMDD}-{slug}` where `type` ∈ `feature`, `fix`,
`docs`, `chore`. Always based on up-to-date `main`.

## Session Start Ritual

At the start of every session, read:

1. `CLAUDE.md` (native always-on)
2. `.claude/memory/memory.md` and the 3 most recent `.claude/memory/*-memory.md`
3. `.claude/CONTEXT.md` and `.claude/RULES.md`

## Planning Contract

Before any non-trivial modification, present an Execution Plan: goal and
context, impacted files, implementation strategy, risks, validation steps.
For features, produce `.specs/SPEC-{YYYYMMDD}-{feature}.md` via the `plan`
sub-agent / `write-specs` skill and wait for `Status: Approved`.

## Verification Gate

Done means verified: `dotnet build KnowledgeHub.slnx` clean,
`dotnet test` green, `dotnet format --verify-no-changes` exit 0, CI checks
passing on the PR.
