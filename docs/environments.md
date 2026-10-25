# Development and production environments

Use the shared development environment for integration testing, test bookings,
agreement signing, and other operations that create or change data. Production
serves real customers. Automated local tests normally mock API requests.

The API now requires distinct tenant and platform resource audiences. Existing
Keycloak clients must be migrated before deploying that change; import JSON alone
does not update them. See [API authentication and platform rollout](features/api-authentication.md)
for configuration, scoped check/apply migration, permission negatives and isolated
real Keycloak/Playwright verification. Production and shared dev clients have not
been migrated automatically by these source changes.

## Environment map

| Component | Shared development | Production |
| --- | --- | --- |
| Website | http://130.61.208.211/ | https://readysetsiivous.fi/ |
| API | http://130.61.208.211:8090 | https://api.readysetsiivous.fi |
| Keycloak | http://130.61.208.211:8081 | https://auth.readysetsiivous.fi |
| Tenant / Keycloak realm | `readysetsiivous` | `readysetsiivous` |
| Frontend OAuth client | `cleaning-suite-web` | `cleaning-suite-web` |
| Backend host | Agent VM, `130.61.208.211` | Oracle VM, `130.61.140.7` |
| Host deployment directory | `/home/ubuntu/cleaning-dev` | `/home/ubuntu/cleaning-suite` |
| Compose source | `api/deploy/docker-compose.dev.yml` | `api/deploy/docker-compose.yml` |
| API image | `cleaning-dev-api:dev`, built on the agent VM | `ghcr.io/maleeshakumarage/readysetsiivous-api:sha-<commit>` |

The dev database uses its own `dev_pgdata` volume and host port `5436`. Production
uses `cleaning_pgdata` and loopback port `5435`. The identical tenant name does
not imply shared data: these stacks run on separate hosts and databases.

The environment map comes from the former preview configuration,
`.github/workflows/docker-deploy-oracle.yml`, and the two deployment Compose files.
The former Mac mini deployment is not the current workflow target.

## Testing a change

1. Run backend unit and HTTP regression tests from `api/`:

   ```bash
   dotnet restore CleaningSuite.Backend.sln
   dotnet test CleaningSuite.Backend.sln
   ```

2. Run frontend checks from `site/`:

   ```bash
   npm ci
   npx playwright install chromium
   npx tsc --noEmit
   npm run build
   npm run test:e2e
   ```

   Playwright starts a local Next.js development server on port `3000` and tests
   desktop and mobile Chromium. Its configured suites mock their API calls;
   they do not deploy or test the running shared dev backend. The separate
   `e2e/api-booking.spec.ts` live-API smoke test is excluded by the default
   `testMatch` configuration.

3. For live integration testing, deploy the intended commit to shared dev using
   the preview process below. A Git push to a feature branch does not update
   the running preview by itself.

4. Check the dev stack, then test the booking/admin/signing flows using test data:

   ```bash
   curl --fail http://130.61.208.211:8090/healthz
   curl --fail 'http://130.61.208.211:8090/api/v1/public/readysetsiivous/services?lang=en'
   ```

   API health is a liveness check, not proof that database access or sign-in works.
   Verify the specific flow under test. Use an existing dev account with the
   required roles; credentials are not published in this guide.

## Frontend environment selection

`NEXT_PUBLIC_*` values are embedded at build time. Configure them before running
`npm run dev` or `npm run build`, and rebuild when changing environments. Omitted
API and auth values default to production, even when the UI runs locally.
Use base URLs without a trailing slash.

For a local UI connected to shared dev, create the ignored `site/.env.local`:

```dotenv
NEXT_PUBLIC_API_URL=http://130.61.208.211:8090
NEXT_PUBLIC_TENANT_SLUG=readysetsiivous
NEXT_PUBLIC_KEYCLOAK_URL=http://130.61.208.211:8081
NEXT_PUBLIC_KEYCLOAK_REALM=readysetsiivous
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=cleaning-suite-web
```

The imported dev Keycloak client currently allows the shared dev website origin
(`http://130.61.208.211`). To sign in from a local UI, add its exact origin and
redirect URI to the dev client. Check the API's CORS allowed origins and JWT
issuer configuration as well; the preview Compose file currently uses the
internal Keycloak URL `http://keycloak:8080`. Alternatively, test authentication
directly through the shared dev website. Local frontend mocks do not verify
these settings.

## Updating the shared dev preview

The coding-agent workflow no longer deploys or seeds shared dev. Deployment is a
separate manual operator operation using an explicitly reviewed commit. A feature
branch push or draft agent PR does not update this environment. Coordinate with
other testers before replacing the shared stack; review the chosen commit and
its deployment files before running them on the host. A future automated dev
deployment requires a new separately reviewed manual workflow and protected
environment approval; it is not supplied by the agent pipeline.

To reproduce just the API preview deployment, run on the agent VM from a checkout
of the intended commit:

```bash
repo="$PWD"
docker build -f api/docker/Dockerfile -t cleaning-dev-api:dev api
cp api/deploy/docker-compose.dev.yml /home/ubuntu/cleaning-dev/docker-compose.yml
cp api/deploy/keycloak-realm.json /home/ubuntu/cleaning-dev/keycloak-realm.json
cd /home/ubuntu/cleaning-dev
docker compose up -d --force-recreate api
curl --fail http://localhost:8090/healthz
```

The host needs its existing `.env` containing dev Keycloak configuration. For a
fresh dev database, wait for API schema initialization before applying
`api/deploy/seed-dev.sql` from the checkout. The seed activates the dev tenant
and inserts/updates the sample services; use it only in dev. The current preview
Compose file does not persist the API uploads directory, so recreating the API
can remove dev agreement files and images.

To update the website, build `site/` with the dev variables listed above and
publish `site/out/` to `/home/ubuntu/cleaning-dev/site/` using the existing preview
server. Updating the API image alone does not update the website.

## Local backend development

`api/dev/docker-compose.yml` is a third, local-only setup, separate from the
shared preview. It starts PostgreSQL on `5434` and Keycloak on `8081`; run the API
directly with .NET:

```bash
cd api
cp dev/.env.example dev/.env
cp .env.example .env
# Set matching local Keycloak credentials in both files.
docker compose --env-file dev/.env -f dev/docker-compose.yml up -d
dotnet run --project CleaningSuite.Api --launch-profile http
```

The API listens at `http://localhost:5259`, with Swagger at `/swagger` in
Development. This local stack does not import the shared dev realm or seed a
tenant automatically. Provision a local tenant before testing tenant endpoints.
Set the local frontend API URL to `http://localhost:5259` and configure the local
Keycloak client for its frontend origin. Keep real credentials in ignored env
files or the deployment host's env file.

## Production release

The API release sequence is:

1. Merge the reviewed change into `main`.
2. `docker-build-push.yml` invokes the reusable backend/frontend checks in
   `ci-build-test.yml` for that same commit. Image publishing requires success.
3. The image is published to GHCR with a `sha-<commit>` label; deployment identity must use its immutable digest.
4. Successful completion triggers `docker-deploy-oracle.yml`, which verifies the exact successful build's release manifest, checks out and hashes its assets, stages them, and deploys its immutable image only after the reviewed compatibility gate permits replacement. See [release integrity and activation requirements](features/release-integrity.md). The default unapproved policy blocks activation safely; production must not be switched without the documented rollout review.

The production stack's secrets live in `/home/ubuntu/cleaning-suite/.env`.
Its API and Keycloak are bound to host loopback ports `8090` and `8082` and
accessed publicly through their HTTPS domains. Production agreement uploads
persist in the `cleaning_uploads` volume.

The website deploys separately through `deploy.yml` to GitHub Pages on changes
under `site/**` on `main` or `master`, or manual dispatch. That Pages workflow
builds the static site and waits for the matching API release when API-related files changed.
Feature-branch pushes alone do not deploy production.

## Verification record

On 2026-10-13, read-only requests returned HTTP 200 for the dev website, dev API
health endpoint, dev service catalog, dev Keycloak discovery endpoint, and
production API health endpoint. This confirms reachability at that time, not
that a particular feature commit has been deployed or that every user flow passes.

## Employee invitations and public auth URLs

Issue49's native setup emails require explicit public frontend/auth URLs and
Keycloak realm SMTP. Configure `App:FrontendBaseUrl` with the site origin and
`Auth:Keycloak:ServerUrl` with the public auth host. Dev compose now separates
`AdminServerUrl=http://keycloak:8080` as internal REST/JWKS transport from public
`ServerUrl=http://130.61.208.211:8081`. A private container hostname must never
appear in browser/email URLs or be used as the expected public token issuer.

SMTP may use the current Resend provider through the deployment INVITE_SMTP_*
settings; setting Resend API delivery alone is insufficient. Existing configured
SMTP is preserved. Defaults do not deliver real email without configured sender
credentials. See [employee invitations](features/employee-invitations.md) for
24-hour token policy, redirect/role/user-profile setup and legacy migration.

The reviewed realm import and new-tenant provisioner include `admin`, `manager`
and `employee`. Manager is tenant-wide and has dedicated operational APIs; it has
no platform, payroll, billing, template or report privileges. Existing realms gain
the empty manager role idempotently when an admin selects/invites that role.
No existing identity automatically gains manager access. Native account setup
emails continue; QC activity forms are filled through authenticated My shifts and
do not send emails. See [tenant manager access](features/tenant-manager-access.md).
## Language-game AI

Game generation and answer evaluation use a shared FIFO with one worker. Every
Gemini is the only game provider; OpenAI keys and models are not configured or
used by the game. Production GitHub Actions supports `GEMINI_KEY_1` through
`GEMINI_KEY_10` and legacy Gemini secrets. Empty numbered slots are ignored.
The deployment script writes keys to the API host's private `.env`, validates
that at least one key resolves without printing values, and restores the
previous environment file if deployment fails. Local dev can set the same
numbered variables in its untracked `.env`; no key belongs in source control or
browser settings.

The coordinator limits Gemini to the configured RPM (default 3) and rolling
24-hour request count (default 15), reserving usage in the database. All keys
from one Google project share quota; key rotation only handles explicit
key-specific authentication failures. Retryable 429/5xx errors receive at most
one bounded retry with jitter and `Retry-After` support. Daily quota resets are
reported only when Gemini identifies a daily limit; generic quota errors keep
an internal cooldown and show that the reset time is unknown. The database
reservation is safe for the current single API instance and must become atomic
before horizontal scaling.

The Gemini-only `game-ai-live-smoke` workflow is manual because it consumes provider quota.
Ordinary CI uses provider fakes and never needs a provider secret. There is no
sample-answer or local-grading fallback: a failed request leaves the current
question and typed answer available for retry.
See [language game](features/language-game.md) for scoring, hint visibility and
game-flow behavior.

## Backend runtime and migration rehearsals

Backend projects use .NET10 LTS, SDK10.0.402 (`global.json`), runtime10.0.13,
and Marten/Marten.Newtonsoft9.50.1. Docker and GitHub CI use the same pinned
versions. Newtonsoft JSON, GUID optimistic-concurrency versions and tenant sessions
retain their existing behavior. Startup builds all mappings, applies only the
configured AutoCreate policy, and verifies the schema; initialization failure
prevents the API from reporting healthy.

CI creates disposable PostgreSQL16 data for tenant-isolation, dictionary-input,
serialization, concurrent task creation and stale-write regressions. Local tests
use `QC_TEST_CONNECTION_STRING` and `INVITE_TEST_DATABASE` only with an isolated
synthetic database. Two live Keycloak fixtures still require separate credentials.

`BackgroundWorkers__Enabled=false` disables QC generation and entertainment
cleanup in new-image private rehearsals; normal environments default to enabled.
Previous images do not understand this flag. Restrict older-image restore checks
to an internal network and a read-only database connection, stop after startup
health before the 15-second QC delay, and avoid the UTC midnight cleanup boundary.
Never publish restored customer data or credentials. Backups must be restored and
verified before upgrading production, and the previous image must be retained.
## Dev identity and file persistence

Before applying the persistent dev database/upload/key configuration to an existing
stack, follow [the backup, migration and rollback procedure](environments/dev-state-persistence.md).
Keep the existing Compose project name and preserve the current embedded Keycloak
data, uploads and encryption key before recreating containers.
