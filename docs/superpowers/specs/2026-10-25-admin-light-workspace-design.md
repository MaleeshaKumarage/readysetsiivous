# Admin light workspace redesign

## Goal and approved direction

Make tenant administration easier to scan and use on desktop and mobile while
retaining the current indigo theme, Inter typography, logo and branding. The user
approved improved layouts, navigation, tables and forms with light mode enforced.
This is a presentation change: existing permissions, routes and business actions
remain authoritative.

## Scope and visual rules

All `/admin` pages use a white workspace, pale gray canvas, indigo primary actions,
dark readable text, subtle borders and restrained shadows. Keep Mantine and Lucide;
no new component framework, fonts, colors or backend endpoints. Use existing data
only; do not invent metrics, alerts or activity feeds.

UI/UX Pro Max local search recommends minimal, consistent, high-contrast enterprise
UI. Its generated marketing hero/CTA and code-font suggestions do not fit this
operational product and are rejected. Use targeted React and accessibility guidance
for implementation instead. Existing public-site styling is separately scoped.

## Shared layout

`admin/layout.tsx` owns the verified access gate, Mantine provider, navigation and
responsive shell. Preserve its uncached identity verification, role/tenant checks,
race cancellation and conditional My shifts action exactly.

- Desktop: persistent white sidebar with one brand area, grouped navigation and a
  clearly indicated current page. Group Dashboard; Operations (Shifts, Quality
  Cycle); Customers (Leads, Companies, Agreements); People (Employees); Finance
  (Invoices); Catalog (Services). Labels supplement rather than replace links.
- Header: page context and existing My shifts/account actions; avoid duplicate
  brand and duplicate install/sign-out actions on the same visible surface.
- Mobile: accessible burger opens the navigation; selecting a route closes it.
  Actions wrap or collapse to labeled icon controls without horizontal overflow.
- Main content: consistent responsive gutters, heading hierarchy and page spacing.
  Existing page headings remain the source of page titles; do not add duplicate h1s.

## Dashboard

Replace the undifferentiated shortcut grid with grouped, clearly titled workspace
sections covering all nine existing destinations, including Invoices and Quality
Cycle. Keep descriptions useful and links keyboard accessible. Do not fetch extra
data or display fabricated operational summaries. Existing routes stay unchanged.

## Page components

Use shared Mantine theme defaults and scoped CSS for consistent cards, labels,
inputs, buttons, table headers, row padding, focus rings and empty/loading/error
surfaces. Keep inline validation and existing action feedback visible. Destructive
actions remain distinguishable and retain their current confirmation behavior.

Review each admin page (Services, Agreements, Employees, Leads, Companies, Shifts,
Invoices, Quality Cycle) for toolbar wrapping and table overflow. Where necessary,
use explicit table scroll containers, keeping action controls reachable. Do not
hide columns or change sorting/filtering semantics just to fit a smaller viewport.
Complex page-specific forms keep their current fields and workflows.

## Light-only behavior

Force Mantine light mode before authenticated page content is displayed, including
sign-in/loading/access-denied/error states. Scope explicit light tokens and
`color-scheme: light` to admin, including body-portaled dialogs, menus, notifications
and select dropdowns. OS dark mode, installed-PWA display mode and saved public
theme must not turn admin dark. Preserve and restore pre-existing theme attributes
on leaving admin; public, manager and employee routes retain their behavior.
Avoid global selectors that accidentally recolor unrelated routes.

## Accessibility and interaction

Keep visible keyboard focus, accessible names for icon actions, semantic headings,
navigation landmarks, `aria-current` on the active route and readable text contrast
of at least 4.5:1. Primary touch controls should be at least 44px. Respect reduced
motion. Tables may scroll within their container, but the page itself must not
overflow at 375px. No decorative animation that delays work.

## Verification and delivery

- Run TypeScript/build checks using the repository's existing toolchain.
- Extend existing Playwright admin-theme coverage for light/dark OS preferences,
  saved dark public theme, installed mode, portaled editor and dropdown surfaces.
- Check grouped navigation, active state, mobile opening/closing, dashboard routes
  and access gate behavior. Update assertions only for intentional presentation
  changes; retain business and authorization checks.
- Inspect desktop (1440px), tablet (768px) and phone (375px) screenshots of the
  dashboard and representative dense list/editor screens.
- Run relevant existing lifecycle/lead/shift browser tests and report any limits.
- Create a reviewable PR with screenshots, verification evidence and scope notes.
  No production records are edited during design verification.

## Risks

Mantine portals inherit variables outside the shell, so root/body scope must be
tested explicitly. Existing pages may contain local styles that override shared
defaults; page-by-page inspection is required. Avoid brittle blanket CSS and keep
content/interaction test coverage while updating appearance.
