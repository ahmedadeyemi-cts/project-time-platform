DO $$ BEGIN
 IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='124_module025_service_scope')
 OR (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
  AND table_name='module025_sow_gsd_engagements' AND column_name IN
  ('service_scope','generated_service_overview','service_overview_manually_edited')) <> 3
 OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public'
  AND table_name='ai_capability_routes' AND column_name='service_scope_full_text_approved')
 OR (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
  AND table_name='ai_capability_route_audit' AND column_name IN
  ('previous_scope_full_text_approved','new_scope_full_text_approved')) <> 2 THEN
  RAISE EXCEPTION 'Module 025 Service Scope migration is incomplete';
 END IF;
 IF EXISTS(SELECT 1 FROM ai_capability_routes
  WHERE feature_code <> 'sow_gsd_planning' AND service_scope_full_text_approved) THEN
  RAISE EXCEPTION 'Full-text scope approval must be limited to SOW/GSD';
 END IF;
END $$;
