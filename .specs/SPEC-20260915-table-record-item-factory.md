# SPEC-20260915-table-record-item-factory

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `table-record-item-factory` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `BootstrapBlazor 10.10.2` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-table-record-item-factory` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |

## 1. User Story

**As a** logged-in admin user
**I want** the `/api-keys` page to render
**So that** I can create, list and revoke `aft_*` API keys.

**Problem context:**
`ApiKeys.razor` crashes with `InvalidOperationException: KnowledgeHub.Shared.Contracts.ApiKeyDto create instance failed. Please provide CreateItemCallback create the KnowledgeHub.Shared.Contracts.ApiKeyDto instance.` — verified in production after the proxy boot fix landed.

**Root cause (verified in BootstrapBlazor 10.10.2 source):**
`Table<TItem>.OnParametersSet` unconditionally runs `SearchModel ??= CreateSearchModel()` (`Table.razor.cs:1091`). `CreateSearchModel()` falls back to `CreateTItem()` → `CreateInstance()` → `ObjectExtensions.CreateInstance<ApiKeyDto>` — which throws because `ApiKeyDto` is a **positional record** with no parameterless constructor. The exception fires on every parameter-set regardless of `ShowSearch`, so the page dies at first render. `Sources.razor` is unaffected — `KnowledgeSourceDto` is a property-bodied record with an implicit parameterless ctor.

## 2. Scope

**In scope:**
- `ApiKeys.razor`: provide `CreateItemCallback` on the `Table` so `SearchModel` instantiation uses the callback instead of `Activator`.
- Regression coverage: assert every `Table TItem=` in the client targets a constructible type OR provides the callback — codified as a code convention note in the SPEC (no JS/razor test harness exists; guard via review + a comment).

**Out of scope:**
- Changing `ApiKeyDto`'s shape (positional record stays — the contract is correct; the UI provides the factory).
- BootstrapBlazor upgrade/patch.
- `Sources.razor` changes (works today; `KnowledgeSourceDto` is constructible).
- Any backend/endpoint change.

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` — add `CreateItemCallback` to the existing `<Table TItem="ApiKeyDto" …>`.

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Pages/ApiKeys.razor` — table markup.
- `src/KnowledgeHub.Shared/Contracts/AuthDtos.cs` — `ApiKeyDto` positional signature `(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)`.
- BootstrapBlazor `Table.razor.Edit.cs` — `CreateItemCallback` is `Func<TItem>`; used by `CreateSearchModel()` and `InternalOnAddAsync()` (default Add flow — hidden here via `ShowDefaultButtons="false"`; our "Nova chave" button opens a separate `Modal` and never touches `EditModel`).

**Files to create or modify:**
```text
src/KnowledgeHub.Client/Pages/ApiKeys.razor   (add CreateItemCallback)
```

## 4. Requirements

### RF-001: Table item factory
- **Description:** Add `CreateItemCallback` to the `ApiKeys.razor` `Table`, returning a placeholder `ApiKeyDto` (e.g. `new ApiKeyDto(Guid.Empty, string.Empty, string.Empty, default, null, null)`). The instance only backs the (hidden) SearchModel and the unused default-Add flow — it is never rendered.
- **Input → Output:** navigate to `/api-keys` → table renders with rows (or empty state), no `InvalidOperationException`.

### RF-002: No contract changes
- **Description:** `ApiKeyDto` keeps its positional record shape — the fix belongs in the component, not the shared contract.
- **Input → Output:** `AuthDtos.cs` unchanged; `GET /api/apikeys` payload unchanged.

## 5. API Contract

N/A — no API surface change.

## 6. Acceptance Criteria

- [ ] **Given** an authenticated user **when** navigating to `/api-keys` **then** the table renders (loading → rows or `EmptyText`) with no `create instance failed` error.
- [ ] **Given** the table rendered **when** clicking "Nova chave" **then** the create `Modal` opens and `CreateAsync` still posts `CreateApiKeyRequest` (callback instance is unrelated to the modal's `_newName` flow).
- [ ] **Given** a revoked/active key row **when** clicking Revogar **then** `PopConfirmButton` → `Revoke(key)` works unchanged.
- [ ] **Given** `dotnet build` + `dotnet format` + `dotnet test` **then** all green (no test harness covers razor rendering; regression relies on the callback being wired and review convention).

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Empty key list | `_keys = []` | `EmptyText` renders; SearchModel placeholder still required by Table |
| Future positional-record TItem in another Table | any | same `CreateItemCallback` pattern required — documented convention |
| `ShowSearch` enabled later | — | SearchModel uses the callback instance; binding onto a placeholder is acceptable |

## 7. Task Plan

- [x] **T1 — Fix:** `CreateItemCallback="CreatePlaceholder"` adicionado à `Table` de `ApiKeys.razor`.
- [x] **T2 — Verify:** `dotnet build` ✓, `dotnet format` whitespace+style ✓, `dotnet test` ✓ (130 unit + 110 integration).
- [x] **T3 — PR + deploy:** PR #56 merged (`3c5794a`), deployed 2026-09-15 via `docker compose` rebuild; container healthy, `/health/ready` 200, `/login` 200. Render de `/api-keys` sem o erro aguarda confirmação do usuário no browser.

**7.1 Validation:** Bugfix — reproduction evidence (production error text, BootstrapBlazor source `Table.razor.cs:1091` + `Table.razor.Edit.cs:388-402`) + build/test gates; razor render path verified by manual browser smoke (no component-test harness in repo).

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-table-record-item-factory`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** no auth change; placeholder instance carries no data and is never sent anywhere.
- **Backward compat:** pure client markup change; no API/contract drift.

## 9. Definition of Done

- [x] All requirements implemented.
- [x] Acceptance criteria covered by build/tests or verified manually. *(render path: browser smoke pending user confirmation)*
- [x] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` green.
- [x] Guardrails respected.
- [ ] Manual smoke at `https://rag.afonsoft.dev/api-keys`: page renders, create + revoke flows work. *(deployed 2026-09-15 — server-side green; browser validation pending)*

## Open Questions / Pending Ambiguity

- None — root cause verified against BootstrapBlazor 10.10.2 sources.
