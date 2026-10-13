# API audiences and platform tenant management — issues 63 and 64

Status: proposed; awaiting the user's decision. No authentication behavior or realm
configuration has been changed by this investigation.

Spec review: approved by the independent spec reviewer on 2026-10-13. Product
approval remains pending. The implementation plan must include explicit platform
issuer negatives even if the registry contains that realm, a genuine JWT/JWKS
HTTP harness, idempotent mapper migration, and pre-controller rejection auditing.

## Verified problem and scope

At `599e39b`, `KeycloakJwt` sets `ValidateAudience = false`. Runtime defaults and
both compose files allow the SPA client ID, provisioning client ID and generic
`account` audience. The tenant SPA import and newly provisioned SPA clients have
no explicit API audience mapper. `SystemController` authorizes all four tenant
lifecycle routes by provisioner-realm equality only. A valid master-realm identity
therefore needs no explicit platform permission.

Fix these two related boundaries together. Keep existing tenant `admin` and
`employee` roles. Manager scope and workspace changes remain separate decisions.
Keep the backend's outbound Keycloak Admin REST credential separate from identities
authorized to call this application's platform API.

## Options

1. **Recommended:** distinct tenant and platform API audiences, with a dedicated
   platform tenant-management permission and issuer restriction. This makes the
   intended resource and business permission explicit without renaming tenant roles.
2. Enforce only a common API audience first. Smaller configuration change, but it
   leaves the realm-only platform authorization defect and needs a second rollout.
3. Split platform management into a separate deployed API. Stronger infrastructure
   separation, but adds deployment and operational work unnecessary for these fixes.

## Authentication contract

- Tenant access tokens must contain the exact `cleaning-suite-api` audience.
- Provisioner-realm tokens must contain the exact `cleaning-suite-platform-api`
  audience. A token intended for another service, the SPA ID alone or `account`
  alone fails authentication. An array containing the correct audience is valid.
- Audience validation follows the validated issuer realm, rather than accepting
  the union of tenant and platform audiences for every realm.
- Retain signature, expiry and issuer validation. Test missing expiry, expired,
  future-not-before, unsigned, tampered and incorrectly signed tokens as well as
  missing, empty, wrong, case-mismatched and mixed audience arrays.
- Require nonempty distinct configured audiences. Reject unsafe configuration at
  startup rather than silently reverting to audience-disabled validation.
- New tenant SPA clients and the dev import use an access-token audience mapper.
  Do not add the API audience to ID tokens: ID tokens are for the SPA.
- Use one source of configured tenant audience for token validation and realm
  provisioning. Existing realms require an explicit mapper migration; changing
  import JSON does not update already-created realms.

## Platform authorization contract

- All system tenant routes require authentication, the configured provisioner
  issuer realm, platform audience, and exact `platform.tenants.manage` realm role.
- Assign this role only to the dedicated platform service account/operator.
  Tenant `admin`, tenant `employee`, unrelated master users and malformed/missing
  roles receive no platform permissions. A tenant realm cannot grant itself
  platform access by inventing a same-named role or audience.
- Platform tokens cannot access tenant administration or employee self APIs.
  This restriction must hold even with tenant roles, both audiences or an active
  registry entry for the platform realm; do not rely on an absent registry entry.
- Wrong or missing resource audience returns 401 on protected routes; a valid
  platform token lacking permission returns 403. Denied requests do not invoke
  mediator handlers or mutate tenant state.
- Log structured actor subject, issuer realm, operation, affected tenant and
  outcome for platform requests. Do not log tokens, secrets, request bodies or
  returned owner temporary passwords. Distinguish rejected, failed, conflict and
  successful operations; listing has registry scope rather than one tenant.
  Audit authentication/authorization rejection before controller execution too;
  unvalidated token claims are never recorded as an authenticated actor.
- There is no new platform browser workspace in this change. Tenant UI keeps its
  existing roles and sign-in behavior.

## Provisioning and deployment

Document a dedicated confidential client with service accounts enabled, platform
audience mapper and explicitly granted platform permission. Keep browser flows
disabled for that service client. Provisioning bootstrap credentials are never
sent to the browser. Backend Keycloak Admin REST may continue using its existing
separate configured credential; an `admin-cli` token is not an application API
authorization bypass.

Prepare a repeatable, narrowly scoped migration/check procedure for existing SPA
clients and the platform client. Preflight genuine tokens before deploying strict
validation; after enabling it, tokens issued before the mapper change must refresh
or sign in again. Preserve existing roles and client settings during migration.
Do not mutate shared dev or production identities while implementing: use owned
isolated services and disposable realms. Production rollout remains separately
reviewable, with a documented rollback that does not disable audience validation.
Identify owned audience mappers by stable names and verify their type/configuration
before updating. Re-running migration must not duplicate mappers or broaden role
grants. Document exact configuration keys and backup/restore checks. Rollback may
restore a known strictly validating application build or safely stop protected
access while correcting client issuance; the vulnerable audience-disabled build
is not a valid security rollback.

## Verification and acceptance evidence

| Layer | Required checks |
| --- | --- |
| Unit/token validation | Correct tenant/platform audience by realm; wrong/missing/empty/case mismatch; multiple audiences; bad signature/issuer/lifetime; invalid configuration |
| HTTP integration | Genuine JWT middleware, all four system routes; anonymous 401; master without permission 403; tenant identities denied; proper platform identity allowed; handlers never called for negatives |
| Realm/client contract | Dev JSON mapper and new provisioned client match configured tenant audience; platform service client emits audience and permission without granting tenant roles |
| Real Keycloak | Owned realms, signed genuine tenant and service-account tokens; audience/role negatives; refresh preserves intended audience; logout invalidates refresh session |
| Playwright | Real PKCE browser login reaches account/admin flow; API accepts issued access token; refresh/re-login work; browser logout ends session; service identity permission matrix via APIRequestContext |
| Regression | Full backend suite and existing role-aware browser contracts; type check and production build |

Track evidence by AUTH-003, AUTH-005 and AUTH-007; TEN-001, TEN-004 and TEN-005.
Record exact commands, counts, service versions, fixtures and cleanup. Mock UI
booleans cannot prove JWT audiences, and a unit token test cannot prove PKCE or
Keycloak refresh/logout. Never claim global logout revokes already-issued access
tokens: employee lifecycle enforcement remains the separate issue49 mechanism.

## References

- [Issue 63](https://github.com/MaleeshaKumarage/readysetsiivous/issues/63)
- [Issue 64](https://github.com/MaleeshaKumarage/readysetsiivous/issues/64)
- [Keycloak audience support](https://www.keycloak.org/docs/26.2.5/server_admin/index.html#audience-support)
- [Issue register](../../features/issue-register.md)
- [Role and scope discussion](../../features/technical-lead-review.md)

The Keycloak documentation describes a hardcoded audience mapper for access
tokens. The separate platform resource and permission are this project's proposed
authorization design, not an automatic property of a master-realm token.
