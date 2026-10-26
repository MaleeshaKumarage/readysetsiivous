# Employee identity extension — development only

This Keycloak 26.8.1/Java 21 provider exposes only the employee operations under
`/realms/{realm}/cleaning-identity/v1`. It never proxies Admin REST. The realm must
explicitly configure `cleaning_identity_runtime_client_id` with the internal UUID
of its confidential client-credentials-only runtime client. Access tokens must
carry audience `cleaning-identity-runtime`. Runtime must have no effective
realm-management roles; this initial boundary also refuses FGAP-enabled realms.

Build with `mvn -B -ntp package`, or build the development image with
`docker build -t cleaning-identity-extension:dev api/keycloak-identity-extension`.
Do not install this into shared dev or production during the rehearsal. Test an
isolated container with synthetic accounts and remove its fixtures afterwards.

The extension materializes complete server-model reads, rejects privileged or
unexpectedly grouped targets before returning data, and filters list candidates
before pagination. Creation accepts only managed enrollment with immutable
employee ownership; unknown fields and arbitrary groups fail before creation.
No credential reset, direct role mapping, realm mutation, client access or user
deletion endpoint exists.

The native user-profile provider validates profiles. Native Keycloak bearer,
redirect and execute-action-token/email APIs are pinned to 26.8.1; upgrading
requires compilation and the complete native negative/positive test matrix.

**Production hold:** full-model checks solve demonstrated steady-state privilege
drift, but do not establish serializable exclusion against simultaneous operator
promotion through role/group/composite writers. Setup tokens already issued
before a promotion also require an explicit redemption/promotion policy before
production adoption. A local lock or ordinary user transaction does not prove
these cases. SMTP is an external side effect and cannot be undone by DB rollback.
This image is a development candidate until these cases and invitation/browser
regressions are verified; do not describe it as production-ready.
