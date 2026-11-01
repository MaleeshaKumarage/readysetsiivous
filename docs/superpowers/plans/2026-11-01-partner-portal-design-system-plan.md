# Partner portal implementation plan

Primary goal: partners can complete all existing company/property/cleaning tasks
in a consistent branded light portal, in either Finnish or English.

1. Extract a typed partner-only FI/EN copy catalog and error/status localization.
   Persist language independently of public/admin preferences and preserve drafts.
2. Rebuild the partner page with existing design-system components and scoped light
   tokens. Retain request idempotency, role checks, focused inline forms and APIs.
3. Add bilingual desktop/mobile regressions and run existing partner/quote/auth/
   cancellation suites. Inspect screenshots and a production export.
4. Update the existing partner PR with reviewable design/testing evidence. Keep
   the separate pending deployment-policy approval and live production unchanged.
