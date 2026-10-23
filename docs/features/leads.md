# Leads

Tenant admins can manage prospective customers at `/admin/leads/`. Tenant managers can view leads and private attachments, record voice notes and manage follow-ups at `/manager/leads/`. Employees have no Leads access. Managers cannot edit lead company/branch/contact metadata, convert, delete or change lead activity.

## Capture and manage

Create a lead with a company name. Store business ID, company contact/phone/notes, zero or more explicitly added prospective branches and their address/phone/email, a description and up to 20 distinct company contact emails. Choose one primary company email when addresses are present. Business ID can be supplied later but is required for customer conversion. New leads start without branches. Added branches require names; there is no small branch-count cap. The editor shows five branches per page. Fields are trimmed; emails are deduplicated without case sensitivity.

Filter Active, Inactive, Converted or a sales status (New, Contacted, In discussion, Lost); search company name, business ID or contact emails. Lists are paginated. Inactive leads can be edited and reactivated. Only inactive, unconverted leads can be permanently deleted, after confirmation. Errors preserve entered fields; Reload lead explicitly discards local edits after confirmation.

## Private images

Save a lead before uploading. Add up to three JPEG/PNG/WebP images, maximum 5 MiB each, 4096 pixels per dimension and 20 million pixels total. The backend checks decoded image content; file extensions alone are insufficient. Detail images use authenticated admin/manager downloads and cannot be opened through the public uploads directory. View images is the default tab and has no upload/removal controls. Admins deliberately select Manage images to upload or remove attachments with confirmation on active unconverted leads. Inactive/converted records retain view access but cannot change images.

Uploads run individually with progress. If a batch partially fails, successful uploads remain attached and the UI permits retrying remaining files. A fourth image is rejected server-side even under concurrent requests. Object URLs are revoked when no longer needed. Newly written files are cleaned after failed persistence; removed image metadata is committed before best-effort file cleanup.

## Convert to customer

Save edits, choose Convert to customer and review the confirmation. Conversion atomically creates one active company and every added branch (including zero branches) and marks the lead Converted. No customer is created when the business ID already belongs to an active or inactive company in that tenant. Repeated/concurrent conversion requests return the same customer rather than duplicates.

The selected primary address becomes the company's contact email; each branch email becomes that branch's contact email. Company notes include both the original notes and description. All extra emails, description, images and source fields remain accessible on the read-only converted lead. Open customer links to the company and expands its branches. If that company/branch is later removed, conversion history remains and a clear unavailable-customer message replaces the link. Existing company contact schemas are unchanged.

## API and persistence

Metadata mutation routes remain admin-only under `/api/v1/admin/leads`. GET list accepts search/status/skip/take (maximum 100). POST creates fields; GET by ID returns lead and private image metadata; PUT by ID receives `{version,fields}`. Activate/deactivate/convert/DELETE receive `{version}`. Image POST receives multipart `version` and `file`; image DELETE receives `{version}`. Image GET returns validated image bytes with no-store/nosniff headers.

Lead is a conjoined tenant Marten document. UUID version checks and per-tenant/lead PostgreSQL advisory reservations serialize edits, lifecycle actions, images and conversion across replicas. Conversion stores Company, all Branch records and Lead in one transaction, with the existing tenant business-ID uniqueness index. Authenticated tenant context controls storage and lookup; clients cannot assign tenant, customer IDs, image paths or conversion status.

## Voice notes

Admins and managers record directly in the app, stop, preview, then save or discard. There is no audio file-upload picker. Maximum five minutes and 20 MiB; the recorder stops just before the limit to allow encoder finalization. The server verifies audio-only WebM/Opus, MP4/AAC or Ogg/Opus and decodes it to measure real duration. The API runtime packages ffmpeg/ffprobe; local backend tests also need these tools on PATH (override `LeadAudio:Ffmpeg` / `LeadAudio:Ffprobe` if needed).

Saved notes show the server-resolved recorder name, time and playback. A failed save retains the recording in the open app and retries with the same ID; closing/reloading cannot recover an unsaved recording. Microphone tracks and object URLs are released on stop/leave. Private audio is never sent to email recipients. Admins may delete notes except on read-only converted leads; managers cannot delete saved recordings.

## Follow-ups and notifications

Create independent follow-ups with reasons, first date, Once/Daily/Weekly/Monthly repetition, interval, optional end date and selected active tenant admins. These are internal reminders, not emails to lead contacts. Send time is fixed at 06:00 in the tenant timezone. A reminder set for today after 06:00 is due immediately. Monthly schedules clamp short months without drifting. Changes to a historical schedule's reason/recipients retain its original calendar anchor and next occurrence. Changing cadence requires today or a future first date. An attempted notification with uncertain acceptance blocks editing until resolved or explicitly cancelled/reviewed.

The database-backed worker scans approximately every minute, survives restart and serializes sending with lifecycle writes across replicas. Email acceptance may be later than the scheduled time. Downtime coalesces recurring overdue dates rather than sending a backlog; an expired end date completes the schedule without stale catch-up messages. Lost status, deactivation, conversion and manual cancellation stop pending notifications. Reactivation never restarts old schedules. Permanent lead deletion removes its collaboration metadata; a bounded maintenance sweep cleans unreferenced old audio.

Branded emails contain the lead name, reason, due date/time/timezone, recurrence and authenticated Open lead button. Each recipient has separate status and retries, with a stable provider idempotency key and immutable payload. Sent means accepted by Resend, not confirmed inbox delivery. Transient failures retry within a bounded 20-hour window; uncertain acceptance after that becomes Needs review rather than blindly resending. Successful recipients are not resent because another failed. Missing mail configuration fails visibly. Directory outages defer sending and display a processing error/last-checked time on the follow-up; disabled/non-admin recipients are skipped. There is no inbox/reply tracking or mailbox integration.

Collaboration endpoints live under `/api/v1/manager/leads` with the verified admin/manager operations policy. List/detail/private image reads, settings, eligible admin recipients, voice-note save/playback, follow-up create/edit/cancel/history are tenant-scoped. Voice DELETE additionally requires admin. Original metadata mutations remain admin-only. Converted leads retain their source branches, audio and delivery history. Legacy single-branch leads are projected without a write migration; older clients cannot silently delete extra branches when editing the first one.

## Verification

Tests cover validation and multiple contacts; inactive editing/reactivation/deletion; image limits and safe cleanup; API denial for employees/managers/anonymous requests; tenant-private files; real PostgreSQL tenant isolation, email search, duplicate inactive-company rollback, concurrent conversion, stale writes and simultaneous image uploads. Browser coverage includes create/edit, primary contact, private upload/remove/failure retry, conversion confirmation/conflicts, retained read-only snapshots, unavailable customer/image retry, list/detail retry, filters/pagination and company deep links on desktop and mobile.


## Deployment ordering

For a combined API/frontend release, Pages now waits until `/healthz` reports the required API release (or a descendant containing it) before publishing the new frontend. The API image receives its release SHA at build time. Frontend-only changes do not wait for a new API image. A timed-out API rollout leaves the previous frontend published, preventing the temporary Save-lead 404 seen when the first Leads frontend went live before its API.
