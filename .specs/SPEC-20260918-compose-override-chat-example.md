# SPEC-20260918-compose-override-chat-example

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `compose-override-chat-example` |
| Type | `Docs` |
| Stack | `Docker Compose / YAML` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-override-chat-example` |
| Ticket | `GAP-documentation-compose-override-chat` |
| Status | `Done` |

## 1. User Story

**As a** user deploying KnowledgeHub on a fresh host
**I want** the reference `docker-compose.override.yml.example` to show the Chat provider wiring
**So that** setting `CHAT__*` in `.env` actually reaches the container and the chat features work.

**Problem context:**
The base `docker-compose.yml` does not map any `Chat__*` variables — the
chat provider is wired only via the gitignored `docker-compose.override.yml`
on the production host (`Chat__Provider/Endpoint/Model/ApiKey`). The
checked-in reference file `docker-compose.override.yml.example` documents
only the vault mount; a user copying it gets a deploy where `.env`'s
`CHAT__*` values (documented in `.env.example:32-35`) never reach the
container, because compose only injects explicitly-mapped env vars. The
chat path silently stays `none`/unset.

Evidence: `docker-compose.yml:19-50` (no `Chat__*` mapping),
`docker-compose.override.yml:8-13` (live Chat block),
`docker-compose.override.yml.example` (volumes only),
`.env.example:32-35` (documents CHAT__* with nowhere to land),
`docker inspect knowledgehub` → `Chat__Provider=openai` etc. from override.

## 2. Scope

**In scope:**
- Add a commented `Chat__*` environment block to `docker-compose.override.yml.example` mirroring the live production wiring pattern.

**Out of scope:**
- Moving `Chat__*` into the base `docker-compose.yml` (the override is the documented host layer for this).
- Changing `.env.example` (already documents the vars).
- Any other override documentation (WebDAV vault section already present).

## 3. Technical Context

**Where the change happens:**
`docker-compose.override.yml.example` — the committed reference users copy
to `docker-compose.override.yml`.

**Files to read before implementing:**
- `docker-compose.override.yml.example`
- `docker-compose.override.yml` (live pattern — comment style, `${VAR:-default}` form)
- `docker-compose.yml`, `.env.example`
- `.specs/SPEC-20260916-compose-vault-mount.md`, `.specs/SPEC-20260916-settings-chat-config.md`

**Files to create or modify:**
```text
docker-compose.override.yml.example
```

## 4. Requirements

### RF-001: Document Chat provider wiring in the example
- **Description:** Add an `environment:` block to the example service mapping `Chat__Provider`, `Chat__Endpoint`, `Chat__Model`, `Chat__ApiKey` from `${CHAT__*:-…}` with the same defaults the live override uses (`none` for provider, empty for the rest), plus a comment explaining that `.env` alone does not inject vars — the mapping is required.
- **Rules:** no real secrets or host-specific values; keep the file copy-paste runnable.
- **Input → Output:** `cp docker-compose.override.yml.example docker-compose.override.yml` + `CHAT__*` in `.env` → `docker inspect` shows `Chat__*` in `Config.Env`.

**Business rules / invariants:**
- The example must stay valid YAML for `docker compose config` even without a `.env`.

## 5. API Contract (if applicable)

N/A — deployment documentation file.

## 6. Acceptance Criteria

- [ ] **Given** a fresh copy of the example **when** `docker compose config` runs **then** it parses and shows the Chat env mappings with defaults.
- [ ] **Given** `.env` with `CHAT__PROVIDER=ollama` **when** the example is used as the override **then** the rendered config injects `Chat__Provider=ollama`.
- [ ] **Given** no `CHAT__*` in `.env` **when** the example is used **then** provider defaults to `none` and the container starts.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| `.env` missing | no file | compose defaults apply; example still parses |

## 7. Task Plan (agent execution)

- [ ] **T1 — Discovery:** read the three compose/env files; mirror the live override's Chat block style.
- [ ] **T2 — Implementation:** add the commented environment block to the example.
- [ ] **T3 — Verification:** `docker compose -f docker-compose.yml -f <(rendered example) config` or copy-and-config smoke to prove it parses and injects.
- [ ] **T4 — Validation:** docs diff review.
- [ ] **T5 — Done + PR:** DoD complete → `Status = Done` → PR open.

**7.1 Validation strategy by type/stack**

Docs/Infra: `docker compose config` render evidence — parses cleanly and
shows the Chat mappings resolved from `.env`.

## 8. Organization Guardrails (mandatory when provided)

- **Branches:** never commit to `main`. Use `feature/Devin-20260918-override-chat-example`.
- **Security:** no real endpoints/keys in the example — placeholders and defaults only.
- **Scope:** example file only; base compose and `.env.example` unchanged.

## 9. Definition of Done

- [ ] Example file documents Chat wiring; `docker compose config` renders it.
- [ ] No secrets or host-specific values committed.
- [ ] Guardrails respected.

**Next action after DoD is complete:** set `Status = Done` in section 0 and open the PR on branch `feature/Devin-20260918-override-chat-example` referencing `GAP-documentation-compose-override-chat`.

## Open Questions / Pending Ambiguity

- None.
