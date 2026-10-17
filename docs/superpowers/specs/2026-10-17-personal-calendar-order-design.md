# Personal calendar ordering and date labels

Approved by the user in chat. Past dates sort descending, future dates ascending: the dates closest to today appear first. Apply the same direction to weeks, days and visits within a day. Preserve tenant-local date grouping, pagination, and the single expanded week/day behavior.

Day buttons display `DD/MM/YYYY Weekday` (for example `02/10/2026 Friday`) and a separate count chip (`1 shift` or `2 shifts`). The complete accessible label retains both date and count. No API or stored timestamp changes.

Verification: existing calendar Playwright scenarios on desktop and mobile, with updated date labels and explicit future ordering/count-chip assertions; TypeScript check.
