# SPEC-20260918-release-publish-singlefile

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `release-publish-singlefile` |
| Type | `Bugfix` |
| Stack | `.NET 10 / GitHub Actions` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-release-publish-singlefile` |
| Ticket | `GAP-automation-release-publish-broken` |
| Status | `Done` |

## 1. User Story

**As a** maintainer cutting a release
**I want** the `Release & Publish` workflow to produce the single-file binaries, push the ghcr image, and attach assets to the GitHub Release
**So that** a tagged version actually ships installable artifacts instead of an empty release.

**Problem context:**
The workflow has never succeeded (7/7 runs failed). On tag `v0.0.2` the
`Publish Single-File (linux-x64)` step died with `NETSDK1098` because the
command line passes `-p:PublishSingleFile=true` and
`-p:IncludeNativeLibrariesForSelfExtract=true` as **global** MSBuild
properties — they propagate to the referenced `KnowledgeHub.Client` (Blazor
WASM) project, which cannot produce an application host. The Server csproj
already sets both properties at project level, so the command-line flags are
redundant and harmful. Result: releases `v0.0.2` and `v.0.0.1` exist on
GitHub with **zero assets**, no ghcr image was published, and a malformed
tag `v.0.0.1` (extra dot, rejected by the version regex) still exists.

Evidence: run `35415667891` (`--log-failed`, `Microsoft.NET.Publish.targets(206,5)`),
`.github/workflows/release.yml:63-83`,
`src/KnowledgeHub.Server/KnowledgeHub.Server.csproj` (PropertyGroup with
`PublishSingleFile`), `gh release list` → both releases `assets: []`.

## 2. Scope

**In scope:**
- Fix the publish steps in `release.yml` so single-file publish succeeds for `linux-x64` and `win-x64`.
- Re-dispatch/repair the `v0.0.2` release so it gets real assets + ghcr image.
- Delete the malformed tag `v.0.0.1` (and its empty release).

**Out of scope:**
- Changing the CI pipeline (`ci-build-test.yml`) — it is green.
- New release artifacts/targets (arm64, musl, installers).
- Versioning policy / semver automation.

## 3. Technical Context

**Where the change happens:**
`.github/workflows/release.yml` publish steps + a one-time manual repair of
the `v0.0.2` release. `KnowledgeHub.Server.csproj` already owns
`PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`
at project level — command-line `-p:` globals are what break the WASM
reference build.

**Files to read before implementing:**
- `.github/workflows/release.yml`
- `src/KnowledgeHub.Server/KnowledgeHub.Server.csproj`
- `install.sh` (host_deploy publish invocation — the working reference)
- `.specs/SPEC-20260913-standalone-packaging.md`, `.specs/SPEC-20260914-github-actions-ci.md`

**Files to create or modify:**
```text
.github/workflows/release.yml
```

## 4. Requirements

### RF-001: Remove redundant global publish properties
- **Description:** The publish steps must not pass `-p:PublishSingleFile` / `-p:IncludeNativeLibrariesForSelfExtract` (nor `-p:SelfContained` if it propagates harmfully) as command-line globals; rely on the Server csproj PropertyGroup, keeping only `--runtime`, `--self-contained`, `-p:Version` and `--output`.
- **Rules:** single-file behavior must remain identical to `install.sh --host` output (apphost binary + bundled DLLs); the Blazor WASM Client project must build without NETSDK1098.
- **Input → Output:** tag push `v*.*.*` → green `build-release` job producing `publish/{linux,win}-x64/KnowledgeHub` binaries.

### RF-002: Repair the v0.0.2 release
- **Description:** After the workflow fix merges, re-dispatch the release for `v0.0.2` (via `workflow_dispatch` with version `0.0.2` or tag re-push) so the GitHub Release gains linux-x64 + win-x64 assets and the ghcr image `ghcr.io/afonsoft/langgraph-ui:0.0.2` is published.
- **Rules:** do not re-tag if the release can be repaired by dispatch; verify assets land on the existing `v0.0.2` release entry.
- **Input → Output:** fixed workflow + `v0.0.2` → release with ≥2 binary assets + ghcr tag present.

### RF-003: Remove malformed tag/release v.0.0.1
- **Description:** Delete remote tag `v.0.0.1`, its local ref, and the empty GitHub release named `v.0.0.1`.
- **Rules:** destructive op — requires explicit confirmation at execution time; verify the tag points to an ancestor of `main` before deleting.
- **Input → Output:** `git ls-remote --tags` → no `v.0.0.1`; `gh release list` → no `v.0.0.1`.

**Business rules / invariants:**
- `.github/workflows/` changes go through PR like any other protected path (prior precedent: PR #23 `e563ac9`).
- No secrets in workflow changes; `GITHUB_TOKEN` permissions stay minimal (`packages: write`, `contents: write` only where needed — already the case).

## 5. API Contract (if applicable)

N/A — CI/CD fix, no application API surface.

## 6. Acceptance Criteria

- [ ] **Given** the fixed workflow on `main` **when** a test tag or `workflow_dispatch` runs **then** `build-release` completes green for both RIDs without NETSDK1098.
- [ ] **Given** release `v0.0.2` **when** the repair dispatch finishes **then** `gh release view v0.0.2 --json assets` lists linux-x64 and win-x64 binaries and `docker manifest inspect ghcr.io/afonsoft/langgraph-ui:0.0.2` succeeds.
- [ ] **Given** the malformed tag **when** cleanup runs **then** `v.0.0.1` is absent from remote tags and releases.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Tag `v.0.0.1` (leading dot) | push | validate-tag job fails fast with clear error (already does — regex guard) |
| Re-dispatch on existing release | `workflow_dispatch` `0.0.2` | `action-gh-release` updates the existing release, appends assets (no duplicate) |
| Missing `packages: write` | ghcr push | job fails with auth error — confirm permission block intact |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read files in section 3; confirm csproj PropertyGroup covers everything the command line passed.
- [ ] **T2 — Implementation:** edit `release.yml` publish steps per RF-001.
- [ ] **T3 — Verification:** `dotnet publish src/KnowledgeHub.Server -c Release -r linux-x64 --self-contained -o /tmp/publish-check` locally to prove the command (minus removed props) succeeds and emits a single-file binary.
- [ ] **T4 — Validation:** merge via PR; repair `v0.0.2` per RF-002; cleanup per RF-003; fill DoD.
- [ ] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

Bugfix/Infra: reproduction = failing workflow run; fix validated by a green
release run and populated release assets. Local `dotnet publish` smoke proves
the command shape before touching CI.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`, `master` or `develop`. Use `feature/Devin-20260918-release-publish-singlefile`.
- **Workflows:** `.github/workflows/` changes only via PR (protected path; precedent PR #23).
- **Security:** no secrets in commit; keep `GITHUB_TOKEN` permission blocks as-is.
- **Scope:** fix only the release failure + stated repairs; do not redesign the release pipeline.

## 9. Definition of Done

- [ ] All requirements (section 4) implemented.
- [ ] Green `Release & Publish` run (dispatch or tag) with artifacts uploaded.
- [ ] `v0.0.2` release lists binary assets; ghcr image `0.0.2` inspectable.
- [ ] `v.0.0.1` tag + release removed.
- [ ] Edge cases handled; guardrails respected; no secrets committed.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-release-publish-singlefile` referencing `GAP-automation-release-publish-broken`.

## Open Questions / Pending Ambiguity

- Whether `workflow_dispatch` can reuse the existing `v0.0.2` release entry (expected: yes via `action-gh-release`) — resolve at execution.
