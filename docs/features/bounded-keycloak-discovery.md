# Bounded asynchronous Keycloak signing-key discovery

Bearer validation uses asynchronous discovery and clones validation parameters per request. The external `Auth:Keycloak:ServerUrl` issuer must match exactly, including its HTTP/HTTPS scheme. Deployments that previously relied on accepting an HTTP token issuer while configuring HTTPS must correct Keycloak's public issuer/proxy settings before rollout. `AdminServerUrl` remains the explicit internal HTTP transport override; it does not change the trusted external issuer.

Discovery only accepts canonical lowercase realm names under that issuer. Tenant realms must exist under the identical registry slug; a nonempty KeycloakRealm must match too. Legacy blank KeycloakRealm registrations remain supported. The explicitly configured provisioner realm does not require a tenant registration. Existing tenant/platform audience, signature and lifetime checks still apply. Tenant suspension remains enforced by tenant middleware rather than cached signing-key availability.

Limits per API process:

- Bearer tokens: 16,384 characters; key identifiers: 256 characters.
- Discovery state: 256 realms, including failed lookups and in-flight work. Expired idle entries are removed on admission; full capacity fails closed.
- Registry and JWKS discovery combined: 8 concurrent operations, with immediate refusal at capacity rather than an unbounded wait queue.
- Shared discovery timeout: 5 seconds. An individual request cancellation stops waiting without cancelling another request's shared fetch.
- JWKS: 64 KiB response body, at most 32 signing keys. Redirects are disabled.
- Positive entries: 15 minutes; negative entries: 5 seconds. An unknown key identifier may refresh once after a 30-second cooldown, with concurrent requests sharing one refresh.

Temporary failures deny authentication; provider bodies and bearer values are not added to authentication errors. A newly rotated key may require retry after the cooldown. Capacity and discovery limits intentionally fail closed. This cache does not replace upstream request limiting.
