# Employee identity extension — development only

This Keycloak 26.8.1/Java 21 provider exposes only the employee operations under
`/realms/{realm}/cleaning-identity/v1`. It never proxies Admin REST. The realm must
explicitly configure `cleaning_identity_runtime_client_id` with the internal UUID
of its confidential client-credentials-only runtime client. Access tokens must
carry audience `cleaning-identity-runtime`. Runtime must have no effective
realm-management roles; this initial boundary also refuses FGAP-enabled realms.

Build with `mvn -B -ntp package`, or build the development image with
`docker build -t cleaning-identity-extension:dev api/keycloak-identity-extension`.
Do not install this into shared dev or production during the rehearsal. Test an
isolated container with synthetic accounts and remove its fixtures afterwards.

The extension materializes complete server-model reads, rejects privileged or
unexpectedly grouped targets before returning data, and filters list candidates
before pagination. Creation accepts only managed enrollment with immutable
employee ownership; unknown fields and arbitrary groups fail before creation.
No credential reset, direct role mapping, realm mutation, client access or user
deletion endpoint exists.

The native user-profile provider validates profiles. Native Keycloak bearer,
redirect and execute-action-token/email APIs are pinned to 26.8.1; upgrading
requires compilation and the complete native negative/positive test matrix.

## Guarded setup links

Setup mail uses the signed `cleaning-invitation` token with exact user, employee,
email, client, expiry, unpredictable ticket and security-epoch bindings. The
current ticket is server-owned `cleaning_invitation_ticket` metadata protected by
the user-profile configuration and omitted from application DTOs. The native
single-use object cache stores its bindings. A new successfully delivered ticket
supersedes older links; failed SMTP attempts preserve the previous ticket.

The custom action-token handler validates before native email auto-verification.
The password provider guards challenges and submissions, validates password policy
before atomic cache removal, and never restores a consumed ticket on storage or
transaction failure. Successful ordinary password recovery also revokes pending
invitations. Legacy execute-actions links are rejected in enabled tenant realms;
already-open unguarded forms cannot acquire a trusted submission marker. Ordinary
password/SSO recovery requires native authenticated-flow provenance. Master and
unrelated realms retain their native providers.

## Native administration boundary

Infrastructure administrators belong in the master realm, never employee accounts.
A global event listener acts only on extension-enabled target realms, including
when a master identity administers that realm and admin-event storage is disabled.
Native user mutations revoke the affected ticket. Role/group/client graph changes
rotate a realm security epoch, so restoring an earlier state cannot revive links.
Application operations have their own explicit revocation hooks; issuance audit
events do not revoke the email they just sent. SMTP-only realm changes preserve
the previous invitation when a later delivery attempt fails.

Before native grant transactions commit, a prepare guard enforces a strong edge
invariant: users and groups cannot directly map realm-management roles, and realm
roles or other clients' roles cannot contain direct composites into that client.
The management client's own builtin composite graph is allowed. This forbids
even unassigned privileged carrier definitions, closing the write-skew pattern of
concurrently assigning a benign role and making its definition privileged. It
does not claim all administrative writes are globally serialized. Bounded complete
inventories fail closed; deletion and deactivation remain available for cleanup.
The prepare guard is required because native event dispatch catches listener
exceptions and some native resources explicitly commit their transaction.

Native user-model reads, callback dispatch, prepare order, single-use removal,
provider selection, rollback and browser behavior require the pinned native
regression suite. Direct database access, removing the runtime binding, disabling
the extension or other plugins writing models without native events bypass this
operating model. Unsupported external user stores and multi-node behavior need
separate validation. SMTP remains an external side effect with no DB rollback.
This remains a development candidate until the recorded checks and rollout gates
are satisfied; building the image does not approve production deployment.
