-- Immutable review candidates only. Uploading never changes live SOW/GSD exports.
BEGIN;
SELECT pg_advisory_xact_lock(250117);

CREATE TABLE IF NOT EXISTS module025_template_candidates (
    template_version_id uuid PRIMARY KEY,
    owner_user_id uuid NOT NULL REFERENCES app_users(user_id),
    owner_display_name varchar(320) NOT NULL,
    owner_team_name varchar(255) NOT NULL DEFAULT '',
    organization_visible boolean NOT NULL DEFAULT FALSE,
    document_kind varchar(3) NOT NULL CHECK (document_kind IN ('sow','gsd')),
    customer_program varchar(20) NOT NULL CHECK (customer_program IN ('standard','toyota','hyundai')),
    version_number integer NOT NULL CHECK (version_number > 0),
    label varchar(160) NOT NULL CHECK (length(trim(label)) > 0),
    change_notes varchar(2000) NOT NULL CHECK (length(trim(change_notes)) > 0),
    file_name varchar(200) NOT NULL,
    content_sha256 varchar(64) NOT NULL CHECK (content_sha256 ~ '^[a-f0-9]{64}$'),
    file_content bytea NOT NULL CHECK (octet_length(file_content) BETWEEN 1 AND 4194304),
    validation_json jsonb NOT NULL CHECK (jsonb_typeof(validation_json) = 'object'),
    lifecycle_state varchar(30) NOT NULL DEFAULT 'awaiting_mapping'
        CHECK (lifecycle_state = 'awaiting_mapping'),
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (owner_user_id, document_kind, customer_program, version_number),
    UNIQUE (owner_user_id, document_kind, customer_program, content_sha256)
);

CREATE OR REPLACE FUNCTION module025_protect_template_candidate()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Module 025 template candidates are immutable; upload a new version';
END $$;
DROP TRIGGER IF EXISTS trg_module025_protect_template_candidate ON module025_template_candidates;
CREATE TRIGGER trg_module025_protect_template_candidate
BEFORE UPDATE OR DELETE ON module025_template_candidates
FOR EACH ROW EXECUTE FUNCTION module025_protect_template_candidate();

COMMENT ON TABLE module025_template_candidates IS
    'Immutable manager-submitted originals awaiting validated input mappings and document QA. Never consumed by live exporters in this release.';

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('117_module025_template_candidates','Retain immutable SOW/GSD template review candidates without changing active exports',now())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
