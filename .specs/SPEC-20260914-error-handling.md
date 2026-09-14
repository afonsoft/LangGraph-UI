# SPEC-20260914-error-handling

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `error-handling` |
| Type | `Infra` |
| Stack | `.NET 10`, `Microsoft.AspNetCore.Mvc` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-infrastructure` |
| Status | `Draft` |

## 1. User Story

**As a** API consumer
**I want** structured error messages (RFC 7807 ProblemDetails)
**So that** integrations handle failures programmatically rather than parsing cryptic strings.

## 2. Scope

**In scope:**
- Use built-in `ProblemDetails` middleware.
- Map domain exceptions (e.g., `EmbeddingProviderException`) to `ProblemDetails`.
- Scrub stack traces from production logs/responses.

## 3. Task Plan

- [ ] T1 — Enable `AddProblemDetails` in `Program.cs`.
- [ ] T2 — Implement `IExceptionHandler` to map exceptions to `ProblemDetails`.
