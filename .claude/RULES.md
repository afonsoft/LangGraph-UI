# RULES.md — Guardrails Summary

Computational controls over prompts: `settings.json` permissions, CI gates and
GitHub branch protection cannot be ignored — a prompt can. Full conventions in
`CLAUDE.md` §Convenções; always-on enforcement wording in
`.claude/rules/global-rules.md`.

## Hard Rules (immediate block)

- No commit/push to `main`, `master`, `develop` — enforced server-side by
  branch protection (PR + status checks required).
- `.github/workflows/` is immutable.
- No secrets: `.env`, `*.key`, `*.pem`, tokens — `settings.json` denies reads
  of the common paths.
- Implement only `Status: Approved` SPECs from `.specs/`.

## Soft Rules (warn + confirm)

- Destructive Git ops (branch delete, force-push, reset --hard).
- GitHub governance changes (protection, repo settings) — Tier 3, explicit
  approval.
- Deploy/install mutations (`./install.sh`, `docker compose`, systemd).
- EF migrations and `packages.lock.json` regeneration.

## Tool permissions (`.claude/settings.json`)

| Category | Policy | Examples |
| --- | --- | --- |
| Read-only | `allow` | `Read`, `Grep`, `Glob` |
| Write / local mutation | `ask` | `Edit`, `Write`, `git commit` |
| External / destructive | `ask` | `git push`, `gh pr merge`, `gh api -X PUT/DELETE`, branch deletion |
| Denied | `deny` | `.env`/`*.key`/`*.pem` reads, `.github/workflows/**` writes |

## Per-environment

- **dev** (local): free within permissions above.
- **CI** (GitHub Actions): gate = build + unit + integration + Blazor + Docker.
- **prod** (`rag.afonsoft.dev`): no unauthenticated mutation; systemd/docker
  operations require explicit approval.
