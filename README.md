# ReadySetSiivous monorepo

Monorepo for readysetsiivous.fi.

| Directory | What | Deploy |
|---|---|---|
| `site/` | Next.js 14 static site (fi/en/sv), booking flow + admin UI | GitHub Pages via `.github/workflows/deploy.yml` |
| `api/` | Cleaning Suite backend: .NET 8 + Marten + Keycloak, multi-tenant SaaS | GHCR image via `docker-build-push.yml`, deployed to Oracle by `docker-deploy-oracle.yml` |

## Environments and testing

Use the shared dev environment at http://130.61.208.211/ for live integration
testing. Production is https://readysetsiivous.fi/. See [the environment guide](docs/environments.md)
for API/auth URLs, local setup, dev preview deployment, test commands, and the
production release sequence. Frontend API/auth defaults point to production;
set dev variables explicitly before testing against a live backend.

## Feature inventory and test planning

See [all features and FE/BE gaps](docs/features/feature-inventory.md),
[operation-by-operation FE/BE coverage](docs/features/feature-capability-matrix.md),
[auth, roles and workspace recommendations](docs/features/technical-lead-review.md),
[HTTP endpoints](docs/features/api-endpoints.md),
[edge-case catalogue](docs/testing/edge-case-catalog.md), and
[the Playwright improvement plan](docs/testing/playwright-test-plan.md).
The catalogue separates implemented contracts, known-gap regressions, missing UI,
and business-policy decisions so test improvements do not hide incomplete behavior.

## Site

```bash
cd site
npm ci
npm run build      # static export to site/out
```

Config: `NEXT_PUBLIC_API_URL`, `NEXT_PUBLIC_TENANT_SLUG`, `NEXT_PUBLIC_KEYCLOAK_*` env vars (defaults point at production).

## API

```bash
cd api
dotnet test CleaningSuite.Backend.sln
```

See `api/CLAUDE.md` for architecture, dev env and deploy details.

## CI

- `deploy.yml` — Pages deploy, runs on `site/**` changes only
- `ci-build-test.yml` — backend build/tests and frontend build/browser tests on `api/**` or `site/**`; also reusable by the image-publishing workflow
- `docker-build-push.yml` — runs the reusable backend and frontend CI checks for the same commit before publishing the API image to GHCR (`readysetsiivous-api`)
- `docker-deploy-oracle.yml` — deploys the tested image to Oracle after successful image build

Agreement PDFs and signatures remain on the persistent uploads volume, but `/uploads` serves only the generated service card images at the volume root. Agreement files are available through the agreement controllers, which enforce admin authorization or signer-token checks. Existing stored file paths require no migration.
