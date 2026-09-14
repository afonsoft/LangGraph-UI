# SPEC-20260914-graceful-shutdown

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `graceful-shutdown` |
| Type | `Infra` |
| Stack | `.NET 10` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-infrastructure` |
| Status | `Approved` |

## 1. User Story

**As a** cluster manager
**I want** the app to finish processing active requests on stop
**So that** data corruption and dropped MCP sessions are minimized.

## 2. Scope

**In scope:**
- Ensure `IngestionService` and `VaultWatcherService` respect `CancellationToken`.
- Drain MCP sessions gracefully.
- Configurable `ShutdownTimeout` in Kestrel options.

## 3. Task Plan

- [ ] T1 — Audit background services for `CancellationToken` usage.
- [ ] T2 — Configure `ShutdownTimeout` in `builder.WebHost`.
