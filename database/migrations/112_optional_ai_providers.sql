BEGIN;
ALTER TABLE ai_provider_secrets DROP CONSTRAINT IF EXISTS ck_ai_provider_secrets_provider_code;
ALTER TABLE ai_provider_secrets DROP CONSTRAINT IF EXISTS ai_provider_secrets_provider_code_check;
ALTER TABLE ai_provider_secrets ADD CONSTRAINT ck_ai_provider_secrets_provider_code
    CHECK (provider_code IN ('deepseek_v4','claude','openai','gemini','copilot_studio'));
ALTER TABLE ai_provider_settings DROP CONSTRAINT IF EXISTS ck_ai_provider_settings_provider_code;
ALTER TABLE ai_provider_settings DROP CONSTRAINT IF EXISTS ai_provider_settings_provider_code_check;
ALTER TABLE ai_provider_settings ADD CONSTRAINT ck_ai_provider_settings_provider_code
    CHECK (provider_code IN ('deepseek_v4','claude','openai','gemini','copilot_studio'));
INSERT INTO schema_migrations (migration_id, description)
VALUES ('112_optional_ai_providers', 'Allow optional Gemini and Copilot Studio configuration without enabling or reordering any provider')
ON CONFLICT (migration_id) DO NOTHING;
COMMIT;
