# Development-only employee identity extension

Status: implemented and natively verified on the isolated development candidate.
The [guarded-invitation follow-up](2026-10-26-guarded-invitations-design.md) and its
[verification record](../../security/2026-10-27-guarded-invitations-verification.json)
record the later invitation and privilege-edge safeguards beyond this initial design.
Supersedes the application-facing FGAP grants in the earlier development design.
Production application/provider deployment and the release hold remain unchanged.
The later, explicitly approved master-operator account/SMTP setup was an
[account-only exception](../../security/2026-10-27-master-operator-setup-outcome.md).

## Outcome

Invitations and employee access changes continue to work, but a stolen application
credential cannot use Keycloak Admin REST, operate on privileged identities, read
client secrets, or obtain privileged users through searches or member lists.
Forbidden creation input returns a clear 403 before any user is created.

## Decision and alternatives

Use a small, version-pinned Keycloak RealmResourceProvider. A standalone broker
would add another service and still require privileged transport and transaction
coordination. A custom FGAP policy alone is insufficient: native FGAP v2 query
filtering skips normal per-target evaluation for lists. Keep the existing fixed
groups as the enrollment model, but remove the runtime's Admin REST grants and
its authorization policy/permission entries before enabling the extension.

## Boundary and transport

The endpoint prefix is `/realms/{realm}/cleaning-identity/v1`. A deliberately small
compatibility surface beneath it mirrors only the employee client's required
read/profile/group/logout/invitation operations. It is not an Admin REST proxy.
There is no generic dispatch, client-secret endpoint, arbitrary role mapping,
credential reset, realm update, group creation, user deletion or user-profile
configuration write. Unimplemented operations fail closed.

Tenant clients use this prefix automatically. Platform provisioning continues to
use its separate explicit Admin REST client. No fallback from the extension to
Admin REST is permitted when the extension is missing or returns an error.

Authenticate with Keycloak's BearerTokenAuthenticator, requiring the exact
`cleaning-identity-runtime` audience, configured runtime client UUID/client ID,
and that client's actual service-account user. Validate token issuer, signature,
expiry, revocation and audience through the native verifier. Human access tokens,
other clients, another realm, disabled clients and missing configuration fail.
The configured client is confidential, client-credentials-only, and has zero
realm-management roles and zero FGAP grants. The dedicated audience mapper is
setup-owned. Extension configuration is explicit, never inferred from a caller.

## Complete authorization reads

Resolve fixed root groups by exact path and ID; validate flat application roles,
no nested groups and no client-role mappings. Read actual server models rather
than visibility-filtered Admin REST responses. Materialize relevant streams inside
`AdminPermissionsSchema.runWithoutAuthorization` when needed.

For every target require managed-users membership, no service-account identity,
and no effective realm-management role, including roles inherited through groups,
ancestor groups and composites. Reject unexpected groups and malformed employee
ownership metadata. Tenant owner directory entries may have no employee binding,
but employee mutations and setup emails require one canonical nonzero employee
UUID; ownership is immutable through this surface.

Searches/member lists authorize each candidate before serialization and apply
pagination to authorized results. Never return a privileged target, its email,
attributes or raw role data. For an authorized ordinary target only, minimal
realm-role metadata is available so the application can reject legacy direct
application grants; this does not expose client-role or privileged-target data.
Any count must use that same authorized set.
Fixed groups expose only their minimal validated application-role DTOs.
Realm configuration reads expose only fields needed for invitation preflight,
never SMTP passwords, confidential clients, unrelated attributes or secrets.

## Writes and email

Creation accepts only username/email/name/enabled/emailVerified=false, one valid
ownership UUID and exactly the managed-users group. Reject any other group,
credentials, federation settings, role mappings or arbitrary attributes before
creation. Validate using the native user-profile provider and roll back creation
if defaults or post-creation membership grant unexpected privileges.
The existing client's exact VERIFY_EMAIL / UPDATE_PASSWORD creation actions are
accepted; the server supplies these two actions when omitted. Other actions fail.

Updates accept names, enabled and unchanged ownership metadata only. They cannot
change username/email, verification state, required actions, credentials, other
attributes or roles. Role transitions use only membership of the three validated
application-role groups. Removing the managed-users group is forbidden.

Logout uses native session invalidation. Setup email accepts only the configured
web client, registered redirect URI, bounded lifespan and UPDATE_PASSWORD /
VERIFY_EMAIL actions. Follow the native execute-actions token/email flow; never
return the token or action URL to the application or logs. Preserve normal
invitation retries and existing password/required-action state.

Return safe status/error codes. Do not label every upstream 500 an authorization
denial; specifically reject forbidden input locally. Unknown fields fail rather
than being silently forwarded.

## Concurrency limits and production gate

Complete server reads fix the demonstrated steady-state privilege drift. A single
transaction alone does not prove protection against concurrent operator writes
to group/composite role graphs, and a user-row lock does not cover those graphs.
Native tests must include operator promotion before and during mutation and
promotion after invitation issuance. Do not claim these are closed by a generic
policy, local mutex or cooperative lock that native administration ignores.

Keep production blocked until the supported promotion protocol and concurrent
writer behavior are demonstrated. If strict atomic exclusion needs a controlled
single-writer administration workflow or custom token-redemption guard, record
that explicitly and review the additional design before production adoption.

## Packaging and verification

Java 21, exact Keycloak 26.8.1 dependencies, service registration for
RealmResourceProviderFactory, empty META-INF/beans.xml and a provider JAX-RS
resource. Build a separate development image from the reviewed pinned base;
never replace shared dev or production Keycloak during the experiment.

Native verification covers valid invitations and login, wrong audience/client/
realm/human tokens, missing configuration, direct Admin REST denials, complete
privilege inheritance, drifted users, filtered lists/counts, hidden creation
groups, malformed ownership, arbitrary fields, setup retries, session revocation,
partial failure rollback and concurrent writers. Keep failing canaries as merge
gates rather than accepting their observed insecure statuses.

## Primary source evidence

- Keycloak 26.8 Server Developer Guide, custom REST endpoints:
  https://www.keycloak.org/docs/26.8.0/server_development/
- Pinned bearer authentication:
  https://github.com/keycloak/keycloak/blob/26.8.1/services/src/main/java/org/keycloak/services/managers/AppAuthManager.java
- Effective role contract:
  https://github.com/keycloak/keycloak/blob/26.8.1/server-spi/src/main/java/org/keycloak/models/RoleMapperModel.java
- Native invitation flow:
  https://github.com/keycloak/keycloak/blob/26.8.1/services/src/main/java/org/keycloak/services/resources/admin/UserResource.java
- FGAP complete-read helper:
  https://github.com/keycloak/keycloak/blob/26.8.1/server-spi-private/src/main/java/org/keycloak/authorization/fgap/AdminPermissionsSchema.java
- Query limitation:
  https://github.com/keycloak/keycloak/blob/26.8.1/services/src/main/java/org/keycloak/services/resources/admin/UsersResource.java

These SPIs include private APIs. Signing a JAR does not remove the compatibility
dependency; pinning and native regression tests are required for every upgrade.
