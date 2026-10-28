# Sibling apps' UI design (survey, 2026-10-28)

ashopper follows the design language shared by the user's other apps. This is what they have in common, so it can be kept consistent.

## Shared design language ("the ABook template")
Used by **abook** (origin), **nopds**, **achat**, **abeat**, **acopy**.

- **Shell:**
  - Left sidebar and no top bar. 48px icon rail when collapsed, `min(75vw, 240px)` when expanded.
  - Collapse state is kept in localStorage. It defaults to collapsed on phones (≤768px), where the expanded sidebar overlays the page with a shadow and backdrop.
  - Top: ☰ toggle, app name and a version pill. Middle: emoji nav icons in a 22px slot. Footer, behind a border: language, login/logout, and a theme button that cycles system → light → dark.
- **Tokens:** `--bg / --surface / --surface-2 / --border / --text / --text-muted / --input-bg / --accent / --accent-hover / --accent-faint / --danger / --success / --warning`. **Each app picks its own palette.**
- **Type:** Inter. Page h1 is ~1.4rem bold. Section titles are .7rem, uppercase, letter-spaced and muted.
- **Components:**
  - Flat bordered cards (10px radius, no shadow), with the border turning accent on hover for clickable cards.
  - Pills and chips on an accent-faint background.
  - Buttons: primary, secondary (outline), ghost and danger (tinted).
  - Inputs: 6px radius, inside or under a muted label.
- **Interaction:** native `confirm()`, no toasts, and inline "Saved!" or error text. Empty and loading states are centered muted text.
- **Content width:** about 6xl for main pages and about 3xl for settings.

## Implementations
| App | Stack | Palette |
|---|---|---|
| abook | plain CSS, `[data-theme]` | slate + indigo #6366f1, dark default |
| nopds | **Tailwind v4** (`@theme inline` + `@utility`), `.dark` class, lucide-react, @fontsource Inter, i18next (en/uk/pl/de), react-query | warm paper/brass: #8f4f17 / #d19a4f |
| achat | plain CSS | zinc + teal #0d9488 / #14b8a6 |
| abeat | plain CSS | Beat Saber blue #00a3ff / red #ff1f4b |
| acopy | plain CSS | copper #e2834c + patina #3fae9f |
| adnd | MUI 7 (different language) | violet #7c3aed |

## Key reference files
- nopds: `src/Nopds.Web/ClientApp/src/index.css`, `src/components/Layout.tsx`, `src/components/ui.tsx`, `src/hooks/useTheme.ts`, `src/pages/Login.tsx`, `scripts/check-locales.mjs`
- abook: `src/abook-ui/src/index.css`, `src/components/Sidebar.tsx`, `src/hooks/useTheme.ts`

## ashopper's choice
- Follow the nopds implementation (Tailwind v4), since its stack matches ashopper's.
- Use its own **"market"** palette: slate neutrals with an emerald accent (#0f8a5f light / #26ab7c dark, both checked with the dataviz palette validator). Green suits a "price dropped, buy now" app.
