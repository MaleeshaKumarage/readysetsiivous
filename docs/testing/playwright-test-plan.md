# Playwright improvement plan

Source baseline: `ceff5bd`, reviewed 2026-10-13. This is a plan for the next test
improvement step, not implementation or evidence of execution of the proposed cases.

Read [features and FE/BE gaps](../features/feature-inventory.md),
[stable-ID edge cases](edge-case-catalog.md), [API endpoints](../features/api-endpoints.md),
and [environment setup](../environments.md) together.

## Current coverage and its limits

The current config selects three specs, Chromium desktop and Pixel 5 Chromium
emulation, 30-second timeout, local Next.js dev server on port3000 and blocked
service workers. There are 21 logical tests, reported as 42 desktop/mobile runs.
No real mobile device, Firefox/WebKit, built static-host or real Keycloak login
project exists.

| Spec | Logical tests | Current coverage | Not established |
| --- | --- | --- | --- |
| `admin.spec.ts` | 14 | Mock auth/dashboard/navigation; service create/error; agreement create/input rows; employee create/deactivate; company/branch create; shift schedules/assignment/deactivate | Real role/DB enforcement, field-preserving edits, advanced employee/company controls, signer lifecycle, BE-only screens |
| `quality-cycle.spec.ts` | 4 | Mock template CRUD, submissions/PDF suggested filename, public submit and invalid token | Tenant partition, dispatch/email, parallel submission, photo limits, actual PDF contents, worker |
| `booking-availability.spec.ts` | 3 | Retry, no slots, late old-date response | Complete booking journey, actual reservation/persistence, field validation, lost-response recovery |
| `signature.spec.ts` | 3 defined, excluded | Intended Storybook canvas/touch checks | Requires Storybook config/server; Next does not serve its `/iframe.html` route; sampling assumes DPR sizing rather than fixed800×200 |
| `api-booking.spec.ts` | 1 defined, excluded | Live service-button smoke | No explicit dev target configuration; default API is production; no booking/readback assertion |

The mock sets `__MOCK_AUTHED__`, returns a mock JWT and makes authenticated users
admin in nonproduction helpers. It cannot prove employee/no-role behavior. Fixture
CSS hides the mobile sidebar, so existing mobile tests do not prove the real drawer.
Previous baseline validation passed136 backend tests and42 configured browser
runs, type checking and production build. Those results do not prove live tenant
isolation, Keycloak authorization, transaction contention or email delivery. This
documentation task did not rerun those tests.

## Proposed suite boundaries

| Suite | Target | Cases | Cannot replace |
| --- | --- | --- | --- |
| Public browser contract | Local dev server, API explicitly mocked | WEB/LANG/THEME/QUOTE/CAT/BOOK/SIGN/QCP/CARD/LEGAL visible states | DB/auth/artifact assertions |
| Admin browser contract | Local, role-aware FE state, exact wire DTO mocks | UI portions of AUTH/SVC/EMP/COMP/SHIFT/QC/AGR and X failure matrix | Server permission enforcement |
| Shared-dev API integration | APIRequestContext, real issued tokens, owned records | All77 endpoints, auth negatives, tenant boundaries, state transitions | Missing browser screens |
| Shared-dev browser integration | UI built with dev API/auth URLs, real Keycloak | Booking→readback, assignment→QC token, signing→download, logout | Mock auth booleans |
| Static-host smoke | Production export served as files, API isolated | Direct links/reload/trailing slash/metadata/assets/404/deployment changes | Next dev routing behavior |
| Compatibility/PWA | Secure/trustworthy origin, workers allowed, appropriate browsers | Clipboard/touch/PDF/installation/offline | Chromium emulation alone |
| Backend/database/artifacts | .NET, isolated PostgreSQL, test files, clock/email sink | Recurrence/date math, rounding, corrupt files, retries, concurrent writes | Toast or network-call-only checks |

Use explicit configs/projects/tags. Do not widen testMatch globally and run
Storybook tests against Next or a live smoke against production. These suite names
are proposals, not scripts already present in package.json.

## Test data and isolation

1. Tag data with a run ID. Seed active/inactive services/employees, two companies
   and branches, and two separate active tenants. Use genuine AdminA, EmployeeA,
   NoRoleA, AdminB and dedicated provisioning identities. Never log or commit
   credentials, bearer tokens or browser storageState.
2. Do not rely on the shared fixed sample service staying unchanged. Every mutation
   uses owned IDs and cleanup only for those IDs. Physical deletion is unavailable
   for several resources; use an explicit test harness, not a shared DB reset.
3. Tenant provisioning/suspension uses disposable tenants, never the shared
   readysetsiivous tenant. There is no tenant-delete API; document the cleanup
   mechanism before such integration cases run.
4. Serialize tests sharing fixtures/IP-rate limits. Deliberately launch parallel
   requests within a concurrency case; worker concurrency is not a race oracle.
5. Include one fully populated service with fi/en/sv, custom icon/image/additionalInfo,
   nonzero sort order and featured=true. Assert all untouched fields after edits
   and status changes to reveal G02.
6. Have valid multi-page PDF, small real PNG, boundary-size/dimension and corrupted
   fixtures. Do not produce expected values by calling production validation code.
7. Route dev mail to a test sink/controlled inbox. Inspect recipient/subject/link
   host/delivery state. An empty Resend key or mocked send is not proof of delivery.
   No test messages should go to real customer or employee addresses.

## Time, recurrence and financial boundaries

- Freeze browser Date around local midnight, notice deadlines and New Year; set
  browser timezone explicitly to Europe/Helsinki and a second timezone such as
  Europe/Kyiv. Browser clock mocks do not change the backend clock.
- Booking AvailabilityEngine accepts TimeProvider; shift/QC handlers still use
  DateTime.UtcNow. Exact-time recurrence tests belong in an injectable-clock BE
  harness before deterministic browser integration is possible.
- Use independent expected local→UTC tables for DST. Booking uses tenant timezone;
  shifts currently treat times as UTC wall-clock. Resolve the policy before
  asserting Helsinki recurrence as an implemented contract.
- Test from/to timestamps, not only dates: shifts expand full dates; monthly QC
  report ends at next-month minus one second, which can omit fractional-last-second
  records. Also cover leap February, monthly31 skips and2024 biweekly anchor.
- Use decimal independently computed expectations for rounding/zero-VAT cases,
  and assert snapshots after service/profile changes.

## Accurate mocks and request contracts

- Use actual BE DTOs. Agreement detail lacks list counts; adding them to mocks hides
  G03. Mock mutation readbacks with updated state, not permanent successful stale data.
- Cross-origin requests need CORS/preflight handling. Match query strings and parse
  URLSearchParams, especially QC filter/report and availability paths.
- Booking lookup and cancellation both take phone in the query string. Agreement
  create and image upload are multipart FormData; the browser supplies the boundary.
- Test201/204 and their exact body differences: service/employee `{id}`, public
  booking reference result, and company/shift/QC/agreement typed DTOs.
- Use RFC7807 field errors for400/409, and also test empty-body429, nonJSON500,
  invalid success JSON, dropped requests, and timeouts.
- Intercept WhatsApp/mailto/tel destinations; do not navigate into another user's
  external account or send messages while testing outbound links.
- For real UI integration, explicitly configure dev API/auth URLs before build.
  Record intended deployed commit/image tag. A healthy old dev server does not
  validate the new feature branch automatically.

## Assertions and selectors

- Prefer scoped role/label/name selectors over CSS classes/nth buttons. Next has
  its own role=alert route announcer; scope form errors by text or container.
- Mantine Select uses a labelled textbox/options, not native selectOption.
  SegmentedControl uses radio/label controls, not tab buttons.
- Do not use force clicks or hide mobile UI to evade accessibility failures. Assert
  focus, aria-expanded, drawer operation, modal cancel/Escape/restoration and labels.
- Use controlled promises or waitForResponse/visible state for races; arbitrary
  sleeps and repeated retries can conceal stale-state defects.
- Await download event before clicking. Check suggested filename, real `%PDF-`
  bytes, parseable pages and representative contents. The current QC mock contains
  only header-like bytes; it is not PDF-content coverage.
- Convert signature CSS coordinates by canvas.width/boundingRect.width and
  canvas.height/boundingRect.height. Current canvas is800×200 independent of DPR.
- After every mutation, reload/GET the entity. For conflicts count durable records,
  inspect history and signature/PDF files; status or toast alone cannot prove safety.

## Order for the next step

1. Security/contracts: AUTH-003/007/008/009, QC-006, SVC-008, AGR-006. Investigate
   G01 before trusting tenant-specific QC lists/token access.
2. Booking/document integrity: BOOK-001/014/015/017, SIGN-001/002/012/013,
   SVC-005 and invoice API concurrency/state cases. Keep mocked UX and real DB
   assertions separately reported.
3. Assignment→QC: SHIFT-012/015/016, QC-007/009/011/012, QCP-003/004/006.
   Automatic shift-end assertions remain blocked by G04.
4. Failure recovery: expand X-001 across every visible list/form/download; test
   the full booking details/confirmation flow, not only time-button rendering.
5. Breadth: public sections, locale/theme/contact/legal, static-host deep links,
   advanced role boundaries and genuine mobile navigation.
6. Compatibility/PWA: separate worker-enabled suite; Storybook config or real sign
   page signature coverage; WebKit/Firefox once device support policy is explicit.

## Missing features and policy decisions

`C` cases describe current supported contracts. `R/Gxx` cases are linked desired
regressions; record current failure without weakening the assertion to accept a
defect. `B` blocks only the browser interaction for missing UI; APIRequestContext
can still test implemented endpoints. `D` needs an explicit policy decision before
tests effectively become new requirements.

If temporary expected-failure annotations are used, restrict them to exact known
bugs and report unexpected passes. Never skip/fail an entire feature, add broad
catch-all error suppression, or fabricate selectors for nonexistent routes.

## Completion criteria

- F01–F24 each have a suite/test owner or explicit UI blocker. Each implemented
  route has appropriate permission, schema, success and negative API coverage.
- Case ID→test title→environment→assertion→evidence traceability exists. Report
  supported passing, known-gap unresolved, missing-UI blocked and policy-pending
  separately; navigation mocks alone are not "all features tested."
- Genuine dev booking→readback, assignment→QC and multi-signer→download journeys
  work on the intended commit, with permission negatives and actual partition checks.
- Concurrency cases inspect winners/losers in real PostgreSQL and artifacts. Browser
  mocks do not certify serializable behavior.
- CI remains deterministic and never silently targets production. Shared-dev,
  time-driven, email and provisioning cases are explicitly scoped/opt-in.
- Fixtures, selectors and docs stay aligned after fixes; stable IDs preserve history.

## Confirmed QC activity workflow update

Use [quality-cycle-workflow.md](../features/quality-cycle-workflow.md) as the current
QC business oracle; older mandatory-completion/exception proposals are superseded.
Test selected activities rather than requiring all checks. Notes are optional.
Assert corridor12days/gym4days, duplicate-cleaner/day de-duplication, separate
weekday/biweekly shifts, tenant-admin-only controls, snapshot sharing,30-day
expiry and revoke404. Existing mocked browser coverage now includes these UI
paths, while PostgreSQL regressions exercise actual isolation/concurrent writes
and HTTP tests exercise authorization policies with controlled identities.
Genuine Keycloak login, notification-provider failure/crash cases and DST/overnight
calendar fixtures remain separate live/integration work.

## Issue49 invitation regression coverage

`employee-invite.spec.ts` covers invite/reinvite errors and cooldown, employee
mutation restrictions, account role-specific landing, invalid/mismatched tenant
links and synchronization failure feedback. `employee-invite-live.spec.ts` uses
real temporary Keycloak/SMTP/API/database services, with no auth/request mocks.
Its separate `playwright.invite-live.config.ts` reads an .NET-generated fixture
through INVITE_TEST_CONTEXT_PATH. Verify actual setup password, linked sub/role,
no elevated permissions, completed-account reinvite preserving the password,
promotion/demotion and stale JWT rejection after deactivation. Never point the
fixture/test connection at production or use real employee recipients.

See [invitation contract](../features/employee-invitations.md) for explicit SMTP,
public/internal auth URLs, user profile ownership and legacy identity migration.
