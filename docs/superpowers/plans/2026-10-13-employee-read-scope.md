# Issue65 implementation and verification plan

Base: latest main `68dedb7`. Refreshed spec independently approved; user has
authorized implementation without additional approval gates.

1. Freeze explicit nested administrative employee/booking/invoice JSON contracts
   and apply them at HTTP boundaries, including the admin schedule. Keep internal
   invoice models for authorized PDF rendering. Protect every admin controller
   with tenant-admin policy before administrative queries execute.
2. Add explicit self profile and assigned booking/shift day projections, using
   the lifecycle guard's verified employee. Enforce assignment ownership again
   when projecting repository results; filter foreign/missing company/branch
   parents. Validate strict day input and tenant-local exclusive day boundaries.
3. Preserve the existing own shift clock. Bound interval queries to 32 days and
   reject invalid/overflowing input before reads. Reject foreign form snapshots
   from repository results. Keep optional activity selection and own capabilities.
4. Make the admin shell verify current identity before mounting children. Deny
   employee/manager/no-role principals with account/sign-out actions. Show retry
   for transient verification failures. Do not cache the identity verification.
5. Add serialization allowlist/marker-secret tests, independent time/DST and
   assignment tables, signed-JWT HTTP negative matrices proving no sensitive
   mediator calls, and admin contract regression checks. Exercise real owned
   PostgreSQL/Keycloak tenant/employee/lifecycle cases and captured-mail fixtures.
6. Add complete desktop/mobile admin entry denial/retry/stale-role tests and
   own shift clock regression. Adapt existing admin fixtures to the verified
   identity handshake; retain all existing feature tests. Run real cleaner login
   and API checks against disposable services without authentication mocks.
7. Run full backend, Playwright, type/build checks. Document exact results and
   limitations, push PR, resolve review comments, merge and verify deployment.
   Close #65 only after its scope is proven; start the next issue from latest main.

Reviewer advisories: freeze existing form JSON fields; test malicious foreign
form results even for an owned occurrence; test malformed GUID and documented
timezone fallback. No unapproved manager privilege or unrelated feature UI.
