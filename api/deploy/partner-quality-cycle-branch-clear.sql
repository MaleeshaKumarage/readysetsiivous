-- Clears a branch-level quality cycle template from partner companies.
-- Partner companies use a company-wide template, which covers every property they request visits for.
-- A branch-level template in a partner company's setting would reach visits at other properties.
-- Affected companies get quality cycle turned off, so staff choose a company-wide template again.
-- Non-partner companies are not changed: they may use branch-level templates.
-- Run with psql against a backed-up database while company writes are stopped.
-- Default is a dry run. Explicit -v apply_migration=true commits after preflight.
\set ON_ERROR_STOP on
\if :{?apply_migration}
\else
\set apply_migration false
\endif
BEGIN;
LOCK TABLE mt_doc_company, mt_doc_qualitycycletemplate IN SHARE ROW EXCLUSIVE MODE;
-- BEGIN MIGRATION
WITH affected AS (
  SELECT c.tenant_id, c.id
  FROM mt_doc_company c
  JOIN mt_doc_qualitycycletemplate t
    ON t.tenant_id = c.tenant_id AND t.id = (c.data ->> 'QualityCycleTemplateId')::uuid
  WHERE c.data ->> 'IsPartnerEnabled' = 'true'
    AND t.data ->> 'BranchId' IS NOT NULL
),
updated AS (
  UPDATE mt_doc_company c
  SET data = jsonb_set(jsonb_set(c.data, '{IsQualityCycleEnabled}', 'false'::jsonb, true), '{QualityCycleTemplateId}', 'null'::jsonb, true),
      mt_version = gen_random_uuid(), mt_last_modified = transaction_timestamp()
  FROM affected a
  WHERE c.tenant_id = a.tenant_id AND c.id = a.id
  RETURNING c.tenant_id
)
SELECT tenant_id, count(*) AS cleared FROM updated GROUP BY tenant_id;
-- END MIGRATION
\if :apply_migration
COMMIT;
\else
ROLLBACK;
\endif
