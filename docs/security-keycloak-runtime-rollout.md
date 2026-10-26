# Scoped identity runtime rollout (INF03 — not approved for production rollout)

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

Run `.github/workflows/keycloak-runtime-privileges.yml` against disposable Keycloak 24.0.5 and 26.1.0 services (or deliberately align supported
versions following compatibility evidence). Exercise user creation, email lookup,
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

Tenant runtime receives only `manage-users`, `query-users`, `view-users`, and
`view-realm`. Never grant `view-clients`: Keycloak's confidential client-secret
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
