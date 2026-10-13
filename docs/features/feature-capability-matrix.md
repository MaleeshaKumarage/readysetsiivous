# Feature-wise backend/frontend capability matrix

Reviewed at `ceff5bd` on 2026-10-13. This expands the feature inventory into
individual operations: an API helper is not an implemented screen, and an endpoint
is not proof that its authorization, persistence or business rules are complete.

**Yes** = reachable implementation exists; **partial** = some controls/fields exist;
**none** = no corresponding implementation found. Deactivation, voiding and
cancellation are separate lifecycle operations, not physical DELETE. Do not add
hard-delete merely to make a CRUD grid look complete; history and signed/billing
documents need retention rules. Recommendations are proposals for discussion.

See [inventory/gaps](feature-inventory.md), [auth and workspace review](technical-lead-review.md)
and [test catalogue](../testing/edge-case-catalog.md). Case IDs below identify
test families, not proof that those tests are currently implemented.

| Feature | Operation / field group | BE | FE screen | Missing part / next action | Tests |
| --- | --- | --- | --- | --- | --- |
| Public services | List active/localized catalogue | Yes | Yes | Locale/response races and zero-vs-error fallback need recovery; homepage capped at4 by design | CAT-001–005, LANG-005 |
| Public booking | Availability and create | Yes | Yes | Field errors, lost-response reconciliation and idempotency need work | BOOK-001–020 |
| Customer booking | Lookup and cancel | Yes | None | Add Manage booking flow with credential/privacy policy; number+phone currently query parameters | LOOK-001–004 |
| Admin bookings | List/detail/filter/history | Yes | None | Add Operations booking list/detail, not a disconnected second public wizard | OPS-001/006 |
| Admin bookings | Confirm/cancel/complete | Yes | None | Add allowed actions to booking detail; first define terminal-state transition guards | OPS-002 |
| Admin bookings | Assign/unassign employee | Yes | None | Add in booking detail/calendar; avoid duplicating shift assignment UI logic | OPS-003/004 |
| Admin bookings | Reschedule/edit arbitrary booking fields | None | None | Product decision; do not claim update/reschedule exists because status endpoints exist | BOOK-017, OPS-002 |
| Services | Create/list/detail-in-list/edit | Yes | Yes | Editing currently overwrites fields not exposed by form; preserve complete model | SVC-001–006 |
| Services | Active/inactive, soft-delete | Yes | Yes active toggle | Physical delete not supported/required; ensure toggle preserves image/info | SVC-005 |
| Services | fi/en/sv names/descriptions | Yes | Partial: fi input | Add translation controls with fallback preview; retain languages when editing | LANG-006, SVC-005/006 |
| Services | Icon/image upload/additionalInfo/order/featured/currency | Yes | None or hardcoded | Put advanced catalogue fields in service editor; upload helper alone is not UI | SVC-005/007/008 |
| Employees | Create/list/basic identity fields | Yes | Yes | Role-specific sensitive-data DTOs required; validate actual email uniqueness | EMP-001–003, AUTH-003 |
| Employees | Update/basic edit | Yes | None | Add employee detail/editor; update duplicate-email rules first | EMP-003/008 |
| Employees | Deactivate/reactivate | Deactivate and IsActive update | Deactivate only | Lifecycle must also define linked identity/session policy | EMP-007/008 |
| Employees | Work hours/skills/areas/pay/certifications/notes | Yes | None | Group into employee profile tabs; restrict payroll/HR visibility | EMP-004 |
| Employees | Send invite/reinvite | Yes; flow incomplete | None | Fix identity binding/registration destination, then add invite/status action; existing issue49 | EMP-005/006 |
| Employees | Own profile/work schedule | Some lookup helpers; no complete self-service API | None | Add employee My work workspace with server-side own-resource authorization | AUTH-004, OPS-005 |
| Companies | Create/read | Yes | Yes | Current list first100 only; separate loading failure from empty | COMP-001/005/007 |
| Companies | Update/deactivate | Yes | None | Add customer-detail actions; physical delete policy separate | COMP-004 |
| Companies | Search/pagination | Yes, take1–200 | None | Add list search and pagination; fix selectors in agreement/shift forms too | COMP-005 |
| Branches | Create/list within company | Yes | Yes | Reject missing/foreign company relationships before write | COMP-001/003 |
| Branches | Update/deactivate | Yes | None | Put branch lifecycle inside customer detail; preserve shift/document links | COMP-004 |
| Branches | Manage related shifts | Yes through shift API | Partial, separate shift screen | Customer detail can link/filter Operations shifts; one editor, not duplicated CRUD | SHIFT-003/017 |
| Shifts | Create/list/deactivate | Yes | Yes | Inactive eligibility, scope and recurrence correctness need BE fixes | SHIFT-001/003/013 |
| Shifts | Edit/reactivate/validity dates | Yes | None | Reuse shift editor; BE update must check company/branch ownership | SHIFT-003/010/018 |
| Shifts | Daily/weekly/biweekly/monthly/on-call | Yes | Yes | Biweekly UI should be single-day; persist correctly when changing schedule type | SHIFT-001/008 |
| Shifts | Different times per weekday (type1) | Yes | None | Add to shift editor, not a new top-level feature | SHIFT-002 |
| Shifts | Occurrence preview/calendar | Yes | None | Add calendar/detail preview after timezone/validity policy is fixed | SHIFT-009–011/018 |
| Shifts | Assign/unassign/batch employees | Yes | Yes, sequential batch | Transactional overlap/duplicate rules and per-employee partial results missing | SHIFT-012–016 |
| Employee schedule | Booking day view | Yes, admin-only | None | Integrate into Operations calendar; distinguish booking schedule from recurring shifts | OPS-005/006 |
| QC templates | Create/read/update/hard-delete | Yes | Yes | Scope/active state must survive edit; deletion policy for referenced template unresolved | QC-001–005/016 |
| QC templates | Company/branch scope and active state | Yes | None | Add scope/active controls and ownership validation | QC-004/005 |
| QC dispatch | Manual date/shift dispatch | Yes | None | Admin-only recovery action in Quality; audit created-vs-delivered counts | QC-007/008/011 |
| QC dispatch | On employee assignment | Yes | Indirect via assignment | Wrong/past occurrence and production-only email links need fixes | QC-009/012 |
| QC dispatch | Recurring at shift end | None wired; local untracked draft files | None | Implement shared dispatcher and worker before reporting this as an active feature | QC-010 |
| QC submissions | Read/filter by shift | Yes | Yes | Tenant-session isolation is a release-blocking verification task | QC-006/013 |
| QC submissions | Filter date/status/employee; inspect full detail/photos | Date/shift list filters; no dedicated complete reviewer flow | Partial: table/photo count | Define useful review filters/detail view; photo URL-size/schema safeguards | QC-013/017, QCP-006 |
| QC reporting | Monthly PDF | Yes | Yes | Real PDF content, timezone/month boundaries and failure feedback unproven | QC-014/015 |
| Public QC | Token lookup/items/notes/one-time submit | Yes | Yes | Repeat-submit409, precise item matching and identity/token policies need work | QCP-001–004/008/009 |
| Public QC | Photos | String/dataURL persistence, no upload endpoint | File picker/preview/remove | Decide bounded upload storage; no drag/drop handler or admin viewer | QCP-005–007 |
| Agreements | Create/read/list/detail | Yes | Yes | FE/BE count mismatch and company/PDF validation; partially filled signer rows need explicit errors | AGR-001–006 |
| Agreements | Copy signer links/mailto/document download | Token/file APIs | Yes | Actual email dispatch absent; distinguish client mailto from delivered invitation | AGR-007/012 |
| Agreements | Add/remove persisted signer | Yes | None | Add in agreement detail only after signed-signer/completion policy is defined | AGR-008/009 |
| Agreements | Cancel/activate/deactivate | Yes | None | Add state-aware actions; fix conflict500 and document access policy | AGR-010, SIGN-004/005 |
| Agreements | Replace PDF/title after creation, hard-delete | None | None | Separate decision; signed artifacts should not become generic editable CRUD | AGR-008/009, SIGN-014 |
| Public signing | Preview/draw/type/sign/completion/download | Yes | Yes | Same-token file race, corrupt image validation and recovery require BE integration | SIGN-001–016 |
| Invoices | Create/list/detail/mark-paid/void/PDF | Yes | None | Add Billing workspace; guards, permission scope and parallel-create protection first | INV-001–007 |
| Invoices | Edit issued document/draft CRUD/hard-delete | None beyond domain Draft constant | None | Decide invoicing policy; do not infer APIs from a status constant | INV-005 |
| Tenant settings | Profile/issuer/bank/locale/timezone/notice | Yes | None | Add Settings with tenant-admin controls; validate timezone/locale and effective settings | TEN-006/007 |
| Public content | Dynamic page read/update | Yes | None; website uses static dictionaries | Decide CMS integration scope; connect editor and public rendering in one change | TEN-008/009 |
| Platform tenancy | Provision/list/suspend/activate | Yes | None | Dedicated platform workspace/API permission; never expose through ordinary tenant settings | TEN-001–005 |
| Auth/identity | Login/logout/JWT/realm roles | Yes | Yes | Audience, own-resource scope, role-aware UI, invitations and sensitive DTOs incomplete | AUTH-001–011 |
| Contact/legal/theme/language | Public content, navigation, vCard/QR/legal/theme | Static/local | Yes | Broken hidden-section anchor, dev QR host and accessibility cases remain | WEB/LANG/THEME/CARD/LEGAL |
| PWA | Install/manifest/service worker | Static | Partial | Offline cache population and unsupported-browser fallback not implemented | PWA-001–005 |

## Feature owners and dependencies

| Delivery group | FE work | BE prerequisite | Keep together for acceptance |
| --- | --- | --- | --- |
| Operations | Bookings/detail/actions, calendar, shift editor/assignments | State transitions, timezone/validity, scoped assignments | Scheduling UI must display both booking and recurring-shift obligations without merging their models |
| Customers | Company/branch search, edit and lifecycle | Parent ownership/eligibility rules | Branch choices in shift/agreement workflows reflect actual lifecycle |
| People | Employee profile/advanced fields/invite/lifecycle; My work | Identity binding, self-resource scope, safe DTOs | Role and employment flags agree with token access |
| Quality | Templates/scopes, submissions/detail/photos, recovery dispatch/report | Tenant session, item/photo validation, dispatcher/delivery | Assignment→form→email→submission→report is one testable chain |
| Documents/Billing | Agreement details/lifecycle and invoice screens | PDF/signature integrity, invoice transition/race policy, permission boundaries | Link both from customer/booking detail; keep separate document state machines |
| Settings/Platform | Tenant settings/content; separate tenant lifecycle workspace | API audience/provisioner permissions/tenant boundaries | Tenant admin never gains platform access through role naming |

The most useful implementation order is auth/tenant integrity and field-preserving
edits first; then complete Operations and customer/employee details; then Quality
automation/review and Billing. Cosmetic reorganization should not precede reliable
permissions and write semantics.
