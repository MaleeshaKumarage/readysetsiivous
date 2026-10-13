# ReadySetSiivous monorepo

Monorepo for readysetsiivous.fi.

| Directory | What | Deploy |
|---|---|---|
| `site/` | Next.js 14 static site (fi/en/sv), booking flow + admin UI | GitHub Pages via `.github/workflows/deploy.yml` |
| `api/` | Cleaning Suite backend: .NET 8 + Marten + Keycloak, multi-tenant SaaS | GHCR image via `docker-build-push.yml`, deployed to Mac mini by `docker-deploy-mac.yml` |

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
- `ci-build-test.yml` — API build+test, runs on `api/**`
- `docker-build-push.yml` — runs the reusable backend and frontend CI checks for the same commit before publishing the API image to GHCR (`readysetsiivous-api`)
- `docker-deploy-oracle.yml` — deploys the tested image to Oracle after successful image build

Agreement PDFs and signatures remain on the persistent uploads volume, but `/uploads` serves only the generated service card images at the volume root. Agreement files are available through the agreement controllers, which enforce admin authorization or signer-token checks. Existing stored file paths require no migration.
