# Employee Web Push implementation plan

Approved design: ../specs/2026-11-02-employee-web-push-design.md, including administrator new-partner-request notifications and 30-minute reminders.

1. Add tenant-scoped device, intent and delivery documents; validate browser provider endpoints and encryption keys; implement verified self-service ownership, safe DTOs and test throttling.
2. Add VAPID settings, encrypted Web Push transport, per-delivery PostgreSQL reservations and bounded retry/crash recovery. Disable unavailable configuration and redact provider credentials.
3. Stage assignment, shift, visit and accepted-partner changes in the scheduling transaction. Include previous recipients; stage administrator request events on new partner request creation only.
4. Schedule reminders from the canonical effective roster and QC work evidence. Preserve moved identity, respect opt-in timestamps and expiry, and recheck live eligibility before sending.
5. Add authenticated API, minute tenant worker, dependency registrations, Marten mappings and protected deployment environment passthrough. Keep production disabled by default.
6. Add employee PWA manifest and explicit gesture-driven notification panel; service worker generic payload/click handling, device ownership binding and logout cleanup.
7. Verify backend ownership/provider/retry/reminder/atomicity boundaries with unit and isolated PostgreSQL tests; run API suite, frontend type checks and production export. Record physical iPhone/Android receipt as a rollout check requiring real hardware.

No production configuration changes, real notification sends, commits or deployment are part of implementation validation.
