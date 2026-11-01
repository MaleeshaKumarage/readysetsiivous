# Monthly completed-cleaning invoice presentation

User requested standard customer invoices grouped by branch, individual cleaning
rows with net/VAT/total, no partner-program terminology, retaining the existing
invoice/payment/barcode format. User explicitly selected actual completion month.

Select completed, accepted, non-cancelled and unbilled visits by the latest actual
finish time across the assigned cleaners, interpreted in the tenant timezone.
Use EndedAtUtc, or SubmittedUtc for older submitted forms with no end timestamp;
never invent a completion date from the schedule or creation time. Require every
assigned cleaner's submitted, non-deleted completion evidence. A late-finished
visit is billed in the finish month even if scheduled earlier. Keep scheduled and
completion dates in the immutable invoice snapshot. Preserve frozen agreed prices,
VAT, tenant/company isolation, preview revalidation and duplicate-invoice locks.

Group preview/PDF rows by stable branch ID (name fallback for old snapshots), then
scheduled date, with each visit retained as a priced row. Use the existing invoice
renderer, localization, totals and bank barcode. Internal partner IDs stay internal;
customer copy uses neutral cleaning terminology. Existing issued invoice amounts
remain immutable; no production data correction in this change.

Validate cross-month/tenant-timezone boundaries, multi-cleaner last finish,
incomplete/missing-time/deleted/cancelled/already-billed exclusions, stable branch
groups and PDF EN/FI/SV text. Keep standard recurring invoice calculation unchanged.
