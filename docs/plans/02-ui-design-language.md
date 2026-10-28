# ashopper UI → the abook/nopds design language

## Context
The first scaffold (branch `scaffold`, already committed and pushed) has a generic top-bar UI with hand-written CSS. The user wants ashopper to look like their other apps: abook, nopds, achat, abeat and acopy all share one design language that started in abook.
- **Shell:** collapsible left sidebar (48px icon rail; overlay on phones) with emoji nav icons and no top bar. The footer holds the language select, sign-out and a theme cycle button (system → light → dark).
- **Content:** flat bordered cards (10px radius, no shadow), accent-faint chips and pills, Inter, native `confirm()` and no toasts.
- **Palette:** each app has its own.

Decisions:
- Implement it the **nopds way** (Tailwind v4 `@theme` + `@utility` classes, lucide icons, @fontsource Inter, i18next with en/pl/uk), since nopds is the sibling closest to our stack.
- Use a **new "market" palette:** slate neutrals with an emerald accent.

Reference sources (read-only; port and adapt, don't import):
- `/mnt/git/nopds/src/Nopds.Web/ClientApp/src/index.css`: tokens, `@theme inline`, `@utility` btn/btn-primary/btn-secondary/btn-ghost/btn-danger/input/card/chip/pill-*/link/label/muted/sidebar-btn
- `.../src/components/Layout.tsx`: sidebar shell, localStorage collapse, mobile backdrop, auto-close on navigate
- `.../src/components/ui.tsx`: Spinner, Loading, ErrorBox, Empty, PageTitle, Modal (native `<dialog>`)
- `.../src/hooks/useTheme.ts`: system/light/dark, `.dark` class on `<html>`, theme-color meta
- `.../src/pages/Login.tsx`, `.../src/i18n/*` + `src/locales/*.json` + `scripts/check-locales.mjs`
- `/mnt/git/abook/src/abook-ui/src/index.css`: version pill, table look (`th` uppercase muted .78rem), toggle-switch idea

## Palette (`frontend/src/index.css`)
| token | light | dark |
|---|---|---|
| bg | #f6f7f9 | #0f1419 |
| surface | #ffffff | #18202a |
| surface-2 | #eef1f4 | #212b37 |
| border (line) | #e3e6eb | #2a3441 |
| text (fg) | #16202b | #e6ebf1 |
| text-muted | #5f6b7a | #93a1b1 |
| input-bg | #fbfcfd | #0f1419 |
| accent | #0f8a5f | #34c38f |
| accent-hover | #12a06f | #5ad3a6 |
| accent-contrast | #ffffff | #06281b |
| accent-faint | #0f8a5f1a | #34c38f24 |
| danger / success / warning | #c2362f / #1f8a4c / #b7791f | #f07167 / #6fcf8a / #f2b64f |

- **Chart:** the line uses `accent`; the "lowest" reference line uses `text-muted`. While implementing, run the dataviz validator (`validate_palette.js`) on the accent against both surfaces, light and dark. Adjust the step if contrast fails.
- **Comment header:** same style as nopds ("Layout and components follow ABook…; palette: market — slate and emerald").

## Changes (all in `frontend/`)
1. **Dependencies:**
   - add `tailwindcss`, `@tailwindcss/vite`, `clsx`, `lucide-react`, `@fontsource-variable/inter`, `i18next`, `react-i18next`
   - add the Tailwind plugin to `vite.config.ts`
   - import the font in `main.tsx`
2. **`src/index.css`:** rewrite in nopds structure:
   - `@import 'tailwindcss'`, dark `@custom-variant`, the tokens above, `@theme inline`, the base layer and the same `@utility` set
   - add a few ashopper-specific utilities:
     - `table-head`: abook-style uppercase muted `th`
     - `num`: tabular-nums, right-aligned
     - `segmented` / `segmented-on`: from the nopds ViewToggle
   - chart tooltip styles
3. **`src/hooks/useTheme.ts`:** port from nopds, with key `ashopper.theme`. Call `applyTheme` before render in `main.tsx`. In `index.html`, set `color-scheme` and the two media-specific `theme-color` metas.
4. **`src/components/Layout.tsx`:** port the nopds sidebar shell. It replaces `Shell` in `src/App.tsx`.
   - **Top:** ☰ toggle, "ashopper" and a version pill (`__APP_VERSION__` defined in `vite.config.ts` from package.json).
   - **Nav:** 🛒 Lists, ⚙️ Settings.
   - **"Lists" section:** the user's lists as two-line `SidebarEntry` rows (name + current total), so you can jump between builds. Hidden when collapsed.
   - **Footer:** language select (EN/PL/UK), 🚪 Sign out with the email as title, theme cycle ☀️/🌙/◐.
   - **Behavior:** collapse state in `localStorage['ashopper.sidebar']`, collapsed by default ≤768px, overlay + backdrop on mobile, auto-close after navigation.
   - **Main area:** `max-w-6xl` content width; Settings uses `max-w-3xl`.
5. **`src/components/ui.tsx`:** Spinner, Loading, ErrorBox, Empty, PageTitle (title + actions slot + subtitle), Modal.
   - The requirement editor moves into a `Modal`, the native-dialog pattern from nopds, instead of the inline panel. This gives it room on mobile.
6. **Pages:** restyle with the utilities, keeping the existing data logic and queries untouched.
   - **`LoginPage`:** nopds login card, with a Logo (inline SVG: rounded emerald square + trend line, reusing the current favicon shape), the sign-in/create-account toggle and `btn-primary w-full`.
   - **`ListsPage`:** abook/nopds list cards (`card flex … hover:border-accent`).
     - Each card shows: name; meta pills (market, item count); current total right-aligned; "Open →".
     - The "+ New list" form uses `card p-5`.
     - `/api/lists` doesn't return a total, so the total comes from per-list `current` queries, or the card shows "N items" only.
     - Decision: **show item count only**, to avoid N extra requests. The sidebar list entries likewise show the market only, not the total.
   - **`ListPage`:**
     - `PageTitle`: name, a "Total now" stat block in nopds stat-tile style, and actions (Add item, Delete list in `btn-danger`).
     - Sections, each a `card p-5`: chart with the segmented range control; items as a divided card list (`card divide-y divide-line`), one row per requirement, replacing the table; shops as `chip`s with the active state `bg-accent text-accent-contrast`.
     - Each row: title; spec summary as `pill-muted` chips; matches count; best-offer link plus shop; line price right-aligned; ghost icon buttons (lucide Pencil, Trash2).
   - **`RequirementEditor`:** inside a Modal.
     - Fields use `label`/`input`.
     - Enum values as toggle chips; booleans as a 3-state segmented control (Any/Yes/No).
     - Number ranges as two inputs.
     - The live preview is a divided list with a "Pin" `btn-ghost`.
   - **`SettingsPage`:** nopds settings sections (`section.card p-5` with h2 + muted description). Holds discounts per shop as rows with a `%` input, plus an "Appearance" section with theme and language.
   - **`AccountPages`:** same card as login.
7. **`src/components/PriceChart.tsx`:** keep Recharts. Swap colors to the `var(--accent)`/`var(--text-muted)`/`var(--border)` tokens and restyle the tooltip as a `card` with `shadow-lg`. Keep the table-view toggle (`btn-ghost`).
8. **i18n → i18next** (as in nopds):
   - `src/i18n/index.ts` plus `src/locales/{en,pl,uk}.json`, moving the current dictionaries over and adding Ukrainian.
   - Language detection: saved choice, otherwise the browser language, otherwise en.
   - Replace `useI18n()` with `useTranslation()`. The locale for `Intl` money formatting comes from `i18n.language`.
   - Port `scripts/check-locales.mjs` (all locales have the same keys) and add `npm run lint:i18n`.
9. **Remove:** the old hand-written CSS, the `Shell` in `App.tsx`, and `src/i18n.tsx`.

## Not changed
Backend, API contract, data flow (TanStack Query hooks in `src/queries.ts`, `api/client.ts` auth logic), nginx and Docker setup.

## Verification
1. `npm run lint && npm run lint:i18n && npm run typecheck && npm test && npm run build`, plus updates to the existing vitest tests (specs, client), which are unaffected by styling.
2. `docker compose --profile app up -d --build ui`, then the headless-Chrome CDP screenshot script (`$CLAUDE_JOB_DIR/tmp/screenshot.mjs`, extended) captures:
   - login
   - lists overview
   - list page, light and dark
   - editor modal
   - a 390px-wide mobile viewport, sidebar collapsed and expanded
3. Check the screenshots for label collisions, contrast and overflow. Run the palette validator for the chart accent in both modes.
4. Visual parity check: put an ashopper screenshot next to nopds/abook (same sidebar widths, card radius, button sizes).
5. CI workflow: add the `lint:i18n` step. Then commit on `scaffold` and push.

---

## Outcome (implemented 2026-10-28)
Implemented as planned, with these differences:
- **Dark accent:** changed from `#34c38f` to `#26ab7c`. The dataviz palette validator failed `#34c38f` on the dark chart surface (lightness 0.73, outside the 0.48–0.67 band), while `#26ab7c` passes all checks in dark mode. `#34c38f` is now the dark hover colour. The light accent `#0f8a5f` passes as is.
- **Theme state** is a single shared store (`useSyncExternalStore`), so the sidebar button and the Settings → Appearance control stay in sync. nopds uses per-component state.
- **Phone sidebar** closes from the link click handlers instead of an effect on route change, which fixes the oxlint `set-state-in-effect` warning.
- **Chart Y-axis:** now uses round 1/2/5×10ⁿ steps (`components/chartAxis.ts`) and whole-unit labels. Compact labels (`PLN 5.4K`) produced duplicate ticks.
- **List page:** a "Total now" stat card above the chart. Items are a divided list with spec pills and lucide edit/delete icon buttons. The editor is a native `<dialog>` modal.
- **Screenshots:** in `docs/screenshots/`.
