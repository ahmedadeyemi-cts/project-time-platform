-- Additive input/output split. Existing source text and approvals are not rewritten.
BEGIN;
-- Migration 099 predates schema_migrations registration. Its physical workspace
-- contract is authoritative; do not invent a ledger receipt for an old script.
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='123_module064_external_generation_approval') THEN
  RAISE EXCEPTION 'Service Scope requires the migration 123 approval receipt';
 END IF;
 IF to_regclass('public.module025_sow_gsd_number_seq') IS NULL
 OR to_regclass('public.module025_sow_gsd_engagements') IS NULL
 OR to_regclass('public.module025_sow_gsd_phases') IS NULL
 OR to_regclass('public.module025_sow_gsd_events') IS NULL
 OR to_regprocedure('public.module025_protect_sow_gsd_identity()') IS NULL
 OR NOT EXISTS (
  SELECT 1 FROM pg_trigger
  WHERE tgrelid=to_regclass('public.module025_sow_gsd_engagements')
    AND tgname='trg_module025_protect_sow_gsd_identity'
    AND tgfoid=to_regprocedure('public.module025_protect_sow_gsd_identity()')
    AND NOT tgisinternal AND tgenabled IN ('O','A')
 ) THEN
  RAISE EXCEPTION 'Service Scope requires the migration 099 workspace schema and identity protection';
 END IF;
 IF EXISTS (
  SELECT 1 FROM (VALUES
   ('module025_sow_gsd_engagements','engagement_id','uuid'),
   ('module025_sow_gsd_engagements','owner_user_id','uuid'),
   ('module025_sow_gsd_engagements','service_overview','text'),
   ('module025_sow_gsd_phases','engagement_id','uuid'),
   ('module025_sow_gsd_phases','suggested_hours','numeric'),
   ('module025_sow_gsd_phases','final_hours','numeric'),
   ('module025_sow_gsd_events','engagement_id','uuid'),
   ('module025_sow_gsd_events','evidence_json','jsonb'),
   ('ai_capability_routes','sanitized_external_generation_approved','bool'),
   ('ai_capability_route_audit','previous_external_generation_approved','bool'),
   ('ai_capability_route_audit','new_external_generation_approved','bool')
  ) AS required(table_name,column_name,udt_name)
  LEFT JOIN information_schema.columns AS actual
   ON actual.table_schema='public' AND actual.table_name=required.table_name
      AND actual.column_name=required.column_name
  WHERE actual.udt_name IS DISTINCT FROM required.udt_name
     OR actual.is_nullable IS DISTINCT FROM 'NO'
 ) THEN
  RAISE EXCEPTION 'Service Scope requires complete migration 099 and 123 columns';
 END IF;
END $$;
ALTER TABLE module025_sow_gsd_engagements
 ADD COLUMN IF NOT EXISTS service_scope TEXT,
 ADD COLUMN IF NOT EXISTS generated_service_overview TEXT NOT NULL DEFAULT '',
 ADD COLUMN IF NOT EXISTS service_overview_manually_edited BOOLEAN NOT NULL DEFAULT FALSE;
-- NULL retains the legacy input contract until an authorized user saves Service Scope.
-- In particular, do not copy a generated narrative back into authoritative input.
ALTER TABLE ai_capability_routes
 ADD COLUMN IF NOT EXISTS service_scope_full_text_approved BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE ai_capability_route_audit
 ADD COLUMN IF NOT EXISTS previous_scope_full_text_approved BOOLEAN NOT NULL DEFAULT FALSE,
 ADD COLUMN IF NOT EXISTS new_scope_full_text_approved BOOLEAN NOT NULL DEFAULT FALSE;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('124_module025_service_scope','Separate authoritative Service Scope, generated overview, and explicit full-text AI approval',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
