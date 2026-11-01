# Quote request alerts implementation plan

Primary goal: admins notice pending partner quotes and identify the affected
branches; owners can see their request is awaiting pricing.

1. Reuse `staffPartnerApi('admin').quotes()` and Branch DTOs. Add a shared pending
   quote predicate/badge and an admin-only queue provider/banner with bounded
   visible polling, focus/navigation refresh, stale-response protection and Retry.
2. Mount only after existing admin identity verification. Add the queue deep link
   and shared red badges to Companies, Partner operations and the partner portal.
   Refresh the shared queue after successful pricing without resetting forms.
3. Add focused desktop/mobile coverage for pending state, navigation, pricing
   resolution, refresh and errors; verify unauthenticated/denied access does not
   query the queue. Run TypeScript, affected Playwright suites and production build.
4. Push the isolated feature branch and create a reviewable PR; do not infer
   authorization to merge a new PR or to modify the separately pending #232.
