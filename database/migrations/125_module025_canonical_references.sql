-- Additive canonical-reference library for Module 025 SOW reference sources
-- (Phase 1). Text-only admin-managed templates imported from the Sell Templates
-- folder. No customer columns, no customer FK, no engagement scope: templates are
-- identity-free by construction and never store customer data.
BEGIN;
DO $$ BEGIN
 IF to_regclass('public.app_users') IS NULL THEN
  RAISE EXCEPTION 'Module 025 canonical references require the app_users identity table';
 END IF;
END $$;
CREATE TABLE IF NOT EXISTS module025_canonical_references (
    id uuid PRIMARY KEY,
    label text NOT NULL,
    project_name text,
    source_text text NOT NULL,
    original_filename text,
    source_sha256 varchar(64),
    active boolean NOT NULL DEFAULT TRUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid REFERENCES app_users(user_id),
    updated_by uuid REFERENCES app_users(user_id),
    -- Cap the stored template text consistent with the ServiceOverview budget.
    CONSTRAINT module025_canonical_references_source_text_length
        CHECK (char_length(source_text) <= 30000),
    CONSTRAINT module025_canonical_references_label_present
        CHECK (char_length(btrim(label)) > 0)
);
CREATE INDEX IF NOT EXISTS module025_canonical_references_active_idx
    ON module025_canonical_references (active, updated_at DESC);
INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES('125_module025_canonical_references','Additive admin-managed canonical template library for Module 025 SOW reference sources (Phase 1); text-only, no customer data',NOW())
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
