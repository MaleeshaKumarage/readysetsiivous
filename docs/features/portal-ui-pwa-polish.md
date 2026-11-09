# Partner, admin and cleaner UI/PWA polish (#377)

## Purpose and baseline

Make the cleaning company's existing tasks clearer, more welcoming and comfortable on phones. This is presentation work, preserving current business behavior. The user selected a clean, welcoming visual direction with existing role colors, and polish of existing PWA capabilities rather than new offline or partner-install functionality.

Initial code/design review used main `b3342776`; implementation started from main `20bd88b` on `feat/portal-ui-pwa-polish`. The newer inventory acknowledgment feature in that baseline is retained. #377 supersedes #278; existing navigation grouping remains unchanged by this work.

Read the approved field-service design, implementation plan and delivery ledger together with current code and later requirements. Earlier roadmap text is not proof that a capability is missing or that an older rule still applies.

## Existing behavior retained

- Partner company scope, property rows/city filter, pricing eligibility, request order and request pagination.
- Flexible windows and fixed appointments. Payloads, versions, request idempotency and acceptance rules are unchanged.
- Direct partner editing/cancellation before first acceptance; staff change requests afterward. Started/billed work remains protected.
- Existing automatic and manual acceptance paths and preferences. No default or timing policy change.
- Pending menu count is Requested work + outstanding eligible quotes + unresolved changes. Offered rows remain visible without counting as needing arrangement.
- Persistent red admin quote banner/badges, direct quote-tab links, refresh policy and last-successful data on read errors. Their prominence was explicitly requested; the earlier review suggestion to demote them is withdrawn.
- Shift definitions, occurrences, recurring/visit assignment scopes and workspace/editor handoffs remain distinct.
- Cleaner start eligibility, scheduled/actual times, optional checklist selections, required photo categories, individual photo notes, drafts and uncertain-save recovery.
- Existing prices/VAT, financial snapshots, API contracts, notifications, tenant/role permissions and partner cleaner-name masking.
- Light English Mantine admin, light FI/EN partner, and cleaner light/dark presentation. Public-site styling is not redesigned.

## Implemented presentation

### Partner

One bounded white dialog surface now includes its heading and content. Inner cards no longer look like a separate dialog; the overlay remains the single scroll container. Existing focus, Escape, backdrop and draft handling remain in the shared Dialog.

Property identity/address/status, room/bed facts and pricing/duration occupy clear desktop columns, with existing actions below. Phones retain one-column rows. All five destinations fit a desktop row, while the existing two-column phone layout stays. Mobile header/toolbars have less decorative spacing. Date-window helper copy is supplied in English and Finnish without changing native inputs or validation. Requested, planned and actual timing keep their existing values and have clearer visual separation.

### Cleaner

Below 640px all four section tabs are visible in a two-column grid. Existing tab IDs, panels, selected states and arrow/Home/End keys are retained. Cards use restrained shadows and full-width scheduled/actual timing below the identity/status row; instructions distinguish cleaning, access and contact information.

Evidence remains inline. Photo/note pairs occupy readable cards, full images are contained rather than cropped, and removal has a 44px target outside the image/note area. The existing Add photo file input is visually hidden rather than display-hidden, with a 44px label and visible keyboard focus; upload handlers and validation are unchanged. Required sections and optional checklist behavior are unchanged. Existing offer, inventory and settings content remains in place.

### Admin

Request summaries group existing status, window, price/duration and cleaner information; actions keep their handlers and labels. A short explanation distinguishes the pending Requests badge from offered rows. Red quote alerts remain. The shift workspace's existing sections have distinct surfaces, and work-queue filters wrap within a bounded toolbar. Navigation groups, routes, default views and dashboard destinations are retained.

### PWA

Workspace-only `viewport-fit=cover`, dynamic viewport height and safe-area padding accommodate display cutouts/home indicators. The admin layout's existing client code lives in AdminWorkspace behind a thin server layout so Next can export viewport metadata; authentication and navigation code are unchanged. Me and Partner expose the same viewport setting without changing manifest identity.

Existing admin/employee installation states have clearer icons, spacing and instruction steps. Installation remains user initiated. Mobile editable fields use 16px text, forms use normal scrolling, and no sticky submit bar or forced refresh was introduced.

Manifest identity/start URLs/scope/icons, service-worker registration/lifecycle/fetch/cache behavior, notification privacy/destinations and permission flows are unchanged. No offline persistence, write queue, background sync or dedicated partner app was added. No offline capability is advertised.

## Verification and evidence

Before implementation, the existing properties, employee sections, staff tabs and PWA suites passed 36/36 desktop/mobile checks. Synthetic before screenshots were captured from an isolated unmodified `20bd88b` worktree.

The new portal polish/visual suites cover dialog bounds at 320/390/768/1440px, long EN/FI titles, keyboard/dismissal, short viewports, exact flexible/fixed request payloads, mobile employee tabs, dark mode, required photos/notes and optional checklist submission, pending counts/order, existing installation states and standalone metadata. Chromium display-cutout overrides exercise actual safe-area CSS for all three roles. Screenshot tests attach images to Playwright results; set `PORTAL_UI_EVIDENCE_DIR` to retain copies outside ephemeral test output.

A reduced viewport simulates available keyboard space; it does not prove an actual device keyboard. Browser standalone/prompt emulation does not prove physical-device installation, authenticated Keycloak return or real push delivery. Genuine iOS Home Screen/Android installed-app checks remain a device verification task; no real-device result is claimed.

Final test/build results and implementation, publication and deployment states are recorded in the delivery ledger. Frontend API fixtures are not live backend integration evidence. No production records or settings were changed.

## Release and rollback

Deliver reviewable chunks with focused desktop/mobile tests under the single tracking issue. Use normal latest-head CI/review and release procedures. No migration or production data correction is needed. Record local implementation, publication, merge and deployment separately. Revert presentation changes if action availability, form retention, routes or API payloads regress.
