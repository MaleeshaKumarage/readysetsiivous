# Connected field-service workflows — approved design

Date: 2026-11-02. Baseline: main `b4f7b6260797b3784c0b3de295464d1373fee5fd`.
Status: user approved implementation of this design. Deliver as independently tested increments; do not describe an unimplemented phase as released.

## Goal and decisions

Guide a small cleaning business through **lead/customer → quote or agreement → planned/assigned work → completion → customer invoice and employee payment**. Expenses attach to the customer, property or visit when relevant. Users must not have to memorise prerequisites or re-enter known context.

Target per tenant: 100 customers, 500 properties, 100 staff and several years of work/financial history. Keep the current Mantine light-only English admin theme. Keep partner Finnish/English support and current cleaner theme. Reuse established identity/tenant boundaries; managers are tenant-wide operational users, not branch managers.

Support Individual and Company customers. Individuals do not require a business ID. Existing companies keep their IDs and relationships. Partner access remains company-only. Do not impose a new signed-agreement prerequisite on scheduling.

Payments are made externally and recorded here. Bank execution, statutory payroll/tax calculations, tax filings, accounting ledgers and automatic reminder campaigns are excluded.

## Evidence from the current implementation

- `/api/v1/admin/companies` already supports search/skip/take (maximum 200), but Companies UI uses `listAll()`. Agreement and shift company selectors request only the first 100 records.
- Existing company domain creation requires business ID; private customers are not a first-class customer type.
- Agreements are created in a modal with existing-company selection, PDF and signer rows. There is no inline prerequisite creation. List items lack customer context.
- Shift definitions, occurrence exceptions and visit assignees already exist. Preserve their distinctions; do not replace them with a new job model.
- Leads already uses 25-row search/pagination and lead-to-company conversion. Reuse these patterns without duplicating the converted customer.
- Invoices already support manual and shift preview/issue, issuer settings, snapshots, mark-paid, void, PDF and bank barcode. History and creation currently share one long page. The API list returns an array and supports status/fromUtc, not paging or partial payments.
- Employee `PayRate` exists in the domain and command fields, but no earnings/payment-run module exists.
- No expense module was found. Public booking management APIs exist, but no admin navigation destination exposes them.
- Partner requests/offers and cleaner My shifts already implement handoffs. Extend their discoverability instead of creating parallel assignment rules.

## 1. Customer context, guided creation and compact lists

Rename the navigation label Companies to Customers; retain `/admin/companies/` and existing API routes for compatibility. Add customer type with Company as the default for historical documents. Store reusable customer billing/contact information. Business ID remains required and unique for companies; individual records have no artificial business ID. Partner flags/links must reject individual customers server-side.

Open a customer workspace with lazy-loaded **Overview, Properties/branches, Agreements, Work, Invoices, Expenses** sections. Hide unavailable finance tabs by permission. Context actions preselect customer/property in shared forms; they do not duplicate editors.

Agreement creation: **Customer → Document/signers → Review**. Create a missing customer inside the task, select its returned ID, then continue with all fields/files retained. Shift creation: **Customer/property → Schedule → Assignment/checklist → Review**. Required prerequisites and optional enrichment must be clearly distinguished. Changing parent selection clears incompatible dependent choices and explains why.

Keep the main task mounted while creating a prerequisite; avoid nested modal stacks. Cancelling inline creation returns to the task. A successfully created prerequisite remains saved even if the parent task is cancelled; show this fact. Warn before discarding unsaved changes; do not store personal drafts/files in browser storage. Browser reload may discard a draft after warning; no server draft system is implied.

Shared list contract: 25 rows default, 50/100 choices, search, relevant filters, total result count and deterministic sorting. Filter/count the complete dataset, not the displayed page. Preserve filters/page/context in URL and restore on browser Back. Reset page on changed filters and correct empty out-of-range pages after deletion. Use tabs for stable views/lifecycle, dropdowns for related-record selection and accordions only for secondary content. One clear primary row action; secondary/destructive actions in a labeled menu.

Selectors search server-side with bounded results, cancellation/stale-result protection and retained selected options. Distinguish loading, no matches and request failure; allow retry. Include customer/property context to distinguish duplicate names. Historical inactive values remain readable; exclude them from new work unless existing policy explicitly permits selection.

## 2. Work queue and handoffs

Operations gains Work queue with typed public-booking, partner-request and unassigned-work entries. Retain underlying models and mutation APIs. Views: Needs action, Scheduled, In progress, Completed; filter customer, property, date, assignee and source. Link to the relevant editor/detail; do not reimplement booking or partner transitions.

Shift workspace groups schedule, assignees, occurrences, checklist and pricing. Explicit assignment scopes remain This visit / Recurring assignment / From a date. Preserve future overrides and completed work. Show requested → awaiting acceptance → accepted/scheduled → in progress → completed, with actionable decline/change reasons. Public booking actions remain admin-only unless separately authorised.

Partners see only their company and cleaner staff see only their offers/assigned work. Keep existing red quote badges/pending counts and link them to filtered queues. Show next responsible actor and operation result. Checklist items measure activities; unchecked items do not block completion. Keep scheduled and actual times separate. Add reviewable missing-completion/unusual-duration views without automatically modifying work records.

General quote preparation/acceptance is a later increment in this stream: customer/property context, itemised net prices and VAT, validity, reviewed sending, accepted/rejected state and conversion to related agreement/work. Do not force existing partner property pricing through the new quote process.

## 3. Billing and collections

Invoices tabs: History (default), Create invoice, Settings. Preserve existing forms and financial calculations. Customer/work entry points preselect customer, period and eligible work, and show exclusion reasons. Reuse billing details but always allow review before issue. Employee rates never drive customer pricing.

Add append-only payment entries (amount/date/reference/actor), derive balance/partial/full paid state, and provide Outstanding, Overdue, Paid and Voided views. Overdue depends on due date and outstanding balance. Reviewed email sending/reminders show delivery outcome; no automatic campaign by default. Invoice corrections use linked adjustment/credit documents, never rewrites of issued snapshots. Preserve numbering, preview binding/idempotency, languages, PDF and barcode.

## 4. Employee earnings and payments

Calculation: each employee's hourly rate × planned duration of their own completed visits. A two-hour visit completed by two employees pays two hours to each. Another employee's completion does not establish this employee's earnings. Exclude cancelled, incomplete and already-paid work. Missing rate/duration/completion identity is an exception, never silent zero pay.

Add effective-dated rates to an admin-only Pay profile tab. Use scheduled visit date in tenant timezone for pay month and rate selection. Existing PayRate remains readable; require explicit confirmation/effective date before importing it into the new schedule. Do not invent historical rates.

Payment runs: Draft → Approved → Paid. Draft shows employee/visit breakdown, calculated amount and reasoned paid-hours/amount adjustments. Approval revalidates source version/eligibility and freezes employee, rate, planned duration and work snapshots. Enforce unique employee/visit settlement identity transactionally, including overlapping periods/retries. Work-time corrections do not change planned paid hours automatically. Late-recorded work becomes an unpaid adjustment for review; paid corrections become linked adjustments, not historic mutations. Deactivation/roster removal must not erase earned or paid history.

Record external payment date/reference. Generate employee statements and accountant exports. Employees can view their own statements, not colleagues' rates or payment data. Managers have no employee-rate/payment approval access. Admins/managers who perform assigned work follow the same completion calculation when an explicit rate exists.

## 5. Expenses and reimbursements

Finance Expenses views: Needs approval, Approved/unpaid, Paid, Rejected. Staff (employees/managers) submit their own receipts; tenant admins approve/reject and record settlement. Admins can record company-paid costs. Capture date, category, supplier, amount/currency, description, payer and optional customer/property/visit references. Receipt access is private and authorised.

Distinguish company-paid costs from employee-paid claims. Company-paid costs do not create employee reimbursement. Keep reimbursements separate from hourly earnings; a payment summary may show both with separate subtotals. Expense state changes and payment entry are audited; prevent duplicate reimbursement. Rejected claims show reasons and may be revised/resubmitted before approval. Paid records use corrective entries, not destructive edits. Initial category list is configurable only by admins; this is a register, not a tax-deductibility engine.

## 6. Operational overview and feedback

Dashboard Needs attention links to server-filtered unassigned work, pending acceptance, missing completion, unpaid invoices, expense approvals and payment exceptions. Query permission-scoped real counts; do not fetch all entities to count in the browser. Centralise issuer/payment setup in Settings while retaining invoice shortcuts. Standardise loading/error/retry/empty/success feedback. Explain disabled actions, missing prerequisites and next useful steps without auto-executing follow-on actions.

## Interfaces, accessibility and compatibility

Extend existing tenant-scoped company/create/update/list DTOs compatibly. Preserve existing array endpoints until consumers migrate; add opt-in paged/search interfaces with summary payloads. Add customer/property context to agreement/work summaries. New monetary calculation is server-side decimal; UI formats existing currency conventions. Retain immutable financial snapshots and tenant isolation throughout.

Keyboard navigation, associated labels, focus restoration, screen-reader announcements, 44px primary touch controls and desktop/tablet/phone containment are acceptance requirements. New UI must stay legible under OS dark preference and installed-PWA mode. Use current components/tokens rather than a new design framework.

## Verification and delivery

Deliver in order: customer context/lists; work queue/handoffs; employee earnings/payments; expenses; billing collections/adjustments; dashboard integration. Each increment gets a reviewable implementation and its own focused validation. Detailed checklist is in the companion implementation plan. No production data fix, migration rollout or deployment is implied by local implementation; deployment still uses existing verified release procedures.

## Sequential delivery and employee autoaccept steering

Following design approval, the user requested small PR chunks: focused Playwright E2E tests for each chunk, review/fix/merge, then saved handoff and context compaction before the next chunk. The reusable checklist and delivery ledger are authoritative for progress; completing one chunk does not complete the programme.

The user additionally requested an employee-owned work autoaccept preference, defaulting to manual, with the approvals section hidden when enabled. This is the next small increment after C01. The user confirmed that an admin must supply an exact planned start for automatic acceptance. Enabling is blocked while any existing pending offers remain, with a clear instruction to resolve them first; no pending offer may be silently hidden. Disabling affects future offers only. Autoaccept is one atomic server Offer operation under the same work-reservation locking as preference updates and manual acceptance, preventing a pending-offer visibility race. Reuse existing overlap/date/pricing checks. Send informational notifications, not approval-prompt email. Company/employee isolation remains mandatory.
