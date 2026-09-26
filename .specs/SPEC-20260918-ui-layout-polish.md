# SPEC-20260918-ui-layout-polish

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `ui-layout-polish` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly + BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260918-ui-layout-polish` |
| Status | `Done` — implementado em `feature/Devin-20260918-ui-layout-polish`; build/testes/format verdes, publish emite SW assets manifest |

## 1. User Story

**As a** platform administrator
**I want** a polished admin layout — collapsible icon-rail sidebar, consistent button spacing, a coherent color identity, dead UI elements removed, and an installable light-theme PWA
**So that** the admin UI feels intentional instead of templated, works well on desktop and mobile, and can be installed on a phone/desktop like a native app.

**Problem context:** The UI still carries Blazor template leftovers: the sidebar uses the default `rgb(5,39,103) → #3a0647` gradient, every nav item shows the same `bi-list-nested` icon, `NavMenu` contains a duplicate `navbar-toggler` plus an `@onclick="ToggleNavMenu"` on the whole nav container (collapses the menu when clicking empty areas), and the top row hosts a boilerplate "About" link to learn.microsoft.com. Table action buttons (`Sources`, `ApiKeys`) are `ExtraSmall` and cramped with no consistent gap. On desktop the 250px sidebar is always expanded — no rail mode. The app is not installable (no manifest/service worker).

## 2. Scope

**In scope:**
- Collapsible sidebar on desktop: expand (250px, icon + label) ↔ collapse to icon-only rail (~64-72px) with tooltips; toggle button in the sidebar header; state persisted in `localStorage`.
- Mobile (<768px) keeps the existing off-canvas drawer + backdrop behavior (SPEC-20260916).
- Distinct FontAwesome icon per nav item (BootstrapBlazor already loads FA).
- Remove dead UI: internal `NavMenu` toggler + container `ToggleNavMenu` handler; "About" boilerplate link in `top-row`.
- `top-row` repurposed: mobile hamburger + current page title (desktop shows title too, no empty bar).
- Color system: light theme only; replace template gradient with design tokens — dark solid sidebar (slate/navy), single primary accent (`#1b6ec2` family), active item with accent indicator.
- Button spacing normalization: `gap` grid of 8px — `gap-2` for action groups, `gap-1` in dense table cells; icon-only + tooltip for cramped table actions on <576px; touch targets ≥44px preserved.
- PWA (light theme): `manifest.webmanifest`, `service-worker.js` (dev no-op) + `service-worker.published.js` (precache + navigation fallback), `ServiceWorkerAssetsManifest` in csproj, icons 192/512 + maskable, `theme-color`, `apple-touch-icon`, registration in `index.html`.
- Server: add `/service-worker.js` to the existing `no-cache` boot-shell middleware in `Program.cs` (cache-safety only — no API change).

**Out of scope:**
- Dark mode / theme toggle (single light theme only, per user decision).
- Offline data/sync for authenticated API calls — PWA delivers installability + offline app shell; API calls fail gracefully offline.
- Server API surface or business logic changes.
- New pages or features.

## 3. Technical Context

**Where the change happens:** `src/KnowledgeHub.Client` (Razor components + `wwwroot` static assets) and one line in `src/KnowledgeHub.Server/Program.cs` (cache middleware allowlist).

**Files to read before implementing:**
- `src/KnowledgeHub.Client/Layout/MainLayout.razor` + `.razor.css` — page grid, top-row, mobile drawer wiring.
- `src/KnowledgeHub.Client/Layout/NavMenu.razor` + `.razor.css` — nav items, icons, dead toggler.
- `src/KnowledgeHub.Client/wwwroot/css/app.css` — existing mobile rules (lines ~155-287), `.btn` touch-target rule.
- `src/KnowledgeHub.Client/wwwroot/index.html` — head links, boot scripts.
- `src/KnowledgeHub.Client/wwwroot/js/boot.js` — custom `loadBootResource` chain: mirror `/framework-assets/{stem}/{ext}` → b64 → `defaultUri`. **Offline works via the `defaultUri` fallback** — the SW must precache `/_framework/*` assets and must NOT intercept `/framework-assets/*` with stale responses.
- `src/KnowledgeHub.Client/KnowledgeHub.Client.csproj` — add `ServiceWorkerAssetsManifest`.
- `src/KnowledgeHub.Server/Program.cs` lines ~148-167 — boot-shell `no-cache` middleware.
- Pages with cramped action cells: `Pages/Sources.razor` (~L62-77), `Pages/ApiKeys.razor` (~L48-62).
- `.specs/SPEC-20260916-mobile-layout-responsive.md` — prior mobile work (do not regress).

**Files to create or modify:**
```text
modify  src/KnowledgeHub.Client/Layout/MainLayout.razor
modify  src/KnowledgeHub.Client/Layout/MainLayout.razor.css
modify  src/KnowledgeHub.Client/Layout/NavMenu.razor
modify  src/KnowledgeHub.Client/Layout/NavMenu.razor.css
modify  src/KnowledgeHub.Client/wwwroot/css/app.css
modify  src/KnowledgeHub.Client/wwwroot/index.html
modify  src/KnowledgeHub.Client/KnowledgeHub.Client.csproj
modify  src/KnowledgeHub.Client/Pages/Sources.razor      (action-cell flex/gap, icon-only on xs)
modify  src/KnowledgeHub.Client/Pages/ApiKeys.razor      (idem)
modify  src/KnowledgeHub.Server/Program.cs               (no-cache for service-worker.js)
create  src/KnowledgeHub.Client/wwwroot/manifest.webmanifest
create  src/KnowledgeHub.Client/wwwroot/service-worker.js
create  src/KnowledgeHub.Client/wwwroot/service-worker.published.js
create  src/KnowledgeHub.Client/wwwroot/icon-512.png
create  src/KnowledgeHub.Client/wwwroot/icon-maskable-192.png / icon-maskable-512.png
```

## 4. Requirements

### RF-001: Collapsible icon-rail sidebar (desktop ≥768px)
- **Description:** The desktop sidebar must toggle between expanded (250px, icon+label) and collapsed rail (~64-72px, icons vertically centered).
- **Rules:** toggle button in the sidebar header (visible in both states); labels hidden in rail mode; `title`/tooltip shows the label on hover; active item keeps accent indicator; transition ≤250ms using `width`/`transform` only; state persisted in `localStorage` (`kh.sidebar.collapsed`) and restored on load.
- **Input → Output:** click on toggle / stored preference → sidebar width state.

### RF-002: Mobile drawer preserved
- **Rules:** <768px behavior unchanged — hamburger in top-row, slide-over drawer, backdrop closes, nav click closes. The rail toggle must not appear on mobile; rail state must not affect the drawer.

### RF-003: Distinct nav icons + dead code removal
- **Rules:** each `NavLink` gets its own FA icon: Home `fa-house`, Fontes `fa-database`, Monitor MCP `fa-server`, Aprovações `fa-circle-check`, Chat `fa-comments`, Playground `fa-flask`, API Keys `fa-key`, Settings `fa-gear`, Sair `fa-right-from-bracket`. Remove: inner `navbar-toggler` in `NavMenu`, `collapseNavMenu`/`ToggleNavMenu`/`NavMenuCssClass`, the `@onclick="ToggleNavMenu"` on `nav-scrollable`, unused `bi-*` CSS icon classes. Remove the "About" link from `top-row`.
- **Input → Output:** — → cleaner DOM, per-item icons.

### RF-004: Top-row purpose
- **Rules:** `top-row` shows the mobile hamburger (`d-md-none`) and the current page title on all viewports (title derived from `NavigationManager.Uri` → friendly map; unknown routes show "Knowledge"). No dead/empty elements.

### RF-005: Color tokens (light theme)
- **Rules:** CSS custom properties in `app.css` `:root`: `--kh-sidebar-bg` (dark slate, e.g. `#0f172a`), `--kh-sidebar-fg`, `--kh-accent` (`#1b6ec2`), `--kh-accent-contrast`, `--kh-surface`, `--kh-border`. Sidebar uses solid `--kh-sidebar-bg` (gradient removed); active nav item = accent-tinted bg + left indicator; hover/focus states defined; focus ring preserved; WCAG AA contrast for nav text vs sidebar bg (≥4.5:1).
- **Input → Output:** — → coherent palette replacing template gradient.

### RF-006: Button spacing normalization
- **Rules:** action groups use `d-flex gap-2 flex-wrap`; dense table action cells (`Sources`, `ApiKeys`) wrapped in `d-flex gap-1 flex-wrap` (or equivalent `.kh-actions` class); on <576px crowded row actions may render icon-only with `title` tooltip while keeping ≥44px targets (min-height/min-width preserved from existing rule); no adjacent buttons without gap anywhere in the app.
- **Input → Output:** — → consistent 8px rhythm.

### RF-007: PWA installability + offline shell
- **Rules:**
  - `manifest.webmanifest`: `name` "Knowledge MCP Hub", `short_name` "Knowledge MCP Hub", `start_url` "/", `display` "standalone", `background_color`/`theme_color` matching light theme + sidebar accent, icons `icon-192.png`, `icon-512.png`, maskable variants (`purpose: "maskable"`).
  - `service-worker.js`: dev no-op fetch listener (no caching in dev).
  - `service-worker.published.js`: precache everything in `service-worker-assets.js`; cache-first for fingerprinted/GET static assets; navigation requests (`mode === 'navigate'`) → cached `index.html` fallback.
  - **Network-only (never cached/intercepted):** `/api/*`, `/hubs/*`, `/mcp*`, `/health/*`, `/framework-assets/*` — pass straight through to `fetch`.
  - `KnowledgeHub.Client.csproj`: `<ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>`.
  - `index.html`: `<link rel="manifest">`, `<meta name="theme-color">`, `<link rel="apple-touch-icon">`, SW registration guarded to published environments or harmless in dev (dev SW is a no-op).
  - `Program.cs`: add `/service-worker.js` to the `no-cache` boot-shell list.
- **Input → Output:** `dotnet publish` → installable PWA; offline launch shows app shell + login page (API calls fail gracefully).

### RF-008: No regressions
- **Rules:** `dotnet build` 0 warnings; `dotnet test` green; `dotnet format --verify-no-changes` clean; no horizontal scroll ≥320px on any page; existing mobile behaviors from SPEC-20260916 intact.

**Business rules / invariants:**
- Single light theme — no dark-mode assets or toggles.
- Security-sensitive table actions (revoke/delete) remain visible on mobile.
- SW must never cache authenticated API responses.

## 5. API Contract

Not applicable — no API changes. (Server change is cache-header middleware only.)

## 6. Acceptance Criteria

- [x] **Given** a desktop viewport (1280px) **when** I click the sidebar collapse toggle **then** the sidebar shrinks to an icon-only rail, labels hide, and tooltips appear on hover.
- [x] **Given** the rail is collapsed **when** I reload the page **then** it renders collapsed again (localStorage).
- [x] **Given** a 375px viewport **when** I open the menu **then** the off-canvas drawer + backdrop behave exactly as today (rail toggle hidden).
- [x] **Given** any admin page **when** rendered **then** no "About" link, no dead toggler, and every nav item shows a distinct icon.
- [x] **Given** `/sources` and `/api-keys` at 375px **when** I inspect action cells **then** buttons have ≥8px separation (or icon+tooltip) and ≥44px touch targets; revoke/delete remain reachable.
- [x] **Given** the sidebar **when** rendered **then** it uses the solid slate token (no template gradient) and nav text passes WCAG AA contrast.
- [x] **Given** `dotnet publish` output served over HTTPS **when** I open the app in Chrome **then** the install prompt is available and Lighthouse PWA installability checks pass.
- [x] **Given** the installed app offline **when** launched **then** the app shell + login screen render (no white screen); API errors surface as normal errors, not crashes.
- [x] **Given** DevTools network **when** calling `/api/*`, `/hubs/*`, `/health/*` **then** requests are never served from the service-worker cache.

**Edge cases:**

| Scenario | Input | Expected behavior |
| --- | --- | --- |
| localStorage unavailable | private mode | sidebar defaults to expanded, no crash |
| First visit (no SW yet) | fresh profile | normal online render; SW installs in background |
| Update deploy | new fingerprints | new SW precaches new assets; old cache cleaned on activate |
| Offline API call | any `/api/*` fetch | app surfaces existing error handling (toast/alert), SW does not fabricate responses |
| Nav between desktop breakpoints | resize 767↔768px | drawer state resets; rail state persists only ≥768px |

## 7. Task Plan

- [x] **T1 — Discovery:** read section-3 files; confirm current mobile drawer works; inventory nav items.
- [x] **T2 — NavMenu + layout:** dead-code removal, FA icons, rail toggle + collapse state + localStorage, top-row title + About removal.
- [x] **T3 — CSS tokens:** color tokens, sidebar solid bg, rail width, tooltips, button spacing utilities; mobile rules untouched.
- [x] **T4 — Table actions:** flex/gap wrappers in `Sources`/`ApiKeys`; icon-only+tooltip on xs where needed.
- [x] **T5 — PWA:** manifest, icons (512 + maskable generated from `icon-192.png` source), service workers, csproj property, index.html links/registration, `Program.cs` no-cache addition.
- [x] **T6 — Verification:** `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`; manual check at 375px/768px/1280px; `dotnet publish` → SW assets manifest generated.
- [x] **T7 — Done + PR:** DoD complete → `Status = Done` → open PR.

**7.1 Validation strategy (Frontend/.NET):**
- `dotnet build KnowledgeHub.slnx` — 0 warnings.
- `dotnet test` — full suite green (no new tests needed for pure CSS/Razor markup; PWA files verified via publish output).
- `dotnet format KnowledgeHub.slnx --verify-no-changes`.
- `dotnet publish -c Release` → `service-worker-assets.js` exists and lists assets.
- Manual/mobile viewport checks per section 6.

## 8. Organization Guardrails

- **Branches:** never commit to `main`/`master`/`develop` — use `feature/Devin-20260918-ui-layout-polish`.
- **Workflows:** `.github/workflows/` untouched (protected).
- **Secrets:** none involved.
- **Scope:** visual/PWA only — no API surface, no dark mode, no invented features.
- **Architecture:** changes confined to client presentation + one cache-header line in server middleware.

## 9. Definition of Done

- [x] All RF-001…RF-008 implemented.
- [x] All acceptance criteria verified at 375px / 768px / 1280px.
- [x] Edge cases handled.
- [x] `dotnet build` clean, `dotnet test` green, `dotnet format --verify-no-changes` pass.
- [x] `dotnet publish` produces `service-worker-assets.js` and a valid manifest.
- [x] No dead UI elements remain; guardrails respected.

## Open Questions / Pending Ambiguity

- [A DEFINIR] Sidebar tone: assumed **dark solid slate inside the single light theme** (approved Q4). If "tema claro" was meant to include a light sidebar, switch `--kh-sidebar-bg`/fg tokens — no structural change needed.
- [A DEFINIR] Icon-512 artwork: will be generated by upscaling/recreating `icon-192.png`; replace later if a proper brand asset exists.
