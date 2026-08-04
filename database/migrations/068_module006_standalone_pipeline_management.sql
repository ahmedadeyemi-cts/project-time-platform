-- Project Health Dashboard migration 068
-- Standalone Module 006 Toyota & Hyundai pipeline management.
-- This data is intentionally independent from Modules 055C and 055D.

BEGIN;

DO $projectpulse068_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 068 requires schema_migrations and app_users.';
    END IF;
END;
$projectpulse068_prerequisites$;

CREATE TABLE IF NOT EXISTS module006_pipeline_records (
    module006_pipeline_record_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    source_project_code TEXT NOT NULL CHECK (source_project_code ~ '^P\.[A-Z0-9_-]{1,30}$'),
    source_kind TEXT NOT NULL DEFAULT 'manual'
        CHECK (source_kind IN ('manual', 'snapshot_overlay')),
    customer TEXT NOT NULL CHECK (lower(customer) IN ('toyota', 'hyundai')),
    business_unit TEXT NOT NULL DEFAULT '',
    uss_owner TEXT NOT NULL DEFAULT '',
    project_name TEXT NOT NULL CHECK (length(btrim(project_name)) > 0),
    quote_text TEXT NOT NULL DEFAULT '',
    estimated_value NUMERIC(18,2) NOT NULL DEFAULT 0 CHECK (estimated_value >= 0),
    status TEXT NOT NULL DEFAULT 'No Status',
    lifecycle TEXT NOT NULL DEFAULT 'active' CHECK (lifecycle IN ('active', 'historical')),
    update_date DATE,
    next_review_date DATE,
    latest_note TEXT NOT NULL DEFAULT '',
    revision INTEGER NOT NULL DEFAULT 1 CHECK (revision > 0),
    is_archived BOOLEAN NOT NULL DEFAULT FALSE,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_module006_pipeline_records_source_code
    ON module006_pipeline_records (upper(source_project_code));
CREATE INDEX IF NOT EXISTS ix_module006_pipeline_records_customer_lifecycle
    ON module006_pipeline_records (customer, lifecycle, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_module006_pipeline_records_owner
    ON module006_pipeline_records (uss_owner, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_module006_pipeline_records_next_review
    ON module006_pipeline_records (next_review_date)
    WHERE is_archived = FALSE;

CREATE TABLE IF NOT EXISTS module006_pipeline_updates (
    module006_pipeline_update_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    module006_pipeline_record_id UUID NOT NULL
        REFERENCES module006_pipeline_records(module006_pipeline_record_id) ON DELETE RESTRICT,
    note_text TEXT NOT NULL CHECK (length(btrim(note_text)) >= 3),
    status TEXT NOT NULL DEFAULT '',
    update_date DATE,
    next_review_date DATE,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_module006_pipeline_updates_record_time
    ON module006_pipeline_updates(module006_pipeline_record_id, created_at DESC);

CREATE OR REPLACE FUNCTION projectpulse068_block_pipeline_update_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse068_updates_immutable$
BEGIN
    RAISE EXCEPTION 'Module 006 pipeline update history is append-only.';
END;
$projectpulse068_updates_immutable$;

DROP TRIGGER IF EXISTS trg_module006_pipeline_updates_immutable
    ON module006_pipeline_updates;
CREATE TRIGGER trg_module006_pipeline_updates_immutable
BEFORE UPDATE OR DELETE ON module006_pipeline_updates
FOR EACH ROW EXECUTE FUNCTION projectpulse068_block_pipeline_update_mutation();

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '068_module006_standalone_pipeline_management',
    'Add standalone Toyota and Hyundai pipeline records, editable status fields, and append-only update history for Module 006 without a Module 055C dependency',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
