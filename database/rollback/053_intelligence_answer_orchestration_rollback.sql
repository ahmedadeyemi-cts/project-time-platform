-- Roll back Pulse AI Module 011 migration 053.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'ASK_PULSE_AI_HELP_SEARCH',
        'USE_PULSE_AI_TIMESHEET_GROUNDING',
        'USE_PULSE_AI_FLOWHIVE_PLANNING',
        'VIEW_PULSE_AI_ANSWER_AUDIT',
        'SUBMIT_PULSE_AI_FEEDBACK'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'PULSE_AI_PRIVATE_HELP_SEARCH',
    'PULSE_AI_PRIVATE_TIMESHEET_GROUNDING',
    'PULSE_AI_PRIVATE_FLOWHIVE_PLANNING'
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'ASK_PULSE_AI_HELP_SEARCH',
    'USE_PULSE_AI_TIMESHEET_GROUNDING',
    'USE_PULSE_AI_FLOWHIVE_PLANNING',
    'VIEW_PULSE_AI_ANSWER_AUDIT',
    'SUBMIT_PULSE_AI_FEEDBACK'
);

DROP TRIGGER IF EXISTS trg_pulse_ai_053_retrieval_events_immutable
    ON pulse_ai_retrieval_events;
DROP FUNCTION IF EXISTS pulse_ai_053_block_retrieval_event_mutation();

DROP TRIGGER IF EXISTS trg_pulse_ai_053_answer_runs_updated_at
    ON pulse_ai_answer_runs;
DROP FUNCTION IF EXISTS pulse_ai_053_touch_answer_updated_at();

DROP TABLE IF EXISTS pulse_ai_retrieval_events;
DROP TABLE IF EXISTS pulse_ai_answer_feedback;
DROP TABLE IF EXISTS pulse_ai_answer_citations;
DROP TABLE IF EXISTS pulse_ai_answer_runs;

DELETE FROM schema_migrations
WHERE migration_id = '053_pulse_ai_private_rag_orchestration';

COMMIT;
