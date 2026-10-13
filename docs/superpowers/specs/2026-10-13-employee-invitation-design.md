# Issue49: employee invitation and identity binding

The user approved fixing onboarding fully: a tenant-bound Keycloak account,
intended role, persisted employee binding and secure account-setup destination.
This design implements that approved direction rather than another URL-only fix.

## Evidence and approach

The current handler sends an unbound generic registration URL with /fi/admin/.
Development returns404 there and200 at /admin/. The actual dev realm has admin
and staff roles, no employee role, registration enabled and no SMTP configuration.
The unused provisioner invite method resets existing passwords on every retry.

Alternatives: a URL-only change leaves identity/roles unbound; a custom application
password form adds password-handling responsibilities and token security. Use
Keycloak execute-actions-email with expiring action tokens and its password UI.
Keycloak SMTP can use the current email provider; missing mail/redirect/public URL
configuration is an explicit503, not a falsely successful invitation.

## Flow and boundaries

1. Tenant admin invokes the existing employee invite endpoint. Reject inactive
   employees and invalid roles/email. Validate explicitly configured frontend
   and public Keycloak URLs, realm role, client redirect and SMTP before creating
   an identity. Configuration may explicitly initialize empty SMTP settings;
   existing nonempty realm SMTP settings are preserved.
2. An employee identity provider creates/reuses the exact realm user and tags it
   with the Employee document ID. New users have no password and require
   VERIFY_EMAIL/UPDATE_PASSWORD. Never return credentials to the API caller.
3. Persist KeycloakUserId before sending. Existing bindings must match user email
   and employee ownership. Existing unbound identities are reused only if email
   is verified and managed roles do not conflict; ambiguous/unverified/conflicting
   identities fail409 for administrator reconciliation. Passwords are never reset
   on reinvite. Concurrent creation409 re-reads ownership instead of claiming any
   account. Existing unrelated realm roles are retained; admin/employee managed
   roles must match the employee role exactly.
4. Send a native action email with24-hour configured lifespan and redirect to
   /account/?tenant=<realm>. After initial setup, reinvite does not force another
   password change; it sends verification/continuation actions. Persist attempt,
   sent time and a sanitized failure status. A failed send is retryable with the
   already bound identity. Successful sends within60seconds return the prior
   result to avoid accidental double clicks; no external exactly-once claim.
5. The public account landing page selects the validated tenant realm for the
   configured Keycloak host and initiates normal PKCE sign-in. It verifies the
   API identity/binding. Admins can continue to /admin/; employees see an account
   confirmation and use assigned checklist links. Building My Work is out of scope.
6. Add invite/reinvite controls to the existing employee screen, with visible
   success/error state and no password display.

## Identity lifecycle affected by this fix

Bound employee API access checks active status, verified email and managed role
against the employee document, so previously issued tokens cannot bypass a later
employee deactivation or demotion. Tenant owners/legacy unbound admins remain
compatible; system and anonymous public routes keep their existing contracts.
Employee updates/deactivation persist expected access first, then synchronize
Keycloak roles/enabled state and terminate sessions on deactivation. Failure is
visible/retryable; database guards fail closed for mismatching old tokens. Bound
email changes require an explicit identity-change flow and return409 here.

## Configuration and errors

Separate public Auth:Keycloak:ServerUrl (issuer/browser/email host) from optional
AdminServerUrl (internal admin/JWKS transport). SMTP host/from/auth settings and
frontend URL are explicit. New realm/import defaults include employee role and
closed self-registration. Existing realms require a documented configuration
migration; no production users/credentials are changed by source deployment.
User/account conflicts409, inactive employee409, missing employee404,
configuration503, provider failures502. Do not expose tokens, passwords or raw
provider response bodies in logs, API results or failure fields.

## Verification

Unit tests cover new/reused users, stale bindings, role conflicts, missing config,
failed delivery/retry, cooldown and password preservation. HTTP client tests assert
native Keycloak endpoint/actions/redirect/lifespan, escaped paths and role sync.
HTTP authorization tests exercise bound inactive/demoted/unverified identities.
Browser tests cover invitation UX and account role-specific landing.

A temporary Keycloak24 realm/container plus captured SMTP inbox and isolated
PostgreSQL exercise actual action email, first password setup, token role/sub,
employee binding and repeat/retry behavior. Recipients are test-only .invalid
addresses captured by the inbox. No real employee email, production account
mutation or production deployment is performed. Record runtime limitations
explicitly and remove temporary infrastructure after verification.
