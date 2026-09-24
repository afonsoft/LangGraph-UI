# AD-0009 — OpenTelemetry observability (opt-in exporters)

## Context
RAG latency/debugging needs stage-level visibility; exporters shouldn't impose
dependencies on the zero-config default.

## Decision
`System.Diagnostics` `Meter` + `ActivitySource` instrument search stages
(lexical/vector/fuse/rerank/hydrate), `ask`/`llm_synthesis`, agent iterations
and tool calls, sync runs, cache hit/miss per region, and MCP request filters.
Exporters are opt-in: `Telemetry:Otlp:Endpoint` (OTLP) and
`Telemetry:Metrics:Prometheus` (`/metrics`).

## Consequences
- Positive: zero overhead/dependency when off; Prometheus/Grafana-ready when on.
- Trade-off: metrics are in-process — a multi-replica deployment would need an
  OTel collector (out of scope for the standalone model).

## Related SPEC
- [.specs/SPEC-20260923-observability-metrics.md](../../.specs/SPEC-20260923-observability-metrics.md)
