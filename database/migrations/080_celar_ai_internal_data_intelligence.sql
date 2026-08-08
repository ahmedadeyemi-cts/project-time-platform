-- Pulse migration 080
-- Celar AI governed internal-data intelligence
--
-- Adds verified identity aliases used only for deterministic, permission-
-- scoped internal queries. This migration does not grant directory access and
-- does not expose an alias-management route.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $projectpulse080_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.projects') IS NULL
       OR to_regclass('public.project_tasks') IS NULL
       OR to_regclass('public.project_assignments') IS NULL THEN
        RAISE EXCEPTION 'Migration 080 requires the core identity, project, task, assignment, and migration-ledger foundations.';
    END IF;
END;
$projectpulse080_prerequisites$;

CREATE TABLE IF NOT EXISTS celar_ai_identity_aliases (
    celar_ai_identity_alias_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    alias_text VARCHAR(255) NOT NULL,
    alias_type VARCHAR(40) NOT NULL DEFAULT 'preferred_name'
        CHECK (alias_type IN ('preferred_name','legal_name','legacy_name','email_alias','entra_alias')),
    is_verified BOOLEAN NOT NULL DEFAULT FALSE,
    verification_source VARCHAR(80) NOT NULL DEFAULT '',
    verified_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    verified_at TIMESTAMPTZ NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_celar_ai_identity_alias_text
        CHECK (length(btrim(alias_text)) BETWEEN 2 AND 255),
    CONSTRAINT chk_celar_ai_identity_alias_verification
        CHECK (
            (is_verified = FALSE AND verified_at IS NULL AND verified_by_user_id IS NULL AND length(btrim(verification_source)) = 0)
            OR
            (is_verified = TRUE AND verified_at IS NOT NULL AND length(btrim(verification_source)) > 0)
        )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_celar_ai_identity_alias_user_value
    ON celar_ai_identity_aliases (
        user_id,
        (regexp_replace(lower(btrim(alias_text)), '[^a-z0-9]+', '', 'g'))
    );

CREATE INDEX IF NOT EXISTS ix_celar_ai_identity_alias_verified_lookup
    ON celar_ai_identity_aliases (
        (regexp_replace(lower(btrim(alias_text)), '[^a-z0-9]+', '', 'g')),
        user_id
    )
    WHERE is_active = TRUE AND is_verified = TRUE;

DO $projectpulse080_optional_indexes$
BEGIN
    IF to_regclass('public.work_register_task_assignment_history') IS NOT NULL THEN
        EXECUTE $index$
            CREATE INDEX IF NOT EXISTS ix_celar_ai_current_roster_person_project
            ON work_register_task_assignment_history (assigned_user_id, project_id, effective_start_date)
            WHERE assignment_status = 'active'
        $index$;
    END IF;
END;
$projectpulse080_optional_indexes$;

CREATE OR REPLACE FUNCTION projectpulse080_touch_identity_alias()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse080_touch$
BEGIN
    NEW.alias_text := btrim(NEW.alias_text);
    NEW.verification_source := btrim(NEW.verification_source);
    NEW.updated_at := NOW();
    RETURN NEW;
END;
$projectpulse080_touch$;

DROP TRIGGER IF EXISTS trg_celar_ai_identity_alias_touch ON celar_ai_identity_aliases;
CREATE TRIGGER trg_celar_ai_identity_alias_touch
BEFORE INSERT OR UPDATE ON celar_ai_identity_aliases
FOR EACH ROW EXECUTE FUNCTION projectpulse080_touch_identity_alias();

-- Known directory correction behind the reported Celar AI failure. Seed only
-- when exactly one active Pulse identity has the legacy spelling; otherwise
-- identity resolution remains ambiguous/fail-closed and requires review.
WITH legacy_candidate AS (
    SELECT
        user_id,
        COUNT(*) OVER () AS candidate_count
    FROM app_users
    WHERE is_active = TRUE
      AND regexp_replace(lower(btrim(display_name)), '[^a-z0-9]+', '', 'g') = 'kevindamish'
)
INSERT INTO celar_ai_identity_aliases (
    user_id,
    alias_text,
    alias_type,
    is_verified,
    verification_source,
    verified_at
)
SELECT
    user_id,
    'Kevin Damisch',
    'legacy_name',
    TRUE,
    'migration_080_known_directory_correction',
    NOW()
FROM legacy_candidate
WHERE candidate_count = 1
ON CONFLICT DO NOTHING;

DO $projectpulse080_runtime_grant$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ptp_app') THEN
        EXECUTE 'GRANT SELECT ON TABLE celar_ai_identity_aliases TO ptp_app';
    END IF;
END;
$projectpulse080_runtime_grant$;

INSERT INTO schema_migrations (migration_id, description)
VALUES (
    '080_celar_ai_internal_data_intelligence',
    'Add verified identity aliases and known directory correction for deterministic permission-scoped Celar AI internal-data queries'
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
