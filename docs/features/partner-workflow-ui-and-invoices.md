# Partner operations, acceptance and monthly invoices

Admin Partner requests uses the shared light Mantine admin theme, panels, buttons
and inputs. Manager operations use the same controls inside a local light provider.
Cleaner offers use My shifts' shared cards/buttons/fields and inherit light/dark.
Owner portal language and styling are unchanged.

Pending menu count = Requested cleaning requests + active unpriced quote requests
+ unresolved changes. Relevant tabs show their own counts. Offered/accepted,
completed and cancelled work is not counted as requiring arrangement. Counts
refresh on visible polling, navigation/focus return, retry and staff actions.
Failed reads retain previous counts and show a retry warning. Anonymous/denied
admins never mount these reads. Cancelled requests are hidden, not deleted.

Monthly completed-cleaning invoices use actual completion month in the tenant
zone, not scheduled month. The last assigned cleaner's finish determines the
visit's completion date. Every assigned cleaner must have submitted non-deleted
evidence; EndedAtUtc is primary, SubmittedUtc is a legacy fallback. No timestamp
means no invented completion date. Unassigned evidence cannot move a visit between
months. Cancelled/uncompleted/already-billed visits are excluded. Agreed request
prices/VAT remain frozen and preview eligibility is revalidated on issue.

Preview and PDF group individual shift rows by branch ID, then scheduled date.
Old invoice snapshots fall back to branch name. Scheduled and completion dates
remain visible. Customer-facing copy uses ordinary cleaning terminology. Existing
invoice layout, issuer/payment details, VAT totals and bank barcode are retained.
PDF system labels remain EN/FI/SV; entered business/branch/shift names are retained.
Selecting a company with the existing company flag defaults to completed-month
selection; standard recurring invoice options remain available.

Regression coverage includes desktop/mobile acceptance/decline, validation and
retry; manager pricing without admin reads; count refresh/failure recovery;
non-destructive cancelled-row hiding; grouped preview/issue payloads; cross-month
and Finnish-midnight billing; missing timestamps, multi-cleaner last finish,
legacy evidence, unassigned evidence, exclusions and multilingual PDF grouping.

Validation: 102 selected desktop/mobile partner and invoice browser checks passed; 54 invoice/calculation/PDF checks and 27 barcode checks passed without skips. After narrowing initial completion candidates, 27 invoice-service checks passed again. EN/FI/SV PDFs and admin/cleaner screenshots were visually reviewed. Release tests passed: 10 manifest tests, 21 deployment tests (one Linux-only skip on Windows).

Final production build passed with all 46 pages, lint and type checks.

Full-CI follow-up: widened the exact-visit database lookup to one second with an explicit exact-start filter because PostgreSQL microsecond precision collapses a one-tick bound. Real PostgreSQL-backed invoice concurrency, partner invoice and signing-key timeout checks passed: 10 tests, zero skips. Owned test DB/role, credential file and tunnel were cleaned. Shared admin test fixtures now mock requests/change counts as well as quotes so unrelated page tests do not call unmocked APIs.
