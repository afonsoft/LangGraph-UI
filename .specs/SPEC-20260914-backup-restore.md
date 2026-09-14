# SPEC-20260914-backup-restore

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `backup-restore` |
| Type | `Infra` |
| Stack | `Bash`, `SQLite` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260914-infrastructure` |
| Status | `Approved` |

## 1. User Story

**As a** platform operator
**I want** CLI tools to perform consistent backups of SQLite and the Obsidian vault
**So that** recovery from catastrophe is possible.

## 2. Scope

**In scope:**
- `backup.sh`: Atomically copy `knowledgehub.db` + archive linked vaults (if path is accessible).
- `restore.sh`: Overwrite current file, restart service.

## 3. Task Plan

- [ ] T1 — Script `backup.sh` (using `VACUUM INTO` for SQLite backup).
- [ ] T2 — Script `restore.sh`.
