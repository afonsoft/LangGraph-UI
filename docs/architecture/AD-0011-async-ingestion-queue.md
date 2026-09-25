# AD-0011 — Persisted async ingestion queue

## Context

Source sync used to run inline inside `POST /sources/{id}/sync`: long-running syncs held the HTTP request, gave no progress signal, could not be cancelled, and had no durable state — a crashed process lost mid-sync progress and left documents half-indexed. Auto-sync (scheduled/watcher triggers) had the same fragility. A dedicated queue also had to preserve the legacy synchronous contract for existing callers and avoid queue-full deadlocks under bursts.

## Decision

Introduce a persisted ingestion job queue: `POST /sources/{id}/sync` enqueues and returns `202 + jobId` (`?wait=true` keeps the legacy `SyncResultDto` contract); an `IngestionWorker` background service drains jobs persisted in SQLite with per-document counters and terminal status. `POST /sources/{id}/reindex` forces re-chunk/re-embed and can be selective by chunker version; `POST /api/ingestion/jobs/{id}/cancel` cancels queued jobs (`409` on running/terminal); `GET /api/ingestion/jobs[/{id}]` exposes status and per-doc errors. Scheduled/watcher syncs route through the same queue instead of calling the service inline. `Ingestion:QueueSize` (default 100) bounds the pending depth.

## Consequences

- Positive: progress + cancellation are observable (`/api/ingestion/jobs`, job progress feed), syncs survive restarts, the HTTP surface stays fast, reindex is selective (only stale chunker versions), transient connector errors don't masquerade as deletions.
- Trade-off: job state adds a table + worker lifecycle to manage; queue-full rejection needs client retry handling; ordering guarantees are per-source, not global.

## Related SPEC

- [.specs/SPEC-20260924-async-ingestion-queue.md](../../.specs/SPEC-20260924-async-ingestion-queue.md)
- [.specs/SPEC-20260925-autosync-through-queue.md](../../.specs/SPEC-20260925-autosync-through-queue.md)
- [.specs/SPEC-20260925-job-progress-feed.md](../../.specs/SPEC-20260925-job-progress-feed.md)
