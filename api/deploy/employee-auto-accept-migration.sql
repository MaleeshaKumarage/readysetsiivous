-- Switches every existing work employee (admin, manager, employee) to automatic acceptance.
-- Partner accounts are not affected. Recorded choices are overridden by this one-off release decision.
-- Run with psql against a backed-up database while employee writes are stopped.
-- Default is a dry run. Explicit -v apply_migration=true commits after preflight.
\set ON_ERROR_STOP on
\if :{?apply_migration}
\else
\set apply_migration false
\endif
BEGIN;
LOCK TABLE mt_doc_employee IN SHARE ROW EXCLUSIVE MODE;
-- BEGIN MIGRATION
WITH updated AS (
  UPDATE mt_doc_employee
  SET data = jsonb_set(data, '{AutoAcceptWork}', 'true'::jsonb, true),
      mt_version = gen_random_uuid(), mt_last_modified = transaction_timestamp()
  WHERE data ->> 'Role' IN ('admin', 'employee', 'manager')
    AND data -> 'AutoAcceptWork' IS DISTINCT FROM 'true'::jsonb
  RETURNING tenant_id
)
SELECT tenant_id, count(*) AS switched_to_automatic FROM updated GROUP BY tenant_id;
-- END MIGRATION
\if :apply_migration
COMMIT;
\else
ROLLBACK;
\endif
