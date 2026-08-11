-- Guarded rollback for migration 084.
-- Refuse destructive rollback when any durable defect, intake, evidence,
-- monitoring, suppression, or notification record exists.

BEGIN;

DO $pulse084_rollback_guard$
DECLARE
    populated_tables TEXT[] := ARRAY[]::TEXT[];
    table_name TEXT;
    row_exists BOOLEAN;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'module076_defects',
        'module076_defect_comments',
        'module076_defect_events',
        'module076_defect_evidence',
        'module076_intake_sessions',
        'module076_incident_occurrences',
        'module076_probe_results',
        'module076_monitor_suppressions',
        'module076_notification_outbox'
    ]
    LOOP
        IF to_regclass('public.' || table_name) IS NOT NULL THEN
            EXECUTE format('SELECT EXISTS (SELECT 1 FROM %I LIMIT 1)', table_name)
            INTO row_exists;
            IF row_exists THEN
                populated_tables := array_append(populated_tables, table_name);
            END IF;
        END IF;
    END LOOP;

    IF cardinality(populated_tables) > 0 THEN
        RAISE EXCEPTION
            'Migration 084 rollback refused because durable evidence exists in: %',
            array_to_string(populated_tables, ', ');
    END IF;
END;
$pulse084_rollback_guard$;

DROP TRIGGER IF EXISTS trg_module076_probe_results_immutable_084 ON module076_probe_results;
DROP TRIGGER IF EXISTS trg_module076_incident_occurrences_immutable_084 ON module076_incident_occurrences;
DROP TRIGGER IF EXISTS trg_module076_defect_evidence_immutable_084 ON module076_defect_evidence;
DROP TRIGGER IF EXISTS trg_module076_defect_events_immutable_084 ON module076_defect_events;
DROP FUNCTION IF EXISTS pulse084_append_only_defect_evidence();

DROP TABLE IF EXISTS module076_notification_outbox;
DROP TABLE IF EXISTS module076_monitor_suppressions;
DROP TABLE IF EXISTS module076_probe_results;
DROP TABLE IF EXISTS module076_monitor_policies;
DROP TABLE IF EXISTS module076_incident_occurrences;
DROP TABLE IF EXISTS module076_intake_sessions;
DROP TABLE IF EXISTS module076_defect_evidence;
DROP TABLE IF EXISTS module076_defect_events;
DROP TABLE IF EXISTS module076_defect_comments;
DROP TABLE IF EXISTS module076_defects;
DROP SEQUENCE IF EXISTS module076_defect_number_sequence;

DELETE FROM schema_migrations
WHERE migration_id='084_module_076_celar_ai_defect_operations';

COMMIT;
