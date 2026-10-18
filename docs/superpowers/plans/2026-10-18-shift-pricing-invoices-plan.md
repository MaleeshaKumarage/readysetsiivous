# Shift pricing/invoices implementation

1. Add separate tenant-scoped effective pricing and visit override persistence; admin APIs with shared reservation and billed-period edit guards.
2. Extend invoice snapshots and safe admin DTOs; create a calculator using resolved schedule, calendar proration and completed on-call work. Add deterministic preview and reserved issue with overlap/staleness checks, issuer settings and verified payment reference.
3. Add admin invoice UI and pricing/visit dialogs, monthly subtotal overrides, full error handling and preview invalidation. Add multilingual branded invoice PDF and preserve booking compatibility.
4. Test calculations, authorization/isolation, preview/issue, PDF extraction/layout and UI flows. Verify compact report appendix review with actual tests. Push PRs, address findings and merge only passing changes.
5. Apply narrowly scoped EUR150/month Methehe pricing and known issuer metadata with backup; do not create a customer invoice or invent missing bank/address details.

The writing-plans skill is unavailable in installed skill directories; this plan implements the approved user requirements directly.
