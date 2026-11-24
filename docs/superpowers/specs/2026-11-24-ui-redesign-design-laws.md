# UI redesign: design laws as the review standard

## Purpose

Every screen touched by the UI redesign is reviewed against the 20 design laws below. A law is a checklist item, not a visual style: each one maps to a concrete rule for ReadySetSiivous screens and to something a reviewer can check. This doc applies to all surfaces (admin, manager, partner, employee, public site, quality reports) and sits beside the per-surface design specs in this folder, which still own colour tokens, typography and layout.

## How to use it

1. For each screen in a redesign PR, go through the 20 laws and note where the screen follows or breaks each one.
2. A law that is clearly broken on a changed screen is a review finding. A law that does not apply to the screen (for example, peak-end on a static report) is marked "n/a" with a short reason.
3. Any user-visible rule that comes out of this review gets a Playwright test on `chromium-desktop` and `mobile-touch`, per `CLAUDE.md`.

## The 20 laws

### 1. Aesthetic-usability effect
Users read a polished interface as easier to use. Keep one spacing scale, one type scale and the brand palette consistent across portals so screens look like one product.
- Check: a screen uses only tokens from the shared theme; no one-off colours or font sizes.

### 2. Law of proximity
Related things sit close together; unrelated things are separated. Labels stay with their inputs, and the actions for a row stay with that row.
- Check: the gap between a field and its label is smaller than the gap between two fields.

### 3. Von Restorff effect (isolation)
The item that is different gets noticed. Use one primary action per view and reserve the strong colour for it.
- Check: each screen has at most one filled primary button; destructive actions use a distinct, non-primary style.

### 4. Zeigarnik effect (unfinished tasks)
People remember and return to incomplete tasks. Show progress on multi-step work (quality cycles, invoices, onboarding, invitations) and show what is still open.
- Check: a multi-step flow shows the current step and what remains; a list shows which items need attention.

### 5. Tesler's law (conservation of complexity)
Some complexity cannot be removed, so the system should absorb it. Pre-fill what the system already knows (tenant, role, last-used values) and do not ask the user for it again.
- Check: a form asks only for information the system cannot infer.

### 6. Law of Prägnanz (simplicity)
People read a complex layout as the simplest form that fits. Reduce each screen to its essential content and remove decoration that competes with data.
- Check: each screen has one clear reading order and no decorative element that carries no information.

### 7. Serial position effect
People remember the first and last items best. Put the most important navigation items and the most-needed list actions at the start and end of a group.
- Check: the primary nav entry for each role sits first; the most frequent action sits at a start or end position.

### 8. Progressive disclosure
Show the common controls and hide the rare ones behind an expansion. The expandable visit rows (PR #415) are the existing pattern to follow.
- Check: advanced filters, secondary fields and bulk options are collapsed by default and can be opened in one click.

### 9. Law of common region
Things inside a shared boundary (card, panel, band) are seen as a group. Use cards and panels to group a record's details, and keep groups visually distinct.
- Check: each record's fields sit in one container; no two unrelated records share a container.

### 10. Goal-gradient effect
People speed up as they near the end of a task. Show how close the user is to completion (for example, "3 of 4 visits logged" or a progress bar) and keep the last step short.
- Check: any task with more than two steps shows completion state.

### 11. Law of similarity
Things that look alike are seen as the same kind of thing. Use one style for each kind of status, action or link, and do not reuse a status colour for an unrelated meaning.
- Check: a given status (paid, overdue, pending) always uses the same colour and label across screens.

### 12. Recognition over recall
People recognise options more easily than they recall them. Use selectable lists, search, recent items and visible labels instead of asking users to type codes or remember names.
- Check: a choice from a known set is a dropdown, combobox or chip list, not a free-text field.

### 13. Pareto principle (80/20)
A small share of features carries most of the use. Put the most-used 20% of actions on the main screen and keep the rest one step away.
- Check: the top tasks for each role are reachable from that role's home screen in one click.

### 14. Occam's razor (simplest explanation)
Among designs that work, choose the simplest. Prefer one clear path to a result over several options that do the same thing.
- Check: no screen offers two controls that do the same action.

### 15. Parkinson's law (work expands to fill time)
Tasks take as long as the time allowed for them. Keep forms short and make the common path quick, so the work does not expand.
- Check: the most common task is completed in the fewest steps; there are no unnecessary confirmations on routine actions.

### 16. Uniform connectedness
Elements that are visually connected are seen as related. Use the same border, background and alignment for elements that belong together (for example, a table header and its rows).
- Check: table rows, cards and their actions share the same container treatment.

### 17. Paradox of the active user
Experienced users skip help and look for shortcuts; new users need guidance. Keep the default path guided and add shortcuts (keyboard, bulk actions) for frequent users, without hiding the guided path.
- Check: each frequent action has both a guided path and a shortcut.

### 18. Cognitive load
Working memory is limited. Show only what the current task needs, keep labels short, and avoid asking users to hold values between screens.
- Check: a screen shows at most the fields needed for its task; the user does not have to copy a value from one screen to another.

### 19. Default effect
People keep defaults. Set sensible defaults for every choice that has a clear best value (date range, shift type, language, tenant currency), and make the default visible.
- Check: each form field with a common value has a default that is shown and easy to change.

### 20. Peak-end rule
People judge an experience by its most intense moment and by how it ends. Make the confirmation of a completed task (invoice sent, shift booked, report published) clear and positive, and make error moments recoverable.
- Check: every completed task ends with a clear confirmation; every error has a next step.

## Scope and verification

- These are review standards, not a replacement for the per-surface specs. Colour, typography and layout still come from the existing design-system docs.
- A redesign PR that changes user-visible behaviour is covered by Playwright tests on `chromium-desktop` and `mobile-touch`, per `CLAUDE.md`. The laws decide what is tested; the tests do not need to name the laws.
- This doc does not change `CLAUDE.md` or any existing spec. Adding a pointer to it from `CLAUDE.md` is a separate decision.
