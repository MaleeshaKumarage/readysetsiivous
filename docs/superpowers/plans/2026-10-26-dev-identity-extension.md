# Development identity extension implementation

## Primary goal

Keep employee invitation and access management working while preventing application
credentials from managing privileged Keycloak identities or exposing their data.
This work is an isolated development checkpoint, with production still on hold.

## Phase 0: Verified API discovery

**How this serves the primary goal:** avoid building the protection on role APIs
that silently filter the security information being checked.

Discovery completed against Keycloak 26.8.1 sources. See the approved
[design](../specs/2026-10-26-dev-identity-extension-design.md) for primary sources.
Use RealmResourceProvider, native BearerTokenAuthenticator with an explicit
audience, UserProfileProvider, server-side effective RoleMapperModel.hasRole,
native logout and execute-actions email patterns. There is no supported generic
transaction lock covering all external administration of role graphs.

## Phase 1: Narrow server resource

**How this serves the primary goal:** move authorization to a component that can
read complete privileges, and expose only required employee operations.

Add a separate Java 21/26.8.1 provider project and development image. Copy native
authentication/profile/email patterns from the pinned sources. Authenticate the
configured service-account UUID/client/audience; reject human/alternate clients.
Validate fixed groups and targets before reading or changing them. Enumerate and
filter before pagination; serialize minimal safe DTOs. Reject forbidden creation
groups and unknown fields before creating a user. No Admin REST forwarding.

Verification: compile against exact dependencies; direct malformed/authentication/
privileged-target/hidden-group native probes; verify no partial user on failure.
Guard against empty-role-response proof, arbitrary representations, secret DTOs,
and invented SPI methods.

## Phase 2: Tenant transport and explicit setup

**How this serves the primary goal:** eliminate the application's direct Admin
REST capability while preserving invitation and session behavior.

Tenant Keycloak clients use only `/realms/{realm}/cleaning-identity/v1`, retaining
canonical path validation. Platform clients remain explicitly separate. Add a
dedicated access-token audience mapper and configured runtime UUID. Fresh setup
grants no realm-management roles or FGAP permissions. Migrating the earlier
development experiment requires an explicit flag and validates exact owned
grants before revocation; unexpected privileges fail for manual review.

Verification: transport tests prove no fallback, cross-realm traversal refusal,
unchanged platform routes, zero administration grants, check-mode no writes,
secret proof before apply, idempotency, and preserved unrelated settings.

## Phase 3: Native security and invitation verification

**How this serves the primary goal:** demonstrate successful employee operations
and failed privileged operations against the real service, independently of mocks.

Build and run only a purpose-labelled disposable extension image/container. Test
native email setup/login/retry and API audience, all setup checks, inherited and
later-promoted administrators, service accounts, alternate clients/audiences,
direct Admin REST denial, list filtering and invalid-group rollback. Add explicit
concurrent promotion probes; document any unmet atomicity/redemption condition.
Update CI to the pinned extension image build and keep strict negative assertions.

Run appropriate .NET and Python checks. Independently review all changes; fix
actionable findings. Commit/push only reviewed files and sanitized evidence.
Merge only if the required checks and actual security gates pass. A concurrency
limitation keeps production blocked; it must never be disguised as a green result.

## Ownership

- Server worker: `api/keycloak-identity-extension/**`.
- Client worker: .NET Keycloak code and its tests.
- Setup worker: Python setup/helper/tests and rollout guide.
- Coordinator: CI/native canaries, cross-component verification and evidence.

No shared development service replacement, production mutation, secret rotation,
customer data access, or release approval change is authorized by this plan.
