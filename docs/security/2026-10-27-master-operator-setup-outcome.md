# Production master-operator account setup — scoped outcome

Date: 2026-10-27. Scope: separately authorized production master-realm account
and SMTP setup only. Production application/provider deployment remains held.

## Authorization and completed setup

The user explicitly chose existing production HTTPS after being offered the
environment choice, and authorized sending the setup invitation through the
existing email provider. The reviewed operator helper verified the expected public HTTPS master issuer
for discovery and the configured administration transport. This run used the
same public HTTPS base for both; it did not rely on a separate private ingress.

The helper configured master SMTP using the existing Resend credential retained
remotely and created a separate master-realm operator. The native setup email
required email verification, password setup and TOTP enrollment; SMTP accepted
delivery. No password, action-token URL, provider credential, real operator email
or account identifier is included in this document or the public source.

After the user confirmed completion, read-only verification established:

- The dedicated master account was enabled and its email was verified.
- Password and OTP credentials were configured.
- Master administration access was present.
- No required actions remained pending.

SMTP acceptance alone is not proof of inbox delivery. The subsequent user
confirmation and account-state checks establish the recorded setup completion;
this document does not claim an independent mailbox-delivery audit.

## Scope preserved

The change was limited to the dedicated master operator and master SMTP. Tenant
employee identities were not repurposed. No application or custom provider was
deployed, and production images remained unchanged: the API retained its recorded
`74559` revision prefix, Keycloak remained on 26.1 and PostgreSQL remained on 16.
The application/provider production adoption and release hold persist.

## Relationship to earlier evidence

The [guarded-invitation verification JSON](2026-10-27-guarded-invitations-verification.json)
is the earlier isolated-development checkpoint. Its statements that production
was unchanged and real operator setup was pending were true when recorded.
That historical artifact is deliberately not rewritten to imply that its test
run included this later production account setup.

This supplement resolves the later HTTPS-target choice and operator-delivery
prerequisite only. It does not extend single-node development cache evidence to
multiple nodes, prove external user-storage behavior, establish PostgreSQL
Keycloak rollback equivalence, or approve a production application/provider
rollout. The [current rollout document](../security-keycloak-runtime-rollout.md)
and [guarded design](../superpowers/specs/2026-10-26-guarded-invitations-design.md)
retain those boundaries.
