# Partner acknowledgment of inventory entries

## Product design

An inventory take records what an employee collected. Partner acknowledgment is a
separate review of that evidence; it never changes the take, stock quantity, or
employee completion. Example: a cleaner records key collection with a photo;
an admin asks the property partner to confirm the collection.

Inventory entries currently have no company/property relationship. Sending is
therefore an explicit admin disclosure: select entries, choose an active
partner company, review the notes/photos, add an optional message, and confirm
that the evidence may be shared. Do not infer ownership from a location name.

### Admin inventory log

- Select 1–50 entries and use **Request acknowledgment**. Entries whose evidence
  was purged or which already have a request cannot be selected.
- A searchable, bounded partner selector avoids loading every company. Only
  active, partner-enabled companies can receive a request.
- The review dialog lists each item, quantity, location, date, note and photo
  preview. Employee names/IDs are excluded from partner payloads.
- The log displays Not requested, Awaiting acknowledgment, Acknowledged or Issue
  reported. Open a status to see the company, admin message, response, responder
  and timestamps. Reload/Apply refreshes statuses.
- Failed status reads disable requests until a successful reload. A failed send
  keeps the reviewed data; retrying the same payload uses the same batch ID.

### Partner portal

- A bell notification above the portal navigation shows the server-computed
  number of pending entries. Open it for Pending and Response history views.
- Review an entry to see the evidence and admin message. **Acknowledge entry**
  optionally includes a note. **Report an issue** requires an explanation.
- Responses are per entry, so a batch can contain both accepted evidence and an
  issue. Confirmed responses leave Pending and remain readable in history.
- English/Finnish labels, authenticated photo loading and bounded 25-row pages.
- This increment uses an in-portal notification on load/refresh. It does not send
  email/push notifications or promise real-time updates.

## Persistence and access

`InventoryAcknowledgment` is a tenant-partitioned, optimistic-concurrency Marten
document. Its ID equals the original usage ID: one entry can be disclosed to one
company once. Batch ID groups one admin submission. Item, location, quantity,
unit, note, taken date and photo IDs are frozen on request. Actor subject and
request date are stored internally; response identity/date are audited.

Create/response operations use the existing tenant reservation and a single
commit. Validate the full batch before inserting anything. Overlapping batches
cannot partially insert or overwrite requests. Same-batch retries are safe;
changed payloads conflict. Responses require the reviewed version and a pending
request. Identical response retries preserve the original response audit.

Admin routes require the existing admin policy. Partner routes reload and verify
an active, synchronized partner profile and active partner-enabled company.
Lists, responses and photos are restricted to that company and tenant; unrelated
IDs return not found. Full employee identities never leave the inventory take.
Partner responses include the partner responder's name for audit history.

Photos stay in the existing private inventory file store and retain its
three-month retention policy. Expired evidence cannot be newly requested;
existing text/response history remains when photos expire. Photo reads verify
both company ownership and the photo IDs originally shared. No public photo URL
or persistent browser cache is introduced.

## API

- `GET /api/v1/admin/inventory/acknowledgments/partners?search=` (up to 25)
- `GET /api/v1/admin/inventory/acknowledgments?usageIds=...` (up to 200)
- `POST /api/v1/admin/inventory/acknowledgments`
  `{ batchId, companyId, usageIds, message }`
- `GET /api/v1/partner/inventory-acknowledgments?history=false&skip=0`
  returns `{ items, pendingCount, hasMore }`
- `POST /api/v1/partner/inventory-acknowledgments/{id}/response`
  `{ version, status: "Acknowledged" | "IssueReported", note }`
- `GET /api/v1/partner/inventory-acknowledgments/{id}/photos/{photoId}`

## Deliberate first-increment boundaries

Requests and responses cannot be edited, cancelled, reassigned or resent. An
issue remains visible to admins for follow-up; this increment does not pretend
to resolve it. No automatic reminders, due dates, acknowledgment-all shortcut,
inventory-to-visit linking, or new payroll/billing behavior. Those can be added
as separately reviewed increments if needed.

Schema registration is included in `MartenConfig`. Local implementation is not
a production migration/deployment or a verified production release.
