# SPEC-20260923-source-authorization

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `source-authorization` |
| Type | `Feature` (security / multi-tenant read scoping) |
| Stack | `.NET 10 / C# 14` + EF Core + existing API-key auth |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-source-authorization` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |
| Origin | `gap-analysis-20260923` — GAP-security-source-authorization (média). Proposal §14 ("autorização por ferramenta, fonte e documento"; "propagação das permissões da origem"). |

## 1. User Story

**As a** platform administrator
**I want** each API key scoped to an allowlist of knowledge sources (and an optional tool allowlist)
**So that** a key issued to an external agent can only read the sources it was granted — a leaked key no longer exposes the whole knowledge base.

**Problem context:**
`ApiKey` (`Domain/Entities/ApiKey.cs`) has no source scoping: every valid key searches every active source (`SearchService.cs:55-57` resolves *all* active sources when `sourceId` is null). The proposal explicitly requires authorization by tool, source and document, and propagation of source permissions.

## 2. Scope

**In scope:**
- `ApiKey.AllowedSourceIds` (JSON column or join table `ApiKeySources`) + `AllowedTools` (JSON array, null = all).
- Enforcement in `SearchService` (filter `activeSourceIds` by key scope), `AskEndpoints`/`AgentService` (same filter propagates), `DynamicToolCatalog`/`SourceQueryToolsProvider` (per-source `query_*` tools hidden for unscoped sources), and `tools/call` (calling a hidden tool → friendly `isError`).
- Admin endpoints: `PUT /api/api-keys/{id}/scopes` (set sources+tools), surfaced in `GET /api/api-keys` response.
- Default = unrestricted (existing keys keep working — opt-in tightening).
- Cookie sessions (human admins) unaffected — scope applies to `apikey` principals only.

**Out of scope:**
- Per-document ACL inside a source (source-level granularity only this round).
- Per-user source scoping for cookie users.
- Write-scope distinction (read scope only; `write_knowledge` already gated by HITL + admin UI).

## 3. Technical Context

Caller identity: `ApiKeyAuthenticationHandler` emits `KeyIdClaim`; `CallerIdentity.TryGetApiKeyId` exists (per-key secrets work). Search scoping point: `SearchService.ExecuteAsync` (:55-57). Per-source tool generation: `SourceQueryToolsProvider`. Catalog cache: `DynamicToolCatalog` caches the aggregate — scoped visibility means the cache key must include the caller scope fingerprint (or scope-filter at call time after cached aggregate — cheaper: filter the cached aggregate per caller, no cache invalidation needed).

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs`
- `src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs`
- `src/KnowledgeHub.Server/Mcp/CallerIdentity.cs`
- `src/KnowledgeHub.Server/Services/SearchService.cs` (:55-57)
- `src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs`, `ToolProviders/SourceQueryToolsProvider.cs`
- `src/KnowledgeHub.Server/Api/ApiKeySettingsEndpoints.cs` (key admin surface)
- `src/KnowledgeHub.Server/Services/AgentService.cs` (`BuildLoopAsync` visibility filter :352-357)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Domain/Entities/ApiKey.cs          (modify — scope fields or join entity)
src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs(modify — emit scope claims or lookup)
src/KnowledgeHub.Server/Mcp/CallerIdentity.cs              (modify — expose AllowedSourceIds/AllowedTools)
src/KnowledgeHub.Server/Services/SearchService.cs          (modify — scope filter on activeSourceIds)
src/KnowledgeHub.Server/Services/AgentService.cs           (modify — tool visibility ∩ AllowedTools)
src/KnowledgeHub.Server/Mcp/DynamicToolCatalog.cs          (modify — per-caller filtering of cached aggregate)
src/KnowledgeHub.Server/Api/ApiKeySettingsEndpoints.cs     (modify — scopes admin endpoint)
src/KnowledgeHub.Server/Migrations/*                       (new migration)
tests/KnowledgeHub.Tests.Integration/*                     (new — scoped key sees subset)
```

## 4. Requirements

### RF-001 — Scope storage
- **Description:** `ApiKey` gains `AllowedSourceIdsJson` (JSON array of GUIDs, null = all) and `AllowedToolsJson` (JSON array of tool names, null = all); admin endpoints read/write them.
- **Rules:** EF migration additive; JSON columns keep the entity flat (consistent with existing `ChatSettings` JSON usage if applicable — confirm at implementation).

### RF-002 — Caller scope resolution
- **Description:** `CallerIdentity` exposes `IReadOnlySet<Guid>? AllowedSourceIds` and `IReadOnlySet<string>? AllowedTools` resolved once per request from the key record (cacheable in `IMemoryCache` 60s keyed by key-id + revoked/version stamp — reuse the secrets-cache pattern).
- **Rules:** cookie principals → `null` (unrestricted); revoked/missing key → request already rejected upstream.

### RF-003 — Search enforcement
- **Description:** `SearchService` intersects `activeSourceIds` with `AllowedSourceIds` when non-null; explicit `source`/`sourceId` outside scope → empty result (not an error — mirrors "no active source").
- **Rules:** intersection happens before vector/lexical calls (never retrieve-then-filter).

### RF-004 — Tool surface enforcement
- **Description:** `DynamicToolCatalog` filters per-source `query_*` tools and any tool not in `AllowedTools` **per caller** (post-cache filtering — aggregate cache unchanged); `tools/call` on a filtered-out tool → `isError` "tool not available"; `agent_chat` visible-set intersects `AllowedTools` too.
- **Rules:** `write_knowledge`/`set_*` tools unaffected unless explicitly restricted via `AllowedTools`.

### RF-005 — Audit
- **Description:** Scope checks that deny access emit `ApiKeyUsageEvent`/`security_events` rows (key id, denied scope kind, tool/source requested — never content).

**Business rules / invariants:**
- Null scope = unrestricted (backward compat — no migration of existing keys needed).
- Deny-by-scope never throws; it filters (search) or friendly-errors (tools/call).

## 5. API Contract

**Endpoint:** `PUT /api/api-keys/{id}/scopes` — **Auth:** `CookieSession` (keys cannot scope keys, same rule as key management)

**Request:**
```json
{ "allowedSourceIds": ["<guid>", "…"], "allowedTools": ["search_knowledge", "ask_knowledge"] }
```
**Response:** `204` or `400` (unknown source id / tool name). `GET /api/api-keys` includes scopes per key.

**Expected errors:** `400` invalid ids · `401` · `403` non-cookie caller.

## 6. Acceptance Criteria

- [ ] **Given** a key scoped to source A **when** it calls `search_knowledge` (no source arg) **then** only A's chunks return.
- [ ] **Given** a scoped key **when** it passes `source=B` explicitly **then** empty results (no error, no leak).
- [ ] **Given** `AllowedTools=[search_knowledge]` **when** calling `ask_knowledge` **then** friendly `isError`.
- [ ] **Given** a key with null scopes (existing) **when** used **then** behavior identical to today.
- [ ] **Given** catalog cached **when** two keys with different scopes list tools **then** each sees its own filtered set without cache misses per key.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Scope references deleted source | stale id | silently excluded from intersection |
| Empty array vs null | `[]` vs `null` | `[]` = deny all sources; `null` = all — distinct semantics, documented |
| Scope change mid-request | PUT during a call | next request sees it (60s cache max staleness) |
| Cookie user | admin session | unrestricted regardless |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; confirm claims + catalog caching details.
- [ ] **T2 — Storage + migration + admin endpoint.**
- [ ] **T3 — CallerIdentity scope resolution (+60s cache).**
- [ ] **T4 — Enforcement:** SearchService, catalog per-caller filter, agent visibility, tools/call guard.
- [ ] **T5 — Audit events + integration tests (scoped vs unscoped keys).**
- [ ] **T6 — Verification:** build/test/format green. Done + PR.

## 8. Organization Guardrails

- Scope admin is `CookieSession`-only — API keys cannot escalate themselves.
- Deny paths never reveal whether a source exists (empty result, generic message).
- No raw key material in scope logs/events.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Backward-compat proven (null-scope key = today's behavior).
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- `[]` vs `null` semantics (deny-all vs unrestricted) — default proposal above; confirm at implementation.
