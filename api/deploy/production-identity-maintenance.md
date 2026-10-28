# Production identity maintenance helper

`keycloak-production-identity-setup.py` is separate from the development setup CLI.
It has no development bypass. It defaults to a read-only proposed-change report;
writes require **both** `--apply --production`. It neither creates employee logins,
adopts an email match, sends setup mail, resets passwords nor rotates credentials.

Run on the maintenance host with an owner-only regular JSON plan (mode 0600 or
0400, owned by the executing user). The plan comes from the reviewed database and
Keycloak snapshot, never browser input. Keep actual identities outside source and
workflow output. The private administration base must be a literal-loopback HTTP
URL with an explicit port. Discovery fetched through that private transport must
declare the exact public HTTPS issuer below and the reviewed Keycloak version.
Redirects are refused, including on token and metadata requests. Public HTTPS
checks happen separately before/after maintenance; the public tunnel is down
during this helper's writes.

Protected environment variables:

- `KEYCLOAK_MIGRATION_ADMIN_TOKEN`: temporary, privileged maintenance bearer.
- `KEYCLOAK_TENANT_RUNTIME_CLIENT_SECRET`: the reviewed delivered runtime secret;
  existing credentials must already match. No secret is printed or generated.

Plan format (all example identities below are synthetic):

```json
{
  "schema_version": 1,
  "tenant": "tenant-a",
  "expected_issuer": "https://auth.readysetsiivous.fi/realms/tenant-a",
  "expected_version": "26.8.1",
  "runtime_client_id": "employee-runtime",
  "maintenance": {
    "api_container_name": "production-api-1",
    "api_container_id": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "compose_project": "production"
  },
  "employees": [
    {
      "user_id": "11111111-1111-4111-8111-111111111111",
      "employee_id": "22222222-2222-4222-8222-222222222222",
      "email": "employee@example.invalid",
      "expected_enabled": true,
      "enabled": true,
      "role": "employee",
      "allow_missing_owner": false
    }
  ],
  "tenant_admin_owners": [
    {
      "user_id": "33333333-3333-4333-8333-333333333333",
      "email": "owner@example.invalid",
      "expected_enabled": true,
      "approve_ownerless_admin": true
    }
  ]
}
```

Every employee entry is an exact existing database-to-login UUID link. Setting
`allow_missing_owner: true` explicitly approves adding ownership metadata only
to that exact UUID after email/status/role checks; it is never email adoption.
Ownerless tenant admins get the managed/admin groups while remaining unbound to
an employee. They are ordinary application admins, not master-realm operators.
Missing login UUIDs, conflicting owners, duplicate identities, unexpected groups,
administrative grants and application-role mismatch stop all writes at preflight.
Omit unresolved/unmatched accounts; the helper leaves them unchanged.

Before every mutation and after each phase, the helper requires the loaded
`cloudflared.service` to be inactive, the exact reviewed API container to be
stopped, and no running API replica in the reviewed Compose project. It checks
the complete defaults/role/group/service-account administration boundary first.

The reviewed setup library supplies fixed groups and the dedicated audience.
Its activation callback is deferred. Accounts join managed and intended-role
groups before only their matched direct application grant is removed. Native
profile updates explicitly preserve identity/name fields without echoing
credentials, required actions or verification settings. Credential/MFA metadata,
required actions and non-application direct grants are verified unchanged.

Only after all enrollment succeeds does a fresh realm-attribute read receive the
runtime binding. Runtime proof checks the extension and direct Admin REST denials.
An already converged plan is a no-op, including after an approved enabled-state
transition. In check mode exit 2 means proposed changes; exit 0 means no changes.

**This is not a transaction across HTTP requests.** Any apply failure leaves
ingress/API stopped and requires the separately reviewed whole-Keycloak-database
and API rollback. The helper does not attempt to undo partial enrollment or claim
that deleting a client would reverse all changes. Root must review and rehearse
this helper before any production execution; application canaries/reopening
ingress happen separately after it succeeds.
