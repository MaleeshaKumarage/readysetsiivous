# Quality report workspace

Tenant admins manage reports from Quality Cycle Management → Reports. Templates remain a separate tab. Managers and cleaners cannot access this workspace.

Select up to 50 live shifts (active or labelled inactive, excluding deleted archives) and either up to 12 year/month values or an inclusive From–To range. At most 366 distinct selected days are allowed. The year control adds months from another year without clearing existing choices. Duplicate months/shifts are counted once. A report contains only selected periods, never the gaps between selected months. All date boundaries use the tenant timezone and scheduled visit start; actual work timestamps are unchanged. A selected shift with no visits still appears with zero scheduled totals and unavailable actual hours.

The table shows company, branch, shift, scheduled visits, completed visits, scheduled hours, actual staff hours and actions. A visit completes when its expected cleaner tasks are submitted; cleaners need not check every activity. Several cleaners add staff hours but do not multiply scheduled hours. Missing/invalid timing pairs are excluded and marked incomplete; unavailable hours are not zero. Shifts without checklist templates still contribute scheduled visits. Historical branch changes remain separate location rows.

Changing shifts or periods clears the loaded table, preventing exports from stale filters. Export one shift or all selected shifts in a single branded PDF. Language choices are English, Finnish and Swedish. All report labels and appendix labels are translated; customer names, activities and notes remain exactly as entered. PDFs preserve activity detail, actual work times, and dated photo/note appendices.

Localization covers reference labels, visit statuses, missing-data messages, image warnings, photo captions, page footers and the legacy summary formatter (including localized month names). Unknown Finnish/Swedish system-label keys fail explicitly instead of silently falling back to English. PDF text regression tests inspect the generated documents, including empty reports and legacy records. The brand name, IANA timezone identifier and UTC abbreviation remain identifiers.

## Email and acknowledgment

Share one shift or all selected shifts; select a company contact email, branch contact email or custom address in the popup. Branch contact email is optional and editable in Companies → Branch. Suggestions deduplicate addresses. One email recipient per report creates an independent frozen snapshot and 30-day link.

The recipient sees a public summary, downloads the PDF and explicitly clicks Acknowledge receipt. GET/opening the link never acknowledges it. The first acknowledgment time is preserved; it is receipt through a bearer link, not identity verification or legal signing. Public responses exclude recipient addresses, cleaner identities, form IDs and form tokens. The PDF retains the approved photo/note evidence.

Admin status distinguishes link created, email failed, email accepted by provider, acknowledged, revoked and expired. Provider acceptance does not prove inbox delivery. Retry uses the same snapshot, language, recipient, token and original expiry; it does not regenerate the report. A successful send cannot be overwritten by a later failed retry. Revocation changes only availability, preserving acknowledgment and email history. Expired/revoked reports reject view, PDF, acknowledgment and retry. Existing PDF-only report links continue to work.

## Environment configuration

The existing `App:FrontendBaseUrl` must be the public site origin (dev and production use their own configured origins). `Resend:ApiKey` and `Resend:From` configure email delivery. Missing configuration is an explicit failed email state with Retry; no false sent status. Tests use mocked email and an isolated disposable PostgreSQL database on the dev host; no customer emails are sent.

## Test cases for Playwright and API regression

- Selected shifts only; no initial all-tenant report or export.
- Nonconsecutive months, year change retaining selected months, duplicate selection, inclusive one-day ranges, reversed/incomplete ranges and >366-day rejection.
- Tenant midnight, DST transitions, overnight shifts and actual work outside the scheduled day.
- Missing/deleted/foreign shift rejected; no archived shift resurrection in new selected reports.
- One cleaner versus multiple cleaners; partial crew submission; unchecked activities; incomplete/invalid/missing actual timestamps.
- Template-free shifts, cancellation, extra visits, moved visits across a selected boundary and historical branch changes.
- Per-shift versus combined export selection; language changes preserved; no stale filter exports or out-of-order response replacement.
- Company/branch/custom address, missing contacts, duplicate contacts, malformed email, send failure/retry, expired/revoked retry and no token regeneration.
- Recipient page in all three languages, no automatic acknowledgment, repeat acknowledgment/reload, failure/retry and safe public DTO.
- Concurrent acknowledgment, delivery update and revoke preserve each other; tenant isolation tested against real PostgreSQL.
- Branded multi-page PDF labels/footer/appendices, long Swedish labels, legacy English snapshots, original Unicode notes/photos unchanged.
