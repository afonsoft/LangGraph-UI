# SPEC-20260914-locked-restore

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `locked-restore` |
| Type | `Infra` |
| Stack | `.NET 10`, `NuGet` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-persistence-hardening` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |

## 1. User Story

**As a** maintainer
**I want** NuGet lock files committed and enforced
**So that** restores are bit-for-bit reproducible across dev machines, CI and the Docker build — the .NET analog of `pip freeze` pinning in the article.

**Problem context:** csproj pins versions but transitive packages float. `packages.lock.json` makes the resolved graph (including transitives + content hashes) deterministic and auditable in PR diffs.

## 2. Scope

**In scope:**
- `Directory.Build.props` with `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` and `RestoreLockedMode` in CI-ish contexts (`--locked-mode` used explicitly in Dockerfile/verify commands rather than a global locked mode that annoys local dev).
- Generated `packages.lock.json` per project (5 csproj: 4 src + 2 tests = 6 files).
- Dockerfile restore step uses `--locked-mode`.
- README dev note.

**Out of scope:**
- Central Package Management (`Directory.Packages.props`) — bigger refactor, separate spec.
- NuGet audit (`NuGetAudit`) — separate concern.

## 3. Technical Context

**Where the change happens:** repo root (`Directory.Build.props`, `packages.lock.json` × 6), `Dockerfile`, `README.md`.

**Files to read:** all `*.csproj`, `Dockerfile`, `global.json`.

**Files to create or modify:**
```text
Directory.Build.props        (new)
**/packages.lock.json        (new — generated)
Dockerfile                   (--locked-mode)
README.md                    (note)
```

## 4. Requirements

### RF-001: Lock files generated
- **Description:** `dotnet restore` produces `packages.lock.json` beside each csproj recording resolved versions + SHA512 content hashes for direct and transitive packages.

### RF-002: Locked-mode enforcement points
- **Description:** `install.sh` build gate supports `LOCKED_RESTORE=1` → `dotnet restore --locked-mode`. Dockerfile restores unlocked: `packages.lock.json` captures SDK-workload implicit refs (`Microsoft.NET.Sdk.WebAssembly.Pack`, `ILLink.Tasks`, `AspNetCore.App.Internal.Assets`) whose versions track the SDK band, and the MCR `sdk:10.0` band differs from the distro SDK that generated the locks — locked mode in the image would fail on version drift unrelated to declared refs. Enforcement point = the pinned-SDK environment (dev machine per `global.json`, CI).

### RF-003: Lock file maintenance documented
- **Description:** README: after changing any `PackageReference`, run `dotnet restore` and commit the updated `packages.lock.json` files.

## 5. API Contract

N/A — build infra.

## 6. Acceptance Criteria

- [ ] **Given** `packages.lock.json` committed **when** `dotnet restore --locked-mode` runs on the same SDK band **then** it succeeds without modifying lock files.
- [ ] **Given** a csproj gains a package reference without regenerating locks **when** `LOCKED_RESTORE=1 ./install.sh` or `dotnet restore --locked-mode` runs **then** restore fails with NU1004.
- [ ] **Given** normal dev flow **when** `dotnet build`/`dotnet test`/`docker build` run **then** they work unchanged.

## 7. Task Plan

- [ ] **T1 — Enable + generate:** `Directory.Build.props`, `dotnet restore`, commit 6 lock files.
- [ ] **T2 — Enforce in Dockerfile:** `--locked-mode` on the restore layer.
- [ ] **T3 — Docs + verify:** README note; `dotnet restore --locked-mode`, build, test, `docker build` all green.

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260914-persistence-hardening`.
- **Deps:** none added — only locking existing graph.
- **Workflows:** `.github/workflows/` untouched.

## 9. Definition of Done

- [ ] Lock files committed for all 6 projects.
- [ ] `dotnet restore --locked-mode` and `docker build` green.
- [ ] README documents the update workflow.

## Open Questions / Pending Ambiguity

- None.
