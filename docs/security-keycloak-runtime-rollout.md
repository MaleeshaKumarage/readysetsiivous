# Scoped identity runtime rollout (INF03 — not approved for production rollout)

## Current development-only extension contract (2026-10-26)

The approved development identity boundary is the version-pinned server extension
described in [the extension design](superpowers/specs/2026-10-26-dev-identity-extension-design.md).
The earlier coarse-role and FGAP contracts retained below are historical evidence;
they are not instructions to grant those rights to the new runtime client.
Production configuration, images and the release hold remain unchanged.

The current runtime client has **zero realm-management roles, zero FGAP grants and
no group memberships**. Realm `adminPermissionsEnabled` must be false. It uses
client credentials with a dedicated access-token-only `cleaning-identity-runtime`
audience and `fullScopeAllowed=false`. The realm attribute
`cleaning_identity_runtime_client_id` binds the extension to the client's immutable
internal UUID, not merely its display/client name. The application calls only
`/realms/<tenant>/cleaning-identity/v1`; it never falls back to Admin REST when the
extension is missing or denies a request. Platform provisioning remains separate.

Explicit setup is `api/deploy/keycloak-employee-setup.py`. Check is read-only by
default (a client-credentials exchange is the only non-GET proof). Exact serverinfo
version 26.8.1 is required, and apply requires `--apply --development` for the
isolated fixture. Existing secrets must authenticate before any setup mutation;
they are never rotated. Missing prerequisite/profile/group changes are reported as
NEEDS with exit 2; ready checks exit 0. API runtime cannot repair prerequisites.
After apply, and on an unchanged check, setup obtains a fresh audience-bearing
token and verifies the authenticated extension response for the exact realm.
A stock server or denied extension cannot be reported ready merely because
client-credentials token issuance succeeded.

Migration of an earlier reviewed FGAP fixture additionally requires
`--migrate-fgap --development`. Setup validates the exact known policy subject,
group bindings, scopes and policy relationships, then deletes only those owned
permissions/policy and direct `query-users`/`view-realm` grants. It verifies their
absence and disables the FGAP path before installing the dedicated audience and
activating the extension binding last. Foreign policies, broader or inherited
administration roles, malformed/incomplete inventories, group-derived runtime
privileges and existing binding/mapper drift require manual reconciliation.
The migration flag never authorizes arbitrary privilege revocation or secret rotation.

Both fresh setup and migration retain the four plain fixed root groups, flat
application roles and administrator-only `cleaning_employee_id` profile field.
No existing person is automatically enrolled or adopted. Unrelated realm/profile
settings remain intact. Existing SMTP passwords are never copied into a response
or blindly written back from masked values.

Required evidence includes native invitation delivery and login, direct Admin REST
denials, forbidden creation input with no partial user, protected/drifted account
denials, directory filtering, malformed input and role/composite inheritance.
Concurrency and post-invitation promotion remain explicit production adoption
gates until independently demonstrated; ordinary transaction use is not proof
that operator role-graph changes are serialized.

---

## Historical boundary and rollout evidence

This branch removes the API bootstrap-password fallback and splits platform provisioning
from employee/lead identity clients. Each tenant uses its own realm token endpoint and
explicit confidential service account. Missing configuration fails closed. Runtime
invitation validation reads prerequisites; it never repairs SMTP, frontend URL, the
ownership profile attribute, clients or roles.

## Configuration contract

A protected API configuration source must supply
`Auth:Keycloak:TenantAccounts:<canonical-realm>:ClientId` and `ClientSecret` for every
existing tenant. No default tenant account and no master fallback exist. Keep secrets
out of tracked JSON, command arguments, logs, DTOs and workflow artifacts. The platform
provisioner remains a separate `PlatformKeycloakClient`, using only client credentials.
Its API application audience/permission remains independent of Keycloak administration
permissions. Remove old `AdminUser`/`AdminPassword` from API runtime configuration after
successful rollout. Bootstrap credentials belong only to deliberate setup tooling.

New tenant creation is paused by `Auth:Keycloak:ProvisioningSetupReviewed=false` before
creating any realm. Do not enable this flag until explicit service-account provisioning,
per-tenant secret delivery and setup verification are implemented and rehearsed. A new
realm must never be reported ready while its employee runtime credentials are missing.

## Prerequisites owned by deliberate setup

For each named tenant, check enabled realm, correct public frontend URL, SMTP host/from,
enabled web client and registered redirects, all admin/manager/employee realm roles,
and the `cleaning_employee_id` user-profile attribute (single value, admin-only view/edit).
A check command must be read-only by default; `--apply` may update only explicitly owned
fields and must preserve unrelated profile/realm settings. Never echo masked SMTP passwords
back into an update. Secrets come from environment or protected configuration only.

## Required synthetic gate before merge/rollout

The earlier rehearsal targeted disposable Keycloak 24.0.5 and 26.1.0 services;
the current extension workflow targets only the reviewed pinned 26.8.1 image.
Exercise user creation, email lookup,
owner linking, role changes, invite/actions email, enable/disable, logout and lead admin
directory. Assert tenant A credentials receive denial for tenant B and master, realm/SMTP/
client/profile changes, and mapping administration roles not granted to the account.
Do not treat endpoint filtering in this API as proof that stolen credentials are scoped.
Check exact-version role-map semantics. The official guide documents limits on mapping
admin roles and notes that built-in admin roles bypass fine-grained permissions:
https://www.keycloak.org/docs/latest/server_admin/index.html

Local fake-HTTP tests establish routing/early denials only. They cannot establish the
Keycloak service-account permission matrix. The native invitation fixture now provisions explicit prerequisites and uses a scoped tenant
client. CI runs this fixture and audience regression separately from direct privilege probes.

## Concrete rollout and rollback review

1. Inventory only canonical realm names and identity prerequisites; no customer payloads.
2. Snapshot Keycloak database/config and retain existing credential references securely.
3. Prepare explicit service accounts and grants using the tested version-specific setup;
   default to check, review the diff, then apply only named realms. Protect per-tenant
   secret delivery into API configuration; verify no secret-bearing log/artifact.
4. Rehearse all identity and denial cases in synthetic CI, then review production changes.
5. Deploy configuration and matching API together in a maintenance window. Verify auth,
   safe identity operations and directory; remove API access to bootstrap credentials.
6. If verification fails, pause invitations/identity changes, restore previous image and
   protected configuration together, retain local IdentitySyncPending state and reconcile.
   Never repair failure by granting realm-admin/master rights to tenant runtime accounts.

Setup automation is `api/deploy/keycloak-employee-setup.py`: check by default, explicit
`--apply` for named-tenant prerequisites and missing runtime client/grants. The script
requires `KEYCLOAK_MIGRATION_ADMIN_TOKEN`, accepts optional SMTP JSON in
`KEYCLOAK_TENANT_SMTP_JSON`, and takes the runtime client name/secret from
`KEYCLOAK_TENANT_RUNTIME_CLIENT_ID` / `KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET`.
It refuses unexpected administration grants rather than silently revoking them.
Preload the same tenant client ID/secret into protected API configuration. The app never
exports the generated/provided secret. New-tenant provisioning uses preloaded credentials;
missing credentials, mail settings or rollout approval reject before creating the realm.

Outstanding gates: successful exact-version real-service CI, reviewed production secret
configuration/migration, and rollout evidence. INF03 remains open until these are proven.

### Required protected configuration delivery

Both production and development Compose now declare an API-only `env_file` whose required
host path comes from `API_IDENTITY_ENV_FILE`. Missing path/file blocks Compose startup;
there is no implicit empty fallback. Adding a tenant requires updating this protected host
file, not editing tracked Compose or embedding credentials in the image. It must contain
only complete pairs named `Auth__Keycloak__TenantAccounts__<realm>__ClientId` and
`Auth__Keycloak__TenantAccounts__<realm>__ClientSecret`. Use a dedicated canonical runtime
client name and opaque unquoted secret; interpolation, bootstrap/platform keys, duplicates
and incomplete pairs are rejected. Do not publish `docker compose config` output because
resolved environment values include secrets.

Before any Compose config/pull/up, the deployment preflight must run:

```sh
python3 api/deploy/check-keycloak-runtime-env.py --file "$API_IDENTITY_ENV_FILE"
```

For a non-master platform realm append `--platform-realm <configured-platform-realm>`.
Run this on the actual POSIX deployment host. Windows cannot establish the required POSIX
ownership/permission proof and is intentionally rejected. Use an absolute canonical path
outside this checkout, for example
`/opt/cleaning-suite-secrets/api-identity.env`, owned by root or the deployment user with
mode0400/0600 and one hard link. The checker rejects its own checkout; the rollout reviewer must also verify that the selected host path is outside any other repository checkout. Parent directories must be owned by root/the deployment
user and must not be writable by group or others; do not use `/tmp` or symlink components.
Create/review this protected file through the host's normal secret-administration process;
this change does not create any production secrets or modify host permissions. The checker
prints only success/account count or a generic rejection, never credential values.

The same file must be present on each API host/replica before provisioning a new tenant.
Keep `ProvisioningSetupReviewed` false until the exact rollout configuration is reviewed.
Bootstrap credentials remain separate on the Keycloak setup container and are never
injected into the API. An approved rollout may change the provisioning gate through its
reviewed configuration; the tenant credential file cannot change that gate.

The explicit setup command also requires an expected redirect URI, repeated for each
frontend. Example (token/SMTP/runtime secrets remain only in protected environment):

```sh
python3 api/deploy/keycloak-employee-setup.py --server https://auth.readysetsiivous.fi \
  --tenant tenant-a --public-url https://auth.readysetsiivous.fi \
  --redirect-uri 'https://readysetsiivous.fi/account/?tenant=tenant-a'
```

It refuses missing/disabled web clients and unregistered landing URIs rather than reporting
clean setup. Default check remains read-only; `--apply` repairs only owned prerequisites.
Effective realm/client composite grants are inspected across a bounded complete client
inventory, and service-account group membership or unexpected effective grants require
manual review. No broad or inherited privilege is silently revoked or accepted.

The Python rehearsal also probes cross-realm reads, forbidden configuration writes and
forbidden administrative role mapping directly with the scoped token. Its disposable
realm deletion is confined to generated `scoped-a-*`/`scoped-b-*` names on loopback CI.


### Confidential client isolation and public invitation preflight

The historical coarse setup is superseded for isolated development by the contract below.
Tenant runtime receives only `query-users` and `view-realm`. Never grant `view-clients`: Keycloak's confidential client-secret
endpoint accepts client view permission. Existing accounts with this grant require
explicit operator review/removal; setup refuses unexpected grants rather than
silently revoking them. No preview fine-grained authorization feature is enabled.

Invitation validation retains live realm enabled/frontend/SMTP, role and ownership
profile checks. It validates the web client and exact landing URI through a public
OIDC code-flow authorization request with PKCE and `prompt=none`. A dedicated
HTTP client sends no administrative token or cookies and never follows redirects.
Within ten seconds it must receive a login/interaction-required error redirect to
the exact landing path and original query, with matching random state and the
configured public issuer when present. Extra code/token or unexpected fields fail
closed. Internal transport uses `AdminServerUrl`; issuer comparison uses `ServerUrl`.
The response body is not buffered. Setup still inspects the full public web-client
configuration with its separate administration credentials.
The public probe establishes current authorization and redirect acceptance; it does
not establish that the client is public. Setup separately requires `publicClient`
and `standardFlowEnabled` to be true before reporting clean prerequisites.

The exact-version synthetic matrix must also deny runtime access to its own and a
canary confidential client's secret, verify that realm reads mask the SMTP password,
and reject disabled/missing clients and unregistered redirects. Existing native
invitation delivery remains a required compatibility gate.

After a successful new-realm create, a subsequent provisioning failure attempts
cleanup with an independent ten-second deadline. A create conflict never triggers
deletion. If cleanup fails, retain the realm for explicit reconciliation; a retry
must not adopt or delete the existing realm. Provisioning merges owned frontend
attributes into the current realm attributes before updating SMTP.

### Credential proof and remaining coarse-role blocker

Check/apply must authenticate the supplied runtime client secret against its named
realm token endpoint. For an existing client this proof precedes setup mutations;
an incorrect delivered secret fails without rotating the existing credential.
A missing client checked without apply remains incomplete. After explicit creation,
apply verifies the newly delivered credential. A check token exchange changes no
realm configuration; tokens and secrets must never appear in output. Provisioning
also proves the new runtime credential with a fresh token exchange and ten-second
deadline before reporting the tenant ready; failure enters new-realm cleanup.

The built-in `manage-users` role is coarse: it permits password resets on same-realm
administrative users and group membership changes. A group carrying a Keycloak
administration role can therefore escalate a stolen runtime credential. Removing
`view-clients` does not remove this separate capability. The synthetic gate now
reports the actual reset/group/escalated-mutation statuses and fails if these
operations succeed. Do not interpret previous direct-role-mapping denials as
proof against indirect group escalation. A green gate and approved architecture
decision are required before merge or rollout.

Keeping public Keycloak administration REST inaccessible, with an explicit private
`AdminServerUrl`, reduces exposure of stolen credentials outside the service network;
it does not narrow the same-realm rights available from that network. Achieving
server-enforced per-user or endpoint restrictions needs a separately reviewed design
(for example a restricted administration boundary or supported fine-grained model).
No preview feature, new intermediary, or version upgrade is enabled by this change.


## Superseded development FGAP V2 contract (2026-10-26)

Production rollout, image changes and held PRs remain unapproved. Explicit setup now
requires serverinfo version 26.8.1; `--apply` additionally requires `--development`.
Use only the isolated candidate fixture pinned in the approved development design.
The runtime's only realm-management grants are `query-users` and `view-realm`.
Existing `manage-users`, `view-users`, `view-clients` or other broad grants fail
manual review; setup never silently revokes them or rotates an existing secret.
Delivered-secret proof precedes every existing-client mutation.

Setup owns exactly `/managed-users`, `/app-employee`, `/app-manager`, `/app-admin`.
These are plain roots without parents, children or client roles. Application groups
map exactly their corresponding flat realm role; the main group has no roles.
Unexpected mappings/composites, service accounts and protected administrators in
managed groups fail validation. Missing groups/mappings are additive setup only;
existing users are never enrolled, adopted or reconciled automatically.

FGAP V2 uses realm `adminPermissionsEnabled=true`, the `admin-permissions` client's
`authz/resource-server/policy/user` policy `runtime-subject` with POSITIVE logic
and exactly the runtime service-account user ID. Two scope permissions use
`resourceType=Groups`, `decisionStrategy=UNANIMOUS` and that policy only:
`managed-members` targets only the managed group with `view`, `view-members`,
`manage-members`, `manage-membership`, `manage-membership-of-members`;
`fixed-application-role-groups` targets the three role groups with only `view`
and `manage-membership`. No user/role mapping permission exists. Unexpected
policies or permissions require manual review; setup never resets authorization.

Default check emits NEEDS and exits 2 for missing prerequisites without administrative
writes. A token exchange is the only non-GET credential proof. Explicit apply is
additive and idempotent. The Python helper returns exact server group IDs for the
fixed paths; requests and environment cannot override these paths.

The known hidden privileged-group create response HTTP500 remains a failed
upstream expected403 gate. Application fail-closed behavior and deliberate retry
must be verified separately; neither a handled error nor fake-HTTP tests waive the
required native invitation, browser login, protected-state and stolen-token matrix.
Earlier coarse/version gates above remain historical production blockers, not an
instruction to grant broad rights or change production. Runtime setup must pass
real 26.8.1 validation before any adoption approval.

Keycloak 26.8.1 scope-permission GET is sparse. Setup validates bindings using
`/policy/{id}/resources`, `/scopes`, and `/associatedPolicies`, comparing exact
group IDs, scope names and a single known runtime user-policy ID. It does not
assume POST fields round-trip through permission GET. Null or malformed
relationship responses fail before additive changes.

### Protected-account drift remains an adoption blocker

The authority boundary is membership of the setup-owned managed groups. Before
granting a person any Keycloak administration privilege, an operator must remove
that person from every managed/application-role group. Re-run privileged setup
validation after administrative changes. Setup's privileged inspection rejects
direct and composite administration roles on managed members.

Do not claim the runtime independently detects every violation of this rule.
Keycloak filters user role-mapping responses by the caller's role visibility:
an administrative role can therefore be omitted from an HTTP200 empty list.
The pinned Roles permission schema has no `view` scope; a disposable experiment
to grant it returned400 and was fully cleaned up. Broader client visibility has
not been granted, because client view can expose secrets. The diagnostic realm
attribute `cleaning_runtime_management_client_id` records setup's verified client
ID; it does not prove that a filtered role response is complete.

An experimental runtime drift check failed its native test and was removed,
including its misleading mock-only passing test. The failed result is retained
in local audit evidence. A separately reviewed administration boundary or another
proven policy model is required if protection must survive an operator leaving
an administrative account inside the managed group. This development branch is
not approved for adoption or production rollout under that stronger requirement.
