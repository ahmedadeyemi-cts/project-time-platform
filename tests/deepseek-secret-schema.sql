BEGIN READ ONLY;
SELECT migration_id FROM schema_migrations WHERE migration_id LIKE '101%';
SELECT c.conrelid::regclass AS table_name,c.conname,pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c
WHERE c.conrelid IN ('ai_provider_secrets'::regclass,'ai_provider_secret_audit'::regclass,'ai_provider_settings'::regclass,'ai_provider_settings_audit'::regclass)
ORDER BY 1,2;
SELECT provider_code,count(*) AS saved_count FROM ai_provider_secrets GROUP BY provider_code;
COMMIT;
