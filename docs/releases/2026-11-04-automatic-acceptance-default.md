# Automatic acceptance default for existing employees (R05)

## What changed

- New employees accept partner work automatically unless they choose otherwise.
- Existing employees who never chose a setting are switched to automatic acceptance by
  [`api/deploy/employee-auto-accept-migration.sql`](../../api/deploy/employee-auto-accept-migration.sql).
- Employees who turned automatic acceptance on or off themselves keep their choice. The
  choice is recorded with `AutoAcceptWorkChosenUtc` when they save it.

## Run

1. Back up the database and stop employee writes.
2. Dry run (default, rolls back): `psql ... -f api/deploy/employee-auto-accept-migration.sql`
3. Apply: `psql ... -v apply_migration=true -f api/deploy/employee-auto-accept-migration.sql`

## Admin communication

Tell admins before the release:

> Partner cleaning work is now accepted automatically by default for employees who have
> not chosen a setting themselves. Employees can switch it off in their profile. Employees
> who already chose a setting keep it.

## Known limitation

Employees who switched automatic acceptance off before `AutoAcceptWorkChosenUtc` existed
have no recorded choice. The migration cannot tell those apart from employees who never
chose, so it switches them on. Review the list of employees who had it off before running
the apply step, and ask them to switch it off again if needed.
