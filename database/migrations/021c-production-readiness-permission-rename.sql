-- 021C Production Readiness Permission Rename
-- Preserves role access while standardizing the production readiness command-center permission code.
-- The legacy permission code appears here only as the source value being renamed.

DO $$
DECLARE
    table_record record;
BEGIN
    FOR table_record IN
        SELECT table_schema, table_name, column_name
        FROM information_schema.columns
        WHERE column_name IN ('permission_code', 'code')
          AND table_schema NOT IN ('pg_catalog', 'information_schema')
    LOOP
        EXECUTE format(
            'UPDATE %I.%I SET %I = %L WHERE %I = %L',
            table_record.table_schema,
            table_record.table_name,
            table_record.column_name,
            'VIEW_PRODUCTION_READINESS_COMMAND_CENTER',
            table_record.column_name,
            'VIEW_DEMO_READINESS_COMMAND_CENTER'
        );
    END LOOP;
END $$;
