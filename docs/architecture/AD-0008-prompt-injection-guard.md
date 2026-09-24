# AD-0008 — Prompt-injection guard for retrieved context

## Context
Retrieved chunks are untrusted content pasted into LLM prompts — a poisoned
document can hijack answers.

## Decision
`ContentSanitizer` scans every chunk at ingest (heuristic flags:
instruction-override, jailbreak, exfiltration patterns…) and persists
`DocumentChunk.SuspicionFlags` + a `security_events` audit row (never raw
content). At retrieval, flagged chunks are dropped post-rank
(`Security:Injection:ExcludeFlagged`, default true); when an operator disables
it for audit, flagged items keep flowing but carry `securityFlagged` +
`suspicionFlags` in the contract, a `flagged:` marker in tool text output,
and a warning badge in the Playground. In the prompt, chunks are wrapped in
explicit trust boundaries (`PromptBoundary.WrapChunk`) with escaping.

## Consequences
- Positive: default-deny for poisoned context; auditable; operator can inspect
  flagged content without redeploying.
- Trade-off: heuristics have false-positive tolerance — flags are advisory
  metadata, content stays indexed.

## Related SPEC
- [.specs/SPEC-20260923-prompt-injection-guard.md](../../.specs/SPEC-20260923-prompt-injection-guard.md)
- [.specs/SPEC-20260923-flagged-chunk-badge.md](../../.specs/SPEC-20260923-flagged-chunk-badge.md)
