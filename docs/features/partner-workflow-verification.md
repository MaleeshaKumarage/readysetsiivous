# Partner workflow verification and rollout

## Delivered behavior

A tenant administrator enables partner access on a company, then creates or edits a
person through Employees with role **Partner** and that company selected. Each partner
has exactly one company. Current database identity, company activity and company enablement
are checked on every partner request; changing token claims or supplying another company
ID cannot change the scope. Partners do not enter the workforce roster. A staff-to-partner
role change is rejected while assigned work or an offer remains outstanding; completed
history is retained.

The English light-mode `/partner/` workspace shows that company's properties, cleaning
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
| Final partner Playwright coverage, desktop and mobile | 42 cases verified against mocked API contracts; the full run passed 41 and exposed a mobile navigation fixture timing issue, then the corrected navigation case passed on both projects (2 passed) |
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
unchanged. These are 42 unique cases, not the sum of overlapping full and targeted runs.

Primary repeatable checks (configure test connection variables privately):

```powershell
dotnet test api/CleaningSuite.Tests/CleaningSuite.Tests.csproj --no-restore
cd site
npx tsc --noEmit
npx playwright test e2e/partner.spec.ts e2e/partner-administration.spec.ts e2e/partner-operations.spec.ts e2e/partner-owner.spec.ts
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
