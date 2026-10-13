# Development and production environments

Use the shared development environment for integration testing, test bookings,
agreement signing, and other operations that create or change data. Production
serves real customers. Automated local tests normally mock API requests.

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

The environment map comes from `.github/workflows/agent-pipeline.yml`,
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

The `agent-pipeline.yml` preview job runs on the self-hosted runner labelled
`agent`. It builds the API locally, starts the dev Compose stack, seeds the dev
catalog, builds the frontend with dev API/auth URLs, and serves the static output
on port `80`. That workflow also performs issue/PR work; do not trigger the whole
pipeline merely to run a smoke test.

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
3. The image is published to GHCR with an immutable `sha-<commit>` tag.
4. Successful completion triggers `docker-deploy-oracle.yml`, which connects
   with the `ORACLE_SSH_KEY` Actions secret, updates the host's `IMAGE_TAG`, pulls
   the image, recreates the production stack, and checks `/healthz`.

The production stack's secrets live in `/home/ubuntu/cleaning-suite/.env`.
Its API and Keycloak are bound to host loopback ports `8090` and `8082` and
accessed publicly through their HTTPS domains. Production agreement uploads
persist in the `cleaning_uploads` volume.

The website deploys separately through `deploy.yml` to GitHub Pages on changes
under `site/**` on `main` or `master`, or manual dispatch. That Pages workflow
builds the static site but currently does not depend on the API image test gate.
Feature-branch pushes alone do not deploy production.

## Verification record

On 2026-10-13, read-only requests returned HTTP 200 for the dev website, dev API
health endpoint, dev service catalog, dev Keycloak discovery endpoint, and
production API health endpoint. This confirms reachability at that time, not
that a particular feature commit has been deployed or that every user flow passes.
