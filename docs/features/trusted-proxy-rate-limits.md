# Trusted proxy identity and public request quotas

The API applies a single-instance, per-client sliding quota of 30 requests in 60 seconds to the existing public and entertainment group routes. State is limited to `PublicRateLimit:Capacity` identities (default 10,000, allowed 1–100,000). Admission and quota updates are atomic. Only identities whose last admitted request is at least 60 seconds old can be reclaimed; capacity exhaustion rejects new identities with HTTP 429 and `Retry-After` rather than forgetting an active quota. Rate-limit time uses the monotonic system clock. IPv4-mapped IPv6 addresses share the IPv4 quota. This is an abuse guard, not a distributed or authenticated-user quota.

Forwarded headers run before auditing, rate limiting, and authentication. By default no proxy is trusted and forwarded processing is disabled. Configure the verified immediate upstream peer/topology before enabling it:

```json
{
  "ReverseProxy": {
    "KnownProxies": ["192.0.2.10"],
    "KnownNetworks": [],
    "ForwardLimit": 1
  },
  "PublicRateLimit": { "Capacity": 10000 }
}
```

The address above is documentation-only; do not deploy it. Array configuration can also be supplied as `ReverseProxy__KnownProxies__0`. Networks must be explicit CIDRs and `ForwardLimit` must be 1–10. Invalid configuration fails startup. A direct untrusted peer cannot select its quota identity using `X-Forwarded-For` or `CF-Connecting-IP`. The latter header is never specially trusted.

Production uses a tunnel, but the origin's actual socket peer and chain are not yet verified. Do not assume all private ranges, loopback peers, or Cloudflare public ranges describe that peer. Inspect the deployment's origin network, configure the narrow stable peer/network and expected hop count, then verify two synthetic clients receive distinct identities and direct spoofing is ignored. Until that rollout is reviewed, the safe default can group clients behind an unconfigured proxy into one quota. Reassess distributed storage before scaling API replicas.
