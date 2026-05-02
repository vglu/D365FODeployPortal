# UI Patterns — D365FO Deploy Portal

**Owner:** EPIC-UI-OVERLAP-FIX, Wave 2.2
**Audience:** anyone touching `src/DeployPortal/Components/`

This document codifies which UI pattern to use for which UX problem in this project. The rules here exist because we previously had **three different master-detail patterns** for the same conceptual job, which forced users to learn each page individually. The cost of that drift is real; the cost of one extra rule is small.

If you find yourself reaching for a pattern not listed here, prefer to extend an existing one rather than introduce a new one.

---

## 1. Master-detail patterns

Three patterns, picked by **the purpose of the detail view**, not by personal taste.

### 1.1 Inline expansion (`<ChildRowContent>` in `<MudTable>`)

**Use when:** the detail is *supplementary* to the parent row — read-only context that helps interpret the row but is not the user's main focus. The parent row must stay visible while the detail is open.

**Examples in this project:**
- `Packages.razor` — Merged-package sources (line ~224). Expanding a row reveals "Merged from: A, B" without leaving the list.

**Visual rules:**
- Use the `.master-detail-row` CSS class (defined in `wwwroot/app.css`). It anchors the child block to the parent with a 3px left accent bar and a faint inset background — no margin gap that makes the detail "fall out" of the row.
- Never put a `<MudPaper Class="ma-2">` directly inside the `<td>`. The margin creates visible whitespace that breaks the parent→child link.
- Do not nest a `<MudTable>` inside the expanded row. If you need a tabular detail, use a separate route (1.2).

**Don't use when:** the detail has its own URL, live data, or actions. Use 1.2.

### 1.2 Separate route (`@page "/<plural>/{Id:int}"`)

**Use when:** the detail is *the primary surface* of its own — has a URL, can be shared / refreshed / bookmarked, hosts live data (SignalR), or has its own action set.

**Examples in this project:**
- `DeploymentDetail.razor` (`/deployments/{id}`) — live deployment log, status summary, cancel-action. Linked from `Deployments.razor` row click.

**Layout rules:**
- Use `<MudGrid Spacing="3">` with two `<MudItem>`s that **both carry weight** — never leave one half empty. If you only have data for one column, use a single `<MudItem xs="12">` instead.
- Suggested split:
  - **Info table** on the left, `xs="12" md="7"` — facts, IDs, timestamps, error text.
  - **Summary card** on the right, `xs="12" md="5"` — status icon, key metrics (duration, counts), inline alerts (e.g. "3 errors in log").
- Below the grid, full-width content (logs, large detail) goes inside `<MudPaper Class="code-surface pa-3">` (or another themed surface). Use `clamp(...)` for height, not hard-coded pixels.

**Don't use when:** the detail is read-only and short. Use 1.1.

### 1.3 Dialog (`<MudDialog>`)

**Use when:** the user is **committing or cancelling a form** — a discrete edit/create action that should block other interaction until they choose.

**Examples in this project:**
- `Environments.razor` — Add / Edit environment, Import from Script, Import backup
- `Packages.razor` — Merge name dialog, Load from Build dialog
- `MainLayout.razor` — About dialog

**Sizing rules:**
- `<DialogOptions MaxWidth="MaxWidth.Small" FullWidth="true">` — single field, simple confirm
- `<DialogOptions MaxWidth="MaxWidth.Medium" FullWidth="true">` — multi-step, multi-field forms (Merge, Release Pipeline)
- `<DialogOptions MaxWidth="MaxWidth.Large" FullWidth="true">` — instructional / read-mostly content (Setup-ServicePrincipal guide, View License)
- Always set `CloseOnEscapeKey="true"` for read-only dialogs (About, View License). For destructive forms (Merge, Delete), default behavior (Esc not bound) is fine.

**Don't use when:** the detail is read-only context (use 1.1) or has its own URL (use 1.2). Don't use a dialog as a "secondary page" — that's what 1.2 is for.

---

## 2. Color semantics

`MudBlazor.Color` enum mapped to intent. **Do not** use a color just because it "looks nice" — it carries meaning to repeat users.

| Color | Intent | Examples |
|---|---|---|
| **Primary** | Main action / primary status | Save, Deploy, Merge button, Selected-row chip |
| **Success** | Successful state | Deployment success, Unified package type, OK indicators |
| **Error** | Failure / destructive | Delete, Failed deployment, Validation errors |
| **Warning** | Needs attention / synthetic | Merged package type (review before deploy), interactive sign-in alerts, "Not configured" text |
| **Info** | Informational badges / supplementary icons | Package format chips, Setup-script icons, "update credentials" badge |
| **Default** | Neutral / passive helper text | Caption labels, helper text, Interactive-auth chip |
| **Inherit** | Component should inherit from its container | Icons inside AppBar (inherit white), nav links |

**Do not use:**
- `Color.Secondary` — was abused as a generic accent. Today: only valid use is in `MudTimelineItem` / `MudRadio` where three sequential steps need three distinct colors. Everywhere else: pick one of the seven above.
- `Color.Tertiary` — same as Secondary, plus rarely meaningful. Removed from this codebase except sequential-step usages.

If a new use case doesn't fit, add a row to this table in the same PR — don't silently introduce a new convention.

---

## 3. Spacing

MudBlazor's 4px-grid utility classes. **Primary scale (preferred):**

| Class | Value | When |
|---|---|---|
| `pa-2` / `ma-2` / `my-2` | 8px | Dense table cells, inline chips, internal stack spacing |
| `pa-4` / `ma-4` / `my-4` | 16px | Standard block padding (paper, dialog content), section margins |
| `pa-6` / `ma-6` / `my-6` | 24px | Generous block separation between major sections |

**Tolerated transitional values:**

`pa-3` / `mb-3` / `mt-3` (12px) appear in ~70 places across the codebase as "between dense and standard". W3.6 (2026-05-02) decided **not** to blanket-replace these because each spot needs visual review and most are legitimate (caption-to-caption gaps, switch-to-helper-text). When you touch one of these in the course of other work, prefer migrating to `pa-2` or `pa-4` whichever looks closer in the browser. Don't migrate blindly.

**Avoid in new code:**

`pa-5` / `pa-7` / `pa-8` / `mb-5` etc. — these are the in-between values that have no current legitimate use. If you find yourself reaching for one, the answer is almost always `pa-4` or `pa-6`.

`pa-0` is occasionally needed (table cells with custom padding inside) — that's fine.

---

## 4. Typography

Four levels in this project. **No** `subtitle1` / `subtitle2` / `h1` / `h2` / `h3`.

| Typo | Use |
|---|---|
| `Typo.h4` | Page-level title (one per page) |
| `Typo.h5` | Section / dialog header |
| `Typo.h6` | Sub-section header, table toolbar title |
| `Typo.body1` | Default paragraph text |
| `Typo.body2` | Secondary paragraph (table cells, descriptions) |
| `Typo.caption` | Labels, helper text, metadata |

For sizes that don't map to a `Typo` value (chip metadata, table-aux text, code), use the utility classes from `wwwroot/css/tokens.css`:
- `.dp-text-chip-meta` (0.7rem) — version chips
- `.dp-text-table-aux` (0.85rem) — secondary table-cell text (URLs, hashes)
- `.dp-font-mono` + `.dp-text-table-aux` — short monospace inside table cells
- `.code-block` — multi-line code paper (commands, scripts)
- `.code-surface` — themed log-viewer surface (DeploymentDetail)

Do not write `style="font-size: 0.85rem"` directly. If you need a new size, add a token to `tokens.css` and a row to this table.

---

## 5. Tables

| Property | Default for this project |
|---|---|
| `Dense` | `true` (it's an admin tool, density wins) |
| `Hover` | `true` |
| `FixedHeader` | `true` (when scrollable) |
| `Striped` | `false` |
| `Breakpoint` | `Breakpoint.Sm` (collapse columns on small screens). Don't use `Breakpoint.None` unless you've confirmed the table fits on every supported viewport without horizontal scroll. |
| `Elevation` | `2` for primary tables (one per page), `0` for nested / secondary tables |
| `Height` | `clamp(min, calc(...), max)` for scrollable tables. Never a single `Height="460px"` — it doesn't adapt. |

---

## 6. Where rules live

- **CSS utility classes:** `src/DeployPortal/wwwroot/css/tokens.css`
- **Component-shape CSS:** `src/DeployPortal/wwwroot/app.css` (e.g. `.master-detail-row`, `.code-surface`, `.nav-menu-flex`)
- **This document:** `documents/UI-PATTERNS.md` — update in the same PR that introduces a new pattern or token

If `app.css` and `tokens.css` start contradicting each other, `tokens.css` wins (it's the source of truth for tokens). `app.css` is for component-shape selectors only.
