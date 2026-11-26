# Office portal capabilities

What an office user (tenant admin, or manager where noted) can do on each page of the office portal at `/admin/`. This is an inventory of the current application, taken from the code on `main` (commit `cac82a9b`). It is the reference for the office portal redesign: every capability listed here must have a place in the new design.

Pages are documented one at a time. A page not listed yet has not been inventoried.

| Page | Route | Status |
| --- | --- | --- |
| Office shell (every page) | all `/admin/` routes | Inventoried |
| Dashboard | `/admin/` | Inventoried |
| Shifts (work orders in the redesign) | `/admin/shifts/` | Inventoried |

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
