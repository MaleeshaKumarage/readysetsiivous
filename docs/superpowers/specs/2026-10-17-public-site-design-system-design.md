# Public website design system refresh

## Goal

Apply the Asset Avenue / Readyset Siivous design guide as the single visual system for the public marketing website. The admin portal, employee and manager screens, authenticated shift UI, quality-cycle workflow, and language game are out of scope.

## Source of truth

The design guide at `https://asset-avenue-dev.lovable.app/` defines the public-site palette, typography, logo rules, layout, component styling, motion, and accessibility states. Official assets already live in `site/public/brand/`.

## Theme tokens

Use public-site semantic tokens so light and dark mode share component code. Keep these tokens scoped to the public layout or public component variants because the authenticated screens share some `ds` components. Do not change authenticated visual output as a side effect.

| Role | Light | Dark |
| --- | --- | --- |
| background | `#F7F4ED` | `#0B1330` |
| card | `#FFFFFF` | `#14214D` |
| foreground | `#14214D` | `#F7F4ED` |
| muted-foreground | `#5A6383` | `#A9B0C8` |
| border | `#E4DFD3` | `#26335E` |
| primary | `#14214D` | `#C9A24A` |
| primary-foreground | `#FFFFFF` | `#14214D` |
| accent | `#C9A24A` | `#E0BE6E` |
| accent-foreground | `#14214D` | `#0B1330` |

Map the roles to the existing Tailwind/CSS variable contract (`background`, `foreground`, `card`, `card-foreground`, `muted-foreground`, `border`, `primary`, `primary-foreground`, `accent`, `accent-foreground`, `ring`, and `input`). Use one color representation consistently in the public stylesheet; do not mix complete `oklch(...)` values with variables wrapped in `hsl(...)`. Existing public components must consume these semantic roles instead of raw gray/indigo values where practical. Status colors remain success `#2F8F5B`, warning `#D9952B`, error `#C2412D`, and info `#3B5BA9`.

## Typography and layout

- Load Outfit 400/600/700 for headings and Work Sans 400/500 for body text.
- Preserve existing multilingual content and route structure (`fi`, `en`, `sv`).
- Use a maximum content width of 1200px, a 12-column layout where applicable, and 24px desktop gutters.
- Use the spacing scale 4, 8, 16, 24, 40, 64, and 96px.
- Use 16px cards, full-round buttons, and the guide's 150/250/400ms motion timing.

## Components and page treatment

- Shared public navigation, footer, buttons, cards, forms, badges, headings, theme toggle, hero, service sections, testimonials, FAQ, quote form, and contact sections adopt the tokens. If a shared `ds` component is used by an excluded route, add a public variant or public-scoped class rather than changing the authenticated default.
- The hero and CTA bands may use the official brand pattern behind navy surfaces at 20–40% opacity, with content remaining readable.
- The primary logo is used in hero/footer/document contexts; the logomark is used in the header and compact contexts; dark surfaces use the white-backed badge where the navy mark would lose contrast.
- Buttons are 48px tall, fully rounded, and use gold sparingly as the primary light-mode accent and primary dark-mode action.
- Focus, hover, loading, validation, and error states must remain visible in both themes.

## Scope boundaries

Public route coverage includes `/`, `/fi/`, `/en/`, `/sv/`, localized booking routes, privacy and terms pages, and localized digital business card routes. Customer-facing `/quality-cycle/` and signing routes remain excluded unless explicitly included in a later design pass. Do not change authenticated product workflows, API contracts, permissions, game behavior, or admin navigation. Do not rewrite copy or translation content unless needed to correct a visual component integration.

## Validation

- TypeScript check and production build for `site`.
- Playwright smoke coverage for all listed public routes in all three languages, theme switching, navigation, quote form, booking flow, FAQ, and responsive layouts. Preserve and run the existing `public-accessibility.spec.ts` checks in both themes.
- WCAG AA contrast for text and controls in both themes, keyboard operation with visible focus indicators, accessible status-color pairings that do not rely on color alone, and reduced-motion behavior for transitions and patterned sections.
- Visual review at mobile and desktop widths in light and dark mode, including excluded authenticated routes to verify their appearance did not change.
- Verify all logo and pattern assets load from `/brand/` and no public component references retired logo assets.
