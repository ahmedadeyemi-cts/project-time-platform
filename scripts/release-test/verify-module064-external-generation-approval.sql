-- Read-only postconditions run inside the immutable private-network job.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='123_module064_external_generation_approval')
       OR NOT EXISTS (
           SELECT 1 FROM information_schema.columns
           WHERE table_schema='public' AND table_name='ai_capability_routes'
             AND column_name='sanitized_external_generation_approved'
             AND data_type='boolean' AND is_nullable='NO' AND column_default='false'
       )
       OR (SELECT count(*) FROM information_schema.columns
           WHERE table_schema='public' AND table_name='ai_capability_route_audit'
             AND column_name IN ('previous_external_generation_approved', 'new_external_generation_approved')
             AND data_type='boolean' AND is_nullable='NO' AND column_default='false') <> 2
    THEN
        RAISE EXCEPTION 'Module 064 external generation approval schema verification failed';
    END IF;
END $$;
