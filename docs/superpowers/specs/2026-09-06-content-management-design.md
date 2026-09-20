# Content Management via Admin Panel — Design

Date: 2026-09-06
Status: Approved for implementation planning

## Goal

Let a non-technical user edit every piece of website text (fi / en / sv) from the
admin panel, then publish it live with one button. The static site's source code is
never touched — content flows only through the `src/i18n/*.json` data files.

## Current State

- Site = Next.js 14 static export, deployed to GitHub Pages by
  `.github/workflows/deploy.yml` (trigger: push to `main`/`master` on path `site/**`).
- All site text lives in `site/src/i18n/{fi,en,sv}.json`, bundled at build time.
- Services are already admin-controlled via the API (runtime fetch). This is the only
  content not in i18n JSON.
- Backend = .NET 8 + Marten + Keycloak, clean architecture. Has admin endpoints for
  services/bookings/invoices/employees.
- Backend already has a coarse content feature, unused by the site: `TenantProfile.Pages`
  (`Dictionary<string, LocalizedText>`, flat string values, keys home/services/about/faq/
  footer), `UpdateTenantPageCommand` + `PUT /api/v1/admin/tenant/page`, and
  `GetPublicContentQuery` (resolves Pages to a flat per-language `Dictionary<string,
  string>`). This flat model cannot hold nested arrays (FAQ, testimonials, checklist) and
  is superseded for site content by the richer `SiteContent` doc below.
- Repo = `MaleeshaKumarage/readysetsiivous`, default branch `main`, working branch
  `monorepo`. Backend and site share this one repo.

## Decisions

1. **Content delivery = git-commit JSON.** Admin edits are saved to Marten (draft), then
   a Publish action commits the three JSON files to `main`, which triggers the existing
   Pages deploy. No runtime fetch. No new site code.
2. **Publish target branch = `main`** (deploy.yml trigger).
3. **Edit UX = structured forms + JSON fallback.** 20 sections get friendly shadcn forms
   with repeatable lists; 4 long/legal/structural sections (privacy, terms, schema,
   varaus) get a raw JSON textarea.
4. **Scope = content editing only.** Bookings / Schedule / Invoices / Employees admin
   pages are out of scope (deferred).

## Architecture

### A. Storage (backend)

- New Marten aggregate `SiteContent`, inheriting `BaseDocument`, stored in the tenant's
  own partition with deterministic id via `TenantDocumentIds.SiteContent(tenantId)` (add
  the helper alongside `TenantProfile`). Multi-tenancy is inherited from the existing
  conjoined Marten tenancy — no new tenancy plumbing.
- Shape mirrors the three i18n JSON files exactly: three locale trees (fi / en / sv),
  each a `JsonElement`/`JsonDocument` blob, so round-trip is byte-faithful and the
  backend needs no per-section C# classes. Structured form editing happens client-side
  against a section slice of the blob; the backend stores and validates whole trees only.
- The publish target (repo / branch / path) is per-tenant, stored on `TenantRegistration`
  as `SiteRepo` / `SiteBranch` / `SiteContentPath` (default: `MaleeshaKumarage/
  readysetsiivous`, `main`, `site/src/i18n`). Only one tenant (readysetsiivous) has a
  site today, but the mapping is explicit rather than hardcoded.
- Seeded once: an import step reads the current `site/src/i18n/*.json` and writes the
  initial `SiteContent` doc. Runs as a one-off script or startup migration.
- **Seed content = current live JSON.** The import marks the doc as both draft AND
  published (hash + `lastPublishedUtc` set at import), so the admin panel opens
  pre-populated with the existing site text — no empty forms, and the first Publish is a
  no-op (content already matches what is live). This seed is the "dummy data" that ships
  with the feature.
- Two states: **draft** (current Marten content) and **published** (what was last
  committed). The doc tracks `lastPublishedUtc` and a content hash so the UI can show an
  "unpublished changes" badge.
- History comes from git (the publish commits), not from Marten — no version table.

### B. Admin UI (shadcn, usability-first)

New page under the existing admin area: `src/app/[lang]/admin/content/`.

Layout:
- **Section list** (left sidebar) grouped by tier: Marketing (structured) vs
  Legal/Structural (JSON). Click selects a section.
- **Language tabs** fi / en / sv at top of the edit pane, with a "Fill from fi" button
  that copies fi values into the active language. It only fills empty fields — existing
  en/sv translations are never overwritten.
- **Edit pane** (right): the structured form or JSON textarea for the selected section.
- **Preview pane**: renders the section as the site would, live as the user types. Uses a
  lightweight read-only mirror, NOT the real site components (those pull `useLanguage`/
  `ThemeContext` and animation and are not safe to reuse in the admin area).
- **Publish bar** (top or bottom, persistent): "unpublished changes" badge, last
  published timestamp, and a **Publish** button.

Structured sections (shadcn forms): Site, Nav, Hero, TrustBadges, QuoteForm, WhatsApp,
Services, Kotitalousvahennys, Checklist, FAQ, PaymentBanner, About, PricingTransparency,
KeySecurity, EcoPetBadges, EmergencyCTA, ResponsibleEmployer, Footer, FloatingWhatsApp,
Testimonials.

Repeatable list editors (Dialog + Table) for: FAQ items, testimonials, checklist
items, nav links. Each supports add / remove / reorder.

JSON fallback sections (Textarea with validation): privacy, terms, schema, varaus.

`services` key: the i18n `services` section holds UI labels/heading for the services
block, NOT the service entities (those are API-driven via the Services admin page). It
is editable here as a structured section; there is no overlap with Service entities.

New shadcn components to add via CLI: `textarea`, `tabs`, `form`, `accordion`,
`alert-dialog`, `sonner` (toast), `tooltip`. Existing: card, button, input, label,
select, switch, table, dialog, badge, separator.

Data flow: `adminApi.ts` gains `adminContent` methods — `get()`, `save(section)`,
`publish()`, `publishStatus()`.

### C. Publish flow

`POST /api/v1/admin/content/publish`:

1. Read the `SiteContent` draft.
2. Serialize to `site/src/i18n/fi.json`, `en.json`, `sv.json`.
3. Commit the three files to `main` via Octokit (GitHub API).
4. `deploy.yml` (path `site/**`) rebuilds and redeploys Pages. Live in ~2–4 min.
5. Update `lastPublishedUtc` + published hash on the doc.

Guards:
- Reject a second publish while one is in flight (in-memory lock or status flag on the
  doc).
- Validate all three JSON files parse and are non-empty before committing.
- Round-trip invariant: publishing must never drop a key. The serializer emits exactly
  the union of sections present in the seed; no section is lost because a form was not
  opened.
- Publish commits only `site/src/i18n/*.json` on `main`; it never touches the local
  `monorepo` working branch.
- Surface commit SHA + deploy status back to the admin UI (publish log).

### D. GitHub auth

- New fine-grained PAT with `contents: write` on `readysetsiivous`, stored in the
  backend `.env` as `GITHUB_CONTENT_TOKEN`. The running backend uses Octokit to commit.
- The token must NOT be a deploy secret used by Actions; it is a runtime secret on the
  Mac mini host.

## Error handling

- Publish failure (network, auth, conflict) returns RFC 7807 problem details and keeps
  the draft intact; the UI shows the error and allows retry.
- Commit conflict (someone else pushed): re-fetch, re-apply, retry once; on second
  failure, surface to user rather than silently overwriting.
- JSON validation failure on a raw section: inline error, publish blocked until fixed.

## Testing

- Backend: unit tests for the serializer (Marten content → JSON) and the publish
  command handler (Octokit mocked). Verify round-trip equality of the seeded JSON.
- Frontend: the structured form components render from fixture content; publish button
  calls the API and reflects in-flight / success / error states.

## Out of scope

- Bookings / Schedule / Invoices / Employees admin pages.
- Any change to site components or runtime content fetching.
- Content versioning UI beyond git history / publish log.
