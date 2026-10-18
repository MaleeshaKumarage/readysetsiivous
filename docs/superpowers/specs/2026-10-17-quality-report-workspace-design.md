# Quality report workspace

Approved scope: user requested the summary workspace, emailed acknowledgment and three PDF languages, and delegated the period choice.

## Reports
Searchable shift multi-select; select multiple year/month values OR one inclusive tenant-local From–To range. A maximum 366 selected days and 50 shifts bounds report cost. Deduplicate months and shifts; reject mixed or invalid periods. One summary row per selected shift/location: Company, Branch, Shift, Scheduled occurrences, Completed occurrences, Scheduled hours, Actual hours. Actual hours sum valid submitted staff durations; missing timestamps show unavailable/incomplete rather than fabricated zero. Period attribution uses scheduled local start, including overnight visits. Default no shifts selected and no automatic all-tenant export.

Use one server report builder for screen, per-shift PDF, combined PDF and sharing, extending existing monthly calculation without changing legacy monthly endpoints. Preserve historical location rows if a shift moved branches. Preserve activity details and dated photo/note appendix in PDFs, remove detail sections from the management workspace. Label noncontiguous selected months explicitly. Filter changes clear loaded data so stale results cannot be exported.

## Sharing
Admin only. Popup chooses company contact, branch contact or custom email and report language. Add optional validated branch contact email to branch create/edit. Deduplicate suggestions across selected shifts; custom addresses remain explicit. One recipient per send yields one unguessable token and immutable snapshot. Existing anonymous PDF links remain compatible, 30-day expiry and revoke continue.

Send a link to a public report landing page, not an attachment. Page exposes safe summary, PDF and an explicit Acknowledge receipt POST; GETs/email scanners never acknowledge. This is bearer-link receipt acknowledgment, not verified identity or a signature. Store first acknowledgment time atomically and do not expose recipient email publicly. Track sent-to-provider time, failure and acknowledgment; never claim inbox delivery. Failed sends remain visible and can be retried with the same link/snapshot. No real emails during tests.

## Localization
English, Finnish and Swedish labels throughout PDF summary, tables, appendix and footer; preserve entered names/activity text/notes unchanged. Email and public recipient controls follow chosen language. Existing snapshots default English. Do not use AI translation.

## Verification
Backend tests: period validation and tenant-timezone boundaries, aggregation and deduplication, selected-shift isolation, timing semantics, three-language PDF extraction and render, expiry/revoke, privacy, acknowledgment idempotency, send failure/retry. Browser tests: multiselect/range, stale data reset, per-row/combined scope, recipient suggestions/custom validation, failed send retry, recipient acknowledgment. Existing monthly/share contracts remain covered.
