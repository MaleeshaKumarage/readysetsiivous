# Public website review — closure matrix

This change implements the code-addressable UI/UX findings while retaining the current brand. It does not certify legal compliance or replace business evidence. Public scope only; no auth/runtime privilege changes.

| Finding | Implementation | Verification / remaining dependency |
|---|---|---|
| UX01 contrast | Dark text on WhatsApp green, darker hover; stronger muted text/footer/focus colours | Automated computed-colour check in both themes; manual assistive-technology review remains. |
| UX02 pricing | Backend exposes gross amount and configured duration; frontend per-service VAT-inclusive totals; server rejects stale expected totals | Backend pricing/booking tests and multilingual Playwright. No hourly multiplication: existing backend bills the selected service. |
| UX03 errors | Validation fields, conflict/price change, throttling, unavailable lookup and uncertain writes have different recovery paths | Synthetic API regressions; no automatic write retries. Unknown server outcomes require contact/recheck. |
| UX04 post-booking | Download private details; manual booking/key management; explicit cancel confirmation and downloadable acknowledgement | Key remains in POST/body/component state and deliberate private download, never URL/localStorage/sessionStorage. Automatic durable email workflow/legal applicability not certified. |
| UX05 trust | Confirmed test reviews removed; unverified insurance/vetting/environmental/guarantee claims replaced; owner-provided business ID displayed | Registered trader name and geographical address still needed. Official register API returned no result; no invented identity/address. |
| UX06 navigation | Missing checklist link removed; footer anchors use localized homepage; manage route added | Public-page regressions. |
| UX07 forms | Semantic form/fieldset, required/optional labels, email/tel/autocomplete, server field associations and step focus | Keyboard/booking tests; independent screen-reader audit remains. |
| UX08 context | Three steps, full localized dates, Helsinki timezone/day boundary, summary and pending request status | Date-boundary and three-language tests. |
| UX09 language | Native focusable disclosure buttons with clear language codes and Escape/return focus, no incorrect menu roles | Existing navigation tests; manual screen-reader review remains. |
| UX10 journey | Primary quote action, service request actions, how-it-works, explicit WhatsApp send step; urgent CTA moved lower | Content/flow review; no numerical conversion uplift claimed. |
| UX11 readability | Larger legal links and semantic muted colours, accessible contrast/focus states | Computed contrast for key actions; manual full-page visual QA remains. |
| UX12 resilience | Essential content visible by default; entrance opacity cannot hide content when chunks fail; no-JS fallback retained | JavaScript-disabled essential content test. |
| UX13 mobile | Compact header, larger controls/input text, spacing, no floating button over booking; quote overlay clearance | Desktop/mobile and 320px reflow tests; 200% zoom/assistive tech remain manual. |
| UX14 assets | Licensed local fonts, local QR SVGs, consistent navigation icons, explicit-only preference writes | Same-origin asset assertions. Owner-approved Finland-specific generated images now illustrate the hero, home/deep-cleaning cards and About section; prior assets are preserved and business-uploaded service imagery retains precedence. Owner confirms the original website images were generated via Codex; no unlicensed-use finding established. |

## Legal checklist closure

Privacy and terms pages corrected in FI/EN/SV, with booking collection notices, minimisation guidance and request/complaint routes. No invented universal consent checkbox or cookie banner: no optional marketing introduced, unnecessary font/QR requests removed, and preferences saved only after explicit selection. Cancellation/refund information is integrated into terms rather than an empty duplicate policy page.

Still requires factual/legal evidence: registered name/address, actual retention schedules, processor/transfer arrangements, asset provenance, statutory withdrawal/durable-email workflow and live edge/storage inventory. See `public-website-privacy-operations.md` for the manual request procedure and evidence register. These are not marked verified by automated tests.

## Release compatibility

The previous identity promotion is deployed as cb33068. The owner explicitly approved using cb33068 as the only rollback baseline while retaining the closed-ingress maintenance guard and updating its test. This additive booking/website change does not upgrade or migrate Keycloak. New JSON response fields and snapshot currency are additive; existing records default to EUR. Deployment still requires an authenticated release and a coordinated closed-ingress window. Do not use the legacy 74559 identity model as rollback.


Owner selected the nationwide headline: Cleaning Services for Homes and Businesses Across Finland. FI/EN/SV marketing, footer and structured-data coverage are updated consistently to Finland, based on the owner's confirmed service scope.
