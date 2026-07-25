BEGIN;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM projectpulse_module_availability
        WHERE is_enabled = FALSE
    ) THEN
        RAISE EXCEPTION
            'Module availability rollback blocked: one or more modules are disabled. Re-enable them and preserve audit evidence before rollback.';
    END IF;
END
$$;

DROP TABLE IF EXISTS projectpulse_module_availability_audit;
DROP TABLE IF EXISTS projectpulse_module_availability;

COMMIT;
