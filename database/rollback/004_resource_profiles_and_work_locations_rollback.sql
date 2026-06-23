-- Project Time Platform
-- Rollback: 004_resource_profiles_and_work_locations_rollback.sql
-- Purpose: Roll back work locations, resource profiles, functions, and qualifications.

BEGIN;

ALTER TABLE time_entries DROP COLUMN IF EXISTS work_location_id;
ALTER TABLE time_entries DROP COLUMN IF EXISTS work_location_group_id;

DROP INDEX IF EXISTS idx_time_entries_work_location;
DROP INDEX IF EXISTS idx_resource_qualifications_user;
DROP INDEX IF EXISTS idx_resource_functions_user;
DROP INDEX IF EXISTS idx_resource_profiles_location;
DROP INDEX IF EXISTS idx_resource_profiles_user;
DROP INDEX IF EXISTS idx_work_locations_active;
DROP INDEX IF EXISTS idx_work_locations_group;
DROP INDEX IF EXISTS idx_work_location_groups_active;

DROP TABLE IF EXISTS resource_qualifications;
DROP TABLE IF EXISTS resource_functions;
DROP TABLE IF EXISTS resource_profiles;
DROP TABLE IF EXISTS work_locations;
DROP TABLE IF EXISTS work_location_groups;

DELETE FROM schema_migrations
WHERE migration_id = '004_resource_profiles_and_work_locations';

COMMIT;
