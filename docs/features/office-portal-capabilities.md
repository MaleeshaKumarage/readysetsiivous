# Office portal capabilities

What an office user (tenant admin, or manager where noted) can do on each page of the office portal at `/admin/`. This is an inventory of the current application, taken from the code on `main` (commit `cac82a9b`). It is the reference for the office portal redesign: every capability listed here must have a place in the new design.

Pages are documented one at a time. A page not listed yet has not been inventoried.

| Page | Route | Status |
| --- | --- | --- |
| Office shell (every page) | all `/admin/` routes | Inventoried |
| Dashboard | `/admin/` | Inventoried |
| Shifts (work orders in the redesign) | `/admin/shifts/` | Inventoried |
| Bookings | `/admin/bookings/` | Inventoried |
| Shift calendar (work order calendar in the redesign) | `/admin/shifts/calendar/` | Inventoried |
| Employee schedule | `/admin/shifts/employee-schedule/` | Inventoried |
| Work queue | `/admin/work-queue/` | Inventoried |
| Partner operations | `/admin/partner-requests/` | Inventoried |
| Quality Cycle | `/admin/quality-cycle/` | Inventoried |
| Leads | `/admin/leads/` | Inventoried |
| Companies | `/admin/companies/` | Inventoried |
| Customer workspace | `/admin/companies/customer/` | Inventoried |
| Agreements | `/admin/agreements/` | Inventoried |
| Employees | `/admin/employees/` | Inventoried |

## Office shell (every page)

Source: `site/src/app/admin/AdminWorkspace.tsx`, `site/src/components/NotificationBell.tsx`, `site/src/components/QuoteRequests.tsx`.

### Access and sign-in

- Shows a sign-in screen when the user is not signed in: "Sign in with your ReadySetSiivous admin account."
- Checks that the account may use tenant administration. When it may not, shows "This account cannot access tenant administration." with a **Portals** link (to `/account/`) and a **Sign out** button.
- Shows an access error message when the access check itself fails.
- Forces the light office theme on every device, including when the operating system or the public site is in dark mode. The public site's theme is restored when leaving the office portal.

### Header

- Tenant name, under the label "Tenant administration".
- **Portal switch**, shown only when the user also has their own work identity: switch between Office (or Manager) and the employee portal.
- **Notification bell** with an unread count (shows "99+" above 99).
  - Opens a side drawer listing staff notifications, newest first.
  - Unread items are marked; opening an item marks it read and follows its link.
  - Shows "Nothing needs your attention right now." when empty.
  - **Notification settings** link to the profile page.
  - Close button and Escape close the drawer.
- **Profile** link (`/account/profile/`).
- **Sign out**. A failed sign-out shows "Sign-out failed. Please retry."

### Navigation

- Grouped sidebar, in this order:
  - Workspace: Dashboard
  - Operations: Shifts, Bookings, Shift calendar, Employee schedule, Work queue, Partner operations, Quality Cycle
  - Customers: Leads, Companies, Agreements
  - People: Employees
  - Finance: Invoices
  - Catalog: Services
  - Stock: Inventory
- The current page is marked active.
- **Partner operations** shows a badge with the number of pending partner actions.
- Sidebar footer: **Notifications** (settings on the profile page) and **Install app** (installs the portal as a PWA where the browser allows it).
- On phones the sidebar is a drawer opened by a menu (burger) button.

### Banner on every page: pending quote requests

- Shown when partner branches are waiting for a quote: "N branch(es) awaiting a quote(s)" with a **Review quotes** link to Partner operations, Quotes tab.
- When the partner action counts cannot be read: an alert and a **Retry partner counts** button.
- When quote requests cannot be read: a message (and "Showing the last checked requests" if an earlier read worked) with a **Retry quote requests** button.

## Dashboard

Route: `/admin/`. Source: `site/src/app/admin/page.tsx`, `site/src/components/admin/DashboardStatus.tsx`, `DashboardCharts.tsx`, `CleanerWeekDonuts.tsx`, `LineChart.tsx`.

### Heading

- Logo, the title "Office portal" and "Your workspace for customers, people and daily operations."

### What needs attention

Four tiles. Each is a link to the list it counts.

| Tile | Count | Opens |
| --- | --- | --- |
| Visits needing action | Unassigned, overdue or not started visits | Work queue, Needs action view |
| Scheduled visits | Accepted or assigned visits | Work queue, Scheduled view |
| Partner actions | Quotes, requests and changes to arrange | Partner operations |
| Unread notifications | Unread staff alerts | Account page |

- While loading a tile shows "…". When a count cannot be read it shows "–" (never a false zero), and a **Retry status** button appears.

### Visits per day (chart)

- Line chart of scheduled visits per day for one month.
- Bold line: all customers. Up to four more lines: the top customers for the chosen sort.
- **Month switcher**: previous month and next month buttons, with the month name between them.
- **Sort customers by**: Most visits, Most scheduled hours, or Customer name. The sort decides which customers get their own line.
- Shows "No visits in (month)." when the month has none.
- The chart has a text description for screen readers, with the total per line.

### Scheduled hours vs actual hours per day (chart)

- Line chart of scheduled hours (visit length) and actual hours (submitted employee time) per day, for the same month.
- A visit without a valid submitted time adds no actual hours.
- Has its own month switcher, which moves both charts.
- Shows "No hours in (month)." when the month has none.

### Scheduled hours per employee, by customer

- One donut per active employee for one week: scheduled hours split by customer, and the rest of a full-time week shown as free time.
- The centre shows the employee's scheduled hours. Below: "X of Y h", marked "over" when the week is above full time, and a list of customers with hours.
- **Previous week**, **This week** and **Next week** buttons. The heading shows the week's dates and the full-time hours.
- Shows "No visits this week" for an employee without visits, and "No active employees." when there are none.
- The donut row scrolls sideways when there are many employees.

### Shortcuts

Cards that link to the main pages, in three groups:

- Operations: Shifts ("Create and assign recurring shifts across company branches."), Quality Cycle ("Manage quality cycles, checklists and service follow-up.")
- Customers: Leads, Companies, Agreements
- Business administration: Employees, Invoices, Services

### Errors and loading

- Charts: "Loading charts…" while loading, and "Charts could not be loaded. Refresh to try again." on failure. The weekly donuts still load on their own when the charts fail.
- Weekly donuts: "Loading weekly hours…" and "Weekly hours could not be loaded. Refresh to try again."

### Not on the dashboard

- The "Today" figures (visits in progress now, scheduled visits today, scheduled hours today) are a component that the Shifts page uses, not the dashboard.

## Shifts

Route: `/admin/shifts/`. Source: `site/src/app/admin/shifts/page.tsx`, `site/src/components/admin/ShiftWorkspace.tsx`, `AddVisitDialog.tsx`, `TodayKpis.tsx`, `site/src/components/ShiftPricingDialog.tsx`, `VisitAssigneesDialog.tsx`, `LifecycleTabs.tsx`. The redesign calls a shift a **work order**.

A shift belongs to one company and one branch (property). A property has one main shift; partner bookings and added visits become its numbered visits. Times use the tenant time zone, which the page shows.

### Page

- **Add shift** (disabled until scheduling data has loaded).
- **Today** figures: visits in progress now, scheduled visits today, scheduled hours today. Each loads on its own and shows "Could not load" on failure.
- Filters: **Filter by company** (searchable) and **Filter by shift type** (the seven schedule types). Changing a filter returns to page 1.
- Tabs: **Active**, **Completed** and **Inactive**, each with a count.
- Empty states: "No shifts yet." or "No (tab) shifts. Check the (other) tab."
- Pages of results with a pager.

### Shift list

Columns: Name, Schedule, Employee names, Status, actions.

- Name shows the property address, with chips for size (m²), rooms and beds.
- Employee names: the assigned employees, "None", or "N assigned".
- Status: Active, Completed, Inactive, or "Past visit · check completion" for a one-time shift whose date has passed.
- Clicking a row (or Enter or Space on it) expands it to show its **Visits**:
  - A **Month** dropdown chooses the month shown.
  - The visit list can be collapsed and shows a count.
  - Each visit: visit name, start, cleaning type (Normal or Deep), employee (or "Offer sent to …", or "Unassigned"), status.
  - States: "Loading visits…", an error message, "No visits in this month."

### Row actions (More menu)

| Action | Who | When | What it does |
| --- | --- | --- | --- |
| View details | all | always | Opens the shift workspace (below) |
| Pricing | admin | not a main shift | Opens the pricing dialog |
| Edit schedule | admin | always | Opens the shift form in edit mode |
| Assign employees | all | active, not a main shift | Opens the assignment dialog |
| Assign employees | all | active main shift | Sets the default employee for new visits |
| Visits | all | active | Opens the visits dialog |
| Add visit | all | active main shift, or active on-call shift | Opens the add-visit dialog |
| Checklist template | admin | active | Assigns a checklist template |
| Deactivate | all | active | Confirms, then stops new visits; can be reactivated |
| Reactivate | all | inactive | Confirms, then returns it to Active |
| Delete | all | inactive | Confirms, then deletes it and its assignments; records, photos and reports stay |

### Add shift and Edit schedule (form)

- Company (active companies; an inactive company only when editing a shift that has one) and **Branch** (property).
- Name is generated from the property (cleaning type, address, size, rooms and beds). It is shown read-only when editing.
- **Price version** (when the property has more than one): defaults to the price in force today.
- **Price for this shift**: Property price, Monthly fee, or Price per completed visit. The last two need a price excluding VAT and a VAT rate.
- **Start date** (required) and **End date** (optional, not before the start), except for one-time shifts.
- **Schedule** type and its fields:
  - Daily: start and end time.
  - Varied times by weekday: days, then a start and end time for each day.
  - Specific days: days, start and end time.
  - Bi-weekly: week parity (Week 1 or Week 2), day, start and end time.
  - On-call / flexible: no dates; visits are added one by one later.
  - Monthly: day of month (1–31), start and end time.
  - One-time: date, start time and end time (only kept when editing an existing one-time shift; new one-time work is an added visit).
- Notes, and a **Quality cycle template**.
- Validation messages: schedule type required; one-time date and times required, end after start; start date required; end date on or after start date; company and branch required; at least one day for specific days; each varied day needs valid times; a price above zero and VAT 0–100 for a shift price.

### Shift workspace (View details)

Side panel showing the shift's status, schedule, assignees, checklist template, pricing and visits, with a "Next step" hint (for example, on-call shifts need visits scheduled by hand). Changes are made from the row's More menu.

### Assign employees (regular shift)

- Multi-select of active employees not already assigned, then **Assign**.
- **Refresh assignments**.
- Overlapping shifts are checked on assign, and conflicts are shown.
- Assigned employees are listed, each with an **Unassign** button.

### Default employee (main shift)

- Choose the employee that new visits are offered to, or none (new visits start unassigned). Visits already added keep their employee.

### Visits dialog

- **From** and **To** dates, then **Load**. Changes apply to the selected visit only.
- **Add visit**.
- For a main shift: **Default employee** for new visits.
- Each visit shows start, end, status, assignees (with the offer state: partner booking, waiting for the employee to accept, accepted, or no employee yet), reason, and an overlap flag.
- Visit actions, depending on the visit:
  - **Pricing** for one visit (per-visit priced, on-call or one-time shifts).
  - **Change employee** (main shift): offer the visit to an employee, or leave it unassigned. An employee who held it is released.
  - **Change cleaning type**: Normal or Deep. Deep adds €15.00 excl. VAT. Allowed at any time, including after the cleaning.
  - **Assign** (regular shift): see Assign visit.
  - **Change**: move or change the time, with start, end, an optional replacement employee (regular shifts), a required reason and optional notes for the employee.
  - **Cancel**: a recurring visit by customer or company (with reason); an extra visit through the Cancel visit dialog.
  - **Restore** a changed or cancelled visit, or **Delete visit** for an extra visit.
  - Partner bookings: managed under Partner requests; only the cleaning type can be changed here.
  - A cancelled manual visit stays listed with **Restore**.

### Add visit

Property, date, last day (optional, for several days), start and end time, cleaning type (Normal or Deep), employee (the shift's default employee, unassigned, or a chosen employee who gets an offer), and notes for the employee.

### Assign visit

- Apply to: this occurrence only, or from this date onward.
- Action: replace regular assignees, add extra employees, or restore the recurring assignment (this occurrence only).
- Employees and a reason.
- **Preview assignment** shows the result first, including overlaps with other shifts (each with a link to the clashing shift). **Save assignment** applies it.

### Cancel visit

Cancelled by (customer or company) and a reason. The visit leaves the employee's schedule and is not invoiced; it stays in the list so it can be restored.

### Pricing dialog

Pricing basis (fixed monthly fee, per visit, or property price), net rate, VAT, effective from, the property price version, a reason when a single visit's price is overridden, and the property's price history.

### Checklist template

Choose a checklist template for the shift's company and branch, or none. It applies to upcoming visits and to the current visit if it has no checklist yet; started and submitted checklists keep their items.

## Bookings

Route: `/admin/bookings/`. Source: `site/src/app/admin/bookings/page.tsx`.

Bookings made by customers on the public booking page.

### Page

- **Filter by status**: All statuses, New, Confirmed, Completed, Cancelled.
- View switch: **Bookings** (the list) or **Day view**.
- A success message after each change, for example "B-1042 is now Confirmed.", "B-1042 assigned to Anna K." or "B-1042 is unassigned."
- Errors: "Bookings could not be loaded. Reload and retry." and "Employees could not be loaded, so bookings cannot be assigned. Reload and retry."

### Bookings list

Columns: Booking (number), When (date and time), Customer, Service (Finnish name), Status (New, Confirmed, Completed or Cancelled), Employee (or "Unassigned"), and a **Details** button.

- Empty state: "No bookings." or "No bookings with status (status)."

### Booking details (dialog)

- Status, when, customer (name, phone, email), service and its length in minutes, employee.
- **History**: each change with its time (UTC), status, who made it and a note. "History could not be loaded." on failure.
- Status actions (admin only), depending on the status:
  - New: **Confirm** or **Cancel booking**.
  - Confirmed: **Mark completed** or **Cancel booking**.
  - Completed or Cancelled: no actions; "This booking is (status). No further changes are available."
- Assignment (admin only, while New or Confirmed): choose an active employee and **Assign**; **Unassign** when someone is assigned.

### Day view

- Choose a **Day** and an **Employee** to see that employee's bookings that day.
- Columns: Time, Booking, Customer, Service, Status.
- States: "Choose a day and an employee to see their bookings.", "No bookings for this employee on (date).", and "The day schedule could not be loaded. Reload and retry."

## Shift calendar

Route: `/admin/shifts/calendar/`. Source: `site/src/app/admin/shifts/calendar/page.tsx`, `site/src/lib/adminShiftCalendarApi.ts`. Background: `docs/features/admin-shift-calendar.md`.

A one-day view of every visit, used to check work and to record work times for past visits.

### Access

- Only admins with shift calendar access enabled (set on the Employees page) can use it. Others see an access message and no calendar.

### Day controls

- **Previous day**, a **Calendar date** picker, **Next day**, **Today** and **Refresh**.
- "Times shown in (time zone). Work times can be recorded for past dates only."
- Review summary: "No visits need review on this day." or "N visit(s) need review."
- **Show only visits that need review** checkbox.
- Messages: "Work times saved." and "Photos added to the visit." An error shows with **Retry**.
- Empty states: "No shifts on this day." and "Turn off the review filter to see every visit."

### Visit cards

Sorted by start time. Each card shows:

- Shift name, company and branch, scheduled start and end, a **Cancelled** badge when cancelled.
- "Overlapping visits require review before work times can be recorded." for visits that cannot be told apart.
- An **Unassigned** badge when nobody is assigned.
- For each assigned employee: name, work status and hours, actual start and end ("Not ended" while running), and a review badge (**Missing completion** or **Unusual time**) with its explanation.
- Per employee: **Record work** (or **Correct times** when times exist), shown only for past visits where recording is allowed; and **Add photos** once a work record exists.

### Record work / Correct work times (dialog)

- Employee and shift, the scheduled times, and the review explanation when there is one.
- **Actual start** and **Actual end** (prefilled with the scheduled times for new records), the calculated work duration, and a required **Reason**.
- Saving records the past visit as completed; checklist evidence is kept. Errors: "Enter both times and a reason." and "End must be after start."

### Add photos to this visit (dialog)

- For photos the employee sent outside the app.
- Choose one or more photos (PNG, JPEG, WebP or BMP); each needs its own note. A required reason is kept with the visit.
- Errors: "Choose at least one photo.", a missing note, and "Enter a reason."

## Employee schedule

Route: `/admin/shifts/employee-schedule/`. Source: `site/src/app/admin/shifts/employee-schedule/page.tsx`.

A calendar of every employee's visits, where visits can be moved by dragging.

### Controls

- View: **Day** (one column per chosen employee) or **Week** (one employee, one column per day).
- **Previous day / Today / Next day**, or **Previous week / This week / Next week** in week view.
- **Date** picker (day view) or **Week starting** picker (week view; any date jumps to that week's Monday).
- **Employees** (day view, several) or **Employee** (week view, one), and **Show all employees**.
- **Show 24 h** (wide screens): the grid normally runs 08:00–17:00, widened to fit earlier or later visits; this shows the whole day.
- **Show weekends** (week view).
- Hint on wide screens: "Drag a visit to change its time or employee" (day view) or "…or day" (week view).

### Grid

- Each column header: the employee (or day), their working hours for that day ("08:00–16:00" or "No hours set"), and the number of visits.
- Visits sit at their time, sized by length. Overlapping visits share the column side by side.
- Each visit shows its status: Scheduled, In progress, Completed or Needs action, and an overlap mark when the overlap audit finds a double booking for that employee.
- On phones the grid becomes a list per column ("No visits" when empty).

### Drag and drop (wide screens)

- Drag a visit up or down to change its time; drops snap to 15 minutes. A marker shows the drop time.
- In day view, drop on another employee's column to reassign the visit. In week view, drop on another day.
- A visit cannot be moved when it is in progress or completed, or when it is locked. A main-shift visit changes employee through its offer instead, so it cannot be dropped on another employee who is not already assigned.
- Each drop saves at once with a recorded reason. The message says what changed, with **Undo**; **×** dismisses it.

### Visit details

Clicking a visit opens its details (a slide-over on wide screens, a bottom sheet on phones; Escape closes):

- Status, an **Overlap** badge, shift name, company and branch.
- Visit date and time, visit number, employee, duration, site address.
- Checklist progress ("N of M done", or "No checklist yet").
- A warning when it overlaps another visit for the same employee.
- Visit notes.
- **Open in shift calendar** (for admins with calendar access).

### States

- "Could not load the schedule. Check your connection, then retry." with **Retry**; a separate error with **Reload**.
- "No active employees yet. Add employees, then their visits appear here."
- "No visits this week. Choose another week or check the shift schedules."
- "Choose at least one employee to see their visits."

## Work queue

Route: `/admin/work-queue/`. Source: `site/src/app/admin/work-queue/page.tsx`.

One list of all work from public bookings, partner requests and company shifts. Every filter is kept in the page address, so a filtered view can be shared or bookmarked; the dashboard tiles link here.

### Views (tabs)

**Needs action**, **Scheduled**, **In progress**, **Completed** and **Cancelled**, each with its count.

### Filters

- **Source**: All sources, Public bookings, Partner requests, Company shifts.
- **Search**: customer, booking number or company.
- **From** and **To** dates. An invalid or reversed range is ignored.
- **Assignee**: Anyone, Unassigned, or an employee. With an employee chosen, partner requests (which have no assignee) are hidden, and the page says so.
- The first view shows overdue and upcoming work: from 180 days back to 14 days ahead in the tenant time zone. The page says "Showing overdue and upcoming work (from to to)" with a **Show all work** button that clears the dates and the assignee.
- Changing a filter returns to page 1.

### Work items

Each item shows the customer, the reference and scheduled time, a state badge (for example "Not started" or "Overdue · not started", highlighted), an **Unassigned** badge for overdue unassigned shift visits, the source (Public booking, Company shift or Partner request) and the assignee.

- **Open**: a public booking opens a details drawer; a company shift opens the shift; a partner request opens Partner operations.
- Empty states: "Nothing in this view." or "Nothing in this view matches (search)."
- Errors show with **Retry**.
- Pages of 25: "Showing 1–25 of N", **Previous**, "Page X of Y", **Next**.

### Booking drawer

- Customer, contact (phone and email), service and length, start, status, assigned employee.
- Actions by status, as on the Bookings page: New offers **Confirm** and **Cancel booking**; Confirmed offers **Mark completed** and **Cancel booking**. Cancelling asks for confirmation first.
- "This booking could not be loaded. Retry." and "The action could not be completed. Refresh and check the booking status."

## Partner operations

Route: `/admin/partner-requests/` (Quote requests tab: `?tab=quotes`). Source: `site/src/components/PartnerOperations.tsx`, `PartnerKeyRequestsStaff.tsx`, `QuoteRequests.tsx`. Managers use the same page.

Work for partner companies: pricing their properties, arranging one-time cleanings, handling their change requests and key requests. Only companies with partner access are included. Times use the tenant time zone.

### Page

- Title, a **Refresh** button, and "Price properties, arrange one-time cleaning and handle owner change requests."
- Status lines: "Saving changes…", "Loading partner operations…", the result of the last action, or an error.
- Tabs: **Requests**, **Assigned**, **Completed requests**, **Cancelled requests**, **Quote requests**, **Changes**, **Key requests**, **Properties**. Requests, Quote requests and Changes show a count of items needing action (the same counts as the sidebar badge).
- The Requests tab explains that its badge counts requests needing arrangement, while the list also includes offers waiting for an employee's answer.

### Request lists (Requests, Assigned, Completed requests, Cancelled requests)

- Requests: waiting for an employee (including offers sent). Assigned: accepted or in progress. Completed and Cancelled: closed.
- Each request: property and status, the date window (and fixed start time if set), price excl. VAT, VAT and estimated duration, "Deep cleaning · adds €15.00" when deep, the employee ("Not accepted" until accepted), and the owner's notes.
- Actions while not started, completed or cancelled:
  - **Arrange employee / time**: choose an employee (shown with whether they accept automatically) and an optional planned start inside the window. If someone already accepted, the dialog warns that a new offer removes them. **Send cleaning offer** gives "Work scheduled automatically." or "Offer sent. Waiting for employee…".
  - **Edit / reschedule request**: from date, to date, an optional exact start time (single date only) and cleaning instructions. Changes cancel outstanding offers and need the employee to accept again; started or billed work is protected.
  - **Cancel request**: a required reason. The offer and any scheduled visit are removed and the owner is notified.
- Each list is paged. Empty states: "No requests waiting for an employee.", "No assigned visits.", "No completed requests.", "No cancelled requests."

### Quote requests

- Properties waiting for a price, oldest first: property, company and how long it has waited.
- **Set cleaning price**: fixed price excl. VAT, VAT and estimated duration in minutes. New requests use the new price; existing requests keep their agreed price.
- **Reject quote**: a required reason, which the owner sees; the property leaves the queue.
- Empty state: "No quotes awaiting pricing."

### Changes

- Owner change requests, each with the property, the message, the requested change (for a reschedule: new dates, time and notes) and, when resolved, the resolution.
- **Review and apply** (for a structured change) or **Resolve change request** (for a message): a required resolution text. A structured change is applied to the request as the staff saw it; if the request has changed since, the page asks to reload.
- Paged. Empty state: "No change requests."

### Key requests

- **Fee per key request, excl. VAT**, with **Save fee**. New requests use the new fee. The current fee and VAT are shown.
- Each key request: property, Key handover or Key pickup, date, status, fee, assigned employee, notes and cancellation reason.
- While open: choose who collects or hands over the key, **Assign**, and **Mark completed** (after assignment).
- Once assigned: add photos.
- Empty state: "No key requests yet."

### Properties

- **Add property** and a list of partner properties: name, address, an Inactive badge, and the price (or that it needs a price).
- **Edit property** and **Set cleaning price** per property.
- Property form: company (new properties only), property name (required), street, postal code, city, country, contact phone, contact email, rooms and beds (optional), and **Active property**.

## Quality Cycle

Route: `/admin/quality-cycle/`. Source: `site/src/app/admin/quality-cycle/page.tsx`, `ReportWorkspace.tsx`. Admins only; others see "Only admins can manage checklist templates and view reports."

"Create checklist templates and prepare, export or share client quality reports." A switch chooses **Checklist Templates** or **Reports**.

### Checklist templates

- **New template**.
- **Existing Templates** with **Active** and **Inactive** tabs and counts.
- Each template: title, description, and its checklist items (count and list).
- Per template: **Edit**; **Deactivate** (active) or **Reactivate** (inactive); **Delete** (inactive only, after a confirmation: templates used by a shift or checklist history are kept).
- States: "Loading templates…", "No quality cycle templates created yet.", "No (status) checklist templates. Select (other) to view your other templates."

### Template editor (dialog)

- **Template Title** (required), **Description** (optional).
- **Checklist Items (up to 10)**: one field per item, **Add Item**, and a remove button per item (when there is more than one).
- **Create Template** or **Update Template**, and **Cancel**.

### Reports: build a report

- **Shifts**: choose up to 50 (inactive ones are marked).
- Period: **Months** (a year and up to 12 months) or **Date range** (from and to, inclusive).
- **Report language**: English, Finnish or Swedish.
- **Load report**.

### Reports: loaded report

- The periods and time zone, and how hours are counted: scheduled hours count each visit once; actual hours sum each employee's submitted work time, without incomplete timings.
- **Export all selected shifts** (PDF download) and **Share all selected shifts**.
- A table per shift: company, branch, shift, scheduled visits, completed visits, scheduled hours, actual hours (with the number of incomplete records), and **Export shift** and **Share shift**.
- "No reportable visits for these shifts and dates."

### Share quality report (dialog)

- Sends the chosen shifts as one report; the link expires in 30 days.
- **Recipient source**: a known contact for these shifts, or **Other email address**; **Recipient email**.
- **PDF and email language**: English, Finnish or Swedish.
- **Send report**.

### Shared reports

- **Refresh shared reports**.
- Links expire after 30 days and include report photos and notes. Acknowledgment records receipt through the link, not a signature.
- Each share: recipient (or "Existing report link"), a status badge (Acknowledged, Email failed, Email sent or Link created), an Inactive badge when revoked or expired, the period, language and expiry, when the email provider accepted it, when it was acknowledged, and any email error.
- Actions while active: **Open report**, **Retry email** (when the email was not sent) and **Revoke**.

## Leads

Route: `/admin/leads/`. Source: `site/src/app/admin/leads/page.tsx` and `site/src/components/leads/` (`LeadBranchesEditor`, `LeadCollaborationPanel`, `VoiceRecorder`). Managers have a separate, smaller leads page at `/manager/leads/` (not part of the office portal).

### Lead list

- **New lead**.
- **Search leads**: company, business ID or email (up to 200 characters; results update as you type).
- **Lead filter**: Active (default), Inactive, Converted, New, Contacted, In discussion or Lost.
- Table: company (with business ID), branches (names, or "No branches"), contact (primary email or company phone, and how many email addresses), status badge, images (n/3), and **View lead**.
- Paging of 25 with **Previous** and **Next** and "1–25 of n".
- States: loading, "No leads in this view.", and a load error with **Retry loading leads**.
- A link with `?lead=<id>` opens that lead directly.

### Lead dialog (create, view and edit)

The title is the company name, or "New lead". Closing with unsaved changes, an unsaved recording or an unsaved follow-up asks for confirmation. The close button is hidden while something is saving.

- Load error with **Retry loading lead**; error and success messages at the top.
- **Converted lead:** a read-only notice, with **Open customer** (to the company) or "The converted customer or branch is no longer available."
- **Company details:**
  - **Company name** (required).
  - **Business ID** (required for conversion).
  - **Contact name** and **Company phone**.
  - **Contact email addresses**: one per line, up to 20.
  - **Primary company email**: chosen from those addresses.
  - **Lead status**: New, Contacted, In discussion or Lost.
  - **Company notes** (up to 5,000 characters) and **Description** (up to 10,000).
- **Branches (n):**
  - **Add branch**.
  - Each branch has name (required), street, postal code, city, country (default FI), phone and email, plus **Remove branch n** (with a confirmation).
  - Branches are shown five at a time, with **Previous branches** and **Next branches**.
  - A lead can be saved and converted without a branch.
- **Save lead** (a saved lead may also show a business ID warning) and **Reload lead** (asks before discarding changes).
- **Images (n/3):** JPEG, PNG or WebP up to 5 MiB each, private to tenant admins and managers.
  - **View images**: open an image full size, or **Retry image** when it fails to load.
  - **Manage images**: upload one or more (up to 3 in total, with upload progress) and **Remove image** (with a confirmation).
  - The lead must be saved and active before images can be managed. Partial upload failures keep the images already uploaded.
- **Lifecycle** (saved, not converted, no unsaved changes):
  - **Deactivate** or **Reactivate**.
  - **Convert to customer** (active leads).
  - **Delete lead** (inactive leads; permanent, after a confirmation).

### Voice notes (in the lead dialog)

- **Refresh notes and follow-ups**.
- **Record voice note**: asks for microphone access, records up to 5 minutes (shown as a timer) and **Stop recording**. Then preview it, and **Save recording** or **Discard recording**. A failed save keeps the recording so it can be retried.
- Each note: author, date and time with time zone, length, **Load audio** (then play) or **Retry audio**, and **Delete voice note** (admins, not converted; with a confirmation).
- Paged by 25.
- Recording is disabled for lost, inactive or converted leads. Clear messages cover an unsupported browser, a denied microphone and a recording over 20 MiB.

### Follow-ups (in the lead dialog)

- Reminder emails are sent at 06:00 in the tenant time zone. Lost, inactive or converted leads stop future reminders.
- **Add follow-up** opens a form:
  - **Follow-up reason** (required, up to 1,000 characters).
  - **First reminder date** (defaults to tomorrow).
  - **Repeat follow-up**: Once, Daily, Weekly or Monthly. When it repeats: **Repeat every** (1–365) and **Last reminder date** (optional).
  - **Notify tenant admins**: one or more admins, searchable; **Reload admin recipients**.
  - **Save follow-up** and **Discard follow-up**.
- Each follow-up: reason, state badge, repeat rule, next date, number of recipients, author, last error and last checked time. Active follow-ups have **Edit follow-up** and **Cancel follow-up** (with a confirmation).
- Paged by 25.

### Notification history (in the lead dialog)

- Each email: recipient and address, scheduled time and time zone, state badge, sent time, error and number of attempts.
- "Sent" means the email provider accepted it; "Needs review" means acceptance could not be confirmed.
- Paged by 25.

### Convert lead to customer (dialog)

- Creates a company and all of its added branches (zero branches is allowed). The business ID is required.
- The primary email becomes the company contact email. The other emails, the description and the images stay on the linked lead.
- Shows the company name, business ID, the branches that will be created (read-only) and the primary email.
- **Cancel** and **Confirm conversion**.

## Companies

Route: `/admin/companies/`. Source: `site/src/app/admin/companies/page.tsx`, `site/src/components/admin/BranchPriceHistory.tsx`, `site/src/components/QuoteRequests.tsx`. In the app a company's places are called branches; the customer workspace and the redesign call them properties.

### Company list

- **Add company**.
- **Search companies**: name or business ID (searches on Enter).
- **Active** and **Inactive** tabs with counts.
- Table: name, business ID, contact, **Branches** (expands the company's branches), **Deactivate** or **Reactivate**, and actions: **Open** (customer workspace), **Edit**, and **Delete** (inactive companies only).
- **Companies per page**: 25, 50 or 100; pagination with first, previous, next and last page.
- Filter, page, page size and search are kept in the URL, so Back restores the list. `?company=<id>` opens that company with its branches expanded, even when it is on another page.
- States: loading; "Companies could not be loaded." with **Refresh companies**; "No companies yet. Add one to get started."; "No companies match this search."; "No (active or inactive) companies."; and action errors.

### Branches (expanded under a company)

- **Add branch**.
- **Active** and **Inactive** tabs with counts.
- Each branch: name, city, price excl. VAT, a **Partner booking ready** badge (when priced), a **Quote requested** badge (a partner asked for a price), and an Inactive badge.
- Per branch: **Edit**, **Deactivate** or **Reactivate**, and **Delete** (inactive only, after a confirmation; branches with shift or checklist history are kept).
- States: loading, "Branches could not be loaded." with **Refresh branches**, "No branches yet.", "No (status) branches."

### Add or edit company (dialog)

- **Enable partner access**: partners linked to the company can see its properties and request cleanings.
- When partner access is on: **Quality cycle** (every accepted partner request gets the quality cycle checklist), and then **Quality cycle template** (required; active, company-wide templates only).
- **Business ID** and **Name** (both required), **Contact name**, **Contact email**, **Contact phone** and **Notes**.
- **Cancel** and **Create company** or **Save changes**.

### Add or edit branch (dialog)

- **Name** (required), **Street**, **Postal code**, **City**, **Country**, **Contact email** and **Phone**.
- **Rooms** and **Beds** (optional); **Size in m²** (needed before visits can be scheduled).
- **Price effective from** (blank means today).
- **Pricing basis**: Per completed visit, Fixed monthly fee or Per scheduled hour.
- **Fixed cleaning price, excl. VAT (€)**: left blank until a quote is agreed. New requests use the new price; existing requests keep their agreed price.
- **VAT (%)** (default 25.5) and **Estimated cleaning duration (minutes)**.
- When editing: **Price history** with each period and its price.
- **Cancel** and **Add branch** or **Save changes**.

### Deactivating a company that still has work

When a company cannot be deactivated because of its own shifts or branches, a dialog **Clear related shifts and places?** offers to clear them first:

- It deletes the company's branches and shifts, unassigns every employee, removes its partner requests and voids its open invoices. Checklist submissions are kept. Paid invoices stop the clear (a credit note is needed first).
- **Also delete this company's signed agreements** (off by default).
- **Delete related shifts, unassign employees and clear all places**, or **Skip deletion**.

### Remove company (dialog)

Opens when deleting an inactive company is refused because it has records:

- Tick what to remove; anything not ticked is kept:
  - **Places (n)**: removing places also removes their shifts.
  - **Shifts (n, m with completed checklists)**: unassigns employees.
  - **Invoices (n, m paid)**.
- Ticking all three also removes the company record and its signed agreements, checklist submissions and partner requests. Partner accounts are deactivated. Templates and leads are kept and unlinked.
- Removing paid invoices needs the company name typed exactly.
- **Remove selected** and **Cancel**.

## Customer workspace

Route: `/admin/companies/customer/?id=<company>&section=<tab>`. Source: `site/src/app/admin/companies/customer/page.tsx`. Opened with **Open** in the company list.

- **Back to customers** (returns to the list with the same filter, page and search).
- Header: company name, business ID, an Active or Inactive badge, and a Partner access badge.
- Tabs, kept in the URL so Back and Forward work:
  - **Overview**: contact (name, email, phone), partner access, number of active and inactive properties, and **Edit in companies list**.
  - **Properties**: each property with city and an Inactive badge, and **Manage properties** (to the company list).
  - **Agreements**: title, code, signed and total signers, and status. **New agreement** opens the agreements page for this company.
  - **Work**: the customer's shifts, with **Active** and **Inactive** tabs and counts. Columns: name, property, status (Completed, Active or Inactive), valid from and valid until. **Add shift** opens the shifts page for this company.
  - **Invoices**: invoice number, issue date, due date, status and total. **Open invoices** goes to the invoices page.
- Each tab loads when opened and has its own error and **Retry** button and empty text.
- "Customer not found" for an unknown or unavailable company; "Customer could not be loaded." with **Retry customer**.

## Agreements

Route: `/admin/agreements/`. Source: `site/src/app/admin/agreements/page.tsx`, `site/src/components/AgreementEmailCopyButton.tsx`, `site/src/components/admin/CompanyPicker.tsx`. Agreements are PDF contracts that customer contacts sign online through a personal signing link.

### Agreement list

- **New agreement**. `?company=<id>` (from the customer workspace) opens the form with that company already chosen.
- **Active** and **Inactive** tabs with counts.
- Table: title, customer, code, signers (signed / total), status badge (Draft, PartiallySigned, Completed or Cancelled), and actions:
  - **Signers** (opens the signers dialog).
  - **PDF** (downloads the document).
  - **Deactivate** or **Reactivate**. A cancelled agreement cannot be reactivated, and says so.
  - **Delete** (inactive only). Deleting an agreement that has signatures warns that its signing and download links will stop working. Both cases ask for confirmation and cannot be undone.
- States: loading, "No agreements yet.", "No (status) agreements. Check the (other) tab.", and action errors.

### Legacy agreements needing a company link

Shown only while there are older unsigned agreements without a company.

- Table: agreement, status, a searchable **Company** choice (inactive companies are marked), and **Apply reviewed link**.
- Signed agreements are excluded.
- States: loading, an error, and "No eligible legacy agreements need repair."

### New agreement (dialog)

- **Company** (required; searchable by name or business ID).
- **Title** (required).
- **PDF document** (required, one PDF file).
- **Signers**: name and email per signer, **Add signer**, and a remove button per signer. At least one complete signer is required.
- **Cancel** and **Create**.

### Agreement created (dialog)

- Confirms the agreement was created and lists each signer with their signing actions (below), and **Close**.

### Signers (dialog)

- Code and status.
- Signers (n/m signed). Each signer has a name, email and a Signed or Pending badge. A pending signer can be removed while the agreement is open, after a confirmation (their link stops working).
- Signing actions per signer:
  - **Copy link**: copies the signing link.
  - **Copy email**: copies a formatted email with the link.
  - **Open mail app**: opens a plain-text email in the mail app.
- While the agreement is a draft or partly signed: **Add signer** (name and email), and **Cancel agreement** (after a confirmation; signers can no longer open or sign it, and signed copies stay on record).
- Otherwise: "Signers cannot be changed on a (status) agreement."
- Errors for loading, adding, removing and refreshing.

## Employees

Route: `/admin/employees/`. Source: `site/src/app/admin/employees/page.tsx`. Managers can see the list; every change needs an admin.

### Employee list

- **Add employee** (admins).
- **Active** and **Inactive** tabs with counts.
- Table: name, email, role (admin, manager, employee or partner; partners also show their company), status (Active or Inactive), registration (Invited or Not invited), and actions (admins):
  - **Edit**.
  - **Invite** (not yet invited) or **Resend setup email**: sends an account setup email. If one was sent recently, it says so instead.
  - **Enable shift calendar** or **Disable shift calendar** (active admins only): controls whether that admin sees the shift calendar.
  - **Deactivate** (active). It is refused, with the reason, when the employee still has active work.
  - **Reactivate** (inactive): keeps the existing account and history. If reactivation fails or is only half done, **Retry reactivation** appears.
  - **Delete** (inactive; after a confirmation). Profiles with a login or work history are kept.
- Paged on the page, 25 per page: "Showing 1–25 of n employees", **Previous page**, "Page x of y", **Next page**.
- Success and error messages above the list. States: loading, a load error, "No employees yet.", "No (status) employees. Check the (other) tab."

### Add or edit employee (dialog)

- **Email** (required). It is locked once the profile is linked to a sign-in account.
- **First name** and **Last name** (required), **Phone**.
- **Role**: admin, manager, employee or partner.
- **Partner company** (partners only, required): only active companies with partner access are offered.
- When editing, also:
  - **Skills** and **Service areas** (tags).
  - **Colour** (a hex value such as #1a2b3c; used on the calendars).
  - **Pay rate** (zero or more) and **Notes**.
  - **Certifications**: name and expiry date per certification, **Add certification** and **Remove**.
  - **Default hours**: a start and end time for each day from Monday to Sunday. Both or neither must be filled, and the end must be after the start.
- **Cancel** and **Create employee** or **Save changes**.
