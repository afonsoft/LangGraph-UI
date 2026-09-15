# SPEC-20260915-wasm-boot-proxy-hardening

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `wasm-boot-proxy-hardening` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `ASP.NET Core Minimal API` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-wasm-boot-proxy-hardening` |
| Ticket | `[A DEFINIR]` (follow-up of #48 / PR #49) |
| Status | `Done` |

## 1. User Story

**As a** corporate user behind a restrictive web proxy (Itaú SWG — "Media Type Blocklist" download filter)
**I want** the Blazor WASM app to boot without any request URL ending in a blocked extension, and to degrade gracefully through a text-encoded asset channel if the proxy also content-sniffs binary payloads
**So that** `https://rag.afonsoft.dev` reaches the login screen inside the corporate network.

**Problem context:**
`SPEC-20260915-wasm-boot-proxy-fix` (merged, deployed) remapped `_framework` binary assets to `GET /framework-assets/{fileName}` — **but kept the original file name including its extension in the URL**. Post-deploy browser evidence shows the proxy still blocks on the `.dat` suffix regardless of path:

```text
GET https://rag.afonsoft.dev/framework-assets/icudt_no_CJK.lfu7j35m59.dat  → 403 (MediaTypeBlockedDownload)
GET https://rag.afonsoft.dev/_framework/icudt_no_CJK.lfu7j35m59.dat        → 403 (MediaTypeBlockedDownload)
Failed to find a valid digest in the 'integrity' attribute ... SRI's integrity checks failed.
Error in mono_download_assets: download '.../icudt_no_CJK.lfu7j35m59.dat' failed — TypeError: Failed to fetch
```

Meanwhile `MONO_WASM: WebAssembly resource does not have the expected content type "application/wasm", so falling back to slower ArrayBuffer instantiation` proves a `.wasm` body **did** download through the `/framework-assets/` mirror as `application/octet-stream` — i.e. the blocklist matches the **URL extension** (`.dat`), not the response media type for octet-stream. The boot died at the `.dat` before `.dll` assets were exercised; PE files (`MZ` magic) remain an unverified content-sniffing risk.

Current client chain (`js/boot.js`): mirror fetch with original name → fallback `defaultUri`. Both URLs end in `.dat` → both 403 → boot aborts silently at the loading spinner.

## 2. Scope

**In scope:**
- New extensionless mirror route `GET /framework-assets/{stem}/{ext}` serving `_framework/{stem}.{ext}` — the blocked suffix never appears in the request URL.
- Encoded fallback on the same route: `?enc=b64` returns the file base64-encoded as `text/plain` — defeats both extension filters **and** binary content-sniffing.
- `js/boot.js` retry chain: extensionless mirror (integrity pass-through) → b64 decode + client-side SHA-256 verification via `crypto.subtle` → default URI. `.js` assets keep default loading.
- Per-extension `Content-Type` on the mirror (`.wasm` → `application/wasm`) to restore `WebAssembly.instantiateStreaming` instead of the slower ArrayBuffer path observed in the log.
- Boot-failure UX: when every layer fails, replace the loading spinner in `#app` with a readable message (proxy-blocking guidance + reload link).
- Keep the existing `GET /framework-assets/{fileName}` route unchanged for backward compatibility with already-served `index.html`/`boot.js` caches.
- Integration tests for the new route, the `enc=b64` variant, and traversal/validation rejections.

**Out of scope:**
- `_content/*` assets (BootstrapBlazor `.woff2` fonts, etc.) — intercepting them requires a service worker, not `loadBootResource`. Follow-up SPEC if the proxy proves to block them too.
- Service-worker–based asset proxying / offline PWA.
- `InvariantGlobalization` (still rejected — would drop pt-BR formatting).
- Auth/login behavior (`SPEC-20260914-auth-login`, unchanged).
- Response compression for the b64 variant (no compression middleware exists; ~33% one-time boot overhead accepted).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Client/wwwroot/js/boot.js` — extend `loadBootResource` into a three-layer chain; add `Blazor.start(...)` rejection handler writing to `#app`.
- `src/KnowledgeHub.Server/Api/FrameworkAssetsEndpoints.cs` — add `/{stem}/{ext}` route (raw + `?enc=b64`) alongside the existing `/{fileName}` route.
- `src/KnowledgeHub.Client/wwwroot/index.html` — unchanged (`autostart="false"` + `boot.js` already in place).
- `tests/KnowledgeHub.Tests.Integration/FrameworkAssetsTests.cs` — new cases.
- `docs/architecture/knowledge-hub_deployment.mmd` — update the mirror label (`{stem}/{ext}` + `?enc=b64`).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/wwwroot/js/boot.js` — current remap + fallback.
- `src/KnowledgeHub.Server/Api/FrameworkAssetsEndpoints.cs` — `ValidName` regex, `TryCompressed`, caching headers.
- `tests/KnowledgeHub.Tests.Integration/FrameworkAssetsTests.cs` — fixture + conventions.
- `src/KnowledgeHub.Server/Program.cs` (~line 139) — `app.MapFrameworkAssetsApi()` position.
- `.specs/SPEC-20260915-wasm-boot-proxy-fix.md` — prior art and its confirmed gap.
- Microsoft docs: `loadBootResource(type, name, defaultUri, integrity)` — returning `Promise<Response>` makes Blazor use the response directly (no runtime SRI re-check), so client-side digest verification is required for the transformed (b64) payload.

**Files to create or modify:**
```text
src/KnowledgeHub.Client/wwwroot/js/boot.js                 (three-layer chain + boot failure UX)
src/KnowledgeHub.Server/Api/FrameworkAssetsEndpoints.cs    (new {stem}/{ext} route, ?enc=b64, MIME map)
tests/KnowledgeHub.Tests.Integration/FrameworkAssetsTests.cs (new cases)
docs/architecture/knowledge-hub_deployment.mmd             (label update)
```

## 4. Requirements

### RF-001: Extensionless mirror route
- **Description:** `GET /framework-assets/{stem}/{ext}` resolves `_framework/{stem}.{ext}` via `IWebHostEnvironment.WebRootFileProvider`. `stem` must match `^[A-Za-z0-9._-]+$` and contain no `..`; `ext` must match `^[a-z0-9]{1,10}$`. Any violation, missing file, or directory → `404`. Route is single-segment-per-param (no catch-all → slashes cannot be smuggled).
- **Input → Output:** `GET /framework-assets/icudt_no_CJK.lfu7j35m59/dat` → `200` bytes of `_framework/icudt_no_CJK.lfu7j35m59.dat`.

### RF-002: Response headers and content type
- **Description:** Reuse existing semantics: `Cache-Control: public,max-age=31536000,immutable`, `Vary: Accept-Encoding`, best-effort `.br`/`.gz` sibling serving. `Content-Type` by extension map: `wasm` → `application/wasm`, `json` → `application/json`, `js` → `text/javascript`, default `application/octet-stream`. Rationale: correct `application/wasm` restores streaming compilation (removes the `MONO_WASM` fallback warning seen in production).
- **Rules:** never log the requested name at `Warning`+ on 404; anonymous; no directory enumeration.

### RF-003: Encoded variant `?enc=b64`
- **Description:** When `enc=b64` (only accepted value; anything else → raw behavior or `400`), the endpoint streams the raw file bytes base64-encoded as `Content-Type: text/plain; charset=utf-8`, same cache headers. No `Content-Encoding` negotiation for this variant. Body MUST decode byte-for-byte to the `_framework` file so the client-side digest check matches the boot `integrity` hash.
- **Input → Output:** `GET /framework-assets/icudt_no_CJK.lfu7j35m59/dat?enc=b64` → `200 text/plain` body = `base64(fileBytes)`.

### RF-004: Client three-layer chain (`boot.js`)
- **Description:** `loadBootResource` keeps returning `null` for `.js` and for paths outside `/_framework/`. For other assets it splits the basename at the **last** dot into `(stem, ext)` and tries, in order:
  1. `fetch(framework-assets/{stem}/{ext}, {integrity})` — browser SRI validates identical bytes; return `response` if `ok`.
  2. `fetch(framework-assets/{stem}/{ext}?enc=b64)` — **without** `integrity` (payload is transformed); `text()` → base64 decode → verify `SHA-256` via `crypto.subtle.digest` against the `integrity` param (`sha256-<base64>` format) → `new Response(bytes, { headers: { 'Content-Type': <mime-by-ext> } })`. Verification failure → next layer.
  3. `fetch(defaultUri, {integrity})` — original behavior for dev/no-proxy environments.
- **Rules:** names without a dot keep default loading (`return null`); base64 decode must handle multi-MB payloads (chunked `atob` or `Uint8Array.fromBase64` with fallback); every layer failure is non-fatal until the last; integrity parse failure → skip to layer 3.

### RF-005: Boot-failure UX
- **Description:** `Blazor.start({...})` returns a promise — attach a rejection/`catch` path that replaces `#app` content with a static, styled message: app could not start because required files were blocked by the network/proxy; suggest contacting IT support or trying another network; include a `Reload` link. Also keep `#blazor-error-ui` behavior intact for post-boot errors.
- **Input → Output:** all layers fail → user sees actionable text instead of a frozen spinner.

### RF-006: Tests
- **Description:** Extend `FrameworkAssetsTests` (`WebApplicationFactory`): `GET /framework-assets/{real-stem}/{ext}` → `200` + correct `Content-Type` + immutable cache; `?enc=b64` → `200 text/plain` whose body base64-decodes to the exact file bytes; unknown stem → `404`; `stem`/`ext` failing the regexes, containing `..`, or `enc` with junk value behavior → rejected/`404` (or `400` per implementation, asserted consistently); `.wasm` stem/ext → `application/wasm`. No hardcoded fingerprints — resolve real asset names from the test server's `WebRootFileProvider` as the existing tests do.

## 5. API Contract

```text
GET /framework-assets/{stem}/{ext}
Auth: anonymous (public static asset)
Params: stem — asset basename without final extension (dots allowed mid-stem)
        ext  — original extension, [a-z0-9]{1,10}
Query:  enc=b64 — optional; returns base64 text/plain instead of raw bytes

→ 200  body = raw bytes of _framework/{stem}.{ext}
       Content-Type: application/wasm | application/json | text/javascript | application/octet-stream
       Cache-Control: public, max-age=31536000, immutable
       (+ Content-Encoding: br|gzip when a pre-compressed sibling is served)

→ 200  enc=b64: body = base64(fileBytes)
       Content-Type: text/plain; charset=utf-8
       Cache-Control: public, max-age=31536000, immutable

→ 404  ProblemDetails/empty (unknown asset, illegal chars, traversal attempt)
```

## 6. Acceptance Criteria

- [ ] **Given** the production app behind the Itaú proxy **when** Blazor boots **then** no request URL ends in `.dat`/`.wasm`/`.dll`/`.blat`/`.pdb` — `icudt_no_CJK.{fp}` is fetched via `/framework-assets/icudt_no_CJK.{fp}/dat` and returns `200`.
- [ ] **Given** the `icudt` asset fetched through the extensionless route **when** integrity is passed through **then** browser SRI validates (identical bytes) and `mono_download_assets` completes.
- [ ] **Given** a proxy that also content-sniffs binaries (simulated: extensionless route unreachable) **when** layer 1 fails **then** layer 2 fetches `?enc=b64`, decodes, verifies SHA-256 against the boot integrity hash, and returns a valid `Response`.
- [ ] **Given** a tampered/mismatched b64 payload **when** the client digest does not match `integrity` **then** the asset is rejected and the chain falls through — a corrupted mirror can never be booted.
- [ ] **Given** `dotnet.wasm` through the mirror **when** served **then** `Content-Type: application/wasm` — no `MONO_WASM ... falling back to slower ArrayBuffer` warning.
- [ ] **Given** every layer fails **when** `Blazor.start` rejects **then** `#app` shows the proxy-guidance message with a reload link (no silent spinner).
- [ ] **Given** a browser without the proxy (local `dotnet run`) **when** layers 1–2 are unavailable/fail **then** layer 3 default fetch boots the app unchanged.
- [ ] **Given** `GET /framework-assets/{fileName}` (legacy route) **when** requested **then** still `200` — backward compat for cached pages.
- [ ] **Given** `..`-style stems, illegal chars, or `ext` with separators/uppercase tricks **when** requested **then** `404` — nothing outside `_framework` is served.
- [ ] **Given** `dotnet test` **then** extended `FrameworkAssetsTests` + full suite pass.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Asset name without dot | `dotnet` | default loading (`null`) — no remap |
| Fingerprinted stem with dots | `icudt_no_CJK.lfu7j35m59` + `dat` | resolves `{stem}.{ext}` — dots mid-stem legal |
| `enc` junk | `?enc=zip` | raw behavior or `400` — never crashes |
| b64 body whitespace/wraps | multi-MB file | decoder tolerates standard base64 output of `Convert.ToBase64String` (no wrapping) |
| Proxy strips query strings | `?enc=b64` dropped | endpoint returns raw bytes → client digest check fails → layer 3 |
| Legacy cached `index.html`+`boot.js` | old `/{fileName}` calls | still `200` — old chain unchanged |
| `blazor.boot.{fp}.json` | `.json` ext | remapped like any non-`.js` asset; `application/json` |
| HEAD request | — | `405` or naturally handled; not required |

## 7. Task Plan

- [x] **T1 — Server:** `/{stem}/{ext}` route + `enc=b64` + per-ext `Content-Type` map in `FrameworkAssetsEndpoints.cs`; `/{fileName}` kept untouched.
- [x] **T2 — Tests:** `FrameworkAssetsTests` extended per RF-006 — red (9/9 fail) → green.
- [x] **T3 — Client:** `boot.js` rewritten — `(stem, ext)` split, three-layer chain, base64 decode, `crypto.subtle` integrity verify, `Blazor.start().catch` UX writing to `#app`.
- [x] **T4 — Verify:** `dotnet build` ✓, `dotnet format` whitespace+style ✓, `dotnet test` ✓ (130 unit + 110 integration); live smoke localhost:5199 — `{stem}/{ext}` → 200 octet-stream immutable, wasm → `application/wasm`, `?enc=b64` → text/plain byte-exact round-trip (SHA-256 verified), `..foo`/`D4T`/`d-at` → 404, legacy `/{fileName}` → 200, gzip sibling negotiated.
- [x] **T5 — Deploy + smoke:** PR #54 merged (`2562d29`), deployed 2026-09-15 via `docker compose` rebuild on the prod host. Post-deploy smoke: `/framework-assets/icudt_no_CJK.lfu7j35m59/dat` → 200 octet-stream immutable; `?enc=b64` → 200 text/plain; wasm stem/ext → 200 `application/wasm`; legacy `/{fileName}` → 200; `js/boot.js` (hardened) + `autostart=false` served; `/login` → 200; `/api/auth/me` anon → 401. `docs/architecture/knowledge-hub_deployment.mmd` label updated. Browser-side smoke atrás do proxy Itaú (boot→login→troca forçada, zero URLs com `.dat`/`.wasm` no DevTools) aguarda validação do usuário.

**7.1 Validation:** Bugfix — reproduction evidence (production console log, this SPEC §1) + regression integration tests for both route variants; client JS verified by manual DevTools smoke (no JS harness in repo) including a forced-failure run (block the mirror via DevTools request blocking to exercise layers 2–3 and the UX path).

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-wasm-boot-proxy-hardening`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** endpoint stays anonymous, read-only, rooted under `_framework`, regex-validated params, no listing, no `Warning`+ logs on 404. SRI end-to-end is preserved: layer 1 via native `integrity` pass-through; layer 2 via explicit `crypto.subtle` digest comparison before handing the `Response` to Blazor — a proxy-injected payload is always rejected.
- **Scope:** no auth/UI logic changes beyond the `#app` failure message; no `_content/*`; no service worker.
- **Backward compat:** legacy `/{fileName}` route and layer-3 default fetch keep every non-proxy environment byte-identical to today.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests or verified manually (DevTools/live-smoke evidence). *(behind-proxy browser validation pending the user's browser)*
- [x] Edge cases handled (traversal rejected on both params; no-dot names use default loading; b64 round-trip byte-exact; digest mismatch rejected).
- [x] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` green. *(130 unit + 110 integration, 0 failures)*
- [x] Guardrails respected — `.github/workflows/` untouched; SRI preserved on every layer; `_content/*` untouched.
- [ ] Manual smoke at `https://rag.afonsoft.dev` behind the corporate proxy: app boots → `/login` → forced change → app usable; zero blocked-extension URLs in the network log. *(deployed 2026-09-15 — server-side smoke green; browser-side validation behind the Itaú SWG requires the user's browser; also closes the pending item in `SPEC-20260915-wasm-boot-proxy-fix` §9)*

## Open Questions / Pending Ambiguity

- Whether the proxy content-sniffs `MZ`/PE payloads in addition to URL extensions — unverifiable from outside; layer 2 (b64) covers it either way.
- Whether `.woff2` fonts under `_content/BootstrapBlazor` get blocked next — requires a service-worker approach; separate SPEC if confirmed in production.
- `Uint8Array.fromBase64` browser support matrix at client site — implementation must ship the chunked `atob` fallback regardless.
