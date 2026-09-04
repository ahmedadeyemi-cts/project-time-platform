BEGIN;

ALTER TABLE ai_provider_secrets DROP CONSTRAINT IF EXISTS ck_ai_provider_secrets_provider_code;
ALTER TABLE ai_provider_secrets ADD CONSTRAINT ck_ai_provider_secrets_provider_code
    CHECK (provider_code IN ('deepseek_v4','claude','openai'));
ALTER TABLE ai_provider_settings DROP CONSTRAINT IF EXISTS ck_ai_provider_settings_provider_code;
ALTER TABLE ai_provider_settings ADD CONSTRAINT ck_ai_provider_settings_provider_code
    CHECK (provider_code IN ('deepseek_v4','claude','openai'));

INSERT INTO ai_capability_route_audit
    (feature_code, previous_targets, new_targets, previous_external_context_policy, new_external_context_policy, actor_user_id)
SELECT feature_code, route_targets,
       '["deepseek_v4","celar_ai","claude","openai","local_template"]'::jsonb,
       external_context_policy, external_context_policy, NULL
FROM ai_capability_routes
WHERE NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='101_deepseek_v4_provider');

UPDATE ai_capability_routes
SET route_targets='["deepseek_v4","celar_ai","claude","openai","local_template"]'::jsonb,
    revision=revision+1, updated_at=NOW()
WHERE NOT EXISTS (SELECT 1 FROM schema_migrations WHERE migration_id='101_deepseek_v4_provider');

INSERT INTO schema_migrations (migration_id, description)
VALUES ('101_deepseek_v4_provider', 'Allow encrypted DeepSeek v4 credentials and set DGX-first capability routes')
ON CONFLICT (migration_id) DO NOTHING;
COMMIT;
