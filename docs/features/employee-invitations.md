# Employee invitation and account setup (issue49)

## Confirmed defect and fix

The old API email linked to generic Keycloak registration with a /fi/admin/
redirect. Dev served404 there; the real admin route is /admin/. Registration did
not bind an Employee or assign its role. The unused invite provisioner also
reset existing passwords on retries.

The replacement is tenant-admin-only onboarding through Keycloak's native
execute-actions-email flow. Passwords are entered only on Keycloak pages, never
returned by the invitation API or emailed as temporary credentials.

1. Create the employee with its intended admin/employee role.
2. In Employees, choose **Invite** (or **Resend setup email** for a bound account).
3. API validates explicit frontend/auth URLs, realm roles, registered redirect,
   SMTP and an admin-only ownership attribute in the realm user profile.
4. Acquire a database advisory lock for tenant+normalized email and reload the
   employee. Create/reuse the precise realm account, assign the intended managed
   role after binding KeycloakUserId, then request email delivery. No role is
   granted before the binding save succeeds.
5. Keycloak sends a24-hour setup link. New users verify email and set a password.
   They return to /account/?tenant=<realm> and sign in using the SPA's normal PKCE
   flow. Admins can continue to /admin/; employees see account confirmation and
   use their assigned checklist email links. My Work remains separate scope.

## Reinvite, ownership and failures

- Successful duplicate clicks within60seconds return AlreadySent. A deliberately
  later reinvite sends a new link to the same account, without resetting passwords.
- Keycloak emailVerified/requiredActions determine actions. Completed accounts
  receive an empty native action list for continuation; no forced password change.
- An existing verified unbound user can be linked only with matching managed role
  and no foreign ownership. Unverified/ambiguous/conflicting users require manual
  administrator reconciliation. An ownership tag from the same Employee ID lets
  retries recover when Keycloak creation succeeded before the database save.
- The ownership field is `cleaning_employee_id`, declared in Keycloak's user
  profile with admin-only view/edit. User-editable versions are rejected. The API
  preserves all unrelated profile attributes and never sends an SMTP password
  mask back as a new password.
- The nullable computed identity key is unique within the tenant. Multiple
  unbound employees and the same subject string in another tenant are allowed.
  Duplicate legacy bindings fail closed during lookup.
- Save failure before binding means neither role grant nor email is performed. Delivery failure preserves
  binding and a sanitized error for retry; success is recorded only after the
  native mail endpoint succeeds. SMTP acceptance is not proof of inbox delivery.
  A process crash after delivery but before saving can cause a duplicate email.

API results: `{email,keycloakUserId,status,sentUtc}`. No temporaryPassword field.
Unknown employee404; inactive employee/identity conflict409; missing configuration
503; provider/mail failure502. The employee screen shows these failures instead
of claiming email was sent. Cancellation is propagated.

## Lifecycle and old tokens

Linked employee API requests check current IsActive, verified email and managed
role against the employee document. Deactivated/demoted identities are rejected
even with an unexpired previously issued JWT. Role/active changes are stored
locally first with IdentitySyncPending, then synchronized to Keycloak and sessions
logged out. The pending flag is cleared only after synchronization succeeds. A provider
failure is visible and retryable; pending updates deny access until reconciled.
Retrying the same update reattempts synchronization, even if local role/active values
already match. Unrelated profile edits do not log out an account.
Bound email changes return409 and require a deliberate identity-change process.
Tenant owner admins without Employee documents remain compatible. Anonymous
checklist/report token links keep their existing capability contracts; disabling
an employee login does not revoke those links. Their revocation is separate work.

## Explicit configuration

- `App:FrontendBaseUrl`: public site origin, no /fi or /admin path; required for invites.
- `Auth:Keycloak:ServerUrl`: public auth URL used by browser, issuer and mail links.
- `Auth:Keycloak:AdminServerUrl`: optional private REST/JWKS transport. It does not
  change the expected public JWT issuer.
- `EmployeeInvites:LifespanSeconds`: default86400, allowed300..604800.
- Configure SMTP directly in the realm, or supply `EmployeeInvites:Smtp` Host,
  Port, From, Auth, User, Password, StartTls, Ssl. Empty realm SMTP can be initialized
  from these settings; existing SMTP is preserved. `UseResendApiKey=true` uses
  `Resend:ApiKey` as the SMTP password only when explicitly configured.

[Resend SMTP](https://resend.com/features/smtp-service) supports username `resend`,
host smtp.resend.com, STARTTLS587 or SSL465/2465. SMTP From must be an address from
an authenticated sender domain. Store real keys only in host environment/secret
configuration. Configuring API Resend alone does not configure Keycloak SMTP.

The dev compose uses public auth http://130.61.208.211:8081 and internal transport
http://keycloak:8080. Keycloak24 uses KC_HOSTNAME_URL for that public address.
Production uses its existing public auth URL. New realm/import defaults include
employee/admin roles and disabled self-registration. Imports do not update an
existing realm automatically; apply existing-realm configuration deliberately.

## Existing deployment preflight and reconciliation

1. Back up the database and realm config. Audit duplicate nonempty KeycloakUserId
   values grouped by tenant, and email/role mismatches before deploying.
2. Reconcile duplicates using actual realm+subject and verified employee ownership;
   never infer admin privileges from email text or a displayed staff role.
3. Backfill stored `KeycloakIdentityKey` for existing bound employees before the
   unique index migration. Null/empty user IDs must produce null keys. JSON field
   casing is PascalCase in the current Marten serializer; verify in the database.
   [Backfill script](../../api/deploy/employee-identity-backfill.sql) defaults to a
   rollback dry run, rejects duplicates and preserves document IDs. Run with psql;
   only explicit `-v apply_migration=true` commits. Stop writes and back up first.
4. Ensure each existing realm has employee/admin roles and registered account
   landing redirects. The old dev import has staff instead of employee. Do not
   grant employee/admin indiscriminately to all staff or self-registered accounts.
5. Configure native SMTP and public auth/frontend URLs; verify with a controlled
   test recipient. Native24/26 user profile must allow only admin ownership edits.
6. For old unverified self-registered accounts, verify mailbox ownership through
   a native verification flow before linking. Conflicting roles/foreign ownership
   require explicit administrator review. Invite again after reconciliation.
7. Deploy API/site/config together, then verify setup, roles/binding, reinvite and
   deactivation with old tokens. Source changes do not mutate production accounts.

Useful read-only duplicate audit (check actual schema/JSON before running):

```sql
SELECT tenant_id, data ->> 'KeycloakUserId' AS subject, count(*)
FROM mt_doc_employee
WHERE coalesce(data ->> 'KeycloakUserId', '') <> ''
GROUP BY tenant_id, data ->> 'KeycloakUserId'
HAVING count(*) > 1;
```

## Test coverage and remaining scope

Application tests: explicit URLs, tenant landing, bind-before-send, missing config,
inactive employee, failed delivery/retry, cooldown, duplicate binding, local save
failure and lifecycle ordering. Provider transport tests: new/pending/completed
native actions, conflicting/unverified/foreign account protection, role removal,
SMTP-mask preservation, redirect validation and admin-only profile ownership.
HTTP tests: linked inactive/unverified/demoted/promoted identities and legacy
owner compatibility. Isolated PostgreSQL: tenant-scoped uniqueness and advisory
lock cancellation/normalization. Isolated Keycloak24+captured SMTP: actual setup
email, native failure/retry, concurrent invites, profile tag, role and expiring URL.
Browser tests cover invite errors/retry, role-specific account landing and invalid
realm handling. A separate live suite verifies native password setup and real JWT
binding/permissions. Tests use temporary .invalid recipients, never real employees.

Remaining: authenticated My Work, explicit linked-email-change UI, private
capability-link revocation, production migrations/mail deliverability and a wider
platform authorization redesign. Check these separately from the onboarding fix.

Keycloak behavior follows its [24 Admin REST API](https://www.keycloak.org/docs-api/24.0.5/rest-api/index.html)
and [native action email implementation](https://github.com/keycloak/keycloak/blob/24.0.5/services/src/main/java/org/keycloak/services/resources/admin/UserResource.java).

## Verification on 2026-10-13

- 194 backend tests passed with isolated PostgreSQL, captured SMTP and Keycloak26.1,
 including binding-before-role/save-failure, normalized email ownership,
 synchronization retries and stale-token guards. The native provider flow also
 passed against Keycloak24 earlier in the investigation.
- Full mocked desktop/mobile suite:62runs passed. After the final employee UI
 changes, all14focused onboarding desktop/mobile checks passed.
- Real captured-email browser flows passed on Keycloak24 and26.1. Final26.1 flow
 verified password setup, linked realm/sub, intended role, completed-account
 reinvite preserving the password, promotion/demotion and old-token denial after
 deactivation, with no auth/request mocks.
- TypeScript, .NET build and Next.js production build passed.
- Legacy key backfill SQL was executed as a rollback-only dry run on the isolated
 database. No production data/account/configuration was changed and no real
 employee received email.
