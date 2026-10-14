# Cleaning Suite API

Multi-tenant SaaS backend for cleaning companies. First tenant: ReadySetSiivous (readysetsiivous.fi).

## Stack
- ASP.NET Core 10.0 Web API, Clean Architecture (Api / Application / Domain / Infrastructure / Tests)
- Marten document store on PostgreSQL (conjoined multi-tenancy, optimistic concurrency)
- Keycloak for auth (realm per tenant, provisioned via Admin REST)
- QuestPDF for invoice PDFs
- Production runs on Oracle (130.61.140.7), compose at `/home/ubuntu/cleaning-suite`; shared dev runs on the agent VM (130.61.208.211), compose at `/home/ubuntu/cleaning-dev`.

## Conventions
- Tenant id == Keycloak realm name == slug. Registry partition `registry` holds TenantRegistration docs.
- All documents inherit `BaseDocument` (Id, CreatedUtc, UpdatedUtc, Version).
- Money is decimal; UTC in storage, Europe/Helsinki at the edges.
- Booking/Invoice snapshot Service + Issuer at creation; history never rewritten.
- Error contract: RFC 7807 problem details; 409 for conflicts.
- Mirrors CargoHub conventions (MediatR, FluentValidation, xUnit + Moq/NSubstitute).

## Local dev
- See [environment and testing instructions](../docs/environments.md). Use shared dev for live integration tests and test data.
- `docker compose --env-file dev/.env -f dev/docker-compose.yml up -d` brings local postgres (5434) + keycloak (8081); copy `dev/.env.example` first.
- Copy `.env.example` to `.env`, run `dotnet run --project CleaningSuite.Api`.
- Swagger at `/swagger` in Development.

## Deploy
- Push to main: `docker-build-push` requires reusable `ci-build-test` checks, then successful image publishing triggers `docker-deploy-oracle`.
- SSH secret: `ORACLE_SSH_KEY` in Actions. Image publishing uses `GITHUB_TOKEN`; production runtime secrets are in `/home/ubuntu/cleaning-suite/.env`.
- Production public domains: api.readysetsiivous.fi (host loopback :8090), auth.readysetsiivous.fi (host loopback :8082).
