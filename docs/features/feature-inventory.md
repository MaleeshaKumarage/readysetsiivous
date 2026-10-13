# ReadySetSiivous feature inventory and implementation gaps

Reviewed on 2026-10-13 against commit `ceff5bd`, branch
`feat/quality-cycle-auto-dispatch`. This is a source-based inventory, not a claim
that this commit is deployed in dev or production. Two untracked Quality Cycle
dispatcher files exist locally; they are work in progress, not released features.

Use this document with the [edge-case catalogue](../testing/edge-case-catalog.md),
[Playwright plan](../testing/playwright-test-plan.md),
[HTTP endpoint inventory](api-endpoints.md), and [environment guide](../environments.md).

For per-operation FE-vs-BE coverage, see the
[feature capability matrix](feature-capability-matrix.md). For proposed roles,
permission boundaries and workspace grouping, see the
[technical lead review](technical-lead-review.md).
The [GitHub issue register](issue-register.md) maps every documented gap to an
implementation or discussion issue.

Scope: **24 feature groups, 77 controller endpoints, 32 FE/BE gaps, and 203
edge-case families**. Families with parameter sets expand into multiple tests.

## Reading the inventory

**Implemented** means a reachable screen/controller and its supporting code exist.
**Partial** means only some backend capabilities have UI controls or integration is
incomplete. **BE-only** means there is an API but no corresponding application
screen. **Gap** is an observed omission or risky code path; it is not automatically
a requested new feature. Business-policy decisions are explicitly labelled.

Test outcomes in the catalogue distinguish supported contracts from desired
behavior requiring a fix. Do not make tests accept a defect merely because it is
the current behavior, or expect an unimplemented feature to pass.

## Feature map

| ID | Feature | Frontend / entry point | Backend | Status / case prefix |
| --- | --- | --- | --- | --- |
| F01 | Landing page and navigation | `/fi/`, `/en/`, `/sv/`; desktop/mobile menu, anchors, emergency CTA | Static content | Implemented; WEB |
| F02 | Language selection and root redirect | `/` redirects from saved `lang`; fi default; switcher preserves path/query/hash | Localized service/content lookup falls back to fi | Implemented; LANG |
| F03 | Theme and motion | Saved light/dark theme, OS preference, public animations; admin forced dark | None | Implemented with restoration/accessibility risks; THEME |
| F04 | Quote / WhatsApp contact | Service, property size, city, preferred date; CTA, floating button, phone/email links | No quote submission API | FE-only by design; QUOTE |
| F05 | Public service catalogue | Landing cards show up to four API services or static fallback; booking shows full API list | Public service list, active filter, sorting, localization | Implemented; CAT |
| F06 | Public booking creation | `/{lang}/varaus/`: service → date/time → details → reference | Availability, creation, snapshots, counter, serializable retry | Implemented; BOOK |
| F07 | Customer booking lookup/cancellation | No screen or controls | Lookup by number + phone; cancellation by number + phone | BE-only; LOOK |
| F08 | Admin shell and identity | `/admin/`, navigation/dashboard, sign-in/out, responsive drawer | Keycloak JWT, realm roles, `/me`, tenant resolution | Partial role-aware UI; AUTH |
| F09 | Service administration / images | `/admin/services/`: list, create, edit, activate/deactivate | Full localized fields, image upload, soft-delete | Partial/data-loss risks; SVC |
| F10 | Employee administration / invitations | `/admin/employees/`: list, create basic fields, deactivate | Full employee CRUD, invite email, hours, skills, areas, pay, certifications | Partial; EMP |
| F11 | Companies and branches | `/admin/companies/`: first 100 companies, expand branches, create company/branch | Search/paging, CRUD, deactivate | Partial; COMP |
| F12 | Recurring shifts and assignment | `/admin/shifts/`: create five schedule choices, assign/unassign employees, deactivate | Six schedule types, CRUD, occurrences, bounded overlap checks | Partial; SHIFT |
| F13 | Quality Cycle templates and reporting | `/admin/quality-cycle/`: create/edit/delete templates, submissions, shift filter, PDF | Templates, dispatch, forms, monthly PDF | Partial/integration risks; QC |
| F14 | Public Quality Cycle checklist | `/quality-cycle?token=...`: items, photo files, notes, submit/thanks | Token lookup and one-time submission | Implemented with validation/tenant gaps; QCP |
| F15 | Agreement administration | `/admin/agreements/`: company/PDF/signers, list/details, copy links/email text, mailto, document download | Create/read, add/remove signer, cancel, activate/deactivate | Partial; AGR |
| F16 | Agreement signing | `/{lang}/sign/?token=...`: preview, draw/type, submit, completion/download | Token signing, PNG guards, concurrent updates, final PDF | Implemented; SIGN |
| F17 | Booking operations and employee day schedule | Client helpers exist; no booking/calendar screen | Booking list/detail/status/assignment; employee day schedule | BE-only; OPS |
| F18 | Invoices | Helpers exist; no invoice screen | Create from completed booking, list/detail, paid/void, PDF | BE-only; INV |
| F19 | Tenant profile/content management | Client has profile read helper; no settings/content screen | Profile GET/PUT and page-content PUT/public GET | BE-only UI; TEN |
| F20 | Tenant provisioning and lifecycle | No system administration UI | Provision/list/activate/suspend; Keycloak realm setup | BE-only; TEN |
| F21 | Digital contact card and QR | `/{lang}/card/`, vCard download, phone/email/WhatsApp/home; landing QR | Static vCard; external QR image provider | Implemented; CARD |
| F22 | Legal pages and search metadata | `/{lang}/privacy/`, `/{lang}/terms/`, metadata, schema, sitemap, robots | Static content | Implemented; LEGAL |
| F23 | Admin PWA installation | Install button, Android/browser prompt, iOS instructions, manifest, service worker | Static assets | Installation exists; offline support incomplete; PWA |
| F24 | Cross-cutting reliability/security | Form errors, caches, downloads, optimistic UI/loading states | Problem details, concurrency, liveness, public rate limit, tenant storage | Partial; X |

Public landing sections currently rendered: Hero, EmergencyCTA, QuoteForm,
Services, KeySecurity, EcoPetBadges, FAQ, About, Testimonials, QRSection,
PaymentBanner, Footer and FloatingWhatsApp. PricingTransparency, Checklist,
Kotitalousvahennys and ResponsibleEmployer components exist but are intentionally
commented out in the page. Do not require those hidden sections in happy-path tests.
FAQ has six topics and permits one open panel at a time.

## Behavior and field contracts

### Public bookings and booking operations

Creation fields: `localDate`, `startTime`, `serviceId`, `customerName`,
`customerPhone`, `customerEmail`, `street`, `postalCode`, `city`, optional `notes`.
Name/phone/street/city are required; maxima are 150/40/200/100 characters.
Email validation is conditional on a non-empty email. Date/time validation checks
format with regex; semantic parsing happens later. Postal code and notes have no
application-level length/shape limits. There is no client field-by-field validation
in the booking details step.

Availability uses the tenant timezone (fallback Europe/Helsinki), 08:00–20:00
business hours and 30-minute candidate starts. Service duration determines end
time. Any non-cancelled booking blocks overlap, irrespective of employee. Advance
notice defaults to two hours and applies to listing and creation. Inactive services
return no slots and cannot be reserved. This is a single-team capacity model,
not an employee-availability scheduler. `DefaultHours`, certifications, service
areas and `AllowUnstaffedBookings` are not used to determine public availability.

Booking stores service/price/VAT snapshots, address, customer, local start strings,
UTC start/end, number `RSS-<UTC start year>-<6-digit sequence>`, and a six-character
reference. Reservation retries use a fresh serializable session (three attempts).
No booking request idempotency key exists. A timed-out response may therefore
leave a committed booking that the UI reports as failed.

Public lookup exposes a small DTO only after number+trimmed phone match. Wrong
phone or missing number produces 404. Cancellation rejects wrong phone with 403
and rejects already-cancelled/completed bookings with 409. Customer reference is
not used as the lookup/cancellation credential.

Admin booking statuses are New, Confirmed, Cancelled, Completed. The shared status
handler blocks an already-Cancelled booking but otherwise has few transition
guards. In particular Completed→Confirmed is currently possible. Assignment
checks that employee exists/is active and has no overlapping booking; it does
not check shift assignments, qualifications or default work hours. Unassignment
is repeatable and adds history. Booking operations have no frontend screen.

### Services, employees, companies and branches

Service slug regex permits 2–61 lowercase alphanumeric/hyphen characters, starting
with alphanumeric. Name must contain `fi`; the validator does not ensure that its
value is non-empty. Duration 15–1440 minutes, net price ≥0, VAT 0–50%, sort order
0–10000. Fields also include category, currency, featured, icon, image and localized
description/additional info. Missing requested-language text falls back to fi;
an existing empty translation does not trigger fallback.

The current service form writes only fi, hardcodes EUR/Sparkles/order=0/featured=false,
and sets additional info to null. Editing can overwrite fields not presented in
the form. Status toggles also omit image URL/additional info. VAT zero is converted
to 25.5 by `Number(value) || 25.5`; duration/price are clamped/defaulted client-side.
Image upload accepts jpg/jpeg/png/webp extensions and at most 5 MiB, but does not
validate image content. Uploaded public names are generated GUIDs at the volume
root; nested agreement files are never served by the public file provider.

Employee BE fields: email, first/last name, phone, role (`admin` or `employee`),
color, weekday work hours, skills, service areas, nullable pay rate, certifications
(name/expiry), notes, active status, Keycloak user ID. First/last name 1–100 chars,
pay rate ≥0, certification name 1–200 chars. The UI creates only basic identity
fields and sends empty advanced collections. Creation checks existing email;
update does not perform the same duplicate-email check. Invite sends a registration
link, returns an empty temporary password, and does not create/link the Keycloak
user or assign their realm roles. Deactivation is a data flag, not Keycloak logout.

Company business ID/name are required and trimmed; a database unique index exists
for business ID. Contact/address formats are mostly not validated. Company list
supports case-insensitive name/business-ID search, skip clamped to ≥0 and take
clamped to 1–200; FE requests the first 100. Branch requires a non-empty
company GUID and name, but its handler does not resolve the parent company.
Company and branch edit/deactivate APIs exist without corresponding UI actions.

### Shifts and Quality Cycle

Numeric schedule enum: 0 daily same time; 1 different time per weekday; 2 weekly
(one or multiple weekdays); 3 biweekly; 4 on-call; 5 monthly day 1–31. Fixed
schedules require end>start. TimeSpan conversion accepts HH:mm or other parseable
TimeSpan values; time-of-day bounds/invalid weekday enum values need additional
validation. Biweekly parity is anchored to Monday 2024-01-01, not ISO week number.
Monthly day 31 produces no occurrence in a month without day 31. On-call produces
no concrete occurrences. Occurrence range must have to>from and span ≤366 days.

Create shift verifies company, branch and branch ownership. Update does not
repeat those relationship checks. Calculation currently uses UTC wall-clock
times, expands whole dates inclusively, and does not honor Shift.ValidFrom,
ValidUntil or IsActive. UI does not expose different-times-per-weekday, validity
dates, editing/reactivation, or an occurrence/calendar view. Biweekly UI accepts
multiple selected weekdays but sends only the first. Changing company does not
immediately clear an already-selected branch; response races need coverage.

Employee assignment checks other assigned shifts within a maximum 180-day window,
creates or reactivates one assignment, and soft-deactivates on removal. Touching
time boundaries are not overlaps. Inactive employee/shift guards are absent in
the handler. Two simultaneous assignments can both pass a read-before-write
overlap check; verify against a real database. UI batch assignment is sequential
and can partially succeed without a clear per-employee result.

Assignment with a QC template creates a form for the first generated occurrence
in the next 30 days and attempts email delivery. Because calculation expands
from.Date, that first occurrence may already have started or ended today.
The batch dispatch endpoint generates a selected day's forms and skips existing
occurrence+employee forms; concurrent dispatch has no matching compound unique
index. No wired hosted worker exists for recurring dispatch at shift end.
`IQualityCycleDispatcher.cs` and `QualityCycleDispatcher.cs` are untracked and not
registered/called by Program; do not document them as active automation.

QC templates accept title, description, items, optional company/branch IDs and
active status. FE caps adding fields at 10; BE does not cap item count or validate
company/branch relationships. BE filters blank item texts and errors if none
remain. Updates/deletes do not automatically rewrite historical forms. Filtering
includes templates with null scope as global matches.

Public form stores immutable template text/order, checked flags, photo URL strings,
notes, token, occurrence start/end and submitted time. Submit matches by case-
insensitive trimmed text, then by index. Missing/mismatched items and all-unchecked
submission are accepted; requiring all items is a policy decision. Repeated
submission throws InvalidOperationException, currently mapped to 500. FE photos
are FileReader data URLs stored inside the form, not uploads to an image endpoint.
No item/photo/notes application size limits exist. File-picker preview exists;
drag-and-drop text is shown but no drop handler exists. Photo count is displayed
in admin; no image viewer is exposed there. Public submitted time is not refreshed
from the submit result, so the thank-you screen can show client time.

QC repository injects bare `IDocumentSession`, unlike tenant-bound repositories;
Program does not bind this session to HttpTenantContext. Source inspection indicates
default-partition storage. A two-tenant dev test must establish actual runtime
isolation before relying on QC list/token tests. This was not live-exploit tested.

### Agreements and signatures

Create is multipart title/companyId/pdf/signersJson. Title 1–200 chars, company
GUID required, at least one signer, name/email validation. No company existence,
PDF magic/parseability, signer-uniqueness or agreement-specific upload size check
exists. Framework/server request limits still apply. Original PDF and signatures
are stored before final document commit; failures can leave orphaned files.

List includes signerCount/signedCount; **detail DTO does not**, although FE detail
extends the list interface and displays these fields. Contract-accurate mocks are
essential. UI can add/remove rows only before initial creation; post-create
signer/lifecycle APIs have helpers but no controls. Signing links use current
origin and `/fi/sign`, emails are copied text or mailto links, not backend delivery.
Partially filled signer rows are silently filtered out before POST.

States: Draft→PartiallySigned→Completed; Cancelled is a separate terminal signing
state; IsActive is independent. Signing requires valid token, active/non-cancelled
agreement and pending signer. Already signed/cancelled/deactivated produces 409.
PNG ≤2 MiB, PNG signature bytes, header dimensions ≤1200×400; name 1–200 chars.
The FE exports an 800×200 PNG, rejects missing typed full name or empty pad, accepts
drawn/typed signatures, and treats a single tap as ink. Typed signature text and
full-name field are separate; identity matching is not enforced.

Final signer triggers signed PDF generation; original remains for partial signing.
Concurrency retry exists for different signers, but file writes happen outside
the document transaction and to deterministic paths. Same-token races, failure
after saving a signature, and final PDF conflict/retry need database+file tests.
Token expiry/revocation metadata is absent despite the FE's "expired" wording.

Deactivated unsigned signer cannot fetch file; an already-signed signer may view
it. Cancelled status is not separately checked by the public file handler, so
cancellation prevents signing but does not itself revoke file access. Whether to
revoke access is a policy decision. Add/remove signer permits editing partial
agreements; removal can remove signed signers or the final pending signer without
recomputing completion. Some lifecycle conflicts throw InvalidOperationException
and return 500 rather than 409.

### Invoices, tenant management and infrastructure

Invoice creation requires Completed booking, no non-voided invoice for it and a
tenant profile. It snapshots customer/issuer/service pricing, creates one quantity-1
line, rounds VAT to two decimals, uses EUR, issue time UTC and due date from payment
terms. Mark-paid blocks Voided only; repeated payment overwrites paid time. Void
permits Paid invoices. Draft is a domain constant without a create-draft workflow.
Counter/check/create lacks public booking's fresh serializable retry; simultaneous
invoicing requires integration tests. PDFs are authenticated downloads.

Tenant profile settings include issuer address/bank/contact fields, timezone,
locale, VAT, payment terms (0–120 days), unstaffed flag, notice (0–720 hours).
Locale validation requires two chars, not membership in fi/en/sv; timezone string
is not validated. Dynamic pages require a lowercase page key (2–41 chars) and fi
value. Public home sections do not consume the public content API.

Provisioning creates a Keycloak realm/client/owner plus registry/profile records;
duplicate slug returns 409. Steps span external Keycloak and database writes,
without rollback of all effects on partial failure. System access checks master
realm equality, not a dedicated provisioning role. Suspension invalidates local
status cache. `/me` returns raw realm_access JSON strings, not a normalized role
array. The admin shell gates only authentication, not admin role, while API
policies enforce roles.

Public paths have a process-local 30 requests/minute/IP limit with an empty 429
body and no Retry-After. It executes before authentication; it is not limited to
anonymous users. Proxy forwarded-IP handling is not wired, so dev/prod tunnel
behavior needs verification. Errors generally use RFC 7807: validation 400,
NotFound 404, known domain/concurrency conflict 409, UnauthorizedAccess 403;
unmapped exceptions 500. Healthz is liveness-only, not DB readiness.

Frontend admin GET cache is keyed only by path with 30-second TTL; successful
generic mutations clear it. Some direct image/download requests bypass refresh
and cache invalidation. Ordinary admin fetch exceptions have no common catch or
timeout. Most screens cannot distinguish failed list loading from empty data.
Authenticated output-cache attributes exist, but default output-cache behavior
must be verified before assuming those responses are cached.

PWA service worker intercepts GET network requests and checks cache on failure,
but never populates a cache. There is no complete offline contract. Install button
is always shown even if no install prompt/iOS path exists; it may do nothing on
unsupported browsers. Current Playwright configuration blocks service workers.

## Missing pieces / defect register

Priority here indicates test/improvement urgency, not proof of production severity.
P0 security/data isolation; P1 correctness/data integrity; P2 feature coverage/UX.
"Source-confirmed" describes code behavior; "runtime check" requires dev evidence.

| Gap | Priority / layer | Finding and evidence | Impact / next verification |
| --- | --- | --- | --- |
| G01 | P0 BE, runtime check | QC repo uses bare IDocumentSession rather than ITenantSession (`QualityCycleRepository`, `Program`) | Create QC data in tenant A; tenant B must neither list it nor access A token. |
| G02 | P1 FE, source-confirmed | Service edit/status payload omits image/additional info; edit overwrites translations/icon/order/featured (`services/page.tsx`) | Edit one field of fully populated service; reload raw DTO and ensure unrelated values survive. |
| G03 | P1 FE/BE contract | Agreement detail lacks counts expected by FE (`AgreementQueries`, `AgreementDetail` type) | Render actual detail JSON, never a list-shaped mock; counters must be correct. |
| G04 | P1 BE | No recurring QC hosted dispatcher is registered; two dispatcher files are untracked | Time-driven dispatch cannot be asserted as working; worker and idempotency remain missing. |
| G05 | P1 BE | Shift calculation ignores validity/active status, uses whole UTC days (`ShiftScheduleCalculator`) | Verify invalid/inactive shifts and time bounds; define Helsinki-vs-UTC schedule policy. |
| G06 | P1 BE | Shift assignment lacks inactive guards and transactional uniqueness/overlap protection (`ShiftApp`, Marten indexes) | Parallel requests/inactive employees; no unsafe duplicate/conflicting assignments. |
| G07 | P1 BE | QC dispatch links hardcode production; email failures skipped, no delivery retry state (`ShiftApp`, `QualityCycleApp`) | Dev emails must link to dev; distinguish created forms from delivered messages. |
| G08 | P1 BE | Generic exception mapping turns repeated QC submission/some agreement lifecycle conflicts into 500 (`Program`, handlers) | Defined conflict should return 409 and a usable FE message. |
| G09 | P1 BE | Employee invitation does not link user/roles; deactivation does not revoke identity (`EmployeeCommands`) | Register invitee; prove intended realm roles and disabled-employee access policy. |
| G10 | P1 BE | Missing parent checks in branch creation, shift update, QC scope and agreement creation | Reject foreign/nonexistent IDs without writing orphan/cross-company entities. |
| G11 | P1 BE | PDF/PNG/photo semantic validation and size policies are incomplete | Corrupt files, fake MIME/extensions, malformed base64 and huge photos need predictable 400/413. |
| G12 | P1 FE/BE | Booking has no idempotency key; FE collapses failure statuses and timeouts (`api.ts`, BookingFlow) | Lost success response must not mislead customer or create duplicate retry records. |
| G13 | P1 BE | Booking/invoice transition guards are incomplete (`BookingCommands`, `InvoiceCommands`) | Define terminal-state policy before asserting desired transitions. |
| G14 | P1 BE, runtime check | System tenant actions check master realm only; JWT audience validation disabled (`SystemController`, KeycloakJwt) | Role/audience-negative tests with genuine tokens, not injected mock booleans. |
| G15 | P1 FE/BE | QC submission item fallback and duplicate-text matching are ambiguous; concurrent forms lack compound uniqueness | Preserve template text/order and independent check states; reject or define mismatch handling. |
| G16 | P1 BE | Same-token signature races write same PNG/PDF paths before optimistic commit (`SignAgreementCommand`) | Losing request must not replace winner's artifact; inspect downloaded PDF signatures. |
| G17 | P2 FE | No customer lookup/cancel or admin bookings/invoices/schedule screens | Use APIRequestContext now; UI tests blocked until screen exists. |
| G18 | P2 FE | No tenant settings/content/provisioning UI; public content API unused | Document as BE-only; test API separately, no fabricated settings selectors. |
| G19 | P2 FE | Employee advanced fields/edit/invite/reactivation absent | Test API contracts now; UI coverage limited to actual create/deactivate controls. |
| G20 | P2 FE | Company/branch editing/deactivation/search/paging absent; lists capped at 100 | Records beyond first page inaccessible; selectors must not silently select wrong company. |
| G21 | P2 FE | Shift editing/validity/type-1/occurrences absent; batch assignment lacks partial-failure UX | BE-only cases and failed batch reconciliation needed. |
| G22 | P2 FE | Agreement post-create signer/lifecycle controls absent; signing UI English on all language routes | Keep before-create rows distinct from persisted signer operations; localization incomplete. |
| G23 | P2 FE/BE | QC FE limit 10 vs BE unlimited; no scope/active/date-filter controls; photo viewer/drop handler missing | Preserve unsupported scope on edit; enforce agreed limits server-side. |
| G24 | P2 FE | Admin network throws can leave spinners; failed lists look empty; raw uploads/downloads bypass token refresh | Abort/retry/session expiry matrix must exercise real adapters. |
| G25 | P2 FE | Uncancelled service/language/company/detail requests can overwrite current state | Deliberately reorder responses, verify selected route/entity owns displayed data. |
| G26 | P2 FE | VAT=0 defaults to 25.5; booking labels net service price as hourly and assumes EUR | Test exact submitted values and clarify price/time/currency presentation. |
| G27 | P2 FE | Admin allows all authenticated users to see actions; normal mock makes every authenticated user admin | Role-aware actions/forbidden feedback are untested and not implemented consistently. |
| G28 | P2 FE | PWA install no-op and offline cache absent | Dedicated secure-origin service-worker/browser installation tests; decide offline scope. |
| G29 | P2 FE | Footer links to hidden #checklist; QR hardcodes production and depends on external image provider | Broken-anchor and environment-aware navigation cases. |
| G30 | P1 BE/FE, runtime check | Public JWT tenant resolution prefers issuer realm over URL slug (`TenantResolutionMiddleware`) | Token A + public slug B must follow an explicit policy; reject mismatch or consistently use URL tenant. |
| G31 | P2 BE, runtime check | Rate limit may see proxy IP; no forwarded-header wiring/Retry-After (`Program`, limiter) | Independent clients behind tunnel; 31st request then recovery without affecting prod. |
| G32 | P1 BE | Removing pending/signed signers can leave inconsistent partial status/artifacts (`RemoveSignerCommand`) | Define mutation rules and recompute or reject; audit original signer evidence. |

## Evidence index

Paths below are repository-relative. Endpoint appendix links each route to its
controller; use source at the reviewed commit if line numbers drift.

| Area | Primary evidence |
| --- | --- |
| Public routes/sections | `site/src/app/[lang]/page.tsx`, `site/src/components/*`, `site/src/context/*`, `site/src/i18n/*` |
| Booking | `site/src/app/[lang]/varaus/BookingFlow.tsx`, `site/src/lib/api.ts`, `api/CleaningSuite.Application/Bookings/*`, `api/CleaningSuite.Infrastructure/Persistence/BookingRepository.cs` |
| Admin operations | `site/src/app/admin/*/page.tsx`, `site/src/lib/adminApi.ts`, `api/CleaningSuite.Api/Controllers/*AdminController.cs` |
| Auth/tenants | `site/src/lib/auth.ts`, `site/src/app/admin/layout.tsx`, `api/CleaningSuite.Api/Tenancy/*`, `api/CleaningSuite.Infrastructure/Auth/KeycloakJwt.cs`, `api/CleaningSuite.Application/Tenants/*` |
| Shifts | `api/CleaningSuite.Domain/Shifts/*`, `api/CleaningSuite.Application/Shifts/ShiftApp.cs`, `GetShiftOccurrencesQueryValidator.cs`, `ShiftRepository.cs` |
| Quality Cycle | `api/CleaningSuite.Application/QualityCycle/QualityCycleApp.cs`, `api/CleaningSuite.Domain/QualityCycle/*`, `QualityCycleRepository.cs`, `QualityCyclePdfGenerator.cs`, `site/src/lib/qualityCycleApi.ts` |
| Agreements | `api/CleaningSuite.Application/Agreements/*`, `api/CleaningSuite.Infrastructure/Agreements/*`, `site/src/app/[lang]/sign/SignPageClient.tsx`, `SignaturePad.tsx`, `site/src/lib/agreementApi.ts` |
| Invoices | `api/CleaningSuite.Application/Invoicing/*`, `api/CleaningSuite.Infrastructure/Invoicing/InvoicePdfRenderer.cs`, `InvoiceRepository.cs`, `api/CleaningSuite.Domain/Common/ValueObjects.cs` |
| Cross-cutting | `api/CleaningSuite.Api/Program.cs`, `PublicUploadsFileProvider.cs`, `Middleware/PublicRateLimitMiddleware.cs`, `Persistence/MartenConfig.cs`, `Email/ResendEmailSender.cs` |
| Test coverage | `site/playwright.config.ts`, `site/e2e/*.spec.ts`, `site/e2e/admin-helpers.ts`, `api/CleaningSuite.Tests/*` |

## Product decisions needed before tests can enforce policy

Decide: multi-team booking capacity and staff eligibility; price basis and currency;
Helsinki-local vs UTC shift recurrence and DST behavior; allowed terminal status
transitions; whether cancelled agreements revoke viewing and whether signed signers
may be removed; token expiry; required QC checks/photos and maximum counts/sizes;
role/identity binding for invitations and provisioning; genuine offline requirements.
The case catalogue flags these as policy cases, not existing requirements.
