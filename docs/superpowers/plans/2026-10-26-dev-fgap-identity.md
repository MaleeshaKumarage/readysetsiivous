# Development FGAP identity implementation plan

## Primary goal

Development staff invitations and access changes work using only bounded Keycloak group permissions while protected administrators, service accounts and other tenants remain inaccessible. Completion produces reproducible application and direct-token evidence; production and held PRs remain unchanged.

Base/worktree: feat/dev-fgap-identity at 6d080a9 in cleaning-site-fgap-dev. Approved specification: ../specs/2026-10-26-dev-fgap-identity-design.md. Do not commit until verification and root approval; do not push or merge.

## Phase 0: Documentation discovery and interface freeze

**How this serves the primary goal:** ensure integration follows real local APIs and the exact rehearsed permission schema.

Read the approved spec, source rehearsal evidence, pinned 26.8.1 AdminPermissionsSchema/UserResource/AdminRoles sources cited there, and the native source/tests below before editing. Freeze the group configuration contract before parallel work: /managed-users and /app-employee, /app-manager, /app-admin, fixed setup-owned paths/IDs, validated per-tenant values, no user request override. Scope A owns every shared C# client/configuration edit; scope B coordinates schema semantics without touching C# files.

Allowed existing APIs: KeycloakAdminClient.CreateUserAsync(string realm, object user, CancellationToken ct = default), GetUserRealmRolesAsync(string realm, string userId, CancellationToken ct = default) (currently DIRECT mappings), GetUserAsync, UpdateUserAsync, LogoutUserAsync and SendExecuteActionsEmailAsync; TenantKeycloakClients.ForTenant(string tenantId) enforces canonical tenant and explicit credentials. New typed group/effective-role methods need actual pinned endpoint verification; none exist at base. KeycloakException.StatusCode is nullable HttpStatusCode. Do not invent overloads or reuse direct-role results as effective roles.

References: api/CleaningSuite.Infrastructure/Keycloak/KeycloakAdminClient.cs:111,159-178,252; TenantKeycloakClients.cs:10; KeycloakEmployeeIdentityProvider.cs:37,90,106,137. Existing native examples: KeycloakEmployeeIdentityProviderTests.cs and EmployeeIdentityIntegrationTests.cs:24-33,62. Verify planned signatures against source and record exact chosen schema before starting A/B.

## Phase 1A: Runtime provider/client/configuration (parallel scope A)

**How this serves the primary goal:** staff lifecycle requests use bounded group operations and expose upstream failure clearly.

Own api/CleaningSuite.Infrastructure/Keycloak/KeycloakAdminClient.cs, KeycloakEmployeeIdentityProvider.cs, TenantKeycloakClients.cs, any dedicated C# group configuration type and C# provider/client unit tests. Copy existing realm/path escaping, cancellation, HTTP failure and owner-validation patterns. Add verified typed effective-role, configured-group lookup and membership operations. Include configured managed membership in creation, fixed role-group transitions, legacy direct-grant conflict detection, password/session preservation and protected/unmanaged ownership rejection. Preserve preflight and narrow errors. Cover creation and role-operation upstream500, no partial success, no email after failed preparation, deliberate safe retry, stale membership removal before new membership and inherited roles.

Verification: use local SDK10 at C:/Users/malee/source/NewIdeas2/cleaning-site/tmp/dotnet10/dotnet.exe; dotnet test api/CleaningSuite.Tests/CleaningSuite.Tests.csproj with relevant provider, tenant-client and preflight filters, then full suite in final phase. Keep unrelated and ownership tests meaningful; replace direct-role assumptions with inherited-role assertions. Guards: no broad runtime grants, caller groups, arbitrary forwarding, password reset, direct role assignment fallback or email-only adoption. Do not edit provisioner until phase 2.

## Phase 1B: Explicit Python setup/validator/tests/docs (parallel scope B)

**How this serves the primary goal:** the runtime's bounded permission contract can be provisioned and independently checked.

Own api/deploy/keycloak-employee-setup.py, check-keycloak-runtime-env.py, test_keycloak_employee_setup.py, test_keycloak_runtime_env.py and docs/security-keycloak-runtime-rollout.md; add narrowly scoped Python helpers/tests if required. Copy read-only-by-default main(), api(), change(), existing-secret verification-before-mutation and HTTPError sanitization patterns. Implement/check supported FGAP V2 exact policies/scopes and the agreed fixed group schema. Validate effective/composite/group-derived privileges and protected group membership; explicit apply must be idempotent, reject unsafe drift, avoid accidental existing-account enrollment and preserve SMTP/ownership checks. Document development-only application and upstream500 decision separately from unchanged production gates.

Verification: python -m unittest discover -s api/deploy -p 'test_keycloak_*.py'; include check-is-read-only, exact grants, inherited/composite escalation, wrong secret before changes, idempotency, schema drift, invalid fixed paths and sanitized upstream failure. Guards: no C# edits; no destructive blanket reset, secret printing, credential rotation or broad grants to make a probe pass.

## Phase 2: Sequential provisioner and native integration

**How this serves the primary goal:** newly created tenants and native application tests use the same bounded contract.

After A/B reconcile schema, integrate KeycloakProvisioner.ConfigureRuntimeAccountAsync (currently grants manage-users/query-users/view-users/view-realm) with explicit FGAP setup semantics. Scope owner/root edits KeycloakProvisioner.cs, provisioning tests and SyntheticKeycloakBootstrap.cs; coordinate shared client additions through A rather than concurrent edits. Change newly provisioned application owner to the configured role-group scheme where needed; preserve platform/runtime credential separation, secret proof, SMTP prerequisites, realm cleanup/recovery and no automatic legacy adoption.

References: KeycloakProvisioner.cs:27,82,114; Auth/KeycloakProvisioningRecoveryTests.cs, KeycloakAudienceProvisioningTests.cs and Employees/EmployeeIdentityIntegrationTests.cs. Check native integration bootstrap broad-role/direct-role assumptions and update only the isolated FGAP fixture. Verify provisioning recovery, audiences, tenant-client and invitation tests; test existing protected/mismatched ownership and partial failure paths. Guard: production configuration/image untouched; platform privilege never leaks into employee runtime.

## Phase 3: Actual development verification and evidence

**How this serves the primary goal:** prove real staff workflows and stolen-token denials under the final exact implementation.

Use only isolated synthetic development fixtures on the approved existing dev host, the pinned candidate digest, private network, synthetic PostgreSQL/mail/accounts and bounded resources. Inspect names/capacity first and preserve shared services. The dev host has SDK8; execute native integration with cached Docker SDK10.0.402 using a bind-mounted new isolated remote checkout. Configure INVITE_TEST_KEYCLOAK_URL and INVITE_TEST_DATABASE so native EmployeeIdentityKeycloakFact tests execute rather than skip; capture executed counts. Run actual new provider invitation/retry/concurrency, ownership/email conflict/cross-tenant, activation/deactivation and role-change workflows, native SMTP and browser code-flow login, admin/manager shift access, password preservation and session revocation. Protocol probes alone do not satisfy this phase.

Re-run the full direct stolen-runtime-token negative matrix under final permissions: protected admin/service/unmanaged users, credential deletion/reset/inline credentials, action mail, profile/ownership changes, group/composite escalation, secret/realm/permission changes and cross-realm access. Inspect protected state with bootstrap credentials after probes. Retain the known expected403/actual500 probe as an explicit failed upstream gate with no created user; separately prove actual provider fail-closed handling and retry behavior. Run meaningful full .NET/Python suites and current-head review; do not label skipped/incomplete gates passed. Record redacted evidence and exact head/configuration, then remove only uniquely labelled fixtures. Synthetic upgrade/restore evidence remains separate from production approval.

## Phase 4: Root review and stopping condition

**How this serves the primary goal:** make development results reviewable without implying production readiness.

Report changed files, tests and executed/skipped counts, remaining failed upstream gate, exact config, protected-state evidence and cleanup. Request root approval before any commit. No push, merge, held-PR release, production migration or rollout is authorized. Unresolved required workflows or unsafe permission drift block adoption; clear application500 handling never silently waives the upstream failure.


## Required compatibility checks (scope C)

Inspect LeadAdminDirectory: its direct admin-role-user enumeration may miss inherited /app-admin members. Adapt the bounded recipient directory and tests to include managed group admins and preserve ordinary tenant-owner administrator recipients while excluding protected/service accounts; do not silently broaden runtime visibility. Replace native setup-email direct-role assertions with effective /composite role assertions. Resolve the newly provisioned ordinary tenant owner's /managed-users + /app-admin membership explicitly without inventing employee ownership before an employee exists; preserve its existing bootstrap/login behavior and ownership reconciliation boundaries. Program already maps provider errors to HTTP502: retain actionable sanitized text and prove no raw upstream body or secret escapes.
