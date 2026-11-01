# Partner workflow verification and rollout

## Delivered behavior

A tenant administrator enables partner access on a company, then creates or edits a
person through Employees with role **Partner** and that company selected. Each partner
has exactly one company. Current database identity, company activity and company enablement
are checked on every partner request; changing token claims or supplying another company
ID cannot change the scope. Partners do not enter the workforce roster. A staff-to-partner
role change is rejected while assigned work or an offer remains outstanding; completed
history is retained.

The Finnish/English light-mode `/partner/` workspace shows that company's properties, cleaning
requests, read-only shifts and change-request history, including staff resolutions.
A partner can add a property without a price and request a quote. Admins and managers use
`/admin/partner-requests/` or `/manager/partner-requests/` to manage quotes, properties,
requests, offers and change resolutions. Optional room and bed counts describe a property;
they do not calculate its price. Staff set a positive fixed net price, VAT percentage and
estimated duration before the partner can request cleaning.

A request is one cleaning visit within an inclusive date range, or at an exact time on
one date. Dates and exact times use the tenant timezone, with UTC persisted for planned
and actual timestamps. Nonexistent or ambiguous daylight-saving times are rejected, and
the complete estimated visit must fit the requested window. Staff may set its planned
time when offering it; otherwise the selected cleaner supplies it when accepting in
My Shifts. Offers and cleaner acceptance responses omit the customer's price.

Acceptance creates one ordinary one-time shift, occurrence, assignment, billing snapshot
and notification intents in one database commit under the shared scheduling reservation.
Repeated acceptance does not create a second visit. Before first acceptance the partner
can edit or cancel; editing invalidates the offer. After first acceptance the partner asks
staff for changes. Staff rescheduling or reassignment reuses the linked shift and requires
renewed acceptance. Started or billed visits cannot be silently rewritten through either
partner operations or legacy shift mutation routes. Cleaners record actual work using the
existing My Shifts start/end controls; a checklist template remains optional.

## Financial and notification boundaries

Request submission freezes the net price, VAT, estimated duration and pricing revision.
Later property price changes affect new requests only. Actual hours do not change the
fixed cleaning price. Tenant administrators manually preview and issue an invoice for
one company and calendar month. Partner-month selection includes all completed, unbilled
partner visits, including more than 50; ordinary manual shift selection keeps its existing
limit. The scheduled tenant-local visit date determines the billing month. Issued and paid
invoices claim work, voiding releases it, and issue rechecks the preview fingerprint under
the same reservation. Existing invoice PDF, numbering, payment reference and barcode
output are reused. There is no automatic monthly issue or email job.

Business writes commit durable notification intents under the scheduling reservation before
contacting Resend. Work-status reconciliation also uses that reservation. Provider delivery
is serialized by a nonblocking tenant-and-intent advisory lock, so a slow email provider
does not reserve unrelated tenant scheduling. Delivery uses immutable recipient/payload
snapshots and stable provider idempotency keys. Temporary
failures retry with bounded backoff; uncertain delivery stops after 20 hours or 12 attempts
for review. Revoked or permanently rejected recipients do not block other eligible
recipients. Current company, role, binding and email are checked before delivery. Email
links require sign-in and omit property access instructions and cleaner notes. Work
start/completion notifications are reconciled idempotently from existing work records.

## Verification evidence

The PR compatibility review found that an older company update could disable partner
access and an older branch update could clear optional property counts. Updates now
preserve omitted partner fields, while explicit `false` still disables company access
and explicit null still clears a selected room/bed count. Existing pricing was already
retained when all pricing fields were omitted or null; that behavior has regression coverage.
Company rebinding deliberately remains database-only: no company permission is stored in
the JWT, and the current binding and company state are checked on each request. A regression
verifies that rebinding requires no identity-role mutation and that disabling the new company
immediately denies the same login.

Independent final review found no remaining confirmed P1/P2 findings in this bounded change.

Evidence below is scoped to this development implementation. Focused test runs overlap;
their counts must not be added to produce a unique total.

| Check | Result at this checkpoint |
| --- | --- |
| PR compatibility and current-binding regressions | 23 passed, 0 failed, 0 skipped, including 10 new compatibility cases |
| Focused backend core, invoicing and existing shift regressions | 66 passed, 0 failed, 0 skipped, including real PostgreSQL concurrency tests |
| Follow-up notification and company/branch locking regressions | 13 passed, 0 failed, 0 skipped |
| Final notification delivery-lock regressions | 14 passed, 0 failed, 0 skipped, including 7 real PostgreSQL cases and a slow-provider scheduling test |
| Final workforce-transition and core workflow PostgreSQL regressions | 14 passed, 0 failed, 0 skipped |
| Employee identity regression checkpoint | 160 passed, 0 failed, 3 native-service tests skipped in that run; later native evidence is separate |
| Final partner Playwright coverage, desktop and mobile | 44 passed within the clean 100-case affected browser run; final conditional and alternate-entry assertions also passed in two overlapping targeted 4-case desktop/mobile runs |
| Legacy shift assignment and occurrence regressions, desktop and mobile | 56 passed within the same 100-case run, including active-partner exclusion from open workforce selectors and successful staff assignment |
| Existing invitation/company/My Shifts desktop browser regressions | 34 passed |
| Independent UI review follow-up, desktop and mobile | 4 passed, including editor target switching and staff-resolution visibility |
| TypeScript | Passed after the final UI review changes |
| Frontend production export | Passed after the final UI review changes; all 46 pages exported |
| Fresh current-source native Keycloak partner checks | 12 passed, 0 failed, 0 skipped |
| Native Keycloak runtime permission checks | 42 passed, 0 failed, 0 skipped |
| Native guarded invitation/security checks | 139 passed, 0 failed, 0 skipped |
| Real-service .NET identity integration | 2 passed, 0 failed, 0 skipped |
| Final broader backend suite | 1,096 passed, 0 failed, 4 environment-specific skips; 1,100 total, 1 minute 40 seconds |
| Separately configured manager database regression | 1 passed, 0 failed, 0 skipped |
| GitHub CI | Backend, frontend E2E, review and synthetic checks dispatched for [PR #230](https://github.com/MaleeshaKumarage/readysetsiivous/pull/230); results pending at this checkpoint |

The four skips in the broader run were `IdentityKeyIsUniqueWithinTenantWhileUnboundEmployeesAndOtherTenantsAreAllowed`,
`NativeSetupEmailBindsIdentityRetriesDeliveryAndSerializesParallelInvites`,
`RealManagerEditCannotCrossTenantsOrOverwriteConcurrentAdminTemplateChange` and
`ProvisionedRealmIssuesApiAudienceOnLoginAndRefreshButNotIdToken`. The full run did not
set their dedicated invitation/audience fixture variables. The first two passed in the
separate fresh native fixture run, and the manager database test passed separately with
its isolated database configured. The audience integration test was not rerun for this
feature; it remains an environment-specific verification gap, not a claimed pass.

The native identity tests used a freshly compiled Java 21 extension on Keycloak 26.8.1
and PostgreSQL 16, with captured synthetic SMTP only. All three owned fixture containers,
network and tunnel were removed and six pre-existing development services remained
running. After all application tests, the exact temporary application verification database
and its two owned SSH tunnels were removed; shared development services were retained.
Exact source/image/JAR hashes and limitations are in the
[native verification record](../security/2026-11-01-partner-native-verification.json).
This is single-node evidence, not a claim about clustered cache behavior. Browser mocks
verify UI contracts; they are not substituted for real-service identity/database tests.

PR review follow-up added the missing administration, invoicing and lifecycle browser
coverage. The company enablement round-trip test exposed a deferred React event access:
the checkbox handler now snapshots its checked value before updating form state. The
real pointer-driven create/edit test passes on desktop and mobile after that fix.

The remaining review comments are covered by owner property creation, quote and change
requests, fixed appointments, versioned pending-request edit/cancel, enabled and disabled
account entry, manager-only routes and pricing, and partner employee edit/rebinding.
Read-error recovery and stale preplanned-offer acceptance verify explicit refresh without
automatic mutation retries. The admin entry test opens the real mobile drawer after
hydration; it removes only the shared test helper's drawer-hiding style. This final change
is test-only, so the prior production-export and related browser regression evidence is
unchanged.

The final review follow-up adds active-partner negative cases for legacy shift and visit
override selectors, and verifies reusable pricing through the Properties tab. Conditional
state coverage also checks invoice preview invalidation, manual/month mode switching,
February month bounds, and clearing the company binding when a partner becomes staff.
All 100 affected browser cases passed in one run. The final two expanded cases then passed
on both projects (4 cases); these targeted results overlap the 100-case run and must not
be added to it. No production code changed in this coverage follow-up.

A further targeted desktop/mobile run passed 4 checks after adding active-partner exclusion
to the replacement-employee picker and staff-entered planned start to the offer flow. The
second offer uses the refreshed request revision and sends the chosen local datetime.
These checks overlap the same 100 unique cases and do not increase that total.

### UI action coverage map

| Changed interface | Actions and conditional states | Playwright specs |
| --- | --- | --- |
| Partner portal | Property creation, quote/change requests, flexible/fixed cleaning, versioned edit/cancel, accepted lock, history and load recovery | [partner-owner](../../site/e2e/partner-owner.spec.ts), [partner](../../site/e2e/partner.spec.ts) |
| Account entry | Enabled partner link/navigation; disabled company denial without company-data reads | [partner-owner](../../site/e2e/partner-owner.spec.ts) |
| Company and branch administration | Partner enablement create/edit; rooms, beds, fixed price, VAT and duration round trip | [partner-administration](../../site/e2e/partner-administration.spec.ts) |
| Employee administration | Required enabled-company selection; edit/rebind prepopulation; changing to staff hides and clears binding | [partner-administration](../../site/e2e/partner-administration.spec.ts) |
| Staff partner operations | Admin/mobile and manager entry; manager-only API routes; quote/Properties pricing, offers, property create/edit, changes, cancel and recovery | [partner](../../site/e2e/partner.spec.ts), [partner-operations](../../site/e2e/partner-operations.spec.ts), [partner-owner](../../site/e2e/partner-owner.spec.ts) |
| Cleaner offers | Flexible/preplanned acceptance, decline, stale revisions and explicit refresh without automatic mutation retries | [partner](../../site/e2e/partner.spec.ts), [partner-owner](../../site/e2e/partner-owner.spec.ts), [partner-operations](../../site/e2e/partner-operations.spec.ts) |
| Invoice creation | Company/month payload, full-month bounds, preview/issue, preview invalidation and manual/month mode switching | [partner-operations](../../site/e2e/partner-operations.spec.ts) |
| Legacy shift and visit assignments | Open choices exclude active partners, preserve working staff and submit selected staff; preview/save and existing roster regressions | [shift-assignment](../../site/e2e/shift-assignment.spec.ts), [shift-occurrences](../../site/e2e/shift-occurrences.spec.ts) |

Primary repeatable checks (configure test connection variables privately):

```powershell
dotnet test api/CleaningSuite.Tests/CleaningSuite.Tests.csproj --no-restore
cd site
npx tsc --noEmit
npx playwright test e2e/partner.spec.ts e2e/partner-administration.spec.ts e2e/partner-operations.spec.ts e2e/partner-owner.spec.ts
npx playwright test e2e/shift-assignment.spec.ts e2e/shift-occurrences.spec.ts
npx playwright test e2e/employee-invite.spec.ts e2e/company-integrity.spec.ts e2e/my-shifts.spec.ts --project=chromium-desktop
npm run build
```

Run browser tests and production builds sequentially: both use `.next`. The isolated
PostgreSQL tests require `QC_TEST_CONNECTION_STRING`; native identity integration uses the
owned fixture variables documented with the
[identity harness](../../api/keycloak-identity-extension/tests/README.md). Do not point
these verification commands at production or a shared application database.

## Deployment and compatibility

No production rollout was performed. New company enablement defaults to false, profile
company bindings and branch pricing/count fields are additive, and legacy shifts have no
partner request link. Existing companies are not enabled, repriced or backfilled by this
change. The public website and its language switch are outside this feature.

1. Preserve existing release-policy, backup and immutable-image compatibility gates.
   Do not substitute this feature's development evidence for the actual deployed baseline.
2. Rehearse the matching Keycloak extension and fixed partner group/role setup in
   development before allowing partner invitations. Do not grant runtime Admin REST or
   realm-management privileges as a shortcut. Retain tenant/platform audience separation.
3. Deploy the API before the frontend: My Shifts now reads the partner-offer endpoint,
   and the new portal and invoice mode require their corresponding API contracts.
4. Check schema/index initialization, background-worker configuration, frontend origin
   and Resend sender configuration. `BackgroundWorkers:Enabled=false` disables the
   notification worker; production notifications require a running worker.
5. Enable one intended company explicitly, set each bookable property's net price/VAT/
   duration, and invite a partner bound to that company. Verify company isolation, quote,
   request, cleaner acceptance, actual completion and manual monthly invoice in the
   target environment before widening use.
6. Rollback must preserve request, work, pricing and notification history. Do not delete
   partner documents or change a partner into staff to bypass an older client's missing
   UI. Use the repository's reviewed release compatibility procedure and actual prior
   immutable images; do not downgrade a migrated Keycloak database blindly.

API details and exact wire fields are in the [partner API contract](partner-api-contract.md).
The [approved design](../superpowers/specs/2026-10-30-company-partner-workflow-design.md)
and [implementation plan](../superpowers/plans/2026-10-30-company-partner-workflow-plan.md)
record scope and release gates.

## Quote request visibility

Authorized admins see a persistent pending-quote count and Review quotes link on
every admin page. The link opens Partner operations directly on Quote requests.
Pending branches have a red, text-labelled `Quote requested` badge in Companies,
Partner properties, the quote queue and the owner portal. Active state and pricing
readiness determine whether a request is pending; a retained historical request
timestamp does not keep a priced branch marked pending.

The admin queue refreshes on navigation, focus, visibility return and every
30 seconds while visible. Background refresh does not reset inline form drafts.
Saving pricing through either Partner operations or Companies refreshes it
immediately. Failed reads retain the last successful queue and show Retry rather
than claiming there are no requests. Admin identity verification still gates all
queue reads; denied/anonymous visitors do not mount the provider.

`site/e2e/partner-quote-alerts.spec.ts` covers these cases on desktop/mobile,
including inactive/priced exclusions, errors, hidden polling and both pricing
entry points. The owner suite checks the red badge after requesting a quote;
the admin-scope suite checks that denied users issue no administrative reads.
Local verification: 10 new cases and 50 related partner/admin cases passed.

## Partner portal repair regressions

The original Request cleaning action rendered its inline form below every
property without scrolling/focusing it. A 21-property regression reproduced an
entirely off-screen form on both desktop and mobile. Opening now scrolls to and
focuses the request region. Save failures appear inside that form, preserving
dates, instructions and the same idempotency key for a user-initiated retry.
The portal header uses the existing ReadySetSiivous primary logo.

`partner-owner.spec.ts` additionally verifies the real logo loads, request form
visibility, required/reversed dates, disabled unpriced/inactive properties and
error/retry persistence. `partner-cancellation.spec.ts` covers owner/staff draft
cancellation with no writes and sign-in/account/sign-out/reload behaviour.
The selected portal/quote/cancellation suites passed 52 desktop/mobile checks.

Production diagnostics also exposed a notification-worker UTC query failure.
The real PostgreSQL regression calls the polling entry point rather than only
per-ID dispatch. It failed with the observed InvalidUtcDateTimeUsageException
before the fix and now verifies due notifications send while future ones remain
pending. The query parameter matches the existing timestamp mapping without
changing UTC values or schema; all 26 selected partner backend cases passed
against an owned isolated PostgreSQL database with zero skips. Its temporary
database, role, local credential file and SSH tunnel were removed afterward.

## Partner design system and language coverage

The partner workspace reuses the shared navy/gold tokens and Button, Card, Field,
Input, Textarea and Badge components. Styling is scoped to the partner portal and
keeps white surfaces/readable fields even when the global page has a saved dark
theme. Desktop uses two property columns; mobile uses one, including 320px forms.
The four-section navigation supports arrow keys and Home/End with visible focus.

FI/EN is specific to this portal. Selection is remembered under
`readyset:partner-language`; without a saved choice, a Finnish browser starts in
Finnish and others in English. Switching language preserves open forms and entered
values. Navigation, actions, statuses, confirmations, validation, known API errors,
success/empty states and price/time formatting are localized. Company/property/
person names, instructions and staff-written messages retain their entered text.
The main region carries its language; other portals keep their current copy and
theme preferences. Unknown server errors receive a localized retry message.

The combined partner suites passed 58 desktop/mobile checks, including Finnish
create/cancel/property/quote/change flows, language persistence, account-page
isolation, draft preservation and global-dark/320px checks. Four lifecycle checks
also passed after replacing an ancestor-dependent branch selector with its named
accessible group. Screenshots cover Finnish desktop/mobile and narrow request
forms. Earlier PostgreSQL notification regressions remain separately recorded.

Review follow-up: quote-banner navigation uses client-side links and reactive query parameters, preserving open property drafts when switching the current operations page to quotes. Six desktop/mobile checks passed for same-page navigation, cross-page pricing, and populated/empty company-shifts views in both languages (dates, statuses and unchanged entered names).

Six further desktop/mobile checks passed: anonymous and denied admin navigation triggers zero quote reads or polls (including a simulated minute and focus return), and tab navigation covers ArrowRight, ArrowLeft, Home and End with focus assertions.
