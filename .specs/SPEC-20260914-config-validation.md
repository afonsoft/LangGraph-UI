# SPEC-20260914-config-validation

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `config-validation` |
| Type | `Infra` |
| Stack | `.NET 10`, `FluentValidation` (optional) or `DataAnnotations` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-infrastructure` |
| Status | `Approved` |

## 1. User Story

**As a** platform operator
**I want** immediate startup failure if required configuration is missing or invalid
**So that** I don't debug "weird behavior" later when a service fails dynamically.

## 2. Scope

**In scope:**
- Validate required config blocks (`Embeddings`, `VectorStore`, `DeepWiki`).
- Validate formats for URLs, API keys (regex check), paths.

## 3. Task Plan

- [ ] T1 — Create `ConfigurationValidator` service.
- [ ] T2 — Invoke at `Program.cs` start (before `Migrate()`).
