# Public website design system refresh

## Goal

Apply the Asset Avenue / Readyset Siivous design guide as the single visual system for the public marketing website. The admin portal, employee and manager screens, authenticated shift UI, and language game are out of scope.

## Source of truth

The design guide at `https://asset-avenue-dev.lovable.app/` defines the public-site palette, typography, logo rules, layout, component styling, motion, and accessibility states. Official assets already live in `site/public/brand/`.

## Theme tokens

Use semantic tokens so light and dark mode share component code:

| Role | Light | Dark |
| --- | --- | --- |
| background | `#F7F4ED` | `#0B1330` |
| card | `#FFFFFF` | `#14214D` |
| foreground | `#14214D` | `#F7F4ED` |
| muted | `#5A6383` | `#A9B0C8` |
| border | `#E4DFD3` | `#26335E` |
| primary | `#14214D` | `#C9A24A` |
| on-primary | `#FFFFFF` | `#14214D` |
| accent | `#C9A24A` | `#E0BE6E` |
| on-accent | `#14214D` | `#0B1330` |

Existing public components must consume these semantic roles instead of raw gray/indigo values where practical. Status colors remain success `#2F8F5B`, warning `#D9952B`, error `#C2412D`, and info `#3B5BA9`.

## Typography and layout

- Load Outfit 400/600/700 for headings and Work Sans 400/500 for body text.
- Preserve existing multilingual content and route structure (`fi`, `en`, `sv`).
- Use a maximum content width of 1200px, a 12-column layout where applicable, and 24px desktop gutters.
- Use the spacing scale 4, 8, 16, 24, 40, 64, and 96px.
- Use 16px cards, full-round buttons, and the guide's 150/250/400ms motion timing.

## Components and page treatment

- Shared public navigation, footer, buttons, cards, forms, badges, headings, theme toggle, hero, service sections, testimonials, FAQ, quote form, and contact sections adopt the tokens.
- The hero and CTA bands may use the official brand pattern behind navy surfaces at 20–40% opacity, with content remaining readable.
- The primary logo is used in hero/footer/document contexts; the logomark is used in the header and compact contexts; dark surfaces use the white-backed badge where the navy mark would lose contrast.
- Buttons are 48px tall, fully rounded, and use gold sparingly as the primary light-mode accent and primary dark-mode action.
- Focus, hover, loading, validation, and error states must remain visible in both themes.

## Scope boundaries

Do not change authenticated product workflows, API contracts, permissions, game behavior, or admin navigation. Do not rewrite copy or translation content unless needed to correct a visual component integration.

## Validation

- TypeScript check and production build for `site`.
- Playwright smoke coverage for public routes in all three languages, theme switching, navigation, quote form, booking flow, FAQ, and responsive layouts.
- Visual review at mobile and desktop widths in light and dark mode.
- Verify all logo and pattern assets load from `/brand/` and no public component references retired logo assets.
