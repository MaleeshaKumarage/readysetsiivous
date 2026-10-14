# Shift employee assignment feedback

Admin and tenant manager assignment screens notify scheduling conflicts. The API
checks recurring-shift overlap when the assignment is submitted and returns 409
with an explanation. The UI does not claim that every listed employee is available.
Inactive employees and employees already assigned to the current shift are omitted.

Admin multi-selection submits employees separately. Successful assignments remain
saved; a failure does not roll them back. The screen reports the success count and
names each failed employee with the API explanation. Failed choices remain selected
for correction/retry. A fresh roster reconciles uncertain network responses that
actually saved. Mutations lock selection, row navigation and deactivation until
completion. A failed roster refresh blocks further changes until a successful refresh.
Removal errors preserve the current roster and show a retryable error.

Assignments are read fresh, without the generic admin GET cache. Late responses from
closed/switched rows cannot change the current row. Failed reads are visibly different
from a valid empty roster. Other admin API client boolean contracts are unchanged.

## Playwright cases

`shift-assignment.spec.ts` runs on desktop/mobile: overlap detail and retained
selection; server/HTML/network errors; multi-employee partial success; inactive and
duplicate filtering; removal then re-selection; failed roster recovery; removal
permission error; lost response after successful save; post-save refresh failure;
pending navigation lock; and stale roster response suppression. `manager.spec.ts`
verifies overlap notification, retained choice and inactive employee omission.

The existing server conflict policy checks up to 180 days, anchored to the later of
today or shift validity start and capped by validity end. This fix preserves that
policy. Future pre-selection availability filtering must use the same server policy
rather than a separate frontend recurrence calculator.
