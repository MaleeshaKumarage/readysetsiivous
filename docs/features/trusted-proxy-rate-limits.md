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

The address above is documentation-only; do not deploy it. Arrays remain supported in application configuration, including direct-process environment keys such as `ReverseProxy__KnownProxies__0`. Host env-file entries are not automatically forwarded into a container: the tracked production and shared-dev Compose files explicitly map these deployment settings to the API only:

| Host `.env` setting | API configuration | Default |
| --- | --- | --- |
| `API_TRUSTED_PROXY_IPS` | `ReverseProxy__KnownProxiesCsv` | empty; no proxies trusted |
| `API_TRUSTED_PROXY_NETWORKS` | `ReverseProxy__KnownNetworksCsv` | empty; no networks trusted |
| `API_PROXY_FORWARD_LIMIT` | `ReverseProxy__ForwardLimit` | `1` |
| `API_PUBLIC_RATE_LIMIT_CAPACITY` | `PublicRateLimit__Capacity` | `10000` |

Set those names in the reviewed deployment host `.env`, then recreate the API through the release process. No actual peer/network values are committed. Leave both trust values empty until the origin topology is verified. CSV lists accept comma-separated IPs or CIDRs and trim surrounding segment whitespace. Empty CSV strings are safe defaults; whitespace-only strings, empty segments, malformed values and trust-all `/0` networks fail startup. A nonempty CSV and nonempty array for the same address family are rejected as ambiguous; an empty CSV does not override an explicit array. Networks must be explicit CIDRs and `ForwardLimit` must be 1–10. Invalid configuration fails startup. A direct untrusted peer cannot select its quota identity using `X-Forwarded-For` or `CF-Connecting-IP`. The latter header is never specially trusted.

Production uses a tunnel, but the origin's actual socket peer and chain are not yet verified. Do not assume all private ranges, loopback peers, or Cloudflare public ranges describe that peer. Inspect the deployment's origin network, configure the narrow stable peer/network and expected hop count, then verify two synthetic clients receive distinct identities and direct spoofing is ignored. Until that rollout is reviewed, the safe default can group clients behind an unconfigured proxy into one quota. Reassess distributed storage before scaling API replicas.


Verification: the .NET proxy/limiter suite covers environment-variable CSV binding, preserved array configuration, empty defaults and invalid/ambiguous lists. `python3 scripts/tests/test_proxy_compose.py` normalizes both tracked Compose files with synthetic env files and verifies the actual API environment receives the configured values and safe empty defaults. It requires the Docker Compose CLI but never starts containers or contacts a daemon; `proxy-config-tests.yml` runs it on Linux CI.
