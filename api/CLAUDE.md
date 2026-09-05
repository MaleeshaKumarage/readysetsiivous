# Cleaning Suite API

Multi-tenant SaaS backend for cleaning companies. First tenant: ReadySetSiivous (readysetsiivous.fi).

## Stack
- ASP.NET Core 8.0 Web API, Clean Architecture (Api / Application / Domain / Infrastructure / Tests)
- Marten document store on PostgreSQL (conjoined multi-tenancy, optimistic concurrency)
- Keycloak for auth (realm per tenant, provisioned via Admin REST)
- QuestPDF for invoice PDFs
- Deployed to Mac mini (192.168.1.120) via GitHub Actions self-hosted runner, label `macmini`, compose at `/opt/cleaning-suite`

## Conventions
- Tenant id == Keycloak realm name == slug. Registry partition `registry` holds TenantRegistration docs.
- All documents inherit `BaseDocument` (Id, CreatedUtc, UpdatedUtc, Version).
- Money is decimal; UTC in storage, Europe/Helsinki at the edges.
- Booking/Invoice snapshot Service + Issuer at creation; history never rewritten.
- Error contract: RFC 7807 problem details; 409 for conflicts.
- Mirrors CargoHub conventions (MediatR, FluentValidation, xUnit + Moq/NSubstitute).

## Local dev
- `docker compose -f deploy/docker-compose.yml up -d` brings postgres (5434) + keycloak (8081).
- Copy `.env.example` to `.env`, run `dotnet run --project CleaningSuite.Api`.
- Swagger at `/swagger` in Development.

## Deploy
- Push to main: ci-build-test → docker-build-push → docker-deploy-mac (workflow_run chain).
- Secrets: `GHCR_PAT` in repo Actions; `/opt/cleaning-suite/.env` on host.
- Cloudflare Tunnel `cleaning-suite`: api.readysetsiivous.fi → :8090, auth.readysetsiivous.fi → :8081.
