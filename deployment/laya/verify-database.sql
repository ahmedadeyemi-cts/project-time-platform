\set ON_ERROR_STOP on
SELECT set_config('celar_laya.verify_role', :'runtime_role', false);
DO $$
DECLARE role_name text := current_setting('celar_laya.verify_role'); t text;
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=role_name) THEN
        RAISE EXCEPTION 'The configured API role does not exist';
    END IF;
    FOREACH t IN ARRAY ARRAY['celar_laya_settings','celar_laya_settings_audit','celar_laya_decisions','celar_laya_reviews'] LOOP
        IF to_regclass('public.'||t) IS NULL THEN
            RAISE EXCEPTION 'Required Laya table missing: %',t;
        END IF;
        IF NOT has_table_privilege(role_name,'public.'||t,'SELECT') THEN
            RAISE EXCEPTION 'API read grant missing: %',t;
        END IF;
        IF t='celar_laya_settings' THEN
            IF NOT has_table_privilege(role_name,'public.'||t,'UPDATE') THEN
                RAISE EXCEPTION 'API settings update grant missing';
            END IF;
        ELSE
            IF NOT has_table_privilege(role_name,'public.'||t,'INSERT') THEN
                RAISE EXCEPTION 'API audit insert grant missing: %',t;
            END IF;
            IF NOT EXISTS(SELECT 1 FROM pg_trigger WHERE tgrelid=to_regclass('public.'||t)
                AND tgname=t||'_append_only' AND NOT tgisinternal AND tgenabled IN ('O','A')) THEN
                RAISE EXCEPTION 'Laya append-only trigger missing or disabled: %',t;
            END IF;
        END IF;
    END LOOP;
    IF (SELECT count(*) FROM celar_laya_settings WHERE singleton=true AND version>0)<>1 THEN
        RAISE EXCEPTION 'Laya singleton configuration missing or invalid';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.celar_laya_decisions'::regclass AND contype='u') THEN
        RAISE EXCEPTION 'Laya idempotency constraint missing';
    END IF;
    IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.celar_laya_reviews'::regclass AND contype='f'
        AND confrelid='public.celar_laya_decisions'::regclass) THEN
        RAISE EXCEPTION 'Laya review reference constraint missing';
    END IF;
END;
$$;
SELECT 'LAYA_DATABASE_SCHEMA_AND_API_GRANTS=VERIFIED';
