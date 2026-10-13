-- Run with psql against a backed-up database while employee writes are stopped.
-- Default is a dry run. Explicit -v apply_migration=true commits after preflight.
\set ON_ERROR_STOP on
\if :{?apply_migration}
\else
\set apply_migration false
\endif
BEGIN;
LOCK TABLE mt_doc_employee IN SHARE ROW EXCLUSIVE MODE;
DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM mt_doc_employee
    WHERE coalesce(data ->> 'KeycloakUserId', '') <> ''
    GROUP BY tenant_id, data ->> 'KeycloakUserId' HAVING count(*) > 1
  ) THEN
    RAISE EXCEPTION 'Duplicate employee login bindings exist within a tenant. Reconcile before migration.';
  END IF;
END $$;
WITH updated AS (
  UPDATE mt_doc_employee
  SET data = jsonb_set(data, '{KeycloakIdentityKey}',
      CASE WHEN coalesce(trim(data ->> 'KeycloakUserId'), '') = '' THEN 'null'::jsonb
           ELSE to_jsonb(data ->> 'KeycloakUserId') END, true),
      mt_version = gen_random_uuid(), mt_last_modified = transaction_timestamp()
  WHERE data -> 'KeycloakIdentityKey' IS DISTINCT FROM
      CASE WHEN coalesce(trim(data ->> 'KeycloakUserId'), '') = '' THEN 'null'::jsonb
           ELSE to_jsonb(data ->> 'KeycloakUserId') END
  RETURNING tenant_id
)
SELECT tenant_id, count(*) AS keys_backfilled FROM updated GROUP BY tenant_id;
\if :apply_migration
COMMIT;
\else
ROLLBACK;
\endif
