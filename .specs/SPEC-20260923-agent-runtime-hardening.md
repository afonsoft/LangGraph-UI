# SPEC-20260923-agent-runtime-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `agent-runtime-hardening` |
| Type | `Improvement` (performance / memory / response time / resilience) |
| Stack | `.NET 10 / C# 14` + `Microsoft.Extensions.AI` + `System.Threading.Channels` + `Microsoft.Extensions.Http.Resilience` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-agent-runtime-hardening` |
| Ticket | `[A DEFINIR]` |
| Status | `Done` |
| Origin | `gap-analysis-20260923` — GAP-implementation-agent-perf (média); carries H7 deferral from SPEC-20260916. |

## 1. User Story

**As a** platform operator
**I want** bounded SSE buffers, cached tool schemas, an opt-in answer cache and resilient HTTP calls to Ollama/upstreams
**So that** slow clients can't grow memory unboundedly, repeated questions don't re-pay LLM latency, and transient provider failures don't surface as user-visible errors.

**Problem context:**
- `AgentService.StreamAsync` (:54) uses `Channel.CreateUnbounded<SseEvent>` — a slow/disconnected client means every token event accumulates in memory for the whole run (H7 from SPEC-20260916, deferred).
- `GetModelResponseAsync` (:493-529) rebuilds `ChatOptions { Tools = [.. loop.Functions] }` each iteration — tool JSON schemas serialize per model call.
- `ask_knowledge` caches search results (5min) but the final answer re-runs the LLM every time — identical questions pay full latency.
- Named `HttpClient`s (`embeddings`, `webpage`, `notion`, `chat` — `KnowledgeHubServiceCollectionExtensions.cs:40-58`) have timeouts but no retry/circuit-breaker: a single transient Ollama hiccup fails a sync chunk or an answer.

## 2. Scope

**In scope:**
- Bounded SSE channel (`Channel.CreateBounded(capacity, FullMode.Wait)`) with cancellation-aware writer; client disconnect detection via `HttpContext.RequestAborted` already flowing through `ct`.
- Tool schema memoization: build `ChatOptions` once per `LoopState` (`loop.ChatOptions` reused across iterations).
- `AnswerCache` opt-in (`Cache:AnswerCache:Enabled`, TTL default 10min): key `ans:{model}:{topK}:{srcHash}:{qHash}:{indexVersion}`; only non-stream `AnswerAsync` cached; streaming path bypasses (documented).
- `AddStandardResilienceHandler()` (or equivalent `Microsoft.Extensions.Http.Resilience` pipeline: retry 3× exp-backoff+jitter, per-attempt timeout, circuit breaker) on `embeddings`, `webpage`, `notion`, `chat` clients; upstream MCP client factory gets the same policy where the transport is HTTP.
- Memory evidence: bounded-channel ceiling proven by test; answer-cache hit measured.

**Out of scope:**
- Semantic/embedding-similarity answer cache (exact-key only this round).
- Distributed answer cache consistency beyond existing `IDistributedCache` semantics.
- Retrying non-idempotent upstream *mutating* calls (read-only retry policy only).

## 3. Technical Context

SSE streaming: `AgentService.StreamAsync` (:48-87) + `AnswerService.StreamAsync` (:60-127) + `Api/StreamingEndpoints.cs`. Chat options per iteration: `GetModelResponseAsync` (:496). Answer cache path: `AnswerService.AnswerAsync` (:27-58) wrapped around existing `IDistributedCache`+`SafeCache` pattern. HTTP clients: `KnowledgeHubServiceCollectionExtensions.cs:40-58`.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/Services/AgentService.cs`, `AnswerService.cs`
- `src/KnowledgeHub.Server/Api/StreamingEndpoints.cs`
- `src/KnowledgeHub.Server/Caching/SafeCache.cs`, `CacheKeys.cs`
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs`
- `src/KnowledgeHub.Server/Mcp/Upstream/*` (HTTP transports)
- `src/KnowledgeHub.Server/Agent/AgentOptions.cs`

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Services/AgentService.cs            (modify — bounded channel, cached ChatOptions)
src/KnowledgeHub.Server/Services/AnswerService.cs           (modify — answer cache)
src/KnowledgeHub.Server/Caching/CacheKeys.cs                (modify — ans: key)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modify — resilience handlers)
src/KnowledgeHub.Server/Agent/AgentOptions.cs               (modify — SseChannelCapacity)
tests/KnowledgeHub.Tests.Unit/*                             (new — channel backpressure, cache key, options reuse)
tests/KnowledgeHub.Tests.Integration/*                      (modify — resilience policy test with fake handler)
```

## 4. Requirements

### RF-001 — Bounded SSE channel
- **Description:** `Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(Agent:SseChannelCapacity=256){ FullMode=Wait, SingleReader=true, SingleWriter=true })`.
- **Rules:** `TryWrite`→`WriteAsync(ct)` in producer paths where awaiting is safe; on cancellation the writer completes — reader drains then stops; capacity configurable; memory ceiling = capacity × avg event size.

### RF-002 — ChatOptions reuse
- **Description:** `loop.ChatOptions = new ChatOptions { Tools = loop.Functions }` built once in `BuildLoopAsync`; `GetModelResponseAsync` uses it verbatim per iteration.
- **Rules:** no mutation of `ChatOptions` inside the loop; tool visibility changes mid-loop don't exist (allowlist/allowWrite fixed at build) — asserted by test.

### RF-003 — Answer cache (opt-in)
- **Description:** `AnswerService.AnswerAsync` consults `IDistributedCache` before LLM call; hit returns cached `AskResponse` with `Cached=true` flag added to DTO; miss stores with TTL.
- **Rules:** key embeds model + indexVersion + filters hash + question hash — index changes invalidate automatically; never cache error/no-provider results; `StreamAsync` never serves from cache.

### RF-004 — HTTP resilience
- **Description:** All named clients (`embeddings`, `webpage`, `notion`, `chat`) get the standard resilience pipeline (retry ≥2 for transient 5xx/HttpRequestException/timeouts with jittered backoff; total request timeout preserved); embedding calls respect existing per-call `ct`.
- **Rules:** retries only for idempotent GET/POST-inference calls — Notion `POST /search` is read-only (safe); mutating upstream calls (ObsidianNoteWriter HTTP ops, if any) excluded via `DisableHttpResilience` on those requests or policy scoping; resilience failures surface as today (typed exceptions).

### RF-005 — Measured evidence
- **Description:** Unit/integration evidence: bounded channel caps buffered events; answer-cache second-call latency ≈0 LLM ms (fake client call-count assertion); resilience retry verified with a fake handler that fails once.

**Business rules / invariants:**
- Zero behavior change when `AnswerCache:Enabled=false` and channel capacity is large — defaults preserve current semantics except boundedness (always on, capacity generous).
- Cached answers never bypass auth — cache lives server-side behind the same endpoint authorization.

## 5. API Contract

`AskResponse` gains optional `bool Cached` (additive, like `SourceType` precedent). No endpoint changes.

## 6. Acceptance Criteria

- [ ] **Given** a consumer that stops reading SSE **when** capacity (test: 4) is exceeded **then** the writer blocks/cancels — buffered events never exceed capacity.
- [ ] **Given** an agent run with 3 iterations **when** instrumented **then** `ChatOptions` instance is identical across iterations (reference-equality assertion).
- [ ] **Given** `AnswerCache:Enabled=true` and repeated identical ask **when** run twice **then** fake `IChatClient` receives exactly 1 call; second response has `Cached=true`.
- [ ] **Given** an embeddings HttpClient whose handler fails once with 503 **when** called **then** the resilience pipeline retries and succeeds.
- [ ] **Given** `AnswerCache:Enabled=false` **when** repeated asks **then** LLM called every time.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Client disconnect mid-run | aborted ct | writer completes, reader drains, run cancels cleanly |
| Cache backend down | SafeCache fail-soft | behaves as miss — answer still generated |
| Answer with 0 citations / no-match | empty context | not cached (cheap path anyway) |
| Notion mutating call | future write op | excluded from retry policy |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; confirm channel/ChatOptions/cache shapes.
- [ ] **T2 — Bounded channel + ChatOptions reuse** in AgentService (+ audit other `CreateUnbounded` uses — `AnswerService` doesn't channel; check `StreamingEndpoints`/`Hubs`).
- [ ] **T3 — Answer cache:** `CacheKeys.Answer`, `Cached` DTO flag, enablement config.
- [ ] **T4 — Resilience:** `Microsoft.Extensions.Http.Resilience` package + handler on 4 named clients; verify package version ≥7 days old.
- [ ] **T5 — Tests:** channel ceiling, options identity, cache hit/miss, retry test.
- [ ] **T6 — Verification:** build/test/format green. Done + PR.

## 8. Organization Guardrails

- Bounded channel capacity is config-tunable; never silently drops events (Wait, not DropOldest — a dropped token corrupts the answer stream).
- Answer cache TTL short (≤15min default) and keyed on indexVersion — staleness bounded.
- No secrets in cache keys.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] H7 from SPEC-20260916 formally closed (noted in §10 evidence).
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- Whether `query_*`/`search_knowledge` tool answers (non-LLM) need cache — already covered by search-result cache; no change.
