-- Additive input/output split. Existing source text and approvals are not rewritten.
BEGIN;
DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='123_module064_external_generation_approval')
 OR NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='099_module025_sow_gsd_workspace') THEN
  RAISE EXCEPTION 'Service Scope requires migrations 099 and 123';
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
