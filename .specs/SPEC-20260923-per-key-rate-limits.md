# SPEC-20260923-per-key-rate-limits — Custom rate limits per API key

| Field | Value |
|-------|-------|
| Date | `2026-09-23` |
| Author | `Devin` |
| Type | `Feature` (medium — deferral from SPEC-20260923-rate-limiting) |
| Stack | `.NET 10` `System.Threading.RateLimiting` + EF Core + Blazor WASM |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-per-key-rate-limits` |
| Status | `Done` — merged via PR |
| Origin | `SPEC-20260923-rate-limiting` §Open-questions — "per-key custom limits (admin-set per API key) — deferred; `RateLimiting:*` global only this round". |

## 1. User Story

**As an** admin
**I want** to set custom LLM/sync rate limits on an individual API key
**so that** a heavy integration can be throttled (or granted headroom)
without changing global `RateLimiting:*` limits for everyone.

## 2. Context

`SPEC-20260923-rate-limiting` delivered global policies (`llm`, `sync`,
`general`) partitioned by `CallerPartitioner` — `key:{apiKeyId}` already
gets its own bucket. What's missing is a per-key *override* of the
permit/window numbers: today every `key:` partition uses the global
`LlmPermitLimit`/`SyncPermitLimit` and windows.

Both enforcement points must honor the override:

- `Program.cs:81 RateLimiting()` — ASP.NET policy partitions (`llm`, `sync`).
- `McpToolRateLimiter` — in-dispatch limiter for MCP `tools/call`
  (`ask_knowledge`, `agent_chat`, `search_knowledge`, `write_*`).

## 3. Functional Requirements

- **RF-001** `ApiKey` gains 4 nullable columns: `LlmRateLimitPermits`,
  `LlmRateLimitWindowSeconds`, `SyncRateLimitPermits`,
  `SyncRateLimitWindowSeconds`. NULL = inherit global. Migration
  `AddApiKeyRateLimitOverrides`.
- **RF-002** `IApiKeyRateLimitResolver` singleton: sync
  `TryGetOverride(Guid keyId, out ApiKeyRateLimitOverride)` reading a cached
  map of keys that have any override; lazy load + `Invalidate()` on admin
  mutation. (Sync API — rate-limit callbacks cannot await.)
- **RF-003** `McpToolRateLimiter` + `Program.cs RateLimiting()` partition
  callbacks: when the partition key is `key:{guid}` and an override exists,
  use its permits/window for the matching policy family (llm vs sync);
  partial overrides mix per-field with global values.
- **RF-004** Admin surface: `PUT /api/api-keys/{id}/rate-limit` body
  `{llmPermits,llmWindowSeconds,syncPermits,syncWindowSeconds}` (all
  nullable ints; validation 1..100000 / 1..86400); `DELETE` clears to NULL.
  `ApiKeyDto`/admin GET exposes the four values. `ApiKeys.razor` edit
  dialog gains a "Rate limit (opcional)" fieldset.
- **RF-005** Override changes take effect for new partitions immediately;
  the resolver invalidates on save/clear. Existing in-flight windows are
  not reset (acceptable — sliding window converges).

## 4. Acceptance Criteria

- [ ] Setting `llmPermits=1` on a key → 2nd `ask_knowledge`/REST `llm` call
  with that key returns rate-limited while other keys are unaffected.
- [ ] Clearing the override → global limits apply again.
- [ ] Partial override (only `llmPermits`) mixes with global window.
- [ ] Validation rejects 0/negative permits.
- [ ] Build 0 warnings; unit + integration green; format clean.
