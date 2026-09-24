# AD-0010 — Upstream MCP proxies with encrypted per-scope secrets

## Context
KnowledgeHub federates external MCP servers (DeepWiki public/private,
Firecrawl, Tavily, Context7, arbitrary `McpProxy` sources) into one catalog.

## Decision
Each integration is an `IToolProvider` that merges upstream `tools/list` into
the local catalog and passes `tools/call` through via `McpClient` (Streamable
HTTP). Secrets are stored encrypted (`IntegrationSecrets`, masked in every API
response) and overridable per API key — `set_api_key_settings` lets a key
carry its own upstream credentials. HTTP calls go through the shared
resilience pipeline (retry + circuit breaker).

## Consequences
- Positive: one catalog/auth surface for local + remote tools; per-tenant keys.
- Trade-off: upstream latency/failure surfaces as `isError` — catalog merge is
  cached (`*:ToolsCacheSeconds`) and degraded gracefully when a proxy is down.

## Related SPEC
- Proxy SPECs: deepwiki, firecrawl, tavily, context7, mcp-proxy-source-type,
  api-key-settings (`.specs/`)
