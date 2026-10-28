# Development FGAP identity design

Status: APPROVED for development-only implementation on 2026-10-26 following the user's Continue instruction. No production rollout, merge, push, production migration or release approval is included. PRs #202/#203/#206 remain held. Base: 6d080a9.

This bounded implementation adopts the group-based application-role lifecycle and explicit upstream-5xx handling described in the preserved rehearsal below. The earlier review-draft decision and feasibility plan are historical evidence, not a grant to weaken these boundaries.

## Approved implementation contract

- Use the pinned Keycloak 26.8.1 candidate only in isolated development fixtures. Preserve production image/configuration.
- Explicit administrative setup owns one plain /managed-users group and the three fixed application-role groups /app-employee, /app-manager and /app-admin. Each role group maps exactly one corresponding non-composite realm application role; reject privileged parents, composites, effective administration privileges and unexpected grants.
- Main-group scopes are view, view-members, manage-members, manage-membership and manage-membership-of-members; role-group scopes are view and manage-membership only. Runtime coarse roles are query-users and view-realm only. No manage-users, view-users, view-clients, direct role-mapping permission or permission-configuration capability.
- Use one validated main/runtime configuration schema for fixed group identities/paths, shared in meaning by runtime, explicit setup and provisioner. Reject arbitrary caller-provided group paths or identifiers. Configuration does not authorize arbitrary enrollment or change identity ownership.
- Creation supplies the fixed managed-users group. Role transitions remove stale configured role memberships before adding the intended role, read effective inherited roles, preserve passwords and terminate old sessions. Detect legacy direct managed-role grants and require explicit reconciliation rather than widening runtime permissions.
- Preserve tenant and employee ownership verification before mutations, administrator-only cleaning_employee_id profile configuration, exact email lookup, conflicting/ambiguous ownership rejection, invitation retries and concurrency protections. Email equality cannot authorize adoption of an unmanaged account. Existing-account enrollment requires reviewed reconciliation.
- Upstream 5xx must produce an explicit failed provider operation, never successful invitation/linkage or implicit retry/adoption. Verify partial state and ownership safely during a subsequent deliberate retry. Do not grant protected-group visibility or relax permissions to mask upstream errors.
- The observed hidden privileged-group creation HTTP500 remains a failed upstream expected-403 gate. Keep its nonzero result and distinguish the application's verified fail-closed handling from an upstream fix; no waiver or merge approval follows from application handling.

## Preserved approved evidence and design source
# Development identity permissions rehearsal — 2026-10-26

## Outcome

The group-based application-role approach is feasible in the tested synthetic
Keycloak 26.8.1 configuration. This is not an implemented application fix or a
production approval. #202/#203/#206 remain held.

The isolated test ran on the existing development host. Shared dev services and
production were not replaced. It used its own internal network, synthetic H2 and
PostgreSQL databases, synthetic accounts, and an internal Mailpit mailbox. No
production data, credentials, or email recipients were used.
All rehearsal containers and the dedicated network were removed after testing;
`fgap-dev-cleanup.json` confirms shared development and existing audit services
were still running. The downloaded candidate image remains cached for later dev work.

## Evidence

- `fgap-dev-results.jsonl`: initial 75 checks,72 passes. Direct application-role
  assignment failed three feasibility checks with403. An administrator granting
  an individual user extra mapping permission made that diagnostic succeed, but
  it did not solve automatic enrollment for subsequent users.
- `fgap-group-dev-results.jsonl`: a fresh realm using **no** diagnostic user
  mapping grant and **no** direct role mapping permission. 113 checks completed,
  112 passed, one failed expected status as described below. Every final
  group-role feasibility check passed.
- Creation, ownership-attribute persistence, exact-email lookup, duplicate
  username/email rejection, profile changes, activation, deactivation, logout,
  setup-email retries, and password preservation were exercised against real
  Keycloak endpoints. These are protocol probes, not the application's full
  onboarding workflow or browser tests.
- Fresh password-grant test logins received exactly employee, then manager,
  then admin as previous role-group membership was removed before replacement.
  None received Keycloak realm-management roles. These synthetic password-grant
  clients are test fixtures; production login remains a separate code-flow gate.
- Under the **final** group permissions, protected Keycloak administrator,
  runtime service account, and unmanaged user could not be enrolled, disabled,
  renamed, assigned a different email, reset through either password endpoint or
  inline credentials, logged out, impersonated, sent password action emails, or
  assigned direct application/Keycloak roles. Bootstrap reads confirmed profile,
  enablement, and group membership remained unchanged.
- Privileged group joining, direct Keycloak role grants, client-secret reads,
  realm mutations, permission changes, and cross-realm access were denied.
  Credential deletion was additionally denied before the final membership scope
  was added; this endpoint should also be retained in the final integration matrix.
- Revoked refresh returned400 with `invalid_grant`, not merely an arbitrary400.
- `fgap-dev-isolation.json`: no published ports or mounted volumes; candidate
  Keycloak limited to2GiB. Mailpit held nine messages and all recipients were
  synthetic fixture addresses. No mail left the internal network.
- `fgap-upgrade-dev-results.jsonl`:7/7 synthetic PostgreSQL checks passed. A
 26.1 database upgraded on26.8.1 retained user/group IDs, username, email, password,
  inherited role, and a pre-upgrade refresh session. A pre-upgrade `pg_dump -Fc`
  restored into a separate database and logged in successfully on the original
 26.1 image. Rollback proof used restore, never schema downgrade.

## Remaining failed expectation

`create-in-privileged-group` returned500 instead of403 once the runtime had the
read-only roles needed by existing invitation preflight. The server logged
`Unable to find group specified by path: /protected-role-carrier` while resolving
a group hidden by permissions. Bootstrap lookup verified no `escalation-worker`
account was created. The probe deliberately exits nonzero and keeps this failure
in its summary; it is not treated as a passing denial or a demonstrated takeover.

This upstream error needs an explicit regression and handling decision. Future
application code must generate group paths from its fixed configuration, reject
caller-supplied groups, and handle upstream failures without silent partial
success. Do not relax the protected group permission to remove the500 response.

## Proposed implementation design for review

1. Use a supported pinned Keycloak26.8.1 baseline for development integration.
   Keep the production image and release approval unchanged.
2. Explicit setup owns a plain `managed-users` group and three fixed application
   role groups. Each role group maps exactly one non-composite application role;
   none may inherit Keycloak administration privileges or privileged parent groups.
   Existing identities require reviewed ownership reconciliation before enrollment.
3. Runtime gets `query-users` and `view-realm` for current lookup/preflight behavior,
   plus FGAP scopes on these groups. It receives no `manage-users`, `view-users`,
   `view-clients`, arbitrary role-mapping permission, or permission configuration access.
4. Main group scopes: `view`, `view-members`, `manage-members`,
   `manage-membership`, `manage-membership-of-members`. Role group scopes: `view`
   and `manage-membership` only. Protected and service accounts remain outside
   all managed groups. Setup must reject unexpected privileged membership/grants.
5. Application identity code creates users inside the main managed group and
   changes application roles through these three memberships. Read **effective**
   application roles, since the current direct-role query will miss inherited ones.
   Remove old role memberships before adding the new one, preserve passwords,
   and invalidate old sessions. Detect conflicting direct legacy grants instead
   of trying to manage them with a broader runtime credential.
6. Preserve exact tenant/employee ownership checks before mutation. Matching an
   email alone must not adopt an unrelated login. Existing ownership conflict and
   parallel-invitation tests must be rerun with the actual new provider against
   development Keycloak/PostgreSQL, not inferred from these protocol probes.

## Required before adopting or merging the identity fix

Implement the reviewed provider/setup changes on an isolated development branch;
exercise actual API invitations, activation, role changes and code-flow login,
ownership/conflict/retry/concurrency, SMTP delivery, and administrator/manager
shift access. Re-run the full negative matrix under the final exact configuration,
including credential deletion, composites, inherited privileges, profile ownership,
and cross-tenant users. Obtain a clean current-head review and CI result. The
upstream500 and its application behavior need an explicit documented resolution.

The seven database checks demonstrate a synthetic rehearsal only. Any eventual
production upgrade still needs its own backup/restore proof, maintenance window,
exact image compatibility, configuration review and approval.

## Image evidence and sources

- Candidate26.8.1: `quay.io/keycloak/keycloak@sha256:a78d6b79572168cb25886b80a8b5b8d4ed4390743e3eecb810538683e209dd6b`.
- Original26.1: `quay.io/keycloak/keycloak@sha256:be6a86215213145bfb4fb3e2b3ab982a806d00262655abdcf3ffa6a38d241c7c`.
- [Official26.8.1 release](https://github.com/keycloak/keycloak/releases/tag/26.8.1).
- [Pinned permission schema](https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/server-spi-private/src/main/java/org/keycloak/authorization/fgap/AdminPermissionsSchema.java).
- [Pinned user/group enforcement](https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/services/src/main/java/org/keycloak/services/resources/admin/UserResource.java).
- [Pinned administrative role checks](https://raw.githubusercontent.com/keycloak/keycloak/26.8.1/server-spi-private/src/main/java/org/keycloak/models/AdminRoles.java).
