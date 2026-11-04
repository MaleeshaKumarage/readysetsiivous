# Roadmap R01–R12 handoff (2026-11-04)

Status for whoever picks this up. Everything below is merged to `main` via PR #277 (merge commit `8d018cc`).

## Where things stand

| Item | State | Notes |
|---|---|---|
| R01 owner edit property (#260) | Closed | `PUT partner/branches/{id}`; price, company and active flag cannot change |
| R02 owner withdraw quote (#261) | Closed | `POST partner/branches/{id}/quote-request/withdraw` |
| R03 staff reject quote (#262) | Closed | `POST {admin,manager}/partner/branches/{id}/quote-rejection`, reason required |
| R04 inline cancel confirm (#263) | Closed | Merged earlier via #272 |
| R05 automatic acceptance (#264) | **Open, ops step pending** | Code and tests done. Migration must be run on production by hand |
| R06 partner email default (#265) | Closed | Opt-in, off by default, set explicitly in shipped config and compose |
| R07 month invoices (#266) | Closed | Per-occurrence lines; monthly prices over the requested month; UI test added |
| R08 Not started label (#267) | Closed | Past unstarted assigned visits stay in Scheduled with a red tag and border |
| R09 banner counts (#268) | Closed | Counts filtered to partner-enabled companies |
| R10 Playwright gaps (#269) | Closed | Staff and owner tabs, pagers, recording, keyboard paths |
| R11 backend tests (#270) | Closed | No-sign-in offers, flexible invoicing, queue labels |
| R12 Finnish coverage (#271) | Closed | Owner labels typed against the English keys; Finnish e2e tests |
| Banner fail-closed decision | **Open (#279)** | Show no counts vs last loaded counts when the company list can't be read |

Closing note: the PR body's `Closes #…` line only closed #260 (GitHub reads one issue per keyword). The rest were closed by hand after coverage was confirmed.

## Operations still to do

1. **R05 migration (#264).** Back up the database and pause employee writes. Dry run first, then apply:
   - `psql … -f api/deploy/employee-auto-accept-migration.sql`
   - `psql … -v apply_migration=true -f api/deploy/employee-auto-accept-migration.sql`
   - Every existing admin, manager and employee is switched on, including people who had opted out. Tell admins before the run; the message is in `docs/releases/2026-11-04-automatic-acceptance-default.md`.
   - Close #264 after the run.
2. **Push notifications (R06 follow-up).** `PushNotifications.Enabled` is `false` in the shipped `appsettings.json`. Production needs `PUSH_NOTIFICATIONS_ENABLED` and the VAPID settings in its environment.
3. **Shift validity correction.** The compose default is `SHIFT_VALIDITY_CORRECTION_MODE=Report`, so the first deploy only logs. Read the `Shift validity` lines, then set `Apply` to remove the planned records.
   - It removes only roster assignments and cancellation records past a shift's end date that have no work evidence and whose shift has no issued or paid invoice.
   - Extra visits, moved visits and anything with evidence are logged, not removed.
   - The job runs once per tenant and writes a `DataCorrectionRecord` marker.
4. **Partner email.** `PARTNER_EMAIL_ENABLED` defaults to `false`. Keep it that way unless the team decides otherwise.

## Decisions taken

- **R05:** all work employees move to automatic acceptance, including those who opted out (confirmed by the product owner).
- **R06:** partner email is opt-in; app notifications are the default channel.
- **R08:** unstarted past visits stay in Scheduled with a tag, not a separate view.
- **R07:** monthly prices are charged over the requested month, not over the span of completed visits.
- **Correction safety:** shifts with an issued or paid invoice are never removed by the correction.
- **Banner:** fail-closed for now (no counts until the company list loads). Pending #279.

## Shift end-date bug (already fixed)

Occurrences are generated from the schedule and were never clipped to `ValidUntil`, so the roster, calendar, work queue and assignment checks showed visits past a shift's end. The fix is in `ShiftScheduleCalculator.GenerateOccurrences` (domain). Stored data is not changed by that fix; the correction job above handles stored leftovers.

## Testing

**Backend** (`api/CleaningSuite.Backend.sln`)
- Needs the .NET 10 SDK. On Ubuntu 24.04: `apt-get install dotnet-sdk-10.0` (10.0.114).
- `global.json` pins `10.0.402`, so a local build with 10.0.114 fails. Either install the pinned SDK or run from a scratch copy with the pin relaxed. Do not change the repo's pin to get around it.
- DB tests need PostgreSQL and `QC_TEST_CONNECTION_STRING`. Without it, 95 tests skip.
- Last full run: 1384 passed, 4 skipped (opt-in live tests).
- `BrandedQualityReportTests` looks for `site/public/brand/…` by walking up from the build output. It fails in a copy that lacks `site/`.

**Playwright** (`site/`)
- `npm ci`, then `npx playwright test --config playwright.config.ts`.
- Last full run on the final head: 1090 passed.
- Known flake: `manager navigation loads partner operations…` on desktop (pre-existing).

## Key files

- Partner workflow: `api/CleaningSuite.Application/Partners/PartnerService.cs`, `api/CleaningSuite.Api/Controllers/PartnerController.cs`, `PartnerOperationsController.cs`
- Quote banner: `site/src/components/QuoteRequests.tsx`
- Staff queue: `site/src/components/PartnerOperations.tsx`; owner portal: `site/src/app/partner/page.tsx`
- Invoices: `api/CleaningSuite.Application/Invoicing/ShiftInvoiceService.cs`, `ShiftInvoiceCalculator.cs`
- Work queue: `api/CleaningSuite.Application/WorkQueue/WorkQueue.cs`
- Occurrence generation: `api/CleaningSuite.Domain/Shifts/ShiftScheduleCalculator.cs`
- Correction: `api/CleaningSuite.Application/Shifts/ShiftValidityCorrection.cs`, `api/CleaningSuite.Infrastructure/Persistence/ShiftValidityCorrectionRunner.cs`, `api/CleaningSuite.Api/ShiftValidityCorrectionService.cs`
- R05 migration: `api/deploy/employee-auto-accept-migration.sql`

## Open questions for the team

- #279: fail closed or show last loaded counts for the quote banner?
- Whether to keep `PushNotifications` off in production or turn it on, and with which VAPID keys.
