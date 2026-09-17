-- Module 025: authoritative project name for SOW/GSD, downloads, retained versions and SELL handoff.
BEGIN;
SELECT pg_advisory_xact_lock(250109);

ALTER TABLE module025_sow_gsd_engagements
    ADD COLUMN IF NOT EXISTS project_name varchar(500) NOT NULL DEFAULT '';

CREATE INDEX IF NOT EXISTS ix_module025_sow_gsd_project_name
    ON module025_sow_gsd_engagements(lower(project_name), updated_at DESC);

COMMENT ON COLUMN module025_sow_gsd_engagements.project_name IS
    'User-authoritative project/SOW name used in Module 025 UI, document metadata, filenames, retained source snapshots and SELL handoff.';

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('109_module025_project_name','Add authoritative project name to Module 025 SOW/GSD engagements',now())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
