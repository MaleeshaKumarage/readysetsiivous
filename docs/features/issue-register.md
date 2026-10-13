# GitHub issue register for the feature/auth review

Review date: 2026-10-13. Source baseline: `ceff5bd`; documentation commit: `2d5e112`.
Created 25 new GitHub issues and added source evidence/acceptance requirements to
existing issues 49 and 15 rather than creating duplicates. This is a backlog, not
implementation or permission to deploy. No `approved` automation label was added.

Discussion issues require a recorded product/security decision before implementing
new roles, workspace moves or lifecycle policy. Implementation issues have concrete
source findings and acceptance checks; runtime security/DB risks are identified
as requiring real dev verification.

Each issue links feature gaps and stable test-case IDs in the
[edge-case catalogue](../testing/edge-case-catalog.md). The
[operation matrix](feature-capability-matrix.md) distinguishes FE missing screens
from BE missing rules/APIs.

| Priority | Kind | Issue | Feature gaps | Test families |
| --- | --- | --- | --- | --- |
| P0 | implementation | [#62: [P0][BE] Bind Quality Cycle persistence to the request tenant and prove isolation](https://github.com/MaleeshaKumarage/readysetsiivous/issues/62) | G01 | QC-006, AUTH-008, X-003 |
| P1 | implementation | [#63: [P1][Auth] Enforce API token audiences with explicit SPA and provisioner configuration](https://github.com/MaleeshaKumarage/readysetsiivous/issues/63) | G14 | AUTH-003, AUTH-005, AUTH-007 |
| P1 | implementation | [#64: [P1][Auth] Require explicit platform/provisioner permission for tenant lifecycle APIs](https://github.com/MaleeshaKumarage/readysetsiivous/issues/64) | G14 | AUTH-007, TEN-001, TEN-004, TEN-005 |
| P1 | implementation | [#65: [P1][Auth] Restrict employee reads and return permission-safe DTOs](https://github.com/MaleeshaKumarage/readysetsiivous/issues/65) | G27 | AUTH-003, AUTH-004, AUTH-008, EMP-004, OPS-005 |
| P1 | implementation | [#66: [P1][Auth] Align dev realm roles, provisioning and application permission claims](https://github.com/MaleeshaKumarage/readysetsiivous/issues/66) | G09, G27 | AUTH-002, AUTH-003, AUTH-004, EMP-006 |
| P1 | discussion | [#67: [P1][BE] Define and enforce tenant resolution for authenticated public requests](https://github.com/MaleeshaKumarage/readysetsiivous/issues/67) | G30 | AUTH-008, AUTH-009, X-003 |
| P1 | implementation | [#68: [P1][FE] Preserve translations and service metadata during edit and status changes](https://github.com/MaleeshaKumarage/readysetsiivous/issues/68) | G02, G26 | SVC-001, SVC-004, SVC-005, CAT-005 |
| P1 | implementation | [#69: [P1][FE/BE] Align agreement detail DTO with signer counters rendered by the UI](https://github.com/MaleeshaKumarage/readysetsiivous/issues/69) | G03 | AGR-001, AGR-006, AGR-011 |
| P2 | implementation | [#70: [P2][FE] Implement booking management and employee day schedule using existing APIs](https://github.com/MaleeshaKumarage/readysetsiivous/issues/70) | G17, G21 | OPS-001, OPS-002, OPS-003, OPS-005, OPS-006 |
| P2 | implementation | [#71: [P2][FE] Complete employee profile editing and advanced fields with existing backend support](https://github.com/MaleeshaKumarage/readysetsiivous/issues/71) | G19 | EMP-002, EMP-003, EMP-004, EMP-007, EMP-008 |
| P2 | implementation | [#72: [P2][FE/BE] Complete invoice workflow and harden invoice lifecycle and duplicate creation](https://github.com/MaleeshaKumarage/readysetsiivous/issues/72) | G17, G13 | INV-001, INV-002, INV-003, INV-004, INV-005, INV-006, INV-007 |
| P2 | discussion | [#73: [P2][FE/BE] Complete agreement lifecycle actions and preserve signer evidence](https://github.com/MaleeshaKumarage/readysetsiivous/issues/73) | G22, G08, G32 | AGR-008, AGR-009, AGR-010, SIGN-004, SIGN-005 |
| P2 | discussion | [#74: [P2][FE] Add tenant settings and decide dynamic public content integration](https://github.com/MaleeshaKumarage/readysetsiivous/issues/74) | G18 | TEN-006, TEN-007, TEN-008, TEN-009 |
| P1 | implementation | [#75: [P1][BE] Correct shift occurrence boundaries and make assignment integrity explicit](https://github.com/MaleeshaKumarage/readysetsiivous/issues/75) | G05, G06, G10, G21 | SHIFT-003, SHIFT-005, SHIFT-009, SHIFT-010, SHIFT-011, SHIFT-013, SHIFT-014, SHIFT-015, SHIFT-016 |
| P1 | implementation | [#76: [P1][BE] Wire recurring QC dispatch with dev-aware links and durable email delivery status](https://github.com/MaleeshaKumarage/readysetsiivous/issues/76) | G04, G07, G15 | QC-007, QC-008, QC-009, QC-010, QC-011, QC-012 |
| P1 | implementation | [#77: [P1][FE/BE] Harden QC submission matching, photos, conflict errors and reviewer visibility](https://github.com/MaleeshaKumarage/readysetsiivous/issues/77) | G08, G11, G15, G23 | QC-002, QC-003, QC-004, QC-005, QC-017, QCP-003, QCP-004, QCP-005, QCP-006, QCP-007 |
| P1 | implementation | [#78: [P1][FE] Centralize authenticated requests, cache scope and recoverable error states](https://github.com/MaleeshaKumarage/readysetsiivous/issues/78) | G24, G25, G27 | AUTH-005, AUTH-006, AUTH-010, AUTH-011, X-001, X-002, X-010, AGR-011, COMP-006 |
| P1 | implementation | [#79: [P1][FE/BE] Add booking request idempotency and lost-response recovery](https://github.com/MaleeshaKumarage/readysetsiivous/issues/79) | G12, G24 | BOOK-001, BOOK-011, BOOK-012, BOOK-017, BOOK-020, LOOK-001 |
| P2 | discussion | [#80: [P2][FE] Provide customer booking lookup and cancellation with privacy-aware credentials](https://github.com/MaleeshaKumarage/readysetsiivous/issues/80) | G17 | LOOK-001, LOOK-002, LOOK-003, LOOK-004 |
| P1 | implementation | [#81: [P1][BE] Validate parent relationships and binary payloads before durable writes](https://github.com/MaleeshaKumarage/readysetsiivous/issues/81) | G10, G11, G16 | COMP-003, AGR-003, AGR-004, AGR-005, SVC-007, SIGN-009, SIGN-010, SIGN-013, SIGN-014 |
| P1 | discussion | [#82: [Discussion] Define platform admin, tenant admin, scoped manager and employee permissions](https://github.com/MaleeshaKumarage/readysetsiivous/issues/82) | G09, G14, G27 | AUTH-003, AUTH-004, AUTH-007, AUTH-008, EMP-006, EMP-007, TEN-004 |
| P2 | discussion | [#83: [Discussion] Organize workflows into Operations, Customers, People, Quality and My work](https://github.com/MaleeshaKumarage/readysetsiivous/issues/83) | G17, G18, G19, G20, G21, G22, G23 | AUTH-004, AUTH-010, OPS-006, COMP-004, EMP-008, SHIFT-018, INV-007, TEN-009 |
| P1 | discussion | [#84: [Discussion] Define booking/invoice terminal states, schedule timezone and public price basis](https://github.com/MaleeshaKumarage/readysetsiivous/issues/84) | G05, G13, G26 | BOOK-007, BOOK-009, BOOK-018, CAT-005, OPS-002, INV-004, INV-005, SHIFT-011 |
| P2 | implementation | [#85: [P2][FE] Resolve public navigation and PWA affordance gaps before extending browser coverage](https://github.com/MaleeshaKumarage/readysetsiivous/issues/85) | G28, G29 | WEB-003, CARD-002, PWA-001, PWA-002, PWA-003, PWA-004, PWA-005 |
| P1 | implementation | [#86: [P1][Testing] Extend Playwright with feature contracts, real roles and isolated dev integration](https://github.com/MaleeshaKumarage/readysetsiivous/issues/86) | G01, G03, G24, G27, G31 | AUTH-003, AUTH-007, AUTH-008, QC-006, BOOK-014, SIGN-012, X-001, X-004, X-005, PWA-003 |

## Existing issues extended

- [#49: employee cretion signup email show 404](https://github.com/MaleeshaKumarage/readysetsiivous/issues/49): registration destination, realm+sub employee binding, role assignment, reinvite, delivery and deactivation/demotion policy. G09/G19/G27; EMP-005/006/007/008 and AUTH-002/003/004/005.
- [#15: Add Company and Multiple locations](https://github.com/MaleeshaKumarage/readysetsiivous/issues/15): company/branch edit/deactivation, search/paging, shift type1/edit/validity/occurrences and parent selection/ownership. G10/G20/G21/G25; COMP-003/004/005/006/007 and SHIFT-002/003/017/018.

## Recommended first decisions and fixes

1. Investigate/fix QC tenant partition, enforce JWT audiences and dedicated platform permissions, then restrict employee-sensitive reads.
2. Fix invitation identity lifecycle, align dev/provisioned claims, preserve service metadata and align agreement DTOs.
3. Decide manager branch scope and role/permission matrix before broadening delegated access.
4. Complete Operations/customer/employee detail flows with existing APIs; then QC dispatch/review and Billing.
5. Drive testing changes from exact case IDs, safe dev fixtures, genuine role negatives and real persisted/artifact outcomes.

The 25 issue IDs are 62 through 86. No issues were closed or unrelated issue bodies
rewritten. Existing duplicate health-route research issues 45/47 were left alone
because they are outside this feature/auth review.

## QC implementation progress (2026-10-13)

The activity workflow now supports optional selected activities, existing-shift
template assignment, recurring generation, distinct per-item day/occurrence counts,
separate shift/branch PDFs and tenant-admin-only30-day revocable summary links.
QC tenant isolation and concurrent generation/submission have real PostgreSQL
regressions. Admin role negatives are checked by HTTP authorization tests.

#62 is addressed in source, with a legacy-partition release preflight still required.
#76 has the shared dispatcher/worker and configured notification links/retry state;
provider recovery scenarios remain. #77 now has exact item structure and one-time
recording semantics; photo storage/limits and editable correction audit remain.
#75, #82 and #83 retain their wider assignment/identity/workspace scope. Issues
remain open until reviewed/merged and remaining acceptance work is reconciled.
See [QC workflow](quality-cycle-workflow.md) and [rollout](../quality-cycle-rollout.md).
