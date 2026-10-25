# Bounded asynchronous Keycloak signing-key discovery

Bearer validation uses asynchronous discovery and clones validation parameters per request. The external `Auth:Keycloak:ServerUrl` issuer must match exactly, including its HTTP/HTTPS scheme. Deployments that previously relied on accepting an HTTP token issuer while configuring HTTPS must correct Keycloak's public issuer/proxy settings before rollout. `AdminServerUrl` remains the explicit internal HTTP transport override; it does not change the trusted external issuer.

Discovery only accepts canonical lowercase realm names under that issuer. Tenant realms must exist under the identical registry slug; a nonempty KeycloakRealm must match too. Legacy blank KeycloakRealm registrations remain supported. The explicitly configured provisioner realm does not require a tenant registration. Existing tenant/platform audience, signature and lifetime checks still apply. Tenant suspension remains enforced by tenant middleware rather than cached signing-key availability.

Limits per API process:

- Bearer tokens: 16,384 characters; key identifiers: 256 characters.
- Discovery state: separate bounded caches for verified realms (256 by default) and unverified failures (256). Only a successful registry check or the explicit platform realm can allocate a verified slot. Least-recently-used idle entries are evicted; negative floods cannot consume verified slots, and cache capacity never rejects a successfully verified fetch. Configure `Auth:Keycloak:SigningKeys:MaximumKnownRealms` between 1 and 4096 when scaling. Shared in-flight fetches have their own global bound of 8 and are protected from cache eviction.
- Registry and JWKS discovery combined: 8 concurrent operations, with immediate refusal at capacity rather than an unbounded wait queue.
- Shared discovery timeout: 5 seconds. An individual request cancellation stops waiting without cancelling another request's shared fetch.
- JWKS: 64 KiB response body, at most 32 signing keys. Redirects are disabled.
- Positive entries: 15 minutes; negative entries: 5 seconds. An unknown key identifier may refresh once after a 30-second cooldown, with concurrent requests sharing one refresh.

Temporary failures deny authentication; provider bodies and bearer values are not added to authentication errors. A newly rotated key may require retry after the cooldown. Capacity and discovery limits intentionally fail closed. This cache does not replace upstream request limiting.
