# Employee permissions, tenant managers and shift restoration

Implementation: PR99, main `b5bbae3d1f22f649540ee8efc3cddd8da781542c`.
Production verified on 2026-10-14. API image build [38486220034](https://github.com/MaleeshaKumarage/readysetsiivous/actions/runs/38486220034) and production deployment [38487792867](https://github.com/MaleeshaKumarage/readysetsiivous/actions/runs/38487792867) succeeded. The running API container was inspected directly and uses `ghcr.io/maleeshakumarage/readysetsiivous-api:sha-b5bbae3d1f22f649540ee8efc3cddd8da781542c`; its health status is `ok`.

The final website deployment [38486961309](https://github.com/MaleeshaKumarage/readysetsiivous/actions/runs/38486961309) succeeded after verification notes and manager navigation tests merged in PR100 (`cfedd967`). Public bundles contain the manager workspace, inactive-shift filter/reactivation actions and future-start message. Anonymous production requests to manager shifts, self profile and admin shifts return 401. Issues65/82/96/97 have met their release verification criteria. The preceding no-QC-email release93 is also verified live.

## Resulting behavior

- Every administrative family requires tenant admin before reading or mutating
  payroll, financial, agreement/signature, template/report/share or settings data.
  Employee, booking and invoice JSON uses explicit nested contracts.
- Cleaners get their own verified profile and assigned-work APIs, with bounded
  dates, current assignments, tenant-local day boundaries and safe operational
  fields. Wider My Work UI remains70.
- Managers are tenant-wide, not branch assigned. Their dedicated workspace/API
  handles shifts and employee assignments across their tenant while excluding
  account/role administration, payroll, billing, templates and monthly reports.
- Admins/managers can show deactivated shifts and reactivate them without
  rewriting schedule, validity dates, assignment periods, template/notes or QC
  history. Repeated restoration is idempotent and concurrent updates conflict.
- Clock starts expose safe API rejection reasons and wait for the scheduled
  instant. Original failure before the user's deactivation was not established;
  deactivated shifts correctly deny new starts.
- Existing authenticated optional activity measurement, admin monthly reports,
  PDFs and30-day revocable report links remain intact. QC sends no emails.

## Verification

- All368 backend tests passed with owned disposable PostgreSQL/Keycloak/mail
  capture; zero explicit skipped tests. This includes genuine tenant isolation
  and manager edit/reactivation concurrency against an admin template update.
- All168 desktop/mobile Playwright regressions passed. An additional18 focused
  manager runs passed after final account/shift navigation and login/logout tests.
- Genuine Keycloak browser test passed native setup and password preservation,
  cleaner self-work privacy and optional clocking, monthly counts/PDF, manager
  promotion/login, both own branches, cross-tenant GET/PUT denials, manager-created
  shift assignments/removal/restoration, preserved template and stale-role denial.
- TypeScript and the production build passed; automated PR review reported no
  critical issues. Standard CI uses its existing conditional fixture behavior;
  those database/identity cases were exercised locally against owned services.
  The external Gemini live test was not configured locally; CI exercised it.
- The genuine test exposed existing-role409 during manager promotion. Role
  creation now tolerates only that conflict and verifies the existing manager
  role; regression coverage and the real promotion then passed.
- Owned test containers, network, API and tunnels were removed after verification.

Permission and endpoint contracts: [employee access](../features/employee-access.md),
[tenant manager access](../features/tenant-manager-access.md),
[HTTP inventory](../features/api-endpoints.md). Test mapping:
[edge-case catalogue](../testing/edge-case-catalog.md).

Separate open findings:95 tracks the vulnerable Marten dependency and98 tracks
global company business-ID uniqueness. Neither is claimed fixed by this release.
