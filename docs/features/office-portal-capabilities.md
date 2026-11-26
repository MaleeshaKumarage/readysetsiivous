# Office portal capabilities

What an office user (tenant admin, or manager where noted) can do on each page of the office portal at `/admin/`. This is an inventory of the current application, taken from the code on `main` (commit `cac82a9b`). It is the reference for the office portal redesign: every capability listed here must have a place in the new design.

Pages are documented one at a time. A page not listed yet has not been inventoried.

| Page | Route | Status |
| --- | --- | --- |
| Office shell (every page) | all `/admin/` routes | Inventoried |
| Dashboard | `/admin/` | Inventoried |

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
