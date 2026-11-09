# Quality cycle for partner companies only (branch templates cleared)

## What changed

- The company-level quality cycle choice applies only to partner companies. It needs partner access turned on, and it uses a company-wide template that covers every property the company requests visits for.
- Turning partner access off also turns the company's quality cycle off.
- Non-partner companies are not changed and may keep branch-level templates.
- Existing partner companies that had a branch-level template are cleared by
  [`api/deploy/partner-quality-cycle-branch-clear.sql`](../../api/deploy/partner-quality-cycle-branch-clear.sql).
  Quality cycle is turned off for those companies, and staff choose a company-wide template again.

## Run

1. Back up the database and stop company writes.
2. Dry run (default, rolls back): `psql ... -f api/deploy/partner-quality-cycle-branch-clear.sql`
3. Apply: `psql ... -v apply_migration=true -f api/deploy/partner-quality-cycle-branch-clear.sql`

The dry run prints how many companies per tenant would be cleared. Check it before applying.

## Admin communication

Tell admins that partner companies with a branch-level quality cycle lose it and need a company-wide template chosen again.
