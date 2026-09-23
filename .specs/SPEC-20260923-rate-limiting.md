# SPEC-20260923-rate-limiting

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `rate-limiting` |
| Type | `Feature` (security / cost control) |
| Stack | `.NET 10 / C# 14` + `Microsoft.AspNetCore.RateLimiting` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-rate-limiting` |
| Ticket | `[A DEFINIR]` |
| Status | `In implementation` |
| Origin | `gap-analysis-20260923` — GAP-security-rate-limiting (média). Proposal §14 (limites de tamanho, profundidade e quantidade). |

## 1. User Story

**As a** platform operator
**I want** configurable rate limits on LLM-spending endpoints (`ask`, `agent`, `tools/call`, sync triggers) partitioned per caller identity
**So that** a runaway client or leaked API key cannot burn unbounded LLM/embedding quota, and one caller cannot starve others.

**Problem context:**
No rate limiting exists anywhere (verified — zero `RateLimiter`/`EnableRateLimiting` references). `agent_chat` can burn up to `MaxIterations×tool calls` LLM round-trips per request, and `POST /api/sources/{id}/sync` triggers full re-embedding — all unthrottled. The proposal lists rate limits under required governance.

## 2. Scope

**In scope:**
- `RateLimiting` config section + `AddRateLimiter` with partitioned policies:
  - `llm` — `/api/ask`, `/api/agent`, `/api/agent/stream`, MCP `tools/call` on `ask_knowledge`/`agent_chat`: sliding window per caller (default 20 req/min, configurable).
  - `sync` — `POST /api/sources/{id}/sync`, `write_knowledge` upserts: concurrency limit 1 per source (already enforced by `SourceLocks`) + per-caller 10/hour.
  - `general` — remaining `/api/*`: generous fixed window (default 300/min).
- Partition key: API-key id claim → user id → client IP (in that precedence).
- `429` ProblemDetails response with `Retry-After`; `X-RateLimit-*` headers optional.
- MCP `tools/call` returns `isError` + friendly "rate limited, retry in Ns" content (JSON-RPC has no 429).

**Out of scope:**
- Distributed rate limiting across replicas (single-process deployment; Redis-backed limiter is a later option).
- Token-based LLM quotas (request-count only this round).
- UI for configuring limits (config file only).

## 3. Technical Context

ASP.NET middleware applies to Minimal API endpoints via `RequireRateLimiting(policy)` or global `RateLimiter` options with `GlobalLimiter` partitioned by path prefix. MCP `tools/call` bypasses HTTP middleware semantics — enforcement point is the tool dispatch (`KnowledgeHub.McpEngine` dispatcher or per-provider guard in `Mcp/ToolProviders/KnowledgeToolsProvider.cs`). Caller identity: `ApiKeyAuthenticationHandler` claims (`KeyIdClaim`) + cookie `NameIdentifier` + `HttpContext.Connection.RemoteIpAddress`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Program.cs` (middleware order)
- `src/KnowledgeHub.Server/Api/AskEndpoints.cs`, `AgentEndpoints.cs`, `SourcesEndpoints.cs`, `StreamingEndpoints.cs`
- `src/KnowledgeHub.Server/Auth/ApiKeyAuthenticationHandler.cs` (claim names)
- `src/KnowledgeHub.McpEngine/` dispatcher (tools/call enforcement point)
- `src/KnowledgeHub.Server/Mcp/CallerIdentity.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/RateLimiting/RateLimitOptions.cs       (new — RateLimiting:* config)
src/KnowledgeHub.Server/RateLimiting/CallerPartitioner.cs      (new — key precedence)
src/KnowledgeHub.Server/RateLimiting/McpToolRateLimiter.cs     (new — in-dispatch limiter)
src/KnowledgeHub.Server/Program.cs                             (modify — UseRateLimiter)
src/KnowledgeHub.Server/Api/*.cs                               (modify — RequireRateLimiting per group)
src/KnowledgeHub.Server/Configuration/ConfigurationValidator.cs(modify — validate section)
tests/KnowledgeHub.Tests.Integration/RateLimitTests.cs         (new)
```

## 4. Requirements

### RF-001 — Partitioned policies
- **Description:** Three named policies (`llm`, `sync`, `general`) with configurable permit/window/queue; defaults: llm 20/min sliding, sync 10/hour fixed, general 300/min fixed, queue 0.
- **Rules:** partition by key-id → user-id → IP; unauthenticated requests partition by IP and hit a stricter anonymous bucket (llm: 5/min); limits configurable via `RateLimiting:*` without redeploy logic changes.

### RF-002 — HTTP surface enforcement
- **Description:** `/api/ask*`, `/api/agent*` → `llm`; `/api/sources/*/sync` + `write` endpoints → `sync`; other `/api/*` → `general`; `/metrics`, `/health`, static assets, `/hubs/*` (SignalR long-lived) excluded from `general`.
- **Rules:** `429` returns ProblemDetails `{ "error": "rate_limited", "retryAfterSeconds": n }` + `Retry-After` header.

### RF-003 — MCP surface enforcement
- **Description:** `tools/call` on LLM-spending tools (`ask_knowledge`, `agent_chat`, future rerank-enabled `search_knowledge`) passes through `McpToolRateLimiter` — same partition identity resolved from `CallerIdentity`/HTTP context.
- **Rules:** over-limit → tool result `{ isError: true, content: "rate limited — retry in Ns" }` (never an exception); non-LLM tools (settings, `read_document`) exempt unless configured otherwise.

### RF-004 — Auditability
- **Description:** Every rejection logged at `Warning` with partition kind + policy (never the raw key); API-key usage audit (`ApiKeyUsageEvent`) records `rate_limited` events.

**Business rules / invariants:**
- Rate limiting never locks out the last admin: cookie sessions from localhost… no — simpler: limits are per-partition; there is no global cap that could starve everyone.
- Disabled cleanly via `RateLimiting:Enabled=false` (default **true** after rollout — documented).

## 5. API Contract

**Response (429):**
```json
{ "type": "https://tools.ietf.org/html/rfc9110#section-15.5.30", "title": "rate_limited", "status": 429, "detail": "retry in 37s" }
```
Headers: `Retry-After: 37`.

## 6. Acceptance Criteria

- [ ] **Given** 21 `ask` requests in 60s from one API key **when** limit is 20/min **then** the 21st returns 429 + `Retry-After`.
- [ ] **Given** two different API keys **when** both hammer **then** partitions are independent.
- [ ] **Given** `tools/call` `agent_chat` over the limit **when** invoked **then** `isError` friendly result, HTTP 200 preserved.
- [ ] **Given** `RateLimiting:Enabled=false` **when** any request **then** no limiting applied.
- [ ] **Given** SignalR `/hubs/mcp` **when** connected **then** unaffected by `general`.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Proxy/balancer in front | `X-Forwarded-For` | use forwarded header only when `RateLimiting:TrustForwardedHeaders=true` (default false — spoofable) |
| Streaming SSE request | long-lived | counted once at open, not per token |
| Clock skew | window boundaries | sliding window tolerates, no double-charge |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; confirm claim names + dispatcher entry point.
- [ ] **T2 — Options + partitioner + policies** with unit tests.
- [ ] **T3 — HTTP wiring:** `UseRateLimiter` + per-endpoint `RequireRateLimiting`.
- [ ] **T4 — MCP limiter** in dispatch path.
- [ ] **T5 — Config validation + audit events.**
- [ ] **T6 — Integration tests** (WebApplicationFactory, small windows).
- [ ] **T7 — Verification + Done + PR.**

## 8. Organization Guardrails

- Never commit to `main`/`master`/`develop`.
- Defaults must not break the existing test suite (tests run under elevated limits or disabled limiter via test config).
- No raw API key or PII in limit logs — partition kind + hashed id only.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Existing integration suite green under test limits.
- [ ] Config documented in `appsettings` comments / README section.
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- Whether per-key custom limits (admin-set per API key) belong here or deferred — deferred; `RateLimiting:*` global only this round.
