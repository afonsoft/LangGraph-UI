# AD-0001 — Single-process modular monolith (SPA + REST + MCP + SignalR)

## Context
KnowledgeHub must run standalone on a user's machine (no orchestrator, no extra
services) while still exposing four surfaces: Blazor WASM admin SPA, REST
management API, native MCP server (Streamable HTTP + legacy SSE) and a SignalR
monitor hub.

## Decision
One Kestrel process hosts all four surfaces on a single port
(`ASPNETCORE_URLS`). Internally the code is split into four projects —
`Shared` (contracts), `Client` (WASM), `Server` (host + domain), `McpEngine`
(protocol) — so module boundaries are enforced by compilation, not network.

## Consequences
- Positive: zero-install deployment (`dotnet publish -r` single file or one
  container), one port to expose, no inter-service latency or auth plumbing.
- Trade-off: no independent scaling per surface; the MCP dispatcher shares the
  process budget with REST (mitigated by `Mcp:MaxConcurrentCallsPerSession` and
  the rate limiter — AD-0007).

## Related SPEC
- [.specs/SPEC-20260913-mcp-native-server.md](../../.specs/) (hybrid session
  transport), [.specs/](../../.specs/) set
