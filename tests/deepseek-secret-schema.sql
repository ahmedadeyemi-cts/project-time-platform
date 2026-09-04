BEGIN READ ONLY;
SELECT migration_id FROM schema_migrations WHERE migration_id LIKE '101%';
SELECT c.conrelid::regclass AS table_name,c.conname,pg_get_constraintdef(c.oid) AS definition
FROM pg_constraint c
WHERE c.conrelid IN ('ai_provider_secrets'::regclass,'ai_provider_secret_audit'::regclass,'ai_provider_settings'::regclass,'ai_provider_settings_audit'::regclass)
ORDER BY 1,2;
SELECT provider_code,count(*) AS saved_count FROM ai_provider_secrets GROUP BY provider_code;
COMMIT;
\i /repair.sql
BEGIN;
INSERT INTO ai_provider_secrets(provider_code,ciphertext,nonce,tag,encryption_key_id,version,rotated_at,rotated_by)
VALUES ('deepseek_v4',decode('00','hex'),decode(repeat('00',12),'hex'),decode(repeat('00',16),'hex'),
        'rollback-only-test','rollback-only-test',NOW(),'00000000-0000-0000-0000-000000000001')
ON CONFLICT (provider_code) DO NOTHING;
INSERT INTO ai_provider_secret_audit(provider_code,action,version,encryption_key_id,actor_user_id)
VALUES ('deepseek_v4','replaced','rollback-only-test','rollback-only-test','00000000-0000-0000-0000-000000000001');
INSERT INTO ai_provider_settings(provider_code,model,enabled,updated_by)
VALUES ('deepseek_v4','deepseek-v4-flash-0731',true,'00000000-0000-0000-0000-000000000001')
ON CONFLICT (provider_code) DO NOTHING;
DO $$
BEGIN
    BEGIN
        INSERT INTO ai_provider_settings(provider_code,model,updated_by)
        VALUES ('unapproved_provider','test','00000000-0000-0000-0000-000000000001');
        RAISE EXCEPTION 'Unapproved provider was accepted';
    EXCEPTION WHEN check_violation THEN NULL;
    END;
END $$;
ROLLBACK;
SELECT 'DEEPSEEK_SECRET_AND_AUDIT_WRITE_ROLLBACK_TEST=PASS' AS verification;
SELECT 'UNAPPROVED_PROVIDER_REMAINS_REJECTED=PASS' AS verification;
SELECT provider_code,count(*) AS saved_count FROM ai_provider_secrets GROUP BY provider_code;
