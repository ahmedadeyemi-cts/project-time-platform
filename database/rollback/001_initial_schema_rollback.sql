-- Project Time Platform
-- Rollback: 001_initial_schema_rollback.sql
-- Purpose: Drop objects created by 001_initial_schema.sql.
-- Warning: This destroys data in the initial schema tables.

BEGIN;

DROP TABLE IF EXISTS audit_logs CASCADE;
DROP TABLE IF EXISTS notification_log CASCADE;
DROP TABLE IF EXISTS notification_preferences CASCADE;
DROP TABLE IF EXISTS utilization_snapshots CASCADE;
DROP TABLE IF EXISTS accounting_reconciliations CASCADE;
DROP TABLE IF EXISTS approval_records CASCADE;
DROP TABLE IF EXISTS time_entries CASCADE;
DROP TABLE IF EXISTS timesheets CASCADE;
DROP TABLE IF EXISTS accounting_periods CASCADE;
DROP TABLE IF EXISTS project_assignments CASCADE;
DROP TABLE IF EXISTS project_tasks CASCADE;
DROP TABLE IF EXISTS projects CASCADE;
DROP TABLE IF EXISTS clients CASCADE;
DROP TABLE IF EXISTS reporting_relationships CASCADE;
DROP TABLE IF EXISTS team_memberships CASCADE;
DROP TABLE IF EXISTS teams CASCADE;
DROP TABLE IF EXISTS user_roles CASCADE;
DROP TABLE IF EXISTS roles CASCADE;
DROP TABLE IF EXISTS app_users CASCADE;

DROP FUNCTION IF EXISTS set_updated_at() CASCADE;

DELETE FROM schema_migrations WHERE migration_id = '001_initial_schema';
DROP TABLE IF EXISTS schema_migrations CASCADE;

COMMIT;
