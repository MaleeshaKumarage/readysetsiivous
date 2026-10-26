# Private dev ingress

Both development Compose entry points now publish every database, Keycloak and
API port explicitly on `127.0.0.1`. Production configuration is unchanged. This
addresses the source-level unsafe default; it does not establish whether any
existing server is publicly reachable. No live ingress/firewall/TLS probe was run.

Local preview defaults are Keycloak `http://localhost:8081`, frontend
`http://localhost:3000`, and API `http://localhost:8090`. Local Compose runs the
API outside containers: bind that process to loopback too, for example through
`ASPNETCORE_URLS=http://127.0.0.1:5080`. Keep frontend auth/API build settings and
Keycloak registered redirects consistent with those exact local origins.

## Remote development

Keep the loopback bindings. Use an authenticated HTTPS tunnel/reverse proxy on
the host, or SSH local forwarding; do not publish database or authentication
ports on all interfaces. There is deliberately no nonloopback bind environment
variable. Any custom Compose networking override requires a separate review.

For browser access through a tunnel set these in the private dev environment:

```text
DEV_PUBLIC_AUTH_URL=https://auth.dev.example.invalid
DEV_FRONTEND_URL=https://dev.example.invalid
```

These are examples, not configured hosts. Keycloak24's `KC_HOSTNAME_URL` or
Keycloak26's `KC_HOSTNAME` and the preview API's public issuer use the same auth
origin. CORS and invitation redirects use the same frontend origin. Configure
the frontend's public Keycloak/API URLs and its realm/client redirect allowlist
accordingly. For the host-run API set `Auth__Keycloak__ServerUrl` to the same
auth origin and configure its frontend/CORS origin explicitly. Internal preview
`AdminServerUrl=http://keycloak:8080` remains a container-network transport URL,
not a browser address or alternate accepted public issuer.

Run the configuration preflight before starting remote dev:

```sh
docker compose -f api/deploy/docker-compose.dev.yml config --format json | python api/dev/tests/test_dev_ingress.py --check-stdin
```

Use `api/dev/docker-compose.yml` for local Compose. Enable pipeline failure
propagation in your shell and proceed only after both commands succeed. The
validator reads effective configuration from stdin without printing secrets. It
rejects remote HTTP origins, URL credentials/path/query/fragment, any published
nonloopback port, mismatched issuer or frontend/CORS origins. This is an operator
preflight, not a runtime firewall; direct Compose invocation can bypass it.

For SSH-only local browsing, forward the API/authentication ports to the same
local ports used by their public localhost origins. `localhost` in browser
configuration means the browser's computer, not the server. Do not change an
existing populated realm's issuer or callbacks without reviewing login impact.

TLS termination must preserve the public HTTPS origin. Configure Keycloak proxy
headers only after checking the actual proxy topology; do not enable unrestricted
forwarded-header trust. The API trusted-proxy configuration has its own rollout
gate. No current tunnel, proxy IP, firewall rules or installed daemon version is
inferred by this change.

## Host verification and rollout boundary

Before relying on loopback publishing, an operator must verify the actual Docker
**server** version (`docker version --format '{{.Server.Version}}'`), network
gateway mode/direct-routing settings, host/cloud firewalls and tunnel access
controls. Docker documents that versions **older than 28.0.0** can expose
localhost-published ports to peers on the same L2 network. Upgrade or apply
reviewed network controls before considering this gate satisfied; also verify
IPv6 and direct routing. See [Docker port-publishing documentation](https://docs.docker.com/engine/network/port-publishing/).

Retain the existing Compose project name and complete the
[dev state migration](dev-state-persistence.md) before recreating populated
services. Confirm intended login works through the private ingress, raw ports
cannot be reached from an authorized external test location, and TLS/callback
URLs are correct. Record sanitized evidence. These operational checks remain
open until observed; no production change is included.

Hostname option references: [Keycloak hostname v2](https://www.keycloak.org/server/hostname)
and [Keycloak24 hostname guide](https://github.com/keycloak/keycloak/blob/24.0.5/docs/guides/server/hostname.adoc).
