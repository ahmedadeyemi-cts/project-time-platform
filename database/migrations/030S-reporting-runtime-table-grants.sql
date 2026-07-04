-- 030S-reporting-runtime-table-grants.sql
-- Purpose:
--   Grant the Project Health Dashboard runtime database role access to 030 reporting tables.
--   The 030 report preview endpoint reads from reporting_* tables. These tables
--   may be created by migrations under postgres ownership, so the runtime API
--   database user needs explicit privileges.
--
-- Scope:
--   Grants DML access to non-superuser login roles only. Application-level
--   authorization remains enforced by Project Health Dashboard roles and permissions.

DO $$
DECLARE
    role_record record;
    table_record record;
    sequence_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolcanlogin = TRUE
          AND rolsuper = FALSE
          AND rolname NOT LIKE 'pg_%'
          AND rolname <> 'postgres'
        ORDER BY rolname
    LOOP
        FOR table_record IN
            SELECT table_schema, table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
              AND table_name LIKE 'reporting_%'
            ORDER BY table_name
        LOOP
            EXECUTE format(
                'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE %I.%I TO %I',
                table_record.table_schema,
                table_record.table_name,
                role_record.rolname
            );
        END LOOP;

        FOR sequence_record IN
            SELECT sequence_schema, sequence_name
            FROM information_schema.sequences
            WHERE sequence_schema = 'public'
              AND sequence_name LIKE 'reporting_%'
            ORDER BY sequence_name
        LOOP
            EXECUTE format(
                'GRANT USAGE, SELECT, UPDATE ON SEQUENCE %I.%I TO %I',
                sequence_record.sequence_schema,
                sequence_record.sequence_name,
                role_record.rolname
            );
        END LOOP;
    END LOOP;
END $$;
