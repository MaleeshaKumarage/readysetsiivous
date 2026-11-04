# Automatic acceptance default for existing employees (R05)

## What changed

- New employees accept partner work automatically unless they choose otherwise.
- Every existing work employee (admin, manager, employee) is switched to automatic acceptance by
  [`api/deploy/employee-auto-accept-migration.sql`](../../api/deploy/employee-auto-accept-migration.sql).
  This includes employees who had switched it off; the decision is that all employees move to automatic.
- Employees can still switch it off in their profile afterwards.

## Run

1. Back up the database and stop employee writes.
2. Dry run (default, rolls back): `psql ... -f api/deploy/employee-auto-accept-migration.sql`
3. Apply: `psql ... -v apply_migration=true -f api/deploy/employee-auto-accept-migration.sql`

## Admin communication

Tell admins before the release:

> Partner cleaning work is now accepted automatically by default for employees who have
> not chosen a setting themselves. Employees can switch it off in their profile. Employees
> who already chose a setting keep it.

## Note

Employees who had automatic acceptance switched off will have it switched on by the apply step.
Tell them before the release, so they can switch it off again if they need to.
