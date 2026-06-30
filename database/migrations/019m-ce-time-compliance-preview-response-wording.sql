-- 019M-CE Time Compliance Preview Response Wording
-- Safely removes production-facing dry-run wording from time-compliance,
-- reminder, and notification rule text columns when those tables exist.

BEGIN;

DO $$
DECLARE
    c record;
BEGIN
    FOR c IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND data_type IN ('text', 'character varying', 'character')
          AND (
            table_name ILIKE '%time%compliance%'
            OR table_name ILIKE '%reminder%'
            OR table_name ILIKE '%notification%'
          )
    LOOP
        EXECUTE format(
            'UPDATE %I.%I
             SET %I = REPLACE(
                       REPLACE(
                       REPLACE(
                       REPLACE(
                       REPLACE(
                       REPLACE(%I,
                         %L, %L),
                         %L, %L),
                         %L, %L),
                         %L, %L),
                         %L, %L),
                         %L, %L)
             WHERE %I ILIKE %L
                OR %I ILIKE %L
                OR %I ILIKE %L',
            c.table_schema,
            c.table_name,
            c.column_name,
            c.column_name,
            'Dry-run preview required before real send.', 'Notification preview required before real send.',
            'dry-run preview required before real send.', 'notification preview required before real send.',
            'Dry-run only. No email was sent.', 'Notification preview only. No email was sent.',
            'Dry-run notification records were created. No email was sent.', 'Notification preview records were created. No email was sent.',
            'Dry-run notification', 'Notification preview',
            'dry-run notification', 'notification preview',
            c.column_name, '%Dry-run%',
            c.column_name, '%dry-run%',
            c.column_name, '%Dry Run%'
        );
    END LOOP;
END $$;

COMMIT;
