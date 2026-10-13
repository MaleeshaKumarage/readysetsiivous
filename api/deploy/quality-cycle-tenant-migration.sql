-- Reconcile legacy QC ownership without changing evidence, tokens or document ids.
-- Check mode rolls back. Back up and stop QC writes before explicitly applying.
\set ON_ERROR_STOP on
\if :{?apply_migration}
\else
\set apply_migration false
\endif
\if :{?legacy_tenant}
\else
\set legacy_tenant '*DEFAULT*'
\endif
\if :{?unreferenced_template_owner}
\else
\set unreferenced_template_owner ''
\endif
BEGIN;
LOCK TABLE mt_doc_qualitycycleform, mt_doc_qualitycycletemplate, mt_doc_shift,
  mt_doc_employee, mt_doc_company, mt_doc_branch IN SHARE ROW EXCLUSIVE MODE;
CREATE TEMP TABLE qc_migration_config ON COMMIT DROP AS
  SELECT :'legacy_tenant'::text AS legacy, :'unreferenced_template_owner'::text AS explicit_owner;
CREATE TEMP TABLE qc_template_owners ON COMMIT DROP AS
SELECT t.id,
  CASE WHEN count(DISTINCT refs.tenant_id) = 1 THEN min(refs.tenant_id)
       WHEN count(DISTINCT refs.tenant_id) = 0
         AND coalesce(t.data->>'CompanyId','') = '' AND coalesce(t.data->>'BranchId','') = ''
       THEN nullif(cfg.explicit_owner,'') END AS target
FROM mt_doc_qualitycycletemplate t CROSS JOIN qc_migration_config cfg
LEFT JOIN LATERAL (
  SELECT s.tenant_id FROM mt_doc_shift s WHERE s.data->>'QualityCycleTemplateId' = t.id::text
  UNION SELECT c.tenant_id FROM mt_doc_company c WHERE c.id::text = t.data->>'CompanyId'
  UNION SELECT b.tenant_id FROM mt_doc_branch b WHERE b.id::text = t.data->>'BranchId'
) refs ON true
WHERE t.tenant_id = cfg.legacy
GROUP BY t.id,t.data,cfg.explicit_owner;
CREATE TEMP TABLE qc_form_owners ON COMMIT DROP AS
SELECT f.id, CASE WHEN count(DISTINCT s.tenant_id) = 1 THEN min(s.tenant_id) END AS target
FROM mt_doc_qualitycycleform f CROSS JOIN qc_migration_config cfg
LEFT JOIN mt_doc_shift s ON s.id::text = f.data->>'ShiftId'
  AND EXISTS (SELECT 1 FROM mt_doc_employee e WHERE e.id::text = f.data->>'EmployeeId' AND e.tenant_id = s.tenant_id)
WHERE f.tenant_id = cfg.legacy GROUP BY f.id;
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM qc_template_owners m WHERE m.target IS NULL OR NOT EXISTS
    (SELECT 1 FROM mt_doc_tenantregistration r WHERE r.data->>'Slug'=m.target))
    OR EXISTS (SELECT 1 FROM qc_form_owners m WHERE m.target IS NULL OR NOT EXISTS
    (SELECT 1 FROM mt_doc_tenantregistration r WHERE r.data->>'Slug'=m.target)) THEN
    RAISE EXCEPTION 'QC ownership is unresolved/ambiguous or target is unregistered. No migration applied.';
  END IF;
  IF EXISTS (SELECT 1 FROM qc_template_owners m JOIN mt_doc_qualitycycletemplate t ON t.id=m.id
    CROSS JOIN qc_migration_config cfg WHERE t.tenant_id=cfg.legacy AND
    ((coalesce(t.data->>'CompanyId','')<>'' AND NOT EXISTS
      (SELECT 1 FROM mt_doc_company c WHERE c.id::text=t.data->>'CompanyId' AND c.tenant_id=m.target)) OR
     (coalesce(t.data->>'BranchId','')<>'' AND NOT EXISTS
      (SELECT 1 FROM mt_doc_branch b WHERE b.id::text=t.data->>'BranchId' AND b.tenant_id=m.target
        AND (coalesce(t.data->>'CompanyId','')='' OR b.data->>'CompanyId'=t.data->>'CompanyId'))))) THEN
    RAISE EXCEPTION 'QC template scope is orphaned or conflicts with its owner.';
  END IF;
  IF EXISTS (SELECT 1 FROM qc_template_owners m JOIN mt_doc_qualitycycletemplate t ON t.id=m.id AND t.tenant_id=m.target)
    OR EXISTS (SELECT 1 FROM qc_form_owners m JOIN mt_doc_qualitycycleform f ON f.id=m.id AND f.tenant_id=m.target) THEN
    RAISE EXCEPTION 'QC target document collision. Preserve and reconcile evidence before migration.';
  END IF;
  IF EXISTS (SELECT 1 FROM qc_form_owners m JOIN mt_doc_qualitycycleform f ON f.id=m.id
    CROSS JOIN qc_migration_config cfg WHERE f.tenant_id=cfg.legacy AND NOT EXISTS
    (SELECT 1 FROM mt_doc_qualitycycletemplate t LEFT JOIN qc_template_owners tm ON tm.id=t.id
     WHERE t.id::text=f.data->>'TemplateId' AND
       (t.tenant_id=m.target OR (t.tenant_id=cfg.legacy AND tm.target=m.target)))) THEN
    RAISE EXCEPTION 'QC form template is missing or owned by another tenant.';
  END IF;
  IF EXISTS (
    SELECT 1 FROM mt_doc_qualitycycleform f
    LEFT JOIN qc_form_owners m ON m.id=f.id
    CROSS JOIN qc_migration_config cfg
    WHERE f.tenant_id=cfg.legacy OR f.tenant_id IN (SELECT target FROM qc_form_owners)
    GROUP BY CASE WHEN f.tenant_id=cfg.legacy THEN m.target ELSE f.tenant_id END,
      f.data->>'ShiftId',f.data->>'EmployeeId',f.data->>'ShiftOccurrenceUtc'
    HAVING count(*)>1
  ) OR EXISTS (
    SELECT 1 FROM mt_doc_qualitycycleform f
    LEFT JOIN qc_form_owners m ON m.id=f.id
    CROSS JOIN qc_migration_config cfg
    WHERE f.tenant_id=cfg.legacy OR f.tenant_id IN (SELECT target FROM qc_form_owners)
    GROUP BY CASE WHEN f.tenant_id=cfg.legacy THEN m.target ELSE f.tenant_id END,f.data->>'Token'
    HAVING count(*)>1
  ) THEN
    RAISE EXCEPTION 'QC occurrence/token collision. Preserve and reconcile evidence before migration.';
  END IF;
END $$;
WITH moved AS (
  UPDATE mt_doc_qualitycycletemplate t SET tenant_id=m.target FROM qc_template_owners m,qc_migration_config cfg
  WHERE t.id=m.id AND t.tenant_id=cfg.legacy RETURNING t.tenant_id
) SELECT 'templates_moved',tenant_id,count(*) FROM moved GROUP BY tenant_id;
WITH moved AS (
  UPDATE mt_doc_qualitycycleform f SET tenant_id=m.target FROM qc_form_owners m,qc_migration_config cfg
  WHERE f.id=m.id AND f.tenant_id=cfg.legacy RETURNING f.tenant_id
) SELECT 'forms_moved',tenant_id,count(*) FROM moved GROUP BY tenant_id;
\if :apply_migration
COMMIT;
\else
ROLLBACK;
\endif
