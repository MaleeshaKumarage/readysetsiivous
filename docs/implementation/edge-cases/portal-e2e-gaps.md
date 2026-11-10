# Portal E2E gap audit — 2026-11-10

Branch: `test/portal-e2e-gaps`, created from main `2348693642d640e437b5958265e33b9f0ca17124`.

Scope: current partner, admin and employee portals and their existing PWA. Tests follow current source behavior and the approved presentation/field-service requirements. No application, API, authentication, financial or service-worker behavior changes.

## Added coverage

Each scenario runs on `chromium-desktop` and `mobile-touch` without new skips.

| Source of requirement | Added scenarios | Suite |
| --- | --- | --- |
| Partner identity gate in partner/page.tsx | Foreign realm, inactive person, persisted non-partner role, inactive company and disabled partner company; only identity may be read | lifecycle-portal-access-gaps |
| Partner token gate | Missing/employee roles and mixed admin/partner or manager/partner roles; no partner API calls | lifecycle-portal-access-gaps |
| Admin workspace gate | Partner, employee, manager and roleless direct navigation to invoices, bookings, inventory, partner operations, work queue, calendar and customer detail; no admin API calls | lifecycle-portal-access-gaps |
| Persisted admin identity | Inactive employee binding, empty roles and malformed role shape cannot mount invoice data | lifecycle-portal-access-gaps |
| Partner request busy/idempotency guards | Pending save disables the button and rejects a second submit event; abandoning a failed request produces a fresh draft/key | partner-recovery-gaps |
| Completed-visit response guard | Late response after close or replacement cannot reopen/overwrite the dialog; failed read can recover on reopening with no writes | partner-recovery-gaps |
| Optional browser storage | Partner language switches after language-storage reads/writes throw SecurityError | partner-recovery-gaps |
| Existing PWA event lifecycle | Admin and employee accepted/dismissed/external-install outcomes; no automatic prompt, consumed prompts are not reused, fresh prompts remain usable | lifecycle-pwa-event-gaps |
| Employee evidence/draft ownership | HTML-like instructions stay text; two visits retain distinct checklist/note drafts | my-shifts-recovery-gaps |
| Employee work-write busy state | Start/end send one request while pending; completion disables note/photo editing | my-shifts-recovery-gaps |

33 new logical scenarios = 66 project checks. The previously excluded inactive-record editing suite restores another four CI checks, preserving company/branch/shift/employee activity flags in update payloads. Its fixtures now match paginated URLs and its selectors use exact accessible labels.

## Discovery audit

The former testMatch allowlist omitted admin-edit-inactive.spec.ts. Default selection now discovers every local `*.spec.ts` automatically. The same four external suites remain outside this selection: api-audience-live and employee-invite-live require owned service fixtures, api-booking targets seeded development services, and signature requires Storybook. No previously selected test is removed. Discovery lists 1,914 checks in 111 files.

Existing suites already cover request retry identity, cancellation reason/version conflicts, required photo notes/categories, optional checklist completion, lost clock responses, calendar paging/stale responses, partner pricing/acceptance and inventory acknowledgment. They remain in the suite rather than being copied into the new files.

## Verification boundaries

Browser tests use synthetic identities and routed APIs. They verify the browser's request boundaries, payloads, UI state and recovery; they do not establish server authorization or database concurrency guarantees. Native PWA installation, actual device keyboards, push receipt and real Keycloak sessions require their existing device/live-service verification. This audit documents concrete reviewed gaps; it is not a claim that every possible defect has been eliminated.

## Results

- Final clean run: `npm run test:e2e -- lifecycle-portal-access-gaps partner-recovery-gaps lifecycle-pwa-event-gaps my-shifts-recovery-gaps admin-edit-inactive` — **70 passed**, zero failures/skips, both projects (2.3 minutes).
- The preceding broader run also passed all **26 existing portal polish checks** on desktop/mobile. Its two inactive-record desktop failures were stale label selectors, corrected before the final 70/70 run; all four restored inactive-record checks now pass.
- `./node_modules/.bin/tsc --noEmit` and `git diff --check` passed.
- Default discovery: **1,914 checks / 111 files**. Compared the old selection with the new one: no previously selected suite is removed. The full 1,914-check suite was not executed for this test-only branch.
- No test skips, exclusive tests or arbitrary sleep calls were added. Delayed requests use explicit release gates and completion waits. Employee dates use a fixed browser clock to avoid midnight-dependent failures.
- Next: publish this test-only branch for normal review/CI. No application changes, merge or deployment are part of this increment.
