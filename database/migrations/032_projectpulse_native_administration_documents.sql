-- ProjectPulse migration 032: native administration documents for Modules 064–070 and 073–074.
-- Source-only until separately reviewed and applied to the test database.
BEGIN;

DO $$
BEGIN
    IF to_regclass('public.projectpulse_module_audit_events') IS NULL THEN
        RAISE EXCEPTION 'Migration 031 must be applied before migration 032.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS projectpulse_native_admin_documents
(
    module_number varchar(3) NOT NULL,
    document_key varchar(100) NOT NULL,
    document_json jsonb NOT NULL,
    revision_number bigint NOT NULL DEFAULT 0,
    updated_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (module_number, document_key),
    CONSTRAINT ck_projectpulse_native_admin_module
        CHECK (module_number IN ('064','065','066','067','068','069','070','073','074')),
    CONSTRAINT ck_projectpulse_native_admin_key
        CHECK (length(trim(document_key)) BETWEEN 1 AND 100),
    CONSTRAINT ck_projectpulse_native_admin_document
        CHECK (jsonb_typeof(document_json) = 'object'),
    CONSTRAINT ck_projectpulse_native_admin_revision
        CHECK (revision_number >= 0)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_native_admin_updated
    ON projectpulse_native_admin_documents(module_number, updated_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_native_admin_document_revisions
(
    revision_id uuid PRIMARY KEY,
    module_number varchar(3) NOT NULL,
    document_key varchar(100) NOT NULL,
    revision_number bigint NOT NULL,
    document_json jsonb NOT NULL,
    saved_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    saved_at timestamptz NOT NULL DEFAULT now(),
    change_reason varchar(50) NOT NULL DEFAULT 'save',
    restored_from_revision_id uuid NULL,
    CONSTRAINT ck_projectpulse_native_admin_revision_module
        CHECK (module_number IN ('064','065','066','067','068','069','070','073','074')),
    CONSTRAINT ck_projectpulse_native_admin_revision_document
        CHECK (jsonb_typeof(document_json) = 'object'),
    CONSTRAINT ck_projectpulse_native_admin_revision_number
        CHECK (revision_number > 0),
    CONSTRAINT ck_projectpulse_native_admin_change_reason
        CHECK (change_reason IN ('save', 'restore')),
    CONSTRAINT ux_projectpulse_native_admin_revision
        UNIQUE (module_number, document_key, revision_number)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_native_admin_history
    ON projectpulse_native_admin_document_revisions
    (module_number, document_key, revision_number DESC);

COMMENT ON TABLE projectpulse_native_admin_documents IS
    'Current versioned ProjectPulse-native administration documents for Modules 064–070 and 073–074.';
COMMENT ON TABLE projectpulse_native_admin_document_revisions IS
    'Immutable revision history for ProjectPulse-native module administration documents.';

DO $$
DECLARE
    role_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolcanlogin = true
          AND rolsuper = false
          AND rolname NOT LIKE 'pg_%'
          AND rolname <> 'postgres'
        ORDER BY rolname
    LOOP
        EXECUTE format(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.projectpulse_native_admin_documents TO %I',
            role_record.rolname
        );
        EXECUTE format(
            'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.projectpulse_native_admin_document_revisions TO %I',
            role_record.rolname
        );
    END LOOP;
END $$;

COMMIT;
