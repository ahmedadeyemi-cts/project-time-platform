-- Conservative rollback for ProjectPulse migration 088.
-- Migration 088 formalizes ownership columns that may already have been created
-- by an earlier controlled one-time package. The rollback therefore removes only
-- the migration-specific indexes and registration and deliberately preserves
-- ownership columns, foreign keys, and business data.
BEGIN;

DROP INDEX IF EXISTS idx_projectpulse_system_audit_events_time_desc;
DROP INDEX IF EXISTS idx_projectpulse_system_audit_events_category_status;
DROP INDEX IF EXISTS idx_projectpulse_system_audit_events_event_type;
DROP INDEX IF EXISTS idx_projectpulse_system_audit_events_correlation;
DROP INDEX IF EXISTS idx_projectpulse_system_audit_events_target;
DROP INDEX IF EXISTS idx_auth_login_events_created_desc;
DROP INDEX IF EXISTS idx_auth_login_events_user_result;
DROP INDEX IF EXISTS idx_auth_login_events_username_result;

DELETE FROM schema_migrations WHERE migration_id='088_systemwide_enterprise_reliability';
COMMIT;
