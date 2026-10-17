# Shift start and end dates

New shifts require a start date in admin and manager creation flows and in the API. The form defaults to today's date in the tenant timezone; the user can change it. The end date is optional. An end date before the start date is rejected; the same date is allowed.

Dates are calendar boundaries, not UTC instants. Existing occurrence generation includes both boundary dates and uses the tenant timezone for scheduled times. No shift occurrences are generated before the start or after the end date.

Admin and manager edit forms display and save the existing date range. Legacy records without a start date remain editable without silently backfilling or changing their original range. New-shift validation does not rewrite existing shifts or completed QC history.

An existing start date cannot be cleared through the edit form. An API update omitting the start date preserves its loaded value. Creation waits for validated tenant scheduling settings, so a missing/invalid timezone cannot silently use the browser's date.

Regression coverage: required API start date, end-date ordering/same-day acceptance, desktop/mobile creation with an open end, edit persistence and a Helsinki date default after UTC midnight divergence.
