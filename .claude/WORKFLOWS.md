# WORKFLOWS.md — Automation

On-demand reference for the repeatable flows of this repository.

## Feature lifecycle (canonical)

```
gap-analysis → write-specs → [human approves: Status=Approved]
  → create-issues (Epic + slices) → execute-specs
  → verification gate → PR → merge → memory update
```

1. `write-specs` / `plan` agent produces `.specs/SPEC-{YYYYMMDD}-{slug}.md`.
2. User approval flips `Status` to `Approved` — required before any code.
3. `execute-specs` implements on `feature/…` / `fix/…` / `docs/…` branch.
4. Verification loop: `dotnet build` (0 warnings) → `dotnet test` (green)
   → `dotnet format --verify-no-changes` (exit 0) → CI checks on the PR.
5. Merge via PR only — `main` is protected. Update SPEC `Status: Done`
   with merge evidence.
6. Update `.claude/memory/` (orchestrator stats / dated memory).

## CI gate (`.github/workflows/`)

Required status checks on `main`: `Build KnowledgeHub (.NET 10)`,
`Unit Tests (xUnit)`, `Integration Tests (SQLite)`,
`Blazor WASM Client Validation`, `Docker Image Build`.
Additional (non-blocking): CodeQL, Qodana, Snyk, GitGuardian, Devin Review;
SonarQube/Trivy run conditionally.

## Deploy

`./install.sh` — docker build+run (default, host port 5000 → container 8080)
or `--host` self-contained install (`/opt/knowledgehub`, `--systemd` for the
service). `docker-compose.yml` is the declarative equivalent; `.env` feeds
`${VAR}` substitution, `docker-compose.override.yml` (gitignored) for
host-specific mounts/env. Production: `https://rag.afonsoft.dev`.

## Backup / restore

`./backup.sh` / `./restore.sh` — SQLite file + uploads.

## Skills maintenance

`npx skills update -p -y` bumps installed skills from `afonsoft/skills`;
`npx skills add/remove <name>`; `skills-lock.json` records SHA-256 pins.
`.agents/skills/` holds the real files (gitignored); `.claude/skills/*` are
symlinks — fresh clones restore via `npx skills experimental_install`.
