-- Roll back Pulse AI Module 011 migration 054.
-- Removes only Phase 011E schema, permissions, feature registrations, and grants.

BEGIN;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN (
        'ASK_PULSE_AI_SYSTEM_OPERATIONS',
        'VIEW_PULSE_AI_SYSTEM_OPERATIONS',
        'RETEST_PULSE_AI_SAFE_API',
        'VIEW_PULSE_AI_OPERATIONS_HISTORY',
        'EXPORT_PULSE_AI_OPERATIONS_EVIDENCE',
        'PLAN_PULSE_AI_FUTURE_ENHANCEMENT'
    )
);

DELETE FROM app_feature_catalog
WHERE feature_code IN (
    'PULSE_AI_UNIFIED_LIVE_ANSWER',
    'PULSE_AI_SYSTEM_OPERATIONS_COPILOT',
    'PULSE_AI_FUTURE_ENHANCEMENT_PLANNER'
);

DELETE FROM app_permissions
WHERE permission_code IN (
    'ASK_PULSE_AI_SYSTEM_OPERATIONS',
    'VIEW_PULSE_AI_SYSTEM_OPERATIONS',
    'RETEST_PULSE_AI_SAFE_API',
    'VIEW_PULSE_AI_OPERATIONS_HISTORY',
    'EXPORT_PULSE_AI_OPERATIONS_EVIDENCE',
    'PLAN_PULSE_AI_FUTURE_ENHANCEMENT'
);

DROP TRIGGER IF EXISTS trg_pulse_ai_054_evidence_immutable
    ON pulse_ai_system_operations_evidence;
DROP TRIGGER IF EXISTS trg_pulse_ai_054_future_plans_updated_at
    ON pulse_ai_future_enhancement_plans;
DROP TRIGGER IF EXISTS trg_pulse_ai_054_investigations_updated_at
    ON pulse_ai_system_operations_investigations;

DROP FUNCTION IF EXISTS pulse_ai_054_block_evidence_mutation();
DROP FUNCTION IF EXISTS pulse_ai_054_touch_updated_at();

DROP TABLE IF EXISTS pulse_ai_system_operations_evidence;
DROP TABLE IF EXISTS pulse_ai_future_enhancement_plans;
DROP TABLE IF EXISTS pulse_ai_system_operations_investigations;

DELETE FROM schema_migrations
WHERE migration_id = '054_pulse_ai_system_operations_copilot';

COMMIT;
