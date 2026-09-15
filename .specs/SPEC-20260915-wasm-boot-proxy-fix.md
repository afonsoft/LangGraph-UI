# SPEC-20260915-wasm-boot-proxy-fix

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `wasm-boot-proxy-fix` |
| Type | `Bugfix` |
| Stack | `.NET 10`, `Blazor WASM`, `ASP.NET Core Minimal API` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260915-wasm-boot-proxy-fix` |
| Ticket | `#48` |
| Status | `Done` |

## 1. User Story

**As a** corporate user behind a restrictive web proxy (Itaú SWG — "Block Types From Download Media Type Blocklist")
**I want** the Blazor WASM app to boot without downloading any `_framework` asset through an extension that the proxy blocks, and to land on the login screen
**So that** `https://rag.afonsoft.dev` is actually usable inside the corporate network instead of dying at `Failed to start platform`.

**Problem context:**
The proxy intercepts `GET /_framework/icudt_no_CJK.{fp}.dat` and returns an HTML block page (`403 MediaTypeBlockedDownload`). Blazor then fails SRI validation (`Failed to find a valid digest`) and the whole boot aborts — the login screen (already implemented in `SPEC-20260914-auth-login`, merged as `b3dff1f`) never renders. Two console errors exist today:

1. `<link rel=preload> has an invalid 'href' value` — leftover `<link rel="preload" id="webassembly" />` (a .NET 8 template artifact) in `index.html`.
2. `.dat` download blocked → SRI mismatch → `Error in mono_download_assets` → `Failed to start platform`.

Only `.js` and `.json` assets are proven to pass the proxy; every binary boot asset (`*.dat`, `*.blat`, `*.wasm`, `*.dll`, `*.pdb`, `*.symbols`, `*.mmap`) must be treated as potentially blocked.

## 2. Scope

**In scope:**
- Remove the orphan `<link rel="preload" id="webassembly" />` from `index.html`.
- Client-side boot hook: `Blazor.start({ loadBootResource })` that remaps `_framework` binary assets to an extensionless same-origin route and fetches them itself, **passing the boot `integrity` hash through to the remap `fetch`** — the endpoint serves identical bytes, so SRI keeps validating.
- Server-side anonymous endpoint `GET /framework-assets/{fileName}` that serves the corresponding file from `wwwroot/_framework` with immutable caching, path-traversal-safe.
- Client fallback: if the remapped fetch fails, fall back to the default URI so local/dev environments (where the proxy does not exist) keep working unchanged.
- Acceptance verification that the **existing** login gate (`SPEC-20260914-auth-login`) is reachable in production once the app boots — no changes to the auth implementation itself.

**Out of scope:**
- Any change to `MapStaticAssets`, SRI generation, or asset fingerprinting.
- `InvariantGlobalization` — rejected: it would drop pt-BR date/number formatting.
- Remapping `_content/*` (BootstrapBlazor) or other non-`_framework` assets — no evidence the proxy blocks them; same pattern can be extended later if needed.
- Changes to the login screen UI or auth backend (already `Done` in `SPEC-20260914-auth-login`).
- Serving compressed `.br`/`.gz` variants through the new endpoint as a hard requirement — best-effort only (see RF-004).

## 3. Technical Context

**Where the change happens:**
- `src/KnowledgeHub.Client/wwwroot/index.html` — remove the orphan preload link; add `autostart="false"` to `blazor.webassembly.js` and a `<script src="js/boot.js">` that calls `Blazor.start(...)`.
- `src/KnowledgeHub.Client/wwwroot/js/boot.js` — new file: `loadBootResource` remap + fallback.
- `src/KnowledgeHub.Server/Program.cs` — map the new anonymous `framework-assets` endpoint (before `MapFallbackToFile`, after `MapStaticAssets`), plus a small endpoint file under `src/KnowledgeHub.Server/Api/`.

**Files to read before implementing:**
- `src/KnowledgeHub.Client/wwwroot/index.html` — current head/script layout.
- `src/KnowledgeHub.Server/Program.cs` — pipeline order (`UseAuthentication` → `MapStaticAssets` → API maps → `MapFallbackToFile`).
- `src/KnowledgeHub.Server/Api/ToolsEndpoints.cs` — Minimal API + ProblemDetails conventions.
- `tests/KnowledgeHub.Tests.Integration/McpContractTests.cs` — `WebApplicationFactory` fixture pattern.
- `.specs/SPEC-20260914-auth-login.md` — auth surface this SPEC only verifies, does not modify.
- Microsoft docs: `Blazor.start` `loadBootResource(type, name, defaultUri, integrity)` — returning a `Promise<Response>` makes Blazor use it directly (no SRI check); returning a string/`undefined` uses the default fetch with SRI.

**Files to create or modify:**
```text
src/KnowledgeHub.Client/wwwroot/index.html          (remove preload link, autostart=false, boot.js)
src/KnowledgeHub.Client/wwwroot/js/boot.js          (new — loadBootResource remap)
src/KnowledgeHub.Server/Api/FrameworkAssetsEndpoints.cs (new — GET /framework-assets/{fileName})
src/KnowledgeHub.Server/Program.cs                  (map endpoint, anonymous)
tests/KnowledgeHub.Tests.Integration/FrameworkAssetsTests.cs (new)
```

## 4. Requirements

### RF-001: Remove orphan preload link
- **Description:** Delete `<link rel="preload" id="webassembly" />` from `index.html`. It is a .NET 8 template artifact unused by `blazor.webassembly.js` in .NET 10 and produces `invalid 'href' value` in the console.
- **Input → Output:** page load → no preload console error.

### RF-002: Client boot hook (`boot.js`)
- **Description:** `index.html` loads `blazor.webassembly.js` with `autostart="false"`, then `js/boot.js` calls `Blazor.start({ loadBootResource })`. The callback intercepts any boot resource whose `defaultUri` path is under `/_framework/` **and** whose extension is not `.js` (covers `.dat`, `.blat`, `.wasm`, `.dll`, `.pdb`, `.symbols`, `.mmap`, `.json`); it extracts the basename and fetches `{base}framework-assets/{basename}` (resolved against `<base href>`), returning the `Response`.
- **Rules:** extension check is case-insensitive; `.js` files keep the default path (proxy allows them); the manifest (`blazor.boot.json`) may also be remapped harmlessly — no special-casing required.
- **Input → Output:** `loadBootResource('globalization', 'icudt', '/_framework/icudt_no_CJK.{fp}.dat', '…')` → `fetch('/framework-assets/icudt_no_CJK.{fp}.dat')` `Response`.

### RF-003: Fallback to default URI
- **Description:** If the remapped fetch throws or returns a non-OK status, the callback re-fetches the `defaultUri` (with the same `integrity` hash) so Blazor performs the normal fetch+SRI flow. Keeps dev (`dotnet run`, no proxy) and any future environment without the endpoint working unchanged. *(implemented in `boot.js` via `then(ok→response / else→fetch(defaultUri))`)*

### RF-004: `GET /framework-assets/{fileName}` endpoint
- **Description:** Anonymous minimal endpoint (must serve pre-login — it is static content, same trust level as `MapStaticAssets`). `fileName` is a single path segment (no `*` catch-all → slashes cannot match). Additionally reject anything not matching `^[A-Za-z0-9._-]+$`. Resolve via `IWebHostEnvironment.WebRootFileProvider.GetFileInfo("_framework/" + fileName)` — covers both published output and dev static-web-assets; missing → `404`. Success → `Results.File(stream, "application/octet-stream")` with `Cache-Control: public, max-age=31536000, immutable` (fingerprinted assets are immutable).
- **Rules:** never log the requested name at `Warning`+ on 404 (avoid log noise/scan abuse); no auth attributes; do not enumerate the directory.
- **Best-effort:** if a sibling `{fileName}.br` or `.gz` exists and `Accept-Encoding` allows, serve it with the matching `Content-Encoding` header. Not a blocker.

### RF-005: Login gate verified, not changed
- **Description:** The login screen, cookie auth, forced password change and `aft_*` API keys are already implemented (`SPEC-20260914-auth-login`, `Done`). This SPEC adds **zero** auth code; it only carries production smoke acceptance criteria proving anonymous users land on `/login` after the boot fix. On completion, the pending DoD item "Manual smoke at `https://rag.afonsoft.dev`" in `SPEC-20260914-auth-login` is checked off.

### RF-006: Tests
- **Description:** Integration tests (`WebApplicationFactory`): unauthenticated `GET /framework-assets/{real-file}` → `200` + `application/octet-stream` + immutable cache header; unknown name → `404`; name with `..`/slash → `404`/rejected (never a file outside `_framework`); `blazor.boot.json` basename → `200`. The real fixture file name can be discovered from the test server's `WebRootFileProvider` or from a known fingerprinted asset in the publish/dev output — test must not hardcode a fingerprint.

## 5. API Contract

```text
GET /framework-assets/{fileName}
Auth: anonymous (public static asset)

→ 200  body = raw bytes, Content-Type: application/octet-stream
       Cache-Control: public, max-age=31536000, immutable
       (+ Content-Encoding: br|gzip when a pre-compressed sibling is served)
→ 404  ProblemDetails (unknown name, illegal chars, traversal attempt)
```

## 6. Acceptance Criteria

- [ ] **Given** the app loads **when** the page is requested **then** the console shows no `invalid 'href'` preload error.
- [ ] **Given** a proxy that blocks `.dat` downloads **when** Blazor boots **then** `icudt_no_CJK.{fp}.dat` is fetched via `/framework-assets/icudt_no_CJK.{fp}.dat` (extensionless route), returns `200`, and the app reaches the login screen — no `mono_download_assets` / SRI failure.
- [ ] **Given** any `_framework` binary asset (`*.wasm`, `*.dll`, `*.dat`, `*.blat`, `*.pdb`) **when** `loadBootResource` runs **then** it is fetched through `/framework-assets/`; `.js` assets keep the default `/_framework/` URL.
- [ ] **Given** the `/framework-assets/` endpoint returns non-OK for a resource **when** the callback handles it **then** Blazor falls back to the default URI (dev/no-proxy unaffected).
- [ ] **Given** an anonymous browser at `https://rag.afonsoft.dev` **when** the app boots **then** it lands on `/login`; `admin` + initial password → forced `/change-password`; after changing, reaches `/`. (Closes the pending smoke DoD in `SPEC-20260914-auth-login`.)
- [ ] **Given** `GET /framework-assets/../../etc/passwd`-style input **when** requested **then** `404` — nothing outside `_framework` is served.
- [ ] **Given** `dotnet test` **then** new `FrameworkAssetsTests` + full suite pass.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| Asset name without fingerprint | `dotnet.wasm` | `200` if the file exists (tests), `404` otherwise |
| Traversal / separators | `..%2F`, `a/b`, backslash | `404` — regex + single-segment route reject |
| Proxy also blocks `.dll`/`.wasm` | any binary | already covered by the non-`.js` remap rule |
| Endpoint missing (older server) | fetch `404` | client falls back to `defaultUri` — boot still works where proxy allows |
| `HEAD` request | — | `405` or handled naturally; not required |
| Direct browser nav to `/framework-assets/x` | — | `200` download (same exposure as `/_framework/` today — no new surface) |

## 7. Task Plan

- [x] **T1 — Discovery:** read files in section 3; confirm published `wwwroot/_framework` layout (`dotnet publish` or dev bin output) and the `WebRootFileProvider` path that resolves fingerprinted assets. *(publish emits `.wasm`/`.dat`/`.js` + `.br`/`.gz`; dev emits `.gz` only — endpoint prefers `br`→`gzip`→raw; MS docs confirmed standalone WASM uses top-level `loadBootResource`)*
- [x] **T2 — Server endpoint:** `FrameworkAssetsEndpoints.MapFrameworkAssetsApi()` + wiring in `Program.cs` (anonymous, before `MapFallbackToFile`); integration tests red→green. *(10/10 `FrameworkAssetsTests`)*
- [x] **T3 — Client boot:** edit `index.html` (remove preload link, `autostart="false"`, `boot.js`) + write `js/boot.js` with remap + fallback.
- [x] **T4 — Verify:** `dotnet build` ✓, `dotnet format` (whitespace+style) ✓, `dotnet test` ✓ (130 unit + 101 integration); live smoke on `localhost:5099` — `/framework-assets/icudt_no_CJK.lfu7j35m59.dat` → 200 octet-stream immutable, `Accept-Encoding: gzip` → `.gz` sibling, `..foo`/unknown → 404, `js/boot.js` + `autostart=false` served.
- [x] **T5 — Done + PR:** PR #49 merged (`c89f08a`), deployed 2026-09-15. Post-deploy smoke: `GET /` → 200 serving `boot.js`+`autostart=false`; `GET /framework-assets/icudt_no_CJK.lfu7j35m59.dat` → 200 octet-stream via HTTPS público; `/health/ready` → 200; `/login` → 200; `/api/auth/me` anon → 401 (gate ativo). Browser smoke atrás do proxy Itaú (boot→login→troca forçada) aguarda validação do usuário.

**7.1 Validation:** Bugfix — reproduction evidence (console log in this SPEC) + regression integration tests for the new endpoint; full suite green. Client JS verified by manual DevTools smoke (no JS test harness exists in the repo).

## 8. Organization Guardrails

- **Branches:** `feature/Devin-20260915-wasm-boot-proxy-fix`; never commit to `main`/`master`/`develop`.
- **Workflows:** `.github/workflows/` untouched.
- **Security:** new endpoint is anonymous static content — strictly read-only, single-segment name, rooted under `_framework`, no directory listing; SRI is preserved because `boot.js` forwards the boot `integrity` hash to the remapped `fetch` (endpoint serves identical bytes).
- **Scope:** no auth/UI changes; no `InvariantGlobalization`; no `_content/*` remapping.
- **Backward compat:** unchanged where no proxy blocks — fallback preserves default behavior.

## 9. Definition of Done

- [x] All requirements (section 4) implemented.
- [x] All acceptance criteria (section 6) covered by passing tests or verified manually (DevTools evidence). *(browser-side criteria verified by live smoke + MS-docs-confirmed API; real-browser proxy smoke pending deploy)*
- [x] Edge cases handled (traversal rejected, fallback works, non-`.js` rule covers all binaries; `%2E%2E` normalizes to `/` → SPA fallback, never binary content — covered by dedicated test).
- [x] `dotnet build`, `dotnet format --verify-no-changes`, `dotnet test` green. *(130 unit + 101 integration, 0 failures)*
- [x] Guardrails respected — endpoint anonymous but traversal-safe; `.github/workflows/` untouched; SRI preserved (integrity hash passed through to the remapped fetch).
- [ ] Manual smoke at `https://rag.afonsoft.dev` behind the corporate proxy: app boots → `/login` → forced change → app usable. *(deployed 2026-09-15 — server-side smoke green; browser-side validation behind the Itaú SWG requires the user's browser; also checks off the pending item in `SPEC-20260914-auth-login` §9)*

## Open Questions / Pending Ambiguity

- Exact filename served by `WebRootFileProvider` for fingerprinted assets in Development vs Publish — T1 confirms; endpoint resolves whatever basename the client sends, so either layout works as long as the file exists under `_framework`.
- Whether the proxy also blocks `.wasm`/`.dll` — unverified; the spec remaps **all** non-`.js` `_framework` assets so the answer does not matter.
