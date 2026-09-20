DO $verify$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='121_flowhive_sequential_phase_checkpoints')
 OR NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
   AND table_name='project_flowhive_ai_planner_runs' AND column_name='phase_checkpoint' AND data_type='jsonb' AND is_nullable='NO')
 OR NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='project_flowhive_ai_planner_runs'::regclass
   AND conname='ck_flowhive_phase_checkpoint_object' AND convalidated)
 THEN RAISE EXCEPTION 'FlowHive sequential checkpoint schema is incomplete'; END IF;
END;
$verify$;
