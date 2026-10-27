# Guarded invitations and separate operator identities

Status: user approved the proposed separate-operator and guarded-invitation
approach and instructed implementation. Development first; no production changes.
The selected real operator email is kept only in private operator configuration.

## Supported administration model

Keycloak infrastructure administrators use dedicated identities in the master
realm. They are never tenant employee identities and never enter managed-users
or application-role groups. Application tenant admins/managers remain ordinary
tenant identities and may work shifts. No application operation grants Keycloak
administration roles. Do not promote an existing employee account into a Keycloak
administrator; create/use the separate operator identity instead.

Deployment preflight must report unexpected tenant-local administration grants
and require reconciliation, not silently revoke privileges. The existing server
boundary still rejects privileged targets should drift occur.

Enforce this model on covered native Admin REST writes with a globally registered,
tenant-scoped admin-event listener. After an administration-granting mutation,
enforce the structural privilege-edge invariant and enlist a failing prepare
transaction if a forbidden edge would be introduced. No user/group may directly
map a realm-management role, and no realm or other client role may directly
contain a composite into that client. Its own builtin composite graph remains
allowed. Even unassigned privileged carriers are forbidden, closing concurrent
benign-role assignment plus privileged-definition write skew. Complete bounded
inventories fail closed; deletion/deactivation remain available for cleanup. Native user updates may
explicitly commit; merely throwing from the listener or setting its transaction
manager's rollback flag is not accepted as proof of veto. Verify the persisted
role state, not just the HTTP response.

This prevents employee-to-Keycloak-operator promotion through the supported REST
workflow. It does not constrain a fully privileged operator replacing the plugin,
disabling its scope, writing directly to the database or bypassing covered model
paths. Ordinary request transactions are not described as globally serializable.

## Invitation lifecycle

Use a dedicated signed action-token type carrying the exact employee owner UUID,
user/email/client binding, an unpredictable per-invitation ticket and expiry.
Store the current ticket on the user through server-owned metadata and in the
native single-use object store. A newer successful invitation supersedes older
links. Do not expose ticket metadata through application user DTOs or allow it in
profile patches. Preserve the existing password/required-action retry behavior.
Explicit deactivation, actual application-role membership changes and logout
revoke the outstanding ticket. Reactivation must never revive that link or an
already-open form; a new invitation is required after revocation.
The global admin-event listener also revokes tickets for native user mutations.
Role/group/composite graph mutations rotate a protected realm epoch carried by
ticket bindings, so restoring old access cannot revive an older invitation.
Do not revoke the newly issued link on its own email audit event, or rotate the
epoch for SMTP-only settings changes. A revocation failure must arrange a real
prepare-stage rollback rather than relying on swallowed listener exceptions.

Before handling the link, validate the ticket and complete current user privilege,
group, enabled, ownership and email state. Guard before native execute-actions
processing, because it can mark email verified immediately. Use the native
authentication-session flow after this check, and attach trusted guard notes.

Before password storage, revalidate the same state, ticket and expiry. Atomically
claim the native single-use ticket so concurrent submissions cannot both consume
it. Run password validation before atomic consumption and credential storage.
Invalid-password validation may permit retry without consuming or reviving a
revoked/superseded ticket. Never restore a consumed ticket after native storage
or transaction failure: fail closed and require a new invitation rather than
pretending DB rollback reverses an external cache operation. The atomic removal
result, not an earlier read or queued write, determines the submission winner.

Required-action challenge and submission must both apply the guard. A form
already open when a user is disabled, ownership/email changes, an invitation is
superseded or a target becomes privileged must not save the password.

## Existing links and sessions

A new custom token type does not protect older native execute-actions tokens.
Override the native handler for extension-enabled tenant realms so an unguarded
legacy link is rejected with a safe message requesting a new invitation. Preserve
normal behavior in master and unrelated realms. Verify provider selection rather
than assuming duplicate provider IDs select the custom implementation.

Also reject already-open legacy execute-actions password sessions that have no
trusted invitation marker. Preserve ordinary forgot-password/password-change
behavior for eligible application users through complete target checks. Account
owners without employee bindings must retain their supported ordinary password
recovery path; employee invitations still require an exact ownership UUID.

Native email verification has its own action-token handler. Guard any relevant
verification completion too; overriding the VerifyEmail form alone does not
protect the actual emailVerified write.

## Operator setup

Use explicit protected operator configuration; do not hard-code the selected real
email in public code/tests. Check for an exact, unique existing master account
before creation. Do not change a matching tenant employee identity. Never rotate
an existing password or remove another administrator automatically.

First-login delivery is an external prerequisite. The current development master
realm has no SMTP and no matching operator account. Email-based setup requires the
approved email delivery, working protected mail configuration, and the pending
HTTPS environment choice. The development login currently uses public HTTP;
production account setup is not inferred from generic instructions to continue. Do not
send mail or claim an active administrator until this is resolved. Require MFA
enrollment for the new infrastructure administrator, with no password in chat,
source, workflow output or private decision notes.

## Verification

Compile against exact Keycloak 26.8.1/Java 21 APIs and confirm actual provider
selection. Native tests cover successful setup/login, replay, newer invitation,
invalid-password retry, changed email/owner, disable/reactivate, privilege drift
through direct/group/composite grants, legacy links, already-open legacy forms,
open guarded forms followed by promotion, ordinary forgot-password and master
administration remaining separate. Check both credential and verification state
after rejected requests. Keep real operator data out of synthetic fixtures.
Include two concurrent password submissions, disable/reactivate without link
revival, actual role-change revocation, and failures after ticket consumption.
For the latter, use an explicit test-only fault provider/image that fails prepare
after native password storage, never a production fault endpoint or flag. Assert
credential rollback and that the consumed link is not restored.

Cache single-use atomicity is relied on only to the extent of the native provider
contract and observed deployment; do not generalize a single-node test into a
multi-node proof. Master-operator bypass of the supported separation policy must
not be disguised as a guaranteed transactional exclusion.
