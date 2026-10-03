-- Dev tenant + service seed. Idempotent: safe to re-run.
-- Update this file when services / tenant config change.

-- 1. Activate the dev tenant (readysetsiivous) in the registry partition.
INSERT INTO mt_doc_tenantregistration (tenant_id, id, data, mt_version, mt_dotnet_type)
VALUES (
  'registry',
  'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  '{"Slug":"readysetsiivous","CompanyName":"ReadySetSiivous","Locales":["fi","en","sv"],"Currency":"EUR","Status":"Active","KeycloakRealm":"readysetsiivous","CreatedUtc":"2026-01-01T00:00:00Z","UpdatedUtc":"2026-01-01T00:00:00Z"}',
  '00000000-0000-0000-0000-000000000000',
  'CleaningSuite.Domain.Tenants.TenantRegistration, CleaningSuite.Domain'
)
ON CONFLICT (tenant_id, id) DO UPDATE SET data = EXCLUDED.data;

-- 2. Service table (matches Marten schema) + seed services.
CREATE TABLE IF NOT EXISTS mt_doc_service (
  tenant_id varchar NOT NULL,
  id uuid NOT NULL,
  data jsonb NOT NULL,
  mt_last_modified timestamptz DEFAULT transaction_timestamp(),
  mt_version uuid NOT NULL,
  mt_dotnet_type varchar,
  PRIMARY KEY (tenant_id, id)
);
CREATE INDEX IF NOT EXISTS mt_doc_service_idx_slug ON mt_doc_service ((data ->> 'Slug'));

INSERT INTO mt_doc_service (tenant_id, id, data, mt_version, mt_dotnet_type) VALUES
('readysetsiivous', '11111111-1111-1111-1111-111111111111', '{"Slug":"kotisiivous","Category":"cleaning","Name":{"Values":{"fi":"Kotisiivous","en":"Home cleaning","sv":"Hemstädning"}},"Description":{"Values":{"fi":"Säännöllinen kotisiivous","en":"Regular home cleaning","sv":"Regelbunden hemstädning"}},"DurationMinutes":120,"PriceNet":39.00,"VatRatePercent":25.5,"Currency":"EUR","IsActive":true,"IsFeatured":true,"SortOrder":1,"Icon":"Sparkles","ImageUrl":"","AdditionalInfo":{"Values":{}}}', '00000000-0000-0000-0000-000000000000', 'CleaningSuite.Domain.Services.Service, CleaningSuite.Domain'),
('readysetsiivous', '22222222-2222-2222-2222-222222222222', '{"Slug":"muuttosiivous","Category":"cleaning","Name":{"Values":{"fi":"Muuttosiivous","en":"Move-out cleaning","sv":"Flyttstädning"}},"Description":{"Values":{"fi":"Perusteellinen muuttosiivous","en":"Thorough move-out cleaning","sv":"Grundlig flyttstädning"}},"DurationMinutes":180,"PriceNet":55.00,"VatRatePercent":25.5,"Currency":"EUR","IsActive":true,"IsFeatured":false,"SortOrder":2,"Icon":"Home","ImageUrl":"","AdditionalInfo":{"Values":{}}}', '00000000-0000-0000-0000-000000000000', 'CleaningSuite.Domain.Services.Service, CleaningSuite.Domain'),
('readysetsiivous', '33333333-3333-3333-3333-333333333333', '{"Slug":"ikkunanpesu","Category":"cleaning","Name":{"Values":{"fi":"Ikkunanpesu","en":"Window cleaning","sv":"Fönsterputs"}},"Description":{"Values":{"fi":"Ikkunoiden pesu","en":"Window cleaning service","sv":"Fönsterputsning"}},"DurationMinutes":90,"PriceNet":29.00,"VatRatePercent":25.5,"Currency":"EUR","IsActive":true,"IsFeatured":false,"SortOrder":3,"Icon":"Sun","ImageUrl":"","AdditionalInfo":{"Values":{}}}', '00000000-0000-0000-0000-000000000000', 'CleaningSuite.Domain.Services.Service, CleaningSuite.Domain')
ON CONFLICT (tenant_id, id) DO UPDATE SET data = EXCLUDED.data;
