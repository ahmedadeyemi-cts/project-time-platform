-- ProjectPulse 098 rollback — Module 025 persistent SOW/GSD workspace.
-- This removes only schema introduced by migration 098.

BEGIN;

DROP TABLE IF EXISTS sow_gsd_workspace_events;

DROP TRIGGER IF EXISTS trg_sow_gsd_guard_immutable_reference
    ON sow_gsd_workspaces;
DROP FUNCTION IF EXISTS projectpulse_sow_gsd_guard_immutable_reference();

DROP TABLE IF EXISTS sow_gsd_workspaces;
DROP FUNCTION IF EXISTS projectpulse_next_sow_gsd_reference();
DROP SEQUENCE IF EXISTS sow_gsd_reference_sequence;

DELETE FROM schema_migrations
WHERE migration_id = '098_module_025_sow_gsd_workspace';

COMMIT;
