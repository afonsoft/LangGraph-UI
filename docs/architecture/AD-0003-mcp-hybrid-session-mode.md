# AD-0003 — MCP hybrid session mode

## Context
MCP clients split into two generations: `initialize`-handshake clients
(protocol ≤ 2025-11-25, e.g. Cursor/Claude Desktop legacy SSE) expect stateful
sessions with `tools/list_changed` push; the 2026-07-28 transport is stateless.

## Decision
`Mcp:SessionMode = StatefulForInitializeClients` (default): clients that
perform `initialize` (or connect via legacy SSE) get a full session; clients
advertising 2026-07-28 are answered statelessly — every `tools/list` resolves
the catalog live from the DB, so a missing session is harmless.

## Consequences
- Positive: old and new clients work unchanged on the same `/mcp` endpoint;
  catalog changes still push to sessioned clients.
- Trade-off: two session semantics to reason about — pinned by
  `McpContractTests` (catalog names/schemas are a public interface).

## Related SPEC
- MCP session mode + contract tests SPECs (`.specs/`)
