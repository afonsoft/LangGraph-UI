# SPEC-20260918-install-docker-env-passthrough

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `install-docker-env-passthrough` |
| Type | `Bugfix` |
| Stack | `Bash / Docker` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-install-docker-env` |
| Ticket | `GAP-implementation-install-docker-env` |
| Status | `Done` |

## 1. User Story

**As a** user deploying Knowledge MCP Hub with `./install.sh --docker`
**I want** the documented environment overrides (`EMBEDDINGS_*`, `VECTORSTORE_*`, `DEEPWIKI_*`, `FIRECRAWL_*`, `TAVILY_*`, `CHAT_*`, `CACHE_*`, `AUTH_*`) to actually reach the container
**So that** the deployed instance honors my `.env`/shell configuration instead of silently running with defaults.

**Problem context:**
`install.sh` usage (lines 51-52) advertises `EMBEDDINGS_*`, `VECTORSTORE_*`,
`DEEPWIKI_*` as env overrides, and the script loads `.env` into the shell
environment — but `docker_deploy` only passes `-e AllowedHosts` to
`docker run`. Every other documented variable is inert in docker mode: the
container starts with appsettings defaults, a silent degradation that
`docker compose` does not have (it maps the full env surface).
Recorded as a known limitation in `.claude/memory/orchestrator_sessions.md`
("deploys on this host should use docker compose"), but never fixed.

Evidence: `install.sh:13-21` (.env load), `install.sh:51-52` (usage claims),
`install.sh:140-145` (only `-e AllowedHosts`), `docker-compose.yml:19-50`
(full env mapping for parity reference).

## 2. Scope

**In scope:**
- `docker_deploy` propagates the documented app-config env vars to `docker run`, sourced from shell env/`.env` (same names compose uses).
- `--data-dir`/`--port`/image/container-name behavior unchanged.
- Update the usage text if the propagated set differs from what it advertises.

**Out of scope:**
- Host-mode (`--host`) changes — env vars already reach the process there.
- Adding new configuration knobs; only propagating existing ones.
- Vault/extra bind mounts in docker mode (compose override remains the path for host-specific mounts).

## 3. Technical Context

**Where the change happens:**
`install.sh` → `docker_deploy()` `docker run` invocation. The `.env` loader
already exports variables into the shell env; the fix maps them into `-e`
flags (or `--env-file`) on the run command, mirroring `docker-compose.yml`'s
environment block.

**Files to read before implementing:**
- `install.sh`
- `docker-compose.yml` + `docker-compose.override.yml` (canonical env list)
- `.env.example` (documented variable names)
- `.specs/SPEC-20260913-install-deploy.md`

**Files to create or modify:**
```text
install.sh
```

## 4. Requirements

### RF-001: Propagate app config env vars to docker run
- **Description:** `docker_deploy` must pass the application configuration variables consumed by the container — the same set `docker-compose.yml` maps: `AllowedHosts`, `Embeddings__*`, `VectorStore__*`, `DeepWiki__*`, `Firecrawl__*`, `Tavily__*`, `Cache__*`, `Auth__AdminInitialPassword`, and `Chat__*` (matching the override layer).
- **Rules:** preserve shell-env > `.env` precedence already implemented; empty/unset variables must not inject empty `-e` values where the app would treat empty as "set" (mirror compose defaults where applicable, e.g. `Auth__AdminInitialPassword` fallback); secrets must never be echoed to logs.
- **Input → Output:** `EMBEDDINGS_PROVIDER=onnx ./install.sh --docker` → `docker inspect knowledgehub` shows `Embeddings__Provider=onnx`.

### RF-002: Keep usage text truthful
- **Description:** The `Env overrides` line in `usage()` must list exactly the families actually propagated after RF-001.
- **Input → Output:** `./install.sh --help` → documented set == propagated set.

**Business rules / invariants:**
- Non-secret defaults in compose (`*-:true`, `*-:memory`, `*-:deterministic` style fallbacks) must keep working when the var is absent — reproduce the same default expressions or document divergence.
- No secret values in stdout/logs; `-e` flags don't print values.

## 5. API Contract (if applicable)

N/A — deployment script fix.

## 6. Acceptance Criteria

- [ ] **Given** `.env` with `CHAT__PROVIDER=openai` + `EMBEDDINGS_PROVIDER=onnx` **when** `./install.sh --docker` runs **then** `docker inspect knowledgehub` shows both values in `Config.Env` and the app logs no config-validation error.
- [ ] **Given** no optional vars set **when** `./install.sh --docker` runs **then** the container starts healthy on the default port with compose-equivalent defaults.
- [ ] **Given** `./install.sh --help` **when** read **then** the advertised env families match the propagated set.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Var set only in `.env` | `EMBEDDINGS_MODEL=x` in `.env` | propagated (loaded by the existing `.env` reader) |
| Var set in shell and `.env` | shell `EMBEDDINGS_MODEL=y` | shell wins (existing precedence) |
| Secret vars | `*_APIKEY` | passed via `-e NAME` (value not printed) |
| Existing container | prior `knowledgehub` running | replaced, env reapplied (existing rm -f path) |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** diff compose env block vs `docker_deploy` flags; enumerate the full variable list.
- [ ] **T2 — Implementation:** add env propagation to `docker_deploy` (explicit `-e` list or `--env-file` from a filtered env set); update usage text.
- [ ] **T3 — Verification:** `shellcheck install.sh`; dry-run with a populated `.env` → `docker inspect` confirms vars; container health check passes.
- [ ] **T4 — Validation:** `./install.sh --docker` end-to-end on this host; `curl :PORT/health` 200.
- [ ] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

Bugfix/Infra: shellcheck clean + live `docker inspect`/`curl` evidence on a
real deploy (same standard used for the systemd verification in #111).

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`. Use `feature/Devin-20260918-install-docker-env`.
- **Security:** never print or commit secret values; `.env` stays gitignored.
- **Scope:** docker-mode env parity only; do not refactor the whole script.

## 9. Definition of Done

- [ ] RF-001/RF-002 implemented; usage text matches behavior.
- [ ] `shellcheck` clean; live deploy verified with `docker inspect` + health endpoint.
- [ ] Edge cases handled; no secrets exposed in output.
- [ ] Guardrails respected.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-install-docker-env` referencing `GAP-implementation-install-docker-env`.

## Open Questions / Pending Ambiguity

- Propagation mechanism: explicit `-e` list (mirrors compose, self-documenting) vs generated `--env-file` — recommend explicit `-e` list for readability; confirm at execution.
