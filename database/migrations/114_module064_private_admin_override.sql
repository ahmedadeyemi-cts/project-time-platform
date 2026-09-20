BEGIN;
ALTER TABLE ai_private_model_profiles ADD COLUMN IF NOT EXISTS administrator_override boolean NOT NULL DEFAULT false;
INSERT INTO schema_migrations(migration_id, description)
VALUES ('114_module064_private_admin_override', 'Explicit encrypted administrator override for deployment defaults outside immutable releases')
ON CONFLICT(migration_id) DO NOTHING;
COMMIT;
