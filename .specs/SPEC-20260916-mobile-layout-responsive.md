# SPEC-20260916-mobile-layout-responsive

## 0. Metadata

| Field | Value |
| --- | --- |
| Feature | `mobile-layout-responsive` |
| Type | `Frontend` |
| Stack | `.NET 10 / Blazor WebAssembly + BootstrapBlazor` |
| Repository | `afonsoft/LangGraph-UI` |
| Branch | `feature/Devin-20260916-mobile-layout-responsive` |
| Status | `Done` — entregue em `main` (`0107c70`); `MainLayout`/`NavMenu` responsivos, `app.css` breakpoints e páginas ajustadas |

## 1. User Story

**As a** platform administrator
**I want** the Knowledge MCP Hub admin UI to work properly on mobile devices (phones and tablets)
**So that** I can manage sources, API keys, settings, and use the chat interface from any device without horizontal scrolling or broken layouts.

**Problem context:** The current Blazor WASM admin UI is desktop-first. On mobile (viewport < 768px): the sidebar navigation is always visible and consumes screen space, BootstrapBlazor tables overflow horizontally, the chat layout stacks poorly with the thread list consuming full height, form layouts use multi-column grids that become unreadable, and touch targets are too small. This makes the admin UI practically unusable on phones.

## 2. Scope

**In scope:**
- Collapsible sidebar (`NavMenu`) that hides behind a hamburger toggle on screens < 768px, with overlay backdrop when open.
- Responsive `MainLayout` that switches from sidebar+main to full-width stacked layout on mobile.
- Table overflow handling on `/sources`, `/api-keys`, `/approvals`, `/mcp-monitor` — horizontal scroll containers or card-based mobile views.
- Chat page (`/chat`) mobile layout: collapsible thread drawer (slide-over) or stacked threads-above-messages layout on small screens.
- Settings page (`/settings`) form layout: single-column stacking for all form groups on mobile.
- Touch target sizing: all buttons, links, and interactive elements ≥ 44×44 CSS pixels.
- Viewport meta tag verification and `max-width: 100%` enforcement on images, tables, and code blocks.
- CSS custom properties for breakpoint tokens to ensure consistency.
- BootstrapBlazor `Table` responsive mode configuration (when available) or custom CSS wrappers.

**Out of scope:**
- PWA/offline support (separate feature).
- Native mobile app.
- Dark mode (separate feature).
- Changes to server API or business logic.

## 3. Related SPECs

- SPEC-20260913-blazor-admin-ui — base admin UI structure.
- SPEC-20260914-conversation-threads — chat thread UI.
- SPEC-20260916-settings-chat-config — settings form layout.

## 4. Existing Code

The UI uses BootstrapBlazor components with custom CSS in `src/KnowledgeHub.Client/wwwroot/css/app.css`. Key files:
- `Layout/MainLayout.razor` — sidebar + main content grid.
- `Layout/NavMenu.razor` — vertical nav with `navbar-toggler` already present but CSS may not collapse properly.
- `Pages/Chat.razor` — two-column layout (`col-12 col-lg-3` / `col-12 col-lg-9`).
- `Pages/Settings.razor` — two-column form grid (`col-12 col-md-7` / `col-12 col-md-5`).
- `Pages/ApiKeys.razor`, `Pages/Sources.razor`, `Pages/Approvals.razor`, `Pages/McpMonitor.razor` — all use `Table` components.

## 5. Functional Requirements

### RF-001: Collapsible Mobile Navigation

On viewports < 768px:
- The sidebar (`div.sidebar`) must be hidden by default.
- The `navbar-toggler` button in `NavMenu.razor` must toggle the sidebar visibility.
- When open, the sidebar must appear as an overlay above the main content with a semi-transparent backdrop.
- Clicking the backdrop or a nav link must close the sidebar.
- The sidebar width must be 260px (same as desktop) when open.
- Transition: 250ms ease-in-out for slide+fade.

### RF-002: Responsive Main Layout

- On viewports ≥ 768px: existing sidebar+main layout is preserved.
- On viewports < 768px: main content takes 100vw width, padding reduced to `px-2 py-2`.
- The `top-row` header in `MainLayout` must remain visible and usable on mobile (links wrap or hide non-essential items).

### RF-003: Table Overflow Handling

- All pages using `Table<TItem>` must wrap the table in a `.table-responsive` container on mobile.
- Minimum visible columns on small screens: Name + Actions; other columns can be hidden via `Visible` parameter or CSS `d-none d-md-table-cell`.
- On screens < 576px: consider switching from table view to card/list view for better readability (optional enhancement, table-responsive is minimum).
- Touch scrolling on tables must work smoothly (no interference with sidebar swipe).

### RF-004: Chat Page Mobile Layout

- On viewports < 992px (lg breakpoint): the thread list (`col-12 col-lg-3`) and chat area (`col-12 col-lg-9`) must stack vertically.
- The thread list must collapse to a top bar with a dropdown/sheet pattern, or become a slide-over drawer triggered by a "Threads" button.
- The message input area must remain fixed at the bottom with adequate padding for mobile keyboards.
- Message bubbles must use 90% width on mobile (vs. 70% on desktop).

### RF-005: Settings Form Mobile Layout

- All form groups in `/settings` must use `col-12` on mobile; the `col-md-*` breakpoints are acceptable for ≥ 768px.
- Input fields must be full-width on mobile.
- Labels and helper text must not overflow; use `text-wrap`.
- Save/Remove buttons must stack vertically on mobile with full width.

### RF-006: Touch Target Sizing

- All buttons, nav links, table action buttons, and form controls must have a minimum touch target of 44×44 CSS pixels.
- Table row action buttons (`Size="Size.ExtraSmall"`) may need `Size.Small` on mobile or increased padding.
- Popconfirm buttons must not be too close to each other; minimum 8px gap.

### RF-007: Viewport and Overflow Prevention

- `app.css` must enforce `max-width: 100%` on `img`, `table`, `pre`, `code` blocks.
- No horizontal page scroll must occur on any admin page at viewport ≥ 320px.
- The viewport meta tag `<meta name="viewport" content="width=device-width, initial-scale=1.0">` must be present in `index.html`.

## 6. Non-Functional Requirements

### RNF-001: Performance
- CSS transitions must use `transform` and `opacity` only (GPU-accelerated).
- No JavaScript-based layout thrashing; use CSS media queries.

### RNF-002: Accessibility
- Mobile nav toggle must have `aria-label="Toggle navigation"` and `aria-expanded` state.
- Focus trap in mobile sidebar when open (or at minimum, focus moves into sidebar on open).
- All interactive elements must remain keyboard-accessible on mobile (relevant for Bluetooth keyboards).

### RNF-003: Browser Support
- Chrome/Edge/Safari/Firefox latest 2 versions on Android and iOS.
- iOS Safari bottom-safe-area support (`env(safe-area-inset-bottom)` for fixed bottom elements).

## 7. API / Data Contracts

No server API changes required. All changes are client-side CSS/Blazor.

## 8. UI/UX

### Mobile Navigation States
```
[Closed]  Hamburger icon visible in top bar. Sidebar hidden.
[Open]    Sidebar slides in from left (260px). Backdrop rgba(0,0,0,0.4).
          Backdrop click → close. NavLink click → close.
```

### Breakpoint Tokens (CSS custom properties in app.css)
```css
:root {
  --kh-breakpoint-sm: 576px;
  --kh-breakpoint-md: 768px;
  --kh-breakpoint-lg: 992px;
}
```

### Page-Specific Mobile Behaviors

| Page | Mobile Behavior |
| --- | --- |
| `/sources` | Table wrapped in `.table-responsive`; Type/Active/LastSync columns hidden on < 576px |
| `/api-keys` | Table responsive; Prefix/Created columns hidden on < 576px; actions stacked |
| `/approvals` | Table responsive; Status/RequestedAt columns hidden on < 576px |
| `/chat` | Threads as top collapsible section or slide-over; messages full-width |
| `/settings` | Single column forms; integration cards stack vertically |
| `/playground` | Search results stack vertically; raw JSON in full-width expander |
| `/mcp-monitor` | Activity log table responsive; session list becomes cards or horizontal scroll |

## 9. Security

- No security implications; this is purely presentational.
- Ensure that hiding columns on mobile does not hide security-sensitive actions (revoke/delete must remain visible).

## 10. Data Model / Persistence

No database changes.

## 11. Acceptance Criteria

### AC-001: Mobile Navigation
```gherkin
Given I am on any admin page
When I resize the browser to 375px width (or use a phone)
Then the sidebar is hidden
And I see a hamburger menu button in the top bar

When I click the hamburger button
Then the sidebar slides in from the left
And a dark backdrop appears behind it

When I click the backdrop or a nav link
Then the sidebar closes
```

### AC-002: No Horizontal Scroll
```gherkin
Given I am on /sources, /api-keys, /chat, /settings, /approvals, /mcp-monitor, /playground
When I view the page at 375px width
Then no horizontal scrollbar appears
And all content fits within the viewport
```

### AC-003: Touch Targets
```gherkin
Given I inspect any interactive element on mobile
Then its computed height and width are both ≥ 44px
Or its padding ensures a 44px hit area
```

### AC-004: Chat Usability
```gherkin
Given I am on /chat on a 375px wide screen
When the page loads
Then the thread list is collapsed or shown as a compact top section
And the message input is visible and tappable
And I can send a message without zooming
```

### AC-005: Settings Forms
```gherkin
Given I am on /settings on a 375px wide screen
Then all form inputs are full-width
And labels do not overflow
And save/remove buttons are stacked and full-width
```

## 12. Test Strategy

- **Manual:** Test on real iOS Safari and Chrome Android devices (or BrowserStack).
- **Automated:** Add Playwright tests with mobile viewport (375×667 and 768×1024) verifying no horizontal overflow and nav toggle works.
- **Accessibility:** axe-core scan on mobile viewport.

## 13. Rollout

1. Merge CSS changes first (no Blazor changes).
2. Merge layout component changes (`MainLayout`, `NavMenu`).
3. Merge page-specific changes per page (can be parallel).
4. No migration or data changes needed.

## 14. Risks

| Risk | Mitigation |
| --- | --- |
| BootstrapBlazor Table responsive mode may conflict with custom CSS | Test thoroughly; prefer BootstrapBlazor native props when available |
| iOS Safari bottom bar overlaps fixed input | Use `env(safe-area-inset-bottom)` padding |
| Sidebar backdrop click not working in WASM | Use `@onclick` with `StopPropagation` carefully |

## 15. Open Questions

- [A DEFINIR] Should we switch tables to card view on mobile, or is horizontal-scroll table sufficient?
- [A DEFINIR] Do we need a bottom navigation bar for the most-used pages on mobile (like a mobile app tab bar)?
