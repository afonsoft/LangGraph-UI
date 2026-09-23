# SPEC-20260923-observability-metrics

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `observability-metrics` |
| Type | `Infra` (telemetry) |
| Stack | `.NET 10 / C# 14` + `System.Diagnostics.Metrics` + `ActivitySource` + OpenTelemetry exporters (opt-in) |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-{YYYYMMDD}-observability-metrics` |
| Ticket | `#163` |
| Status | `Done` |
| Origin | `gap-analysis-20260923` — GAP-observability-metrics (média). Proposal §13.3 (latência por etapa). |

## 1. User Story

**As a** platform operator
**I want** per-stage latency histograms, counters and traces across the retrieval/agent pipeline exported via OpenTelemetry
**So that** slow searches, embedding outages, cache hit rates and LLM latency are measurable in production — today a slow `ask` is a black box.

**Problem context:**
There is no `Meter`, `ActivitySource` or OTel anywhere in `src/` (verified by grep). Observability is `ILogger` + health checks + the SignalR MCP feed (activity events, not metrics). The proposal requires per-stage latency measurement; SPEC-20260916 RNF-004 demands measured optimizations — there is no instrument to measure with.

## 2. Scope

**In scope:**
- `Meter` (`KnowledgeHub.Server`) with instruments: `knowledgehub.search.duration` (histogram, tags: mode, cache_hit), `knowledgehub.embedding.duration` (provider, model), `knowledgehub.vector_search.duration` (store), `knowledgehub.lexical.duration`, `knowledgehub.llm.duration` (provider, model, kind=ask|agent|rewrite|rerank|summary), `knowledgehub.tool.duration` (tool name), `knowledgehub.sync.duration` + `knowledgehub.sync.chunks` (counter), `knowledgehub.cache.hits/misses` (counter, cache name), `knowledgehub.mcp.requests` (counter, method, transport).
- `ActivitySource` with spans: `search` → children `embed_query`, `vector_search`, `lexical_search`, `hydrate`; `ask` → `search` + `llm_synthesis`; `agent_chat` → per-iteration + per-tool spans; `sync` → `fetch`/`chunk`/`embed`/`persist`.
- `Telemetry:Otlp:Endpoint` config (opt-in `OtlpExporter`) + `Telemetry:Metrics:Prometheus` opt-in endpoint `/metrics`.
- `IChatClient`/`IEmbeddingProvider` wrapping via existing decorator points (no rewrites of providers).

**Out of scope:**
- Distributed tracing propagation into upstream MCP servers (record upstream call duration only).
- Log pipeline changes; alerting rules (dashboard/alert config is operator-side).
- Blazor client telemetry.

## 3. Technical Context

Registration in `KnowledgeHubServiceCollectionExtensions.cs`; pipeline stages in `SearchService.cs`, `AnswerService.cs`, `AgentService.cs`, `IngestionService.cs`, `DynamicToolCatalog.cs`, `McpEngine` dispatch. Cache hit/miss in `Caching/SafeCache.cs`. Health checks in `Health/KnowledgeHubHealthChecks.cs` are unaffected.

**Files to read before implementing:**
- `src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs`
- `src/KnowledgeHub.Server/Services/SearchService.cs`, `AnswerService.cs`, `AgentService.cs`
- `src/KnowledgeHub.Server/Caching/SafeCache.cs`, `CacheKeys.cs`
- `src/KnowledgeHub.Server/Ingestion/IngestionService.cs`
- `src/KnowledgeHub.McpEngine/` (dispatch point for `knowledgehub.mcp.requests`)

**Files to create or modify:**
```text
src/KnowledgeHub.Server/Telemetry/KnowledgeHubMetrics.cs   (new — Meter + instruments)
src/KnowledgeHub.Server/Telemetry/KnowledgeHubActivity.cs  (new — ActivitySource)
src/KnowledgeHub.Server/Telemetry/TelemetryOptions.cs      (new — Telemetry:* config)
src/KnowledgeHub.Server/Services/SearchService.cs          (modify)
src/KnowledgeHub.Server/Services/AnswerService.cs          (modify)
src/KnowledgeHub.Server/Services/AgentService.cs           (modify)
src/KnowledgeHub.Server/Ingestion/IngestionService.cs      (modify)
src/KnowledgeHub.Server/Caching/SafeCache.cs               (modify — hit/miss counters)
src/KnowledgeHub.Server/KnowledgeHubServiceCollectionExtensions.cs (modify — OTel wiring opt-in)
tests/KnowledgeHub.Tests.Unit/Telemetry/*                  (new — MetricCollector-based assertions)
```

## 4. Requirements

### RF-001 — Metrics instruments
- **Description:** The instruments listed in §2 exist under `Meter("KnowledgeHub.Server", version)`; all durations in ms (`double` histograms); counters are `long`.
- **Rules:** tag cardinality bounded — no raw query text, no doc titles, no user ids as tag values; allowed tag sets enumerated per instrument.

### RF-002 — Trace spans
- **Description:** `ActivitySource("KnowledgeHub.Server")` emits the span tree in §2; spans carry tags (`search.mode`, `search.topK`, `vector.store`, `llm.model`, `tool.name`, `sync.sourceId` — no content).
- **Rules:** spans set `Status=Error` + exception event on failure; sampling follows the OTel SDK default parent-based.

### RF-003 — Opt-in exporters
- **Description:** `Telemetry:Otlp:Endpoint` set → `AddOpenTelemetry().WithMetrics(…).WithTracing(…)` + OTLP exporter; `Telemetry:Metrics:Prometheus=true` → `/metrics` scrape endpoint (Operational policy).
- **Rules:** zero config → zero overhead beyond `Meter`/`ActivitySource` no-op listeners; exporter failure never breaks requests (OTel SDK isolation).

### RF-004 — Cache telemetry
- **Description:** `SafeCache` records `knowledgehub.cache.hits`/`misses` tagged by cache region (`embedding`, `search`, `catalog`, `secret`, `indexVersion`) — enables hit-rate dashboards.

### RF-005 — MCP dispatch counter
- **Description:** JSON-RPC dispatcher increments `knowledgehub.mcp.requests` per method+transport+sse/stateless mode.

**Business rules / invariants:**
- No PII, query text, chunk content or secrets in tags/span attributes — enforce via a shared `TelemetryTags` helper + unit test asserting tag keys.
- Telemetry must be allocation-light: `Meter`/`ActivitySource` singletons; no per-call string building on hot paths beyond tag arrays.

## 5. API Contract

**Endpoint:** `GET /metrics` — **Auth:** Operational when Prometheus enabled; absent otherwise (404).

## 6. Acceptance Criteria

- [ ] **Given** a `MetricCollector` listener **when** `search` runs **then** `knowledgehub.search.duration` is recorded with `mode` + `cache_hit` tags.
- [ ] **Given** a cache hit and a miss **when** instrumented **then** counters reflect each.
- [ ] **Given** an `agent_chat` run with 2 tool calls **when** traced **then** span tree contains agent → iteration → tool children.
- [ ] **Given** `Telemetry:Otlp:Endpoint` unset **when** app boots **then** no exporter registered and requests work identically.
- [ ] **Given** a failing LLM call **when** traced **then** the `llm` span carries error status.
- [ ] **Given** tag-value audit **when** unit test enumerates emitted tags **then** no query text/PII keys exist.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| OTLP endpoint down | unreachable endpoint | requests unaffected; exporter retries per SDK defaults |
| High-cardinality tool names | dynamic `query_*` tools | tool tag uses slugged name — bounded by source count |
| Activity source with no listener | no OTel configured | `Activity` is null — zero overhead |

## 7. Task Plan

- [ ] **T1 — Discovery:** read §3 files; map exact instrumentation points.
- [ ] **T2 — Core:** `KnowledgeHubMetrics` + `KnowledgeHubActivity` + `TelemetryOptions` + `TelemetryTags` allowlist helper.
- [ ] **T3 — Instrument services:** search/answer/agent/ingestion/cache/MCP dispatch.
- [ ] **T4 — Exporters:** opt-in OTLP + Prometheus wiring in DI ext.
- [ ] **T5 — Tests:** `MetricCollector`/`ActivityListener` assertions + tag allowlist test.
- [ ] **T6 — Verification:** build/test/format green; manual `/metrics` scrape evidence.
- [ ] **T7 — Done + PR.**

## 8. Organization Guardrails

- New package `OpenTelemetry.Exporter.OpenTelemetryProtocol` (+ `OpenTelemetry.Instrumentation.AspNetCore`) pinned to a version ≥7 days old; no other deps.
- No content/PII in telemetry — hard rule, enforced by the allowlist helper + test.
- `/metrics` never exposed unauthenticated.

## 9. Definition of Done

- [ ] All RFs implemented; all CAs covered.
- [ ] Instruments documented (name → meaning → tags) in `docs/` or the SPEC.
- [ ] Build/test/format green.

## Open Questions / Pending Ambiguity

- Prometheus exporter vs OTLP-only — both implemented opt-in; default stays OTLP-only-off.
