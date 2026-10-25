# Admin light workspace implementation plan

Primary goal: admins can navigate and edit every existing admin section in a
consistent readable light interface on desktop and mobile, retaining current branding.

## Phase 0 — Existing component and test contracts

How this serves the primary goal: reuse the actual UI framework and access gates
without introducing incompatible dependencies or regressions.

Sources: `site/src/app/admin/layout.tsx`, `admin.css`, `page.tsx`, existing admin
section pages, `site/e2e/lifecycle-admin-theme.spec.ts`, `site/playwright.config.ts`.
Allowed existing APIs: MantineProvider forceColorScheme, createTheme component
defaults, AppShell sections, NavLink active, Group/Stack responsive props, existing
Next Link/usePathname. Verify unfamiliar properties against installed types.
Guard: keep identity verification and public theme scope intact.

## Phase 1 — Shared admin presentation

How this serves the primary goal: navigation and reusable visual defaults improve
all admin sections together.

Implement approved grouped navigation, streamlined responsive header/sidebar,
scoped light tokens and consistent theme defaults. Update dashboard shortcuts for
all eight destinations. Check each page's toolbar and table responsiveness.
Guard: no new metrics, endpoints, permissions, filters or workflows.
Verify: TypeScript and responsive screenshots, visible keyboard focus, scoped portals.

## Phase 2 — Regression verification and independent review

How this serves the primary goal: demonstrate the redesign retains working admin
flows and remains light despite saved/system dark themes.

Extend existing Playwright admin theme tests for grouped navigation/current page,
mobile navigation closure, dashboard destinations and portal styles. Run desktop
and mobile theme/lifecycle/representative workflow tests and inspect screenshots
at 375, 768 and 1440 pixels. Independently review auth invariance, CSS scope,
accessibility, portal theming, overflow and unnecessary changes. Fix failures.

## Phase 3 — Reviewable delivery

How this serves the primary goal: deliver verified changes for review with concrete
evidence and clear limitations.

Commit only after verification/review. Push isolated branch and create attached PR
with screenshots or local artifact references and test results; follow existing
review/CI policies. Do not alter production data during verification.
