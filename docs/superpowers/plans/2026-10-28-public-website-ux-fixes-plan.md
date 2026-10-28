# Public website fixes

## Primary goal
Customers can understand service prices and request cleaning, recover from errors, keep private booking details and manage a booking with accessible controls. Published marketing and privacy copy must match known facts, with business/legal evidence gaps visible.

## Phase 1 — correctness and trust
How this serves the primary goal: prevents misleading prices and unsupported expectations.
Expose server-calculated VAT-inclusive service totals, duration and price-change protection. Remove confirmed test reviews and unverified guarantees; publish provided business ID. Correct FI/EN/SV privacy and terms copy, distinguishing requests from agreed services. Track missing legal/business evidence.

## Phase 2 — booking and management
How this serves the primary goal: makes the full customer journey usable after submission too.
Localize progress, required fields, safe errors and Helsinki date/time; preserve details after conflicts, block blind uncertain-write retries, download private confirmation. Add manually keyed lookup/cancellation using existing POST authorization and acknowledgement download.

## Phase 3 — accessible brand polish
How this serves the primary goal: keeps core information readable and reachable.
Contrast, focus, touch targets, anchors, language semantics, visible-by-default content, quote hierarchy and service actions. Self-host fonts with licences and local QR images. Save preferences only after explicit choice. Use supplied brand artwork pending photo provenance.

## Phase 4 — review and verification
How this serves the primary goal: provides evidence rather than declaring success from appearance alone.
Run focused .NET tests, TypeScript/build and desktop/mobile Playwright. Review changed code and closure matrix, resolve material findings, then create a reviewable PR. Do not mark business/legal/manual evidence as verified by automated tests.
