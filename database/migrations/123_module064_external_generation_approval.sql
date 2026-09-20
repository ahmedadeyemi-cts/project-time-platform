-- No existing route is opted into paid structured generation by this migration.
BEGIN;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='071_ai_runtime_production_hardening') THEN
    RAISE EXCEPTION 'Module 064 external generation approval requires migration 071';
  END IF;
END $$;
ALTER TABLE ai_capability_routes
  ADD COLUMN IF NOT EXISTS sanitized_external_generation_approved BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE ai_capability_route_audit
  ADD COLUMN IF NOT EXISTS previous_external_generation_approved BOOLEAN NOT NULL DEFAULT FALSE,
  ADD COLUMN IF NOT EXISTS new_external_generation_approved BOOLEAN NOT NULL DEFAULT FALSE;
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('123_module064_external_generation_approval','Audited per-capability approval for sanitized external SOW generation',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
