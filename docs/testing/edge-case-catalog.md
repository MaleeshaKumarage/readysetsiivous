# Feature edge cases for Playwright and API tests

Source baseline: `ceff5bd`, reviewed 2026-10-13. See the
[feature inventory and gap register](../features/feature-inventory.md).
This catalogue is a test specification; the cases have not been newly executed
as part of this documentation task. Earlier validation of that baseline reported
136 backend tests and 42 configured desktop/mobile browser runs passing.

There are **203 stable-ID case families** across all 24 feature groups. Boundary
parameter sets and the cross-feature failure matrix expand into additional tests.

## Conventions

Each ID is stable and should appear in test titles. Expand semicolon-separated
parameter values into independently reported tests, not one opaque mega-test.
`C` = current supported contract; `R/Gxx` = desired regression outcome for a known
gap (may currently fail); `B/Gxx` = UI missing (API may still be testable);
`D` = business policy needs approval before fixing the expected outcome.

Layers: `UI` mocked browser tests; `DEV` real UI/API on shared dev with genuine
tokens; `API` Playwright APIRequestContext; `BE` .NET/database/artifact tests;
`HOST` built static site/headers; `DEVICE` browser/platform-specific checks.
P0 covers security/tenant boundaries, P1 transactional correctness and major
flows, P2 UX/secondary boundaries. No destructive/live data tests target prod.

Fixtures: A/B = separate active tenants; AdminA/EmployeeA/NoRoleA = real identities
in A; MasterProvisioner = dedicated provisioning account; S = active 60-minute
EUR service with fi/en/sv names, 50 net/25.5 VAT; E = active employee; C/B = active
company/branch belonging to A (use distinct company/branch IDs, not tenant B's ID);
T = QC template with three unique items; AG = valid
PDF with two pending signers; use per-run UUID IDs and owned data. Fixed clock
examples use 2026-10-13 07:00Z (10:00 Helsinki) unless stated otherwise.

For every mutation, verify both the visible result and persisted state after
reload/readback. A success toast, mock call, or HTTP 200 alone is not sufficient.

## Landing page, language, theme, quote and contact features

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| WEB-001 | P1 UI/HOST | Each fi/en/sv route → load at desktop/mobile width | Render current section list, navbar, footer and correct locale; no missing images/JS errors. | C |
| WEB-002 | P2 UI | Mobile width → open nav, select services/booking/card, reopen/close | Drawer closes after navigation; link targets correct language; focus not lost behind overlay. | C |
| WEB-003 | P2 UI | Each visible anchor in navbar/footer → activate by mouse and Enter | Target exists and scrolls into view; #checklist currently lacks its hidden target. | R/G29 |
| WEB-004 | P2 UI | FAQ → open first, open second, close second | One open region; aria-expanded and aria-controls match actual panel; keyboard works. | C |
| WEB-005 | P2 UI/HOST | Disable JS or fail a page chunk → visit root/public homepage | Root language links remain usable; no permanently invisible service cards; verify static content separately from JS interactions. | C |
| WEB-006 | P2 HOST | Fresh production export → direct deep link/reload every public/admin route | Static host resolves trailing-slash routes/assets; unknown language and unknown route produce host 404, not wrong content. | C |
| WEB-007 | P2 UI | Long translated strings; 320px screen; 200% zoom → inspect navigation/forms/table containers | No clipped primary actions; tables may scroll horizontally within their container. | C |
| LANG-001 | P1 UI | Empty storage → visit `/` | Replace navigation lands on `/fi/`; root fallback not added as a repeated back-stack entry. | C |
| LANG-002 | P1 UI | Storage lang=en/sv/fi; invalid `xx`; storage throws → root | Saved supported language wins; invalid/unavailable storage falls back to fi. | C |
| LANG-003 | P1 UI | `/en/?q=a#faq` → switch fi/sv | Locale path changes, query/hash survive, stored preference updated, page text switches. | C |
| LANG-004 | P2 UI | Open language menu → Escape/outside click/keyboard selection | Menu closes; Escape restores trigger focus; current item indicated. | C |
| LANG-005 | P1 UI | Delay en catalogue response; switch to fi; resolve fi then en | Late en response must not overwrite current fi cards or booking services. | R/G25 |
| LANG-006 | P2 UI/API | Request missing language, unsupported language, existing empty en field | BE fallback fi for missing key; empty existing key stays empty; decide whether blank should also fallback. | C/D |
| THEME-001 | P2 UI | Empty storage with light/dark OS preference; saved light/dark overriding OS → reload | Correct root class and saved preference after hydration; no console hydration mismatch. | C |
| THEME-002 | P2 UI | Toggle twice; reload; localStorage throws | Toggle remains functional without storage; persisted preference honored when storage works. | C |
| THEME-003 | P2 UI | Saved light → public→admin→public | Public light preference restored after leaving forced-dark admin, without state/class disagreement. | R/G24 |
| THEME-004 | P2 UI | Reduced-motion preference; keyboard-only navigation → public page | Essential content visible; animation not needed to access actions; check current motion behavior before deciding animation policy. | C/D |
| QUOTE-001 | P1 UI | Valid service/size/city/tomorrow → submit | One WhatsApp navigation/pop-up; encoded translated labels and all four values; no booking API mutation. | C |
| QUOTE-002 | P1 UI | Missing fields individually/all; city spaces only → submit | Inline translated errors, no WhatsApp launch, focus moves to first invalid field. | C |
| QUOTE-003 | P1 UI | Freeze client near Helsinki midnight → today/yesterday/tomorrow input | Tomorrow minimum computed from client-local date; today/past rejected; date display/message remains consistent. | C |
| QUOTE-004 | P2 UI | City contains å/ä/ö, emoji, ampersand, +, newlines → submit | Data round-trips through URL decoding; no markup execution; input is not treated as executable URL. | C |
| QUOTE-005 | P2 UI | Double-click submit; block pop-up; return from WhatsApp | Submitting guard resets after ~1.5s; observe denied-pop-up behavior; agreed failure feedback is not implemented. | C/D |
| CARD-001 | P2 HOST/UI | Each language contact card → phone/email/WhatsApp/home/vCard | Correct protocols/destination, locale home URL; vCard download has expected FN/ORG/contact fields. | C |
| CARD-002 | P2 UI | Each language homepage → inspect QR URL; fail external QR provider | Encoded contact-card language matches; surrounding contact links remain usable; dev QR currently points to production. | C/R G29 |
| LEGAL-001 | P2 HOST/UI | Each locale footer → privacy/terms; inspect page metadata | Correct locale route/content, title/canonical/hreflang; compare robots/sitemap against emitted pages. | C |
| LEGAL-002 | P2 HOST | Build export → fetch favicon, manifest icons, sitemap, robots, vCard, OG image | Correct content type and existing referenced assets; sitemap dates/URLs not stale assumptions. | C |

## Public service catalogue and booking

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| CAT-001 | P1 UI/API | 0/1/4/5+ active services → homepage and booking | Homepage ≤4 sorted cards; booking exposes full active list; inactive service absent from API. | C |
| CAT-002 | P1 UI | Initial service API 500/timeout/invalid JSON → homepage and booking | Homepage static fallback; booking WhatsApp-unavailable view; no infinite service loading. | C |
| CAT-003 | P1 UI | Load live cards then return empty/failed locale response | Old locale/live cards must not stay masquerading as current data; static fallback behavior explicit. | R/G25 |
| CAT-004 | P2 UI | Unknown icon, missing image, relative `/uploads/...`, absolute image URL | Defined icon/image fallback and correctly resolved URLs; broken assets do not hide card actions. | C |
| CAT-005 | P1 UI/API | Service prices 0, 0.005, 50; VAT 0/25.5/50; currency other than EUR | Display and submitted price semantics agreed; current booking uses net price with €/hour label. | D/G26 |
| BOOK-001 | P1 UI/DEV | S available future date → complete all booking steps | POST fields correct; number/reference visible; API readback proves exactly one booking, snapshots and local/UTC times correct. | C |
| BOOK-002 | P1 UI | Details → back to time/service → change selection | Customer inputs persist appropriately; selected service/date/time reflected in summary and final payload. | C |
| BOOK-003 | P1 UI | Availability 503/abort/timeout → retry; retry returns slots | Error replaces spinner; translated retry shown; successful retry clears error and enables choosing slot. | C |
| BOOK-004 | P1 UI | Slow date A; date B resolves first; release A after B | Only date B slots displayed; previous-request response cannot overwrite new selection. | C |
| BOOK-005 | P1 UI | Availability [] → select another date; clear date | Empty-times state differs from network error; no stale clickable time buttons when date empty. | C |
| BOOK-006 | P1 BE/API | Fixed clock, notice=0/2/720; exact deadline±1s → list/create | Listing never offers past/too-soon starts; reservation rejects before deadline; exact deadline permitted with frozen clock. | C |
| BOOK-007 | P1 BE/API | Date yesterday/today/future, start 07:59/08:00/19:00/20:00; 60min S | Start≥08:00, end≤20:00; valid 19:00 end20:00 accepted; outside window gives409. | C |
| BOOK-008 | P1 BE/API | Existing [10:00,11:00]; request [09:00,10:00]/[11:00,12:00]/overlap | Touching boundaries allowed; overlaps rejected; cancelled booking frees slot. | C |
| BOOK-009 | P1 BE/API | S duration15/30/720/1440; start with minutes not on grid; date rollover | Define allowed start grid and reject multi-day business-window bypass; 1440-min end.TimeOfDay alone is insufficient. | R/G13/D |
| BOOK-010 | P1 API | Wrong service GUID/missing service/deactivated after list; suspended tenant | Missing service404; inactive service409; suspended/unknown tenant403; UI preserves details and offers recovery. | C |
| BOOK-011 | P1 UI/API | Name length150/151, phone40/41, street200/201, city100/101, blank/space/null fields | Required/max boundaries enforced by BE; FE needs actionable validation, no misleading success. | C/R G24 |
| BOOK-012 | P1 API | Dates `2026-02-30`, wrong format; `25:99`, `24:00`; null nested payload; malformed JSON | Consistent400 desired; semantic date/time parse currently may surface500; malformed JSON should be rejected before handler. | R/G11 |
| BOOK-013 | P1 API | Email absent/empty/valid/malformed; postal code/notes very long | Optional email allowed, malformed email400; establish postal/notes size policy instead of inventing limits. | C/D |
| BOOK-014 | P1 BE/API | Two simultaneous reservations for same window, existing counter | At most one committed booking; loser409; unique monotonic numbers; assert stored row count and references. | C |
| BOOK-015 | P1 BE/API | First-ever tenant/year counter; parallel disjoint bookings | Distinct sequences, no overwritten counter/booking; verify first-insert race against PostgreSQL, not mock. | C |
| BOOK-016 | P1 BE | Inject concurrency/serialization conflict on first/second/third save | Fresh transaction and revalidation each time; success on recoverable retry; bounded409 after three attempts. | C |
| BOOK-017 | P1 UI/DEV | Commit POST but drop/delay success response past10s; retry | Reconcile committed booking rather than duplicate/misreport; idempotency/recovery mechanism missing. | R/G12 |
| BOOK-018 | P1 BE/API | Booking Dec31 around Helsinki/UTC year change; DST spring/autumn; non-Helsinki tenant | Stored local date/time round-trip; number year uses UTC start year; UTC duration/timezone conversion follows configured tenant. | C/D |
| BOOK-019 | P1 BE/API | Edit/delete S after booking, change VAT/name/price | Booking snapshot remains unchanged; public lookup/invoice later use snapshot, not current service. | C |
| BOOK-020 | P2 UI | All required details empty → submit, BE returns400/409/429/500 | Preserve customer data; usable distinct errors desired; current adapter collapses to generic failure. | R/G24 |
| LOOK-001 | P1 API | Existing number + exact/trimmed matching phone → lookup | Only small public DTO; no email/address/notes/private assignment data. | C |
| LOOK-002 | P1 API | Missing number, wrong phone, equivalent phone with changed punctuation | Lookup404; cancellation wrong phone403; matching is trimmed-string based, not international normalization. | C |
| LOOK-003 | P1 API | New/Confirmed booking → cancel; repeat; Completed→cancel | First204 and cancelled history; repeated/completed409; subsequent availability frees eligible slot. | C |
| LOOK-004 | P2 UI | Customer wants retrieve/cancel booking | UI unavailable; test API now and record missing customer route rather than inventing selector. | B/G17 |

## Identity, tenant access, admin navigation and service operations

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| AUTH-001 | P1 UI | Unauthenticated vs mocked authenticated → each admin route | Login view vs dashboard/page; correct navigation links and direct reload. Mock cannot prove server auth. | C |
| AUTH-002 | P1 DEV | Real AdminA → Keycloak PKCE login, reload, logout, browser back | Valid session restored; logout does not expose cached protected data; API rejects invalidated/expired session appropriately. | C |
| AUTH-003 | P0 API/DEV | No token; malformed/expired token; EmployeeA; NoRoleA → role-protected routes | 401 for unauthenticated;403 for insufficient realm roles; no unauthorized write. Exercise every endpoint role category. | C |
| AUTH-004 | P1 UI/DEV | EmployeeA and NoRoleA → admin shell/actions | Role-aware controls/forbidden state desired; shell currently shows authenticated users admin actions. | R/G27 |
| AUTH-005 | P1 UI/DEV | API401 first attempt → refresh success; refresh failure; repeated401 | One retry when appropriate, login recovery; no Bearer undefined write or loop; no permanently loading form. | C/R G24 |
| AUTH-006 | P1 UI/DEV | Expired token → image upload, agreement create/PDF, invoice/QC PDF | These direct fetch paths currently skip common refresh; correct explicit recovery needed. | R/G24 |
| AUTH-007 | P0 API/DEV | Valid issuer/signature but unrelated audience; master user without provisioning permission | Reject unauthorized audience/provisioning role desired; currently audience disabled and system gate realm-only. | R/G14 |
| AUTH-008 | P0 API/DEV | AdminA requests tenant B object GUIDs across entity types | No B data read/write;404 or403 according route; persist B state unchanged. Include QC separately. | C/R G01 |
| AUTH-009 | P0 API/DEV | Token realm A → `/public/B/...`; anonymous URL B control | Explicit URL-vs-token tenant policy; current middleware picks authenticated realm. | D/G30 |
| AUTH-010 | P1 UI | Mobile drawer keyboard open/close, route change, Escape, focus | Navigation remains reachable; run without helper CSS that hides drawer. | C |
| AUTH-011 | P1 UI/DEV | User/tenant switch without full reload after cached list | Never show other principal's path-keyed cached data; verify invalidation beyond auth mock. | R/G24 |
| SVC-001 | P1 UI/DEV | Create S, edit displayed name, deactivate/reactivate → reload/catalogue | Correct payload, persisted data and public visibility, active badge/actions reconcile. | C |
| SVC-002 | P1 API | Slug lengths1/2/61/62; uppercase/leading hyphen/space/Unicode; duplicate slug |2–61 valid format; invalid400; duplicate409; parallel duplicate insert must not create two slugs. | C/R |
| SVC-003 | P1 API | Duration14/15/1440/1441; price-0.01/0; VAT-0.01/0/50/50.01; order-1/0/10000/10001 | Exact validator boundaries; assert actual error property names, no data write. | C |
| SVC-004 | P1 UI | Enter VAT0, duration0/14, price negative/blank → submit | Correct numeric contract; current form replaces VAT0 and clamps/defaults silently. | R/G26 |
| SVC-005 | P1 UI/DEV | Fully localized S with custom icon/image/info/order/featured → edit only price/status | All unrelated fields retained after reload/raw GET; currently lost by payload construction. | R/G02 |
| SVC-006 | P2 API | Name={} or fi missing/empty/null; description additional translations | Missing fi rejected; empty fi currently allowed; choose non-empty localized-text rule. | C/D |
| SVC-007 | P1 API | jpg/jpeg/png/webp, uppercase extension; zero-byte/5MiB/5MiB+1; fake image payload | Extension/size guards; semantic decoding is missing and needs400; no orphan metadata on failure. | C/R G11 |
| SVC-008 | P0 API/DEV | Public GUID image GET vs nested agreement original/signed/signature; encoded slash/traversal | Only intended root service image served; all private paths404 even with guessed IDs. | C |
| SVC-009 | P1 UI | Reject create/edit/status mutation or abort request while modal open | Form stays open with original inputs and recoverable error; rejected status toggle must not look successful. | R/G24 |
| SVC-010 | P2 UI | Open/close/reopen create/edit; long names; double submit | Correct selected record, no previous-modal errors/drafts leaking unexpectedly, one accepted mutation. | C |

## Employees, companies and branches

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| EMP-001 | P1 UI/DEV | Basic employee create then deactivate → reload | Persist identity/role, default advanced fields; inactive list behavior matches includeInactive setting. | C |
| EMP-002 | P1 API | First/last names empty/space/100/101; invalid email; role staff/unknown; pay-0.01/0; cert name200/201 | Field-specific400 at supported limits; only admin/employee roles accepted. | C |
| EMP-003 | P1 API | Duplicate/case-varied email create and update; two concurrent creates | Consistent uniqueness desired; creation checks repository, update lacks duplicate check; inspect exact casing behavior. | R/G09 |
| EMP-004 | P2 API/UI | Set hours/skills/areas/pay/certifications/notes; edit/reload | API stores fields; no UI controls presently. Hours/expiry format policies require decisions. | C/B G19 |
| EMP-005 | P1 API/DEV | Invite active/inactive/nonexistent E; repeat invite; mail service fails/unconfigured | Existing404 if missing; prove delivery or intentional failure status, avoid claiming accepted=delivered. Inactive/repeat policy undefined. | C/D G09 |
| EMP-006 | P1 DEV | Follow invite registration link → login as newly registered user | Intended employee linked and realm roles assigned; currently no automatic linking/role binding in invite handler. | R/G09 |
| EMP-007 | P0 DEV/API | Deactivate logged-in employee → existing access token/read/write/shift assignment | Explicit disabled-employee access policy; data deactivation alone does not revoke Keycloak identity. | D/G09 |
| EMP-008 | P2 UI | Edit/reactivate employee or resend invite from page | Missing UI action; test available APIs independently. | B/G19 |
| COMP-001 | P1 UI/DEV | Create company+branch with contacts; expand/collapse → reload | Persist parent relationship and fields; expanding one company does not display another's branches. | C |
| COMP-002 | P1 API | Blank/space business ID or name; same trimmed business ID; duplicate update | Required400; unique constraint exists; duplicate needs defined409 rather than unmapped database500. | C/R |
| COMP-003 | P1 API | Create branch with Guid.Empty/nonexistent company/other-tenant company | Empty400; nonexistent/foreign parent rejection desired before write; existence handler missing. | R/G10 |
| COMP-004 | P1 API | Edit/deactivate company/branch; relationship-linked shifts and agreements exist | APIs persist flag/fields; decide cascading/selection eligibility; no data deletion unless explicit. | C/D |
| COMP-005 | P2 UI/API | 0/100/101+ companies; search accents; negative/huge skip/take | BE clamps skip≥0/take1–200, returns total and sorts by name; UI cannot access later pages. | C/B G20 |
| COMP-006 | P1 UI | Switch expanded company or shift company rapidly; responses reverse order | Branches belong to current selection; pending response cannot overwrite newer parent. | R/G25 |
| COMP-007 | P2 UI | Failed branch creation/list request; cancel/reopen dialogs | Clear recoverable error and preserved fields; no stale branches or stuck loading. | R/G24 |

## Shifts, recurrence and employee assignment

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| SHIFT-001 | P1 UI/API | Create each FE type0/2/3/4/5, selected company/branch/template | Numeric enum and correct type-specific fields; readback agrees; on-call has no fixed occurrence. | C |
| SHIFT-002 | P1 API/BE | Create type1 weekday-time map with one/several days | Per-day times honored; type1 supported BE but unavailable in FE creation options. | C/B G21 |
| SHIFT-003 | P1 API | Missing parent/name/schedule; mismatched branch owner; nonexistent IDs | Create400/404 as appropriate; no orphan shift; update must repeat relationship checks. | C/R G10 |
| SHIFT-004 | P1 UI/API | Equal/reversed/overnight time range; missing weekday/parity; unknown enum | BE rejects invalid schedules400; overnight unsupported until policy defined; UI retains draft. | C/D |
| SHIFT-005 | P1 API | TimeSpan negative/≥24h/day component; weekday7/-1; WeeklyDays null/duplicates | Reject invalid time-of-day/day values consistently; current bounds/null-list handling incomplete. | R/G11 |
| SHIFT-006 | P1 BE/API | Monthly1/28/29/30/31; leap/nonleap February and April | Only existing day creates occurrence; no implicit last-day substitution; day0/32 rejected. | C |
| SHIFT-007 | P1 BE/API | Biweekly dates before/after2024-01-01 and New Year; parity1/2/0/3 | Anchor-based parity stable;0/3 rejected; avoid ISO-week assumptions in fixture expectations. | C |
| SHIFT-008 | P1 UI | Biweekly select two days; create→close→reopen different schedule | Only first day currently sent; expose single-day constraint/feedback; retained time/type defaults explicitly tested. | R/G21 |
| SHIFT-009 | P1 API/BE | Occurrence from=to/from>to/spans366d/366d+1s; valid offsets | Invalid span400;≤366days accepted; correctly UTC-normalized timestamps, no silent timezone shift. | C |
| SHIFT-010 | P1 BE/API | Range begins noon/ends morning; shift outside ValidFrom/Until or inactive | Respect actual timestamp/validity/active boundaries desired; current calculator expands whole dates and ignores shift bounds. | R/G05 |
| SHIFT-011 | P1 BE/API | Helsinki spring/autumn DST, employee browser in Kyiv, local08:00 shift | Define schedule timezone first; current calculator tags stored wall times UTC, not Helsinki-local conversion. | D/G05 |
| SHIFT-012 | P1 DEV/API | Assign E to non-overlap, exact-touch, overlap; remove twice; reassign | Overlap409, exact-touch allowed; assignment soft inactive/idempotent remove; no second active duplicate on reassignment. | C |
| SHIFT-013 | P1 API | Assign missing/inactive employee; missing/inactive shift | Missing404; inactive guard desired; currently handler does not reject inactive entities. | C/R G06 |
| SHIFT-014 | P1 BE/API | Overlap beyond180days, open-ended and validity-limited shifts | Test documented conflict horizon; decide whether conflicts beyond cap may be accepted. | C/D |
| SHIFT-015 | P1 BE/API | Concurrent same/different overlapping shift assignments for E | No duplicate/conflicting active assignments; DB race protection currently missing. | R/G06 |
| SHIFT-016 | P1 UI | Assign three employees; second fails409/network; unassign fails | Clearly show partial results and actual assignment list; no falsely successful full batch. | R/G21 |
| SHIFT-017 | P1 UI | Select company A branch; change company B before response | Clear old branch selection; submitted branch belongs to chosen company; avoid stale parent state. | R/G25 |
| SHIFT-018 | P2 UI/API | Edit schedule/validity/reactivate/view occurrences | BE routes work; no corresponding FE control/view. | B/G21 |

## Quality Cycle templates, dispatch, submissions and reporting

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| QC-001 | P1 UI/DEV | Create T, add/remove item, edit/cancel edit/delete with confirmation | Text/order readback stable; cancelled deletion no mutation; committed deletion absent after reload. | C |
| QC-002 | P1 UI/API | Title blank/space, items[]/all spaces/mixed blank+valid | FE prevents blank effective checklist; BE trims/filters; all-blank list currently can500 and needs400. | C/R G08 |
| QC-003 | P2 UI/API | Item counts1/10/11, duplicated text, long description/title | FE caps add at10; BE lacks limit; decide count/text size/duplicate policy before enforcing. | D/G23 |
| QC-004 | P1 API | Global/company/branch-scoped templates; nonexistent/foreign parent IDs | Null scopes included as global; foreign/nonexistent scope relationships rejected desired. | C/R G10 |
| QC-005 | P1 UI/DEV | Edit a scoped or inactive template using current unscoped form | Preserve scope/active state; missing controls must not silently clear existing metadata. | R/G23 |
| QC-006 | P0 DEV/API | AdminA creates T/form; AdminB lists/gets/uses A token | No cross-tenant data; tenant-bound persistence must be proved against actual QC repository. | R/G01 |
| QC-007 | P1 API/BE | Dispatch same day twice; two simultaneous dispatches | Sequential existing check avoids duplicates; parallel requests must yield one occurrence+employee form. | C/R G15 |
| QC-008 | P1 API/BE | Inactive/missing shift/template/employee, removed assignment, on-call | Do not dispatch ineligible recipients; current selected-shift path and calculator can ignore inactivity. | R/G05 |
| QC-009 | P1 BE/DEV | Assign E before/during/after today's shift; template selected | Correct future occurrence linked once; current first-date occurrence may already be past. | R/G05 |
| QC-010 | P1 DEV/BE | Wait past end of recurring shift, no reassignment/manual dispatch | Automatic next form requires a wired worker; no active hosted dispatcher at baseline. | B/G04 |
| QC-011 | P1 BE/DEV | Generate tasks repeatedly with email configured/unavailable; include historical delivery failures | No QC email or delivery attempt; one task per employee/occurrence; historical delivery metadata unchanged. Native account invitations remain separate. | R/issue93 |
| QC-012 | P1 UI/DEV | Employee signs in → My shifts → start → select none/some/all activities → end | No emailed QC link needed; selections persist; empty checklist permits clocking; failures retain input and lost responses recover saved state. | R/issue93 |
| QC-013 | P1 UI/API | Submission list with no forms/pending/submitted/photo notes → shift filter | Correct rows/count/status; no missing vs failed-fetch confusion; filter request includes shiftId. | C/R G24 |
| QC-014 | P1 UI/API | PDF no selected shift; valid year/month; month0/13/year0; failed download401/500 | Required selection feedback; valid authenticated PDF; invalid dates400 desired; visible download failure. | C/R G08/G24 |
| QC-015 | P1 API/BE | Monthly report: first/last instant, UTC vs local month, leap February | Correct inclusion boundary (current UTC month), no next-month records, filename/PDF header; decide timezone policy. | C/D |
| QC-016 | P1 BE/API | Edit/delete T after dispatch, then submit/report old form | Form snapshot survives template mutation; historical checklist/occurrence end unchanged. | C |
| QC-017 | P2 UI | Open attached photos/advanced date filters/manual dispatch/scope controls | Current UI shows photo count; advanced controls are missing, use API tests now. | B/G23 |
| QCP-001 | P1 UI/DEV | Valid token with three unchecked items → check subset, notes, submit | Stored original item text/order and flags; thanks shown; partial/all-unchecked permitted under current contract. | C |
| QCP-002 | P1 UI/API | Missing/unknown token; malformed token; tenant suspended | Invalid-link view; BE404 for missing form and403 inactive tenant; no photo/data disclosure. | C |
| QCP-003 | P1 UI/DEV | Already-submitted token reload; repeat/concurrent submit | Existing submitted token thanks; POST conflict should409, current repeated domain exception maps500; one winning persisted state. | C/R G08 |
| QCP-004 | P1 API/BE | Missing/reordered/extra/renamed/duplicate items; Items null | Preserve every template row and map independent flags; mismatch policy needed; text-then-index fallback currently accepts altered/missing items. | D/G15 |
| QCP-005 | P1 UI/API | Single/multiple photo files, remove, repeat same file; blank notes/Unicode | DataURL previews/removal and persisted photo/notes match payload; same-file input event behavior verified. | C |
| QCP-006 | P1 UI/API | Huge files/count, non-image bytes, fake MIME, malformed/remote/javascript photo URLs | Enforce agreed sizes/types/schemes; application limits absent; reject without corrupting form or exhausting response size. | R/G11 |
| QCP-007 | P2 UI | Drag files over advertised dropzone; submit before FileReader finishes | Actual drop/read-in-progress support missing; no false successful loss of pending photo. | R/G23 |
| QCP-008 | P1 UI | Load/submit network abort/500/429, retry; token changes during delayed load | Recoverable error and no stale wrong-form display; draft preserved; responses belong to current token. | R/G24/G25 |
| QCP-009 | P2 UI/DEV | BE returns submittedUtc different from client clock → thanks | Server-confirmed submission time shown; FE currently sets submitted flag without refreshing form result. | R/G24 |

## Agreement administration, signing and artifacts

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| AGR-001 | P1 UI/DEV | C + valid PDF + two complete signers → create/details/list | Multipart payload and actual DTO; one agreement, generated code/tokens, correct counts/state and working links. | C/R G03 |
| AGR-002 | P1 UI/API | Missing company/title/PDF/signers; title200/201; incomplete row mixed with valid row | Required/max400; FE currently drops incomplete row silently—require explicit correction or warning. | C/R G22 |
| AGR-003 | P1 API | Invalid signersJson, malformed signer email, duplicate signer email/name | Parse/validation400 desired; duplicate identity behavior needs policy. | C/D/R G11 |
| AGR-004 | P1 API/BE | Fake PDF extension/MIME, corrupt/empty/encrypted/many-page PDF; oversized upload | Reject invalid/unsupported files deterministically, no orphan agreement; parser/request-size behavior recorded. | R/G11 |
| AGR-005 | P1 API | Missing/inactive/foreign company GUID | Ensure company exists in request tenant and define inactive parent policy; current handler only requires nonempty GUID. | R/G10 |
| AGR-006 | P1 UI | Detail response contains only actual BE fields, no signerCount/signedCount | Counts computed correctly or contract extended; not `undefined/undefined`; realistic mocks expose current mismatch. | R/G03 |
| AGR-007 | P2 UI | Copy link/email text, clipboard denied, mailto with Unicode/reserved chars | Clipboard content and encoded mailto correct; current origin and `/fi/sign` route; denial recoverable. No real mail sent by test. | C |
| AGR-008 | P1 API | Add/remove persisted signer in Draft/Partial/Completed/Cancelled | Allowed states mutate; forbidden states conflict409 desired, some currently500; no FE post-create controls. | C/R G08/B G22 |
| AGR-009 | P1 API/BE | Remove only/final-pending/already-signed signer from partial agreement | Define mutation policy; no stale PartiallySigned/final-PDF/signature evidence mismatch. | D/G32 |
| AGR-010 | P1 API/UI | Activate/deactivate/cancel; repeat; cancel Completed | Flags/state persist; completed cancellation rejected; lifecycle UI missing; signed viewers handled per explicit access contract. | C/R G08/B G22 |
| AGR-011 | P1 UI | Delay detail A, open B, resolve B then A; fail detail request | Modal displays B only, recoverable error instead of previous agreement or infinite loader. | R/G25 |
| AGR-012 | P1 UI/DEV | Admin document download before/after completion; token expired; missing disk file | Authenticated PDF downloaded with correct original/signed bytes; failure visible and no bogus empty file. | C/R G24 |
| SIGN-001 | P1 UI/DEV | Pending AG token → type signature+full name → sign | First signer partial, remaining pending; refresh prevents resubmission; name/time/signature persisted. | C |
| SIGN-002 | P1 UI/DEV | Last pending signer → sign → download | Completed status/time and signed count; file changes to signed PDF; all signature panels readable. | C |
| SIGN-003 | P1 UI | Missing/unknown token; signed/Completed/Cancelled/deactivated combinations | Correct invalid/done/cancelled/deactivated views; state precedence follows active check; no forbidden Sign button. | C |
| SIGN-004 | P1 UI/API | Deactivated unsigned vs signed token → metadata/file/sign | Unsigned file404/sign409; already-signed viewer may access document; valid metadata is still returned. | C |
| SIGN-005 | P1 API | Cancelled active agreement token → file/sign | Signing409; file access currently allowed. Decide cancellation revocation rather than silently asserting404. | C/D |
| SIGN-006 | P1 UI | Full name empty/spaces; blank draw/type pad; name201 | FE prevents blank name/pad; BE enforces200; do not send an empty PNG masquerading as a valid signature. | C |
| SIGN-007 | P1 UI/DEVICE | Mouse/touch/pen, two separate strokes, pointer exit/cancel, single tap | Correct scaled ink and no stroke-connector/page scroll; single tap currently counts as ink. | C/D |
| SIGN-008 | P1 UI/BE | Narrow/wide container, DPR1/2/3, resize/orientation, long typed name | Export stays800×200, ink retained or explicit reset; typed clipping/legibility policy recorded; no image >1200×400. | C/D |
| SIGN-009 | P1 API/BE | PNG2MiB/2MiB+1; width1200/1201, height400/401; bad magic/truncated/corrupt/zero-size IHDR | Structural guards400; corrupt body/zero dimensions need semantic handling; deterministic errors, not decoder500. | C/R G11 |
| SIGN-010 | P1 API | Raw base64/dataURI/malformed base64/null SignaturePng, name mismatch | Supported base64 forms accepted; malformed/null400 desired; identity-match requirement is not implemented. | C/R G11/D |
| SIGN-011 | P1 UI | Sign409 already-signed/cancelled/deactivated/concurrency,400 validation,500, network timeout | Friendly mapped message, loading clears, input preserved; reconcile post-timeout completed signing on reload. | C |
| SIGN-012 | P1 BE/DEV | Different pending signers submit simultaneously | All accepted signatures survive, one final completed artifact, bounded conflict response, no stale overwrites. | C |
| SIGN-013 | P1 BE/DEV | Same signer token submits two different PNGs simultaneously | At most one successful sign; losing request cannot overwrite winner's stored PNG/final PDF. | R/G16 |
| SIGN-014 | P1 BE | Fail PDF generation/store/commit after signature write; retry last signer | Agreement not falsely completed, no truncated signed PDF; recoverable state/artifact cleanup defined. | R/G16 |
| SIGN-015 | P2 UI/API | Return to pending/partial link hours later; deleted signer token; expiry wording | Removed token404; no actual time expiry exists; future expiration policy separate from current contract. | C/D |
| SIGN-016 | P2 UI | fi/en/sv sign routes; iframe unavailable; popup download blocked | Current English strings documented; localization/download/preview recovery are gaps, not translated-text passing assertions. | R/G22/G24 |

## Backend-only operations, invoices and tenant administration

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| OPS-001 | P1 API | Booking list from/to/status/employee filters; invalid query values | Correct tenant-filtered records, lower bound inclusive/upper exclusive; validate malformed query and empty results. | C |
| OPS-002 | P1 API | New→Confirm→Complete, repeat transitions, Completed→Confirm, Cancelled→Complete | Current cancelled guard409; define terminal-state transitions and prevent reversal if required. | C/D G13 |
| OPS-003 | P1 API | Assign active E, nonexistent/inactive E, overlapping/adjacent booking; unassign twice |409 inactive/overlap;404 missing; assignment snapshots/history persist; cancelled/completed assignment policy undefined. | C/D |
| OPS-004 | P1 API/BE | E assigned shift overlapping booking; unavailable default hours/certification | Current booking assignment ignores these constraints; decide employee eligibility integration. | D |
| OPS-005 | P1 API | Employee day schedule with date boundaries, timezone, bad date, empty day | Matches stored local-date string and employee ID; malformed date currently yields no rows rather than semantic validation. Decide rejection policy. | C/D |
| OPS-006 | P2 UI | Open admin bookings/calendar/status/history controls | No screen implemented; API tests carry coverage until UI exists. | B/G17 |
| INV-001 | P1 API/BE | Completed booking and profile → invoice/detail/PDF | One quantity1 line from snapshot, issuer/customer snapshot, EUR, UTC issue date, due date=terms, correct totals/number. | C |
| INV-002 | P1 API | New/Confirmed/Cancelled/missing booking; existing Issued/Paid invoice; voided prior invoice | Noncompleted/existing nonvoided409, missing404; after void new invoice permitted. | C |
| INV-003 | P1 BE/API | Two parallel invoices for same booking and first/year-counter races | At most one nonvoided invoice, unique numbers; serializable/idempotent protection currently missing. | R/G13 |
| INV-004 | P1 BE | Net0/0.005/39.995, VAT0/25.5/50; edit service/profile after issue | Decimal rounding documented; snapshot/PDF stable after source change. | C |
| INV-005 | P1 API | PaidAtUtc missing/default/future; repeat paid; void Paid; mark-paid Voided | Voided→paid409; current repeats/void Paid allowed; decide accounting state/time policy. | C/D G13 |
| INV-006 | P1 API | List status/from date incl UTC timestamp; nonexistent PDF; token roles | Correct filtered records and authenticated PDF; invalid/date-kind DB errors not leaked as500. | C/R |
| INV-007 | P2 UI | Create/paid/void/detail/download from admin | Helpers only, no page; use API coverage now. | B/G17 |
| TEN-001 | P0 API/BE | MasterProvisioner provisions unique slug + owner → read registry/profile | Realm/client/owner/profile created with deterministic partition; secret temporary password not leaked to other tenants. | C |
| TEN-002 | P1 API | Slug2/3/63/64, uppercase/hyphen start; names200/201; duplicate/concurrent slug | Valid3–63 format; validation400; existing duplicate409; parallel attempts produce one usable registration. | C |
| TEN-003 | P1 BE | Keycloak creation succeeds then registry/profile save fails → repeat provision | Define repair/rollback; no orphan realm or permanently half-provisioned tenant. | R |
| TEN-004 | P0 API/DEV | Non-master/NoRole master user → system tenant list/activate/suspend | Dedicated system authorization desired; current realm-only gate must not be treated as adequate permission proof. | R/G14 |
| TEN-005 | P0 API/DEV | Suspend A while clients/cache warm → public/admin/read/write; reactivate | Active status enforced, local cache invalidated; no B impact; verify across multiple API instances separately. | C |
| TEN-006 | P1 API | Profile VAT0/50, terms0/120, notice0/720; out-of-range, locale invalid2chars/timezonebad | Numeric guards400; locale/timezone membership validation missing, must not break later booking lookup. | C/R G11 |
| TEN-007 | P1 API | Missing profile → update → GET; issuer address null; invalid bank fields | Stable profile identity/readback; reject malformed nested data consistently; bank validation policy explicit. | C/R |
| TEN-008 | P1 API | Page key1/2/41/42, uppercase, dictionary missingfi/null/emptyfi; read with en fallback | Key/fi rules400; GET expected localized page data; empty-value policy explicit. | C/D |
| TEN-009 | P2 UI/API | Change public content/profile then reload website | API updated, static components do not consume content; no settings/content/provisioning UI. | B/G18 |
| TEN-010 | P1 API/DEV | `/me` with genuine roles, inactive tenant, token missing sub | Document actual raw role JSON strings; FE must not assume normalized role array. | C |

## Cross-cutting failure, security, persistence and PWA cases

| ID | Priority/layer | Setup → action | Observable result / oracle | Kind |
| --- | --- | --- | --- | --- |
| X-001 | P1 UI | Each list/create/edit/deactivate/delete/download: abort,400,401,403,404,409,429,500 | No stuck loading/unhandled rejection; clear failure vs empty; draft preserved; unauthorized mutation absent. Expand per reachable feature. | R/G24 |
| X-002 | P1 UI/DEV | Success mutation→navigate away/back within30s; failed mutation; external update | Cache invalidates only after success; stale external update bound documented; no fabricated freshness assertions. | C |
| X-003 | P0 API/DEV | Object IDs/tokens reused across A/B for all tenant repositories |No cross-tenant reads/writes/downloads; inspect persisted partition, not merely generic404. QC G01 is special priority. | C/R G01 |
| X-004 | P1 API | 30 public requests/IP/minute then31st, wait window expiry; authenticated public call |31st429 with current empty body; later succeeds; do not expect Retry-After until added. | C |
| X-005 | P1 DEV/API | Two clients behind Cloudflare tunnel, different forwarded IPs | Limits isolate intended clients; current proxy-IP handling requires deployment evidence. | R/G31 |
| X-006 | P1 API/BE | Malformed JSON, null lists/objects, string numbers, unknown enum, oversized request | Predictable400/413 where contract exists; never raw stack traces/secrets; record unmapped500 cases as defects. | C/R G11 |
| X-007 | P0 UI/API | HTML/script-like strings in every displayed field and PDF/email input | Plain text/escaped markup; no executed script or dangerous URL navigation; do not strip valid Unicode unnecessarily. | C |
| X-008 | P1 BE/DEV | Duplicate clicks/concurrent writes/network-loss-after-commit for every mutation | Exactly defined durable outcomes; compare entity count/history/files; identify non-idempotent endpoints explicitly. | C/R |
| X-009 | P1 BE | Database offline/schema initialization fails; `/healthz` and data request |Healthz200 is current liveness behavior; data failures not mistaken for readiness or healthy release. | C |
| X-010 | P1 UI/DEV | Admin file response nonPDF/empty/corrupt; token refresh; blob URL revoked | Readable actual artifact, correct suggested filename; rejection produces usable error; no silent no-op. | R/G24 |
| X-011 | P2 UI | Modal Escape/cancel/focus trap/restoration; blank labels/canvas/iframe; screen reader error | Accessible names and alert scope; do not collide with Next route announcer; label gaps recorded, not CSS workaround. | C/R |
| X-012 | P2 UI/HOST | New deployment while old tab open; stale chunk404; reload direct route | Essential navigation/recovery; static fallback remains usable; no hidden content requiring failed JS animation. | C |
| PWA-001 | P2 UI/DEVICE | Synthetic beforeinstallprompt: accept/dismiss; appinstalled; standalone | Correct installed badge/prompt consumption; dismissed prompt does not falsely claim installed. | C |
| PWA-002 | P2 UI/DEVICE | iOS Safari user-agent → Install and instructions; unsupported browser | iOS guide shown; unsupported always-visible button currently no-op and needs explicit fallback/hidden state. | C/R G28 |
| PWA-003 | P1 HOST/DEVICE | HTTPS built site with service workers allowed → fresh install→offline | No offline cache population currently; decide offline contract; no claim that intercepted network fallback is sufficient. | D/G28 |
| PWA-004 | P0 HOST/DEVICE | Login A, cache/fetch protected data, logout, offline navigation | No protected responses leaked after logout; verify actual worker scope and cache contents. | C |
| PWA-005 | P2 HOST/DEVICE | Manifest/icon URLs, start_url `/admin/`, worker update between builds | Assets resolve, installed start route works, update takes control without breaking current task. | C |

## Execution notes

- Use the real request/response schema from the endpoint appendix. For example,
  agreement detail mocks must not add counts that BE does not send.
- For expected business-rule failures marked R or D, link the gap and report the
  observed current outcome. Do not weaken assertions or use broad catch-all skips.
- Precise hour/day, rounding, parity, PNG decoding and conflict-injection cases
  are best asserted in BE tests; a small real Playwright/API integration subset
  proves wiring and persistence. Browser mocks cannot prove transaction safety.
- Cases marked B still matter: implement APIRequestContext coverage today and
  explicitly block only the browser interaction until its UI feature is delivered.
- After fixing a gap, change its cases to supported contracts and add the exact
  regression to the relevant CI suite. Keep IDs unchanged for traceability.

## QC contract correction after user clarification

All checklist activities are optional. Submitting any subset, including none,
records the day/occurrence; notes are optional. Do not classify unchecked activities
as exceptions or failed work. Monthly counts use submitted selected items only,
with distinct tenant-local days and distinct occurrence starts per shift/item.
Duplicate cleaners on one occurrence never multiply activity frequency. Missing
records and unchecked activities must remain distinct. Summary shares expire
in30days, are revocable, freeze totals, and remove all cleaner evidence/tokens.
See the current [workflow/test contract](../features/quality-cycle-workflow.md).

## Issue49 onboarding cases added

Native setup email must target the bound tenant account and /account/ landing,
with expiring actions and no temporary credentials. Check missing/invalid frontend
auth URLs, unregistered redirects, absent employee role/SMTP, password masks,
admin-only ownership attributes, orphan/unverified/conflicting existing users,
concurrent claims, failed persistence before send, failed SMTP delivery and retry,
60-second duplicate clicks, completed-user empty-action reinvite, optional owner
compatibility, unlinked employee denial, pending synchronization denial, identical
update retry recovery, verified/current-role checks and old JWTs after demotion or
deactivation. Unrelated profile edits must preserve credentials and sessions.
The original generic registration assumptions are superseded by the
[employee invitation contract](../features/employee-invitations.md).

## Issue65 manager and lifecycle test additions

| ID | Layer | Setup/action | Observable result |
| --- | --- | --- | --- |
| AUTH-MGR-001 | API/DEV/UI | Verified manager with two tenant branches and a second tenant | Both own branches visible, foreign shift GET/PUT404, no foreign mutations |
| AUTH-MGR-002 | API/UI | Employee/staff/no-role direct manager/admin access | 403/401; no operational/sensitive handler calls or child reads |
| AUTH-MGR-003 | API/DEV | Manager roster and self work with marker payroll/email/subject/token data | Exact nested allowlists; administrative markers absent |
| AUTH-MGR-004 | API/DEV | Promote/demote manager; stale token; inactive/unverified/pending/unbound | Managed roles reconciled, password/unrelated roles preserved, stale access403 |
| AUTH-MGR-005 | UI | Manager create/edit/cancel each schedule, assign/remove, load/save retry | Safe payloads only, preserved input after failure, no admin/QC/billing controls |
| SHIFT-RESTORE-001 | API/UI | Deactivate, include inactive, reactivate, repeat | Preserved schedule/parents/template/notes/assignment periods/history; one new active plan |
| SHIFT-RESTORE-002 | DB | Concurrent admin template update and manager edit/reactivate | Losing request409; winning admin data and active state preserved |
| SHIFT-RESTORE-003 | BE/API | Missing/inactive/foreign parent or expired validity | Invalid parent rejects without write; expiry unchanged, no out-of-range work |
| CLOCK-REASON-001 | UI/API | Server409/403 vs network failure | Actual safe reason shown; saved-response recovery remains; selections preserved |
| CLOCK-FUTURE-001 | UI/BE | Upcoming UTC occurrence, advance to scheduled instant | Start initially disabled, enabled by timer; API remains authoritative |

These cases are implemented alongside65/82/96/97; production evidence is tracked
separately from mocked UI assertions. Dependency advisory95 and company-index
scope98 are open findings, not completed by this authorization release.
# Language-game provider fallback — issue105

| ID | Layer | Setup/action | Observable result |
| --- | --- | --- | --- |
| GAME-FALLBACK-001 | BE | Gemini returns valid English/Finnish/string or fi-object hints | Same content returned, not marked as sample |
| GAME-FALLBACK-002 | BE | HTTP401/429/5xx, network/deadline failure | One bounded attempt; valid sample at translator level |
| GAME-FALLBACK-003 | BE | Missing/wrong/blank output or corrupt JSON | Sample returned; unexpected programming exceptions still propagate |
| GAME-FALLBACK-004 | BE | All six levels, ten recent samples, exhausted larger history | Twelve complete samples per level; recent text avoided; bounded least-recent cycling |
| GAME-FALLBACK-005 | BE | Pre-cancel/during generation/ignored cancellation, start and next round | Cancellation propagated; no group/round mutation or save |
| GAME-FALLBACK-006 | BE | Full game with failed provider | Create/join/start/rotation/end complete; valid distinct sentences/hints and standings |

Contract: [game sample fallback](../features/game-sample-fallback.md).


## Shift evidence appendix — issue102

| ID | Layer | Setup/action | Observable result |
| --- | --- | --- | --- |
| QC-EVID-001 | Domain/API/UI | Photo + null/blank/whitespace note | 400 or inline rejection; no save/checklist/timestamp mutation; note focused; inputs retained |
| QC-EVID-002 | Domain/UI | Notes alone; no evidence; remove last image | Completion allowed, conditional note requirement removed, checklist remains optional |
| QC-EVID-003 | UI | Delayed/error photo read; failed submission and retry | No completion request while reading; visible error; existing photos/notes/selections retained |
| QC-EVID-004 | Report/PDF | Submitted vs draft records; two cleaners; branch/shift filtering; UTC date rollover | Only submitted evidence in correct group; scheduled date uses tenant timezone; original counts unchanged |
| QC-EVID-005 | PDF | Multiple portrait/landscape/phone photos; long note; note-only record | Real image resources and visible rendered photos; date/note per photo; long text paginates; no clipped content |
| QC-EVID-006 | PDF/API | Corrupt/base64/unsupported/remote/oversized legacy photo; older report; public share | Visible fallback, date/note retained, no remote reads or report failure; old snapshots work; shared PDF omits private evidence |

Implementation/test contract: [shift photo reporting](../features/shift-photo-reporting.md).
