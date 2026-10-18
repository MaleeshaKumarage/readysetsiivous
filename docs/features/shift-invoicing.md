# Shift pricing and invoices

Tenant admins use **Shifts → Pricing** for effective-dated net EUR prices, and **Invoices** for issuer settings, preview, issue, PDF, paid status and voiding. Managers/cleaners have no pricing/invoice access. Quality reports never include prices.

- Monthly: rate × inclusive active calendar days ÷ days in that calendar month. Intersect the billing period, shift validity/active history and price effective dates. Full months charge once, regardless of visit count. Each shift/month subtotal can be overridden with an amount and reason. Different VAT rates cannot be collapsed into one override.
- Hourly: rate × resolved scheduled visit hours. Actual recorded hours and cleaner count do not multiply the invoice. Cancelled visits are excluded; moved and extra visits follow the resolved schedule.
- Per occurrence: on-call shifts only. One charge per completed resolved visit, after all expected cleaner tasks are submitted. A per-visit override replaces the standard net rate with an explained amount.

Prices may be zero but missing prices are an error. Effective dates are unique and each rate runs until the next one. An edit cannot rewrite an issued/paid invoice's billed period; start a new price after it or void the invoice first. Amounts use decimal arithmetic, rounding each line's net and VAT to cents away from zero. Invoice totals sum those rounded line values. Price changes split monthly lines by rate/branch, retaining the calculation basis.

A shift entered after its scheduled start still contributes contracted past visits from its explicit validity start. Billing uses an in-memory snapshot that extends the first active schedule back to that start; later schedule changes retain their effective boundaries. It never rewrites work history or attachments. Legacy shifts without a validity start are not billed before their creation date.

Choose one company, up to 50 shifts and an inclusive period of at most 366 days. Enter the customer's billing address and choose English, Finnish or Swedish. Preview shows names/branches, covered dates, scheduled hours, occurrences, basis, net, VAT and total. Selection/address/language/override edits clear the preview; a changed issuer, schedule, roster, completion or price requires a fresh preview before issue. Read-only preview never allocates a number or creates an invoice.

Issue recalculates under the tenant's cross-replica write reservation and rejects overlapping Issued/Paid invoices containing the same shift and billing dates. Booking invoices with no shift-period metadata never block shift invoices. Voiding permits reissue. Numbering shares existing booking invoice counters; explicit issue-year allocation handles tenant-local New Year correctly. Stored invoices retain issuer, customer address/business ID, shift/branch/period, hours/count, rates, overrides, VAT, language and payment reference independently of future edits. Existing invoices/PDFs remain readable after shift deletion.

Issuer name, business ID, address, email/phone, IBAN/BIC, payment terms and default VAT come from tenant settings in the database. Valid IBAN/BIC and complete issuer/customer addresses are required to issue. Sample issuer details are explicitly labelled in UI/PDF and the flag is frozen in the invoice. Replacing settings later does not alter issued samples. No customer email or invoice is automatically created by setup/testing.

The active Metehe agreement supplies the legal name Samarawickrama Sujith Rangana (ReadySet Siivous), business ID 3404861-9, public contact details and 10-day payment terms. It does not supply bank details or an issuer address. The user authorized sample issuer address/bank values until updated, and EUR150/month before VAT for the biweekly shift; its partial October is prorated unless an admin overrides the month subtotal.

## API

- GET/PUT `/api/v1/admin/shifts/{id}/pricing`: effective pricing history.
- PUT `/api/v1/admin/shifts/{id}/occurrence-price`: on-call visit override with UTC occurrence start, amount and reason.
- GET/PUT `/api/v1/admin/invoices/issuer`: invoice-only tenant settings.
- POST `/api/v1/admin/invoices/shift-preview`: read-only computed snapshot and preview token.
- POST `/api/v1/admin/invoices/shifts`: same inputs plus preview token; creates the invoice snapshot.
- Existing invoice list/detail/PDF/mark-paid/void routes are retained.

## Regression scenarios

Test full/partial/leap-month proration, inclusive one-day periods, inactive days, validity dates, mid-month rate/branch changes, fractional hours, zero rates, missing pricing, overrides and mixed VAT rejection. Resolve cancelled/moved/extra/on-call visits across tenant timezone/DST boundaries. Deduplicate completed visits with several cleaners; pending tasks are not billable on-call work. Test stale preview edits, double clicks/concurrent issues, overlapping date endpoints, foreign/missing shift/branch, issuer/customer validation, bad IBAN, sample flag and no accidental email. Verify immutable snapshots, tenant isolation, local New Year counter allocation, legacy booking compatibility, long Unicode descriptions/notes and complete EN/FI/SV PDF system labels. Browser checks cover preview/issue, input invalidation, pagination/load retry, pricing/overrides and actual API error reasons on desktop/mobile.

Full calendar-month selections charge the full fixed monthly fee for shifts active during any part of that month. Partial date selections remain calendar-day prorated. Existing issued invoices are unchanged; void/reissue is required to correct an issued amount.
