-- Roll back Pulse AI Module 011 migration 054.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
        'VIEW_PULSE_AI_API_INVENTORY',
        'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
        'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
        'VIEW_PULSE_AI_CONVERSATION_HISTORY',
        'RETEST_PULSE_AI_SAFE_API',
        'VIEW_PULSE_AI_SYSTEM_AUDIT'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'PULSE_AI_SYSTEM_INTELLIGENCE',
    'PULSE_AI_API_DISCOVERY',
    'PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'PULSE_AI_ENHANCEMENT_ADVISOR',
    'PULSE_AI_CONVERSATIONS'
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
    'VIEW_PULSE_AI_API_INVENTORY',
    'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
    'VIEW_PULSE_AI_CONVERSATION_HISTORY',
    'RETEST_PULSE_AI_SAFE_API',
    'VIEW_PULSE_AI_SYSTEM_AUDIT'
);

DROP TRIGGER IF EXISTS trg_pulse_ai_054_message_insert
    ON pulse_ai_conversation_messages;
DROP FUNCTION IF EXISTS pulse_ai_054_increment_conversation();

DROP TRIGGER IF EXISTS trg_pulse_ai_054_tool_events_immutable
    ON pulse_ai_system_tool_events;
DROP FUNCTION IF EXISTS pulse_ai_054_block_tool_event_mutation();

DROP TRIGGER IF EXISTS trg_pulse_ai_054_inquiry_run_updated_at
    ON pulse_ai_system_inquiry_runs;
DROP FUNCTION IF EXISTS pulse_ai_054_touch_inquiry_run();

DROP TRIGGER IF EXISTS trg_pulse_ai_054_conversation_updated_at
    ON pulse_ai_conversations;
DROP FUNCTION IF EXISTS pulse_ai_054_touch_conversation();

DROP TABLE IF EXISTS pulse_ai_system_tool_events;
DROP TABLE IF EXISTS pulse_ai_system_inquiry_runs;
DROP TABLE IF EXISTS pulse_ai_conversation_messages;
DROP TABLE IF EXISTS pulse_ai_conversations;

DELETE FROM schema_migrations
WHERE migration_id = '054_pulse_ai_system_intelligence_conversations';

COMMIT;
