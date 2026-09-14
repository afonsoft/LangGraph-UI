# SPEC-20260913-install-deploy

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `install-deploy` |
| Type | `Infra` |
| Stack | `.NET 10`, `Bash`, `Docker`, `docker-compose` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260913-install-deploy` |
| Ticket | `[A DEFINIR — GitHub Issue via /create-issues]` |
| Status | `Approved` |

## 1. User Story

**As a** end user / operator
**I want** a single `install.sh` that builds, tests and deploys KnowledgeHub — either as a Docker container or as a self-contained host binary — plus a production `Dockerfile` and `docker-compose.yml`
**So that** I can go from a fresh clone to a running instance with one command, on any Linux machine, with or without Docker.

**Problem context:** The platform already supports standalone single-file publish (SPEC-20260913-standalone-packaging). What's missing is the delivery layer: a reproducible container image and one entrypoint script that wraps build → test → publish → run.

## 2. Scope

**In scope:**
- `Dockerfile` multi-stage: SDK 10 build → `publish -r linux-musl-x64` (self-contained, single-file) → minimal Alpine `runtime-deps` final image, non-root, `/data` volume, healthcheck.
- `.dockerignore` to keep the build context small.
- `install.sh` (bash, `set -euo pipefail`) with two modes:
  - `--docker` (default): `docker build` + `docker run` with `./data` bind-mounted to `/data`.
  - `--host`: `dotnet publish` single-file for the detected RID + install to a prefix + optional `--systemd` unit.
- `docker-compose.yml` mirroring the `--docker` run declaratively.
- `README.md` section documenting both modes.
- `.gitignore` additions for `publish/` and `data/`.

**Out of scope:**
- Registry push (GHCR/Docker Hub), image signing, multi-arch manifests.
- CI workflow (`.github/workflows/` is protected).
- Kubernetes/Helm, TLS termination, reverse proxy.
- Windows/macOS host install (host mode targets Linux; macOS publish may work but is not a validation target).

## 3. Technical Context

**Where the change happens:** repository root (`Dockerfile`, `.dockerignore`, `install.sh`, `docker-compose.yml`, `README.md`, `.gitignore`). No C# changes required — `KnowledgeHub.Server.csproj` already carries `PublishSingleFile`/`SelfContained` and `Program.cs` already resolves `KnowledgeHub:DatabasePath`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/KnowledgeHub.Server.csproj` — publish properties (`AssemblyName=KnowledgeHub`, self-contained).
- `src/KnowledgeHub.Server/Program.cs` — startup order, `DatabasePath.EnsureDirectory`.
- `src/KnowledgeHub.Server/DatabasePath.cs` — env var contract.
- `.specs/SPEC-20260913-standalone-packaging.md` — approved packaging behavior.
- `.gitignore`, `global.json`, `README.md`.

**Files to create or modify:**
```text
Dockerfile                 (new)
.dockerignore              (new)
install.sh                 (new, chmod +x)
docker-compose.yml         (new)
README.md                  (add "Docker" + "install.sh" sections)
.gitignore                 (add publish/, data/)
```

## 4. Requirements

### RF-001: Multi-stage Dockerfile
- **Description:** Stage `build` uses `mcr.microsoft.com/dotnet/sdk:10.0`, restores via the `.slnx` and runs `dotnet publish src/KnowledgeHub.Server -c Release -r linux-musl-x64 -o /app/publish`. Stage `runtime` uses `mcr.microsoft.com/dotnet/runtime-deps:10.0-alpine`, creates a non-root user (UID 1000, `app`), copies `/app/publish` to `/app`, sets `WORKDIR /app`, `EXPOSE 8080`, `ENV ASPNETCORE_URLS=http://+:8080`, `ENV KnowledgeHub__DatabasePath=/data/knowledgehub.db`, declares `VOLUME /data`, and `ENTRYPOINT ["./KnowledgeHub"]`.
- **Input → Output:** `docker build -t knowledgehub .` → runnable image where a container serves the SPA + API + MCP on `:8080`.

### RF-002: Healthcheck and globalization
- **Description:** `HEALTHCHECK` uses busybox `wget` against `http://localhost:8080/` (SPA fallback returns 200). `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is NOT forced — `icu-libs` are installed if absent so culture-dependent paths behave like the host build.
- **Input → Output:** `docker ps` → container reports `healthy` within ~30 s of start.

### RF-003: .dockerignore
- **Description:** Excludes `.git/`, `**/bin/`, `**/obj/`, `publish/`, `data/`, `.specs/`, `docs/`, `*.md` (except none needed in image), `.claude/`, `skills-lock.json`.
- **Input → Output:** `docker build` context stays small; `bin/obj` artifacts never leak into the build stage.

### RF-004: install.sh — common stage
- **Description:** Bash script at repo root, `chmod +x`, `set -euo pipefail`, `--help` usage text. Flags: `--docker` (default when `docker` is on PATH, else `--host`), `--host`, `--port <p>` (default `5000`), `--skip-tests`, `--systemd` (host mode only), `--prefix <dir>` (host mode, default `/opt/knowledgehub`), `--data-dir <dir>` (default `./data`). Common stage runs `dotnet build KnowledgeHub.slnx -c Release` and `dotnet test -c Release` unless `--skip-tests`.

### RF-005: install.sh — docker mode
- **Description:** Builds `knowledgehub:latest`, `mkdir -p "$DATA_DIR"`, stops/removes an existing `knowledgehub` container, then `docker run -d --name knowledgehub -p ${PORT}:8080 -v ${DATA_DIR_ABS}:/data --restart unless-stopped knowledgehub:latest`. Prints the URL and `docker logs -f knowledgehub` hint.

### RF-006: install.sh — host mode
- **Description:** Detects RID (`linux-x64` default; `linux-arm64`/`osx-arm64` via `uname -m`/`uname -s`), runs `dotnet publish src/KnowledgeHub.Server -c Release -r $RID -o publish/$RID`, installs the `KnowledgeHub` binary to `$PREFIX` (via `sudo` when needed), creates `$DATA_DIR`, and with `--systemd` writes/enables a `knowledgehub.service` unit (`ExecStart=$PREFIX/KnowledgeHub`, `Environment=KnowledgeHub__DatabasePath=$DATA_DIR/knowledgehub.db`, `Restart=on-failure`).

### RF-007: docker-compose.yml
- **Description:** Single service `knowledgehub`: `build: .`, `image: knowledgehub:latest`, `ports: ["${KNOWLEDGEHUB_PORT:-5000}:8080"]`, `volumes: ["./data:/data"]`, `restart: unless-stopped`, env passthrough for `Embeddings__*`, `VectorStore__*`, `DeepWiki__*`.
- **Input → Output:** `docker compose up -d` → same behavior as `install.sh --docker`.

### RF-008: README + gitignore
- **Description:** README gains a "Deploy" section showing `./install.sh`, `./install.sh --docker --port 8080`, `./install.sh --host --systemd` and `docker compose up -d`. `.gitignore` ignores `publish/` and `data/`.

## 5. API Contract

N/A — infra/delivery feature. Runtime contract is unchanged: SPA at `/`, REST at `/api/*`, MCP at `/mcp` and `/mcp/sse`.

## 6. Acceptance Criteria

- [ ] **Given** a clean clone on a machine with Docker **when** `./install.sh` runs **then** `http://localhost:5000` serves the SPA and `docker ps` shows `knowledgehub` healthy.
- [ ] **Given** the container is running **when** it is stopped and recreated **then** `data/knowledgehub.db` on the host still holds prior sources/documents.
- [ ] **Given** a Linux machine without Docker but with .NET SDK 10 **when** `./install.sh --host` runs **then** the binary is installed under the prefix and `http://localhost:5000` responds.
- [ ] **Given** `./install.sh --host --systemd` on a systemd distro **when** the script finishes **then** `systemctl status knowledgehub` is `active (running)` and survives `systemctl restart`.
- [ ] **Given** `docker compose up -d` **when** the stack is up **then** `curl -sf http://localhost:5000/api/sources` returns `200`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Docker absent | `./install.sh` | auto-falls back to `--host` with a printed notice |
| Port in use | `--port 5000` already bound | clear error before `docker run`/`dotnet` start |
| `./data` owned by other UID | host bind mount | script warns and suggests `sudo chown -R 1000:1000 ./data` |
| Existing container | re-run `--docker` | old `knowledgehub` container replaced, data preserved |
| Missing SDK | `--host` without dotnet | fail fast: "requires .NET SDK 10 (see global.json)" |

## 7. Task Plan

- [ ] **T1 — Dockerfile + .dockerignore:** multi-stage musl publish → alpine runtime-deps, non-root, healthcheck. Validate: `docker build -t knowledgehub .` and `docker run --rm -p 5000:8080 knowledgehub` + `curl localhost:5000`.
- [ ] **T2 — install.sh:** arg parsing, common build/test stage, `--docker` and `--host` modes, `--systemd`. Validate: `shellcheck install.sh`, `./install.sh --help`, `--docker` run, `--host` run.
- [ ] **T3 — docker-compose.yml:** service mirroring RF-007. Validate: `docker compose config` + `docker compose up -d` + curl.
- [ ] **T4 — Docs + gitignore + done:** README Deploy section, `.gitignore` entries, fill DoD, `Status = Done`, PR.

## 8. Organization Guardrails

- **Branches:** work on `feature/Devin-20260913-install-deploy` from `main`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** no secrets in the image, script or compose file; all config via env vars; container runs non-root; `./data` never committed.
- **Scope:** no changes to C# sources; delivery layer only.

## 9. Definition of Done

- [ ] `docker build` produces a working image; container healthy and serving SPA/API/MCP.
- [ ] `./install.sh --docker`, `--host`, and `docker compose up -d` all verified end-to-end.
- [ ] `shellcheck install.sh` clean; `dotnet build` + `dotnet test` green.
- [ ] README updated; `publish/` and `data/` gitignored.
- [ ] Acceptance criteria checked off; `Status = Done`.

## Open Questions / Pending Ambiguity

- None blocking. Optional follow-ups (out of scope here): GHCR push, multi-arch (`linux/arm64`) image, chiseled base variant.
