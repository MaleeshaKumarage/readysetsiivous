# Leads collaboration implementation plan

Approved design: `../specs/2026-10-23-lead-voice-followups-design.md`.
The writing-plans skill is unavailable in the installed catalog; this plan follows the approved design directly.

1. Add tenant-scoped voice, follow-up and delivery documents, recurrence calculator and service contracts. Preserve independent versions and deterministic delivery identities.
2. Implement private audio store and bounded ffprobe validation; package ffprobe in API image. Idempotent recording saves, authenticated playback, author attribution and cleanup.
3. Implement reminder repository, tenant-admin directory and strict idempotent Resend transport. Serialize mutations/dispatch with the existing lead reservation, atomically cancel schedules on lifecycle changes and remove collaboration metadata on deletion.
4. Add worker scanning registered active tenants, recurrence/catch-up, per-recipient delivery history and bounded retries. Use durable documents and PostgreSQL reservations across replicas.
5. Expose guarded operations endpoints plus separate admin-only voice deletion. Manager navigation uses the existing manager access gate; no broadening of admin routes.
6. Add shared recording and follow-up components, admin integration and manager read-only lead workspace. Browser recorder handles permission/error/stop/discard/retry and releases microphone tracks. Forms include recipients, recurrence and history.
7. Verify application boundaries with unit and real PostgreSQL tests, HTTP authorization/media checks, desktop/mobile Playwright scenarios, typecheck and production build. Use synthetic identities, media and email transport.
8. Review final diff, create PR, address comments and require green CI before merge/deployment. Verify production release without creating production reminder emails.

Progress will be recorded in feature documentation and the final PR. Do not modify unrelated untracked workspace files or merge the excluded alternative Leads PR #183.
