-- ProjectPulse migration 067
-- PR #467: immutable, version-specific Project Manager acceptance for Module 005.
-- Immutable project/work numbers remain owned by migration 066.

BEGIN;

DO $projectpulse067_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.project_expense_uploads') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.app_users') IS NULL THEN
        RAISE EXCEPTION 'Migration 067 requires the Module 005 expense, project, user, and schema-migration foundations.';
    END IF;
END;
$projectpulse067_prerequisites$;

CREATE TABLE IF NOT EXISTS project_expense_upload_acceptances (
    project_expense_upload_acceptance_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    project_expense_upload_id UUID NOT NULL UNIQUE
        REFERENCES project_expense_uploads(project_expense_upload_id) ON DELETE RESTRICT,
    project_id UUID NOT NULL REFERENCES projects(project_id) ON DELETE RESTRICT,
    expense_owner_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    accepted_version_number INTEGER NOT NULL CHECK (accepted_version_number > 0),
    accepted_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acceptance_reason TEXT NOT NULL DEFAULT '',
    accepted_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_project_expense_upload_acceptances_project
    ON project_expense_upload_acceptances(project_id, accepted_at DESC);

CREATE OR REPLACE FUNCTION projectpulse067_validate_expense_acceptance_insert()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse067_acceptance_validate$
DECLARE
    source_project_id UUID;
    source_owner_user_id UUID;
    source_version INTEGER;
    source_is_current BOOLEAN;
    source_is_deleted BOOLEAN;
BEGIN
    SELECT
        upload.project_id,
        upload.expense_owner_user_id,
        upload.version_number,
        upload.is_current,
        upload.deleted_at IS NOT NULL
    INTO
        source_project_id,
        source_owner_user_id,
        source_version,
        source_is_current,
        source_is_deleted
    FROM project_expense_uploads upload
    WHERE upload.project_expense_upload_id = NEW.project_expense_upload_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'The expense upload selected for PM acceptance does not exist.';
    END IF;
    IF source_is_deleted THEN
        RAISE EXCEPTION 'Deleted expense evidence cannot be accepted.';
    END IF;
    IF NOT source_is_current THEN
        RAISE EXCEPTION 'Only the current expense version can be accepted.';
    END IF;
    IF NEW.project_id IS DISTINCT FROM source_project_id
       OR NEW.expense_owner_user_id IS DISTINCT FROM source_owner_user_id
       OR NEW.accepted_version_number IS DISTINCT FROM source_version THEN
        RAISE EXCEPTION 'Expense acceptance evidence does not match the selected upload version.';
    END IF;

    RETURN NEW;
END;
$projectpulse067_acceptance_validate$;

DROP TRIGGER IF EXISTS trg_project_expense_acceptance_validate_insert
    ON project_expense_upload_acceptances;
CREATE TRIGGER trg_project_expense_acceptance_validate_insert
BEFORE INSERT ON project_expense_upload_acceptances
FOR EACH ROW EXECUTE FUNCTION projectpulse067_validate_expense_acceptance_insert();

CREATE OR REPLACE FUNCTION projectpulse067_block_expense_acceptance_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse067_acceptance_immutable$
BEGIN
    RAISE EXCEPTION 'Project expense PM acceptance evidence is immutable.';
END;
$projectpulse067_acceptance_immutable$;

DROP TRIGGER IF EXISTS trg_project_expense_acceptance_immutable
    ON project_expense_upload_acceptances;
CREATE TRIGGER trg_project_expense_acceptance_immutable
BEFORE UPDATE OR DELETE ON project_expense_upload_acceptances
FOR EACH ROW EXECUTE FUNCTION projectpulse067_block_expense_acceptance_mutation();

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '067_uat_expense_lifecycle_work_identifiers',
    'Add immutable version-specific Project Manager acceptance for Module 005; project/work identifiers remain governed by migration 066',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
