# SPEC-20260914-health-checks

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `health-checks` |
| Type | `Infra` |
| Stack | `.NET 10`, `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-infrastructure` |
| Status | `Draft` |

## 1. User Story

**As a** platform operator
**I want** endpoints `/health` and `/ready`
**So that** orchestration tools (Docker, K8s) can monitor and manage the container lifecycle reliably.

## 2. Scope

**In scope:**
- Register `Microsoft.Extensions.Diagnostics.HealthChecks`.
- **`/health/ready` (Ready)**: Returns `200` only when DB migrated, embedding provider ready, and ingestion services initialized.
- **`/health/live` (Live)**: Returns `200` if Kestrel is running.
- UI: `/mcp-monitor` page shows aggregated health status.

**Out of scope:**
- External monitoring integration (Prometheus/Grafana).

## 3. Task Plan

- [ ] T1 — Add `Microsoft.Extensions.Diagnostics.HealthChecks` package.
- [ ] T2 — Implement `HealthCheck` providers for DB, Embeddings, Ingestion.
- [ ] T3 — Wire endpoints `app.MapHealthChecks("/health/ready", ...)` and `app.MapHealthChecks("/health/live", ...)` in `Program.cs`.
