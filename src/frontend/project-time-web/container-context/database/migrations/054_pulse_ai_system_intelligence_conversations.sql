-- Pulse AI Module 011 — Phase 011E
-- Durable system-intelligence conversations, live API discovery evidence,
-- troubleshooting tool runs, and future-enhancement analysis.
--
-- This migration creates ProjectPulse-owned metadata only. It does not enable a
-- model, call an external provider, execute a diagnostic retest, change Azure,
-- or deploy an environment.

BEGIN;

CREATE TABLE IF NOT EXISTS pulse_ai_conversations (
    pulse_ai_conversation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE CASCADE,
    conversation_mode VARCHAR(50) NOT NULL DEFAULT 'system_help' CHECK (conversation_mode IN (
        'system_help', 'api_inventory', 'troubleshooting',
        'future_enhancement', 'project_intelligence', 'general'
    )),
    title VARCHAR(240) NOT NULL DEFAULT 'New Pulse AI conversation',
    status VARCHAR(30) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'archived')),
    scope_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    message_count INTEGER NOT NULL DEFAULT 0 CHECK (message_count >= 0),
    last_message_at TIMESTAMPTZ NULL,
    retention_until TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversations_user
    ON pulse_ai_conversations(effective_user_id, status, updated_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversations_actual_user
    ON pulse_ai_conversations(actual_user_id, updated_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_conversation_messages (
    pulse_ai_conversation_message_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_conversation_id UUID NOT NULL REFERENCES pulse_ai_conversations(pulse_ai_conversation_id) ON DELETE CASCADE,
    sequence_number INTEGER NOT NULL CHECK (sequence_number > 0),
    role VARCHAR(20) NOT NULL CHECK (role IN ('user', 'assistant', 'system')),
    message_status VARCHAR(30) NOT NULL DEFAULT 'completed' CHECK (message_status IN (
        'queued', 'completed', 'partial', 'failed', 'blocked'
    )),
    message_text TEXT NOT NULL DEFAULT '',
    structured_response_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    system_inquiry_run_id UUID NULL,
    private_answer_run_id UUID NULL,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    model_provider VARCHAR(240) NOT NULL DEFAULT '',
    model_name VARCHAR(240) NOT NULL DEFAULT '',
    tool_codes_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    source_states_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    data_as_of TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_conversation_id, sequence_number)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_messages_conversation
    ON pulse_ai_conversation_messages(pulse_ai_conversation_id, sequence_number);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_conversation_messages_correlation
    ON pulse_ai_conversation_messages(correlation_id)
    WHERE correlation_id <> '';

CREATE TABLE IF NOT EXISTS pulse_ai_system_inquiry_runs (
    pulse_ai_system_inquiry_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_conversation_id UUID NULL REFERENCES pulse_ai_conversations(pulse_ai_conversation_id) ON DELETE SET NULL,
    user_message_id UUID NULL REFERENCES pulse_ai_conversation_messages(pulse_ai_conversation_message_id) ON DELETE SET NULL,
    assistant_message_id UUID NULL REFERENCES pulse_ai_conversation_messages(pulse_ai_conversation_message_id) ON DELETE SET NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    intent_code VARCHAR(80) NOT NULL,
    detail_level VARCHAR(50) NOT NULL DEFAULT 'comprehensive',
    question_sha256 VARCHAR(64) NOT NULL,
    selected_tools_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    tool_summary_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    answer_status VARCHAR(30) NOT NULL DEFAULT 'running' CHECK (answer_status IN (
        'running', 'completed', 'partial', 'failed', 'blocked'
    )),
    registered_api_count INTEGER NOT NULL DEFAULT 0 CHECK (registered_api_count >= 0),
    successful_tool_count INTEGER NOT NULL DEFAULT 0 CHECK (successful_tool_count >= 0),
    failed_tool_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_tool_count >= 0),
    confidence NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (confidence BETWEEN 0 AND 1),
    diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
    correlation_id VARCHAR(160) NOT NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_inquiry_runs_user
    ON pulse_ai_system_inquiry_runs(effective_user_id, started_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_inquiry_runs_conversation
    ON pulse_ai_system_inquiry_runs(pulse_ai_conversation_id, started_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS ux_pulse_ai_system_inquiry_runs_correlation
    ON pulse_ai_system_inquiry_runs(correlation_id);

CREATE TABLE IF NOT EXISTS pulse_ai_system_tool_events (
    pulse_ai_system_tool_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_system_inquiry_run_id UUID NOT NULL REFERENCES pulse_ai_system_inquiry_runs(pulse_ai_system_inquiry_run_id) ON DELETE CASCADE,
    tool_code VARCHAR(120) NOT NULL,
    module_code VARCHAR(20) NOT NULL DEFAULT '',
    method VARCHAR(12) NOT NULL DEFAULT 'GET',
    path VARCHAR(1000) NOT NULL DEFAULT '',
    event_status VARCHAR(30) NOT NULL CHECK (event_status IN (
        'succeeded', 'partial', 'failed', 'forbidden', 'not_found', 'skipped'
    )),
    status_code INTEGER NOT NULL DEFAULT 0 CHECK (status_code BETWEEN 0 AND 599),
    duration_ms NUMERIC(14,3) NOT NULL DEFAULT 0 CHECK (duration_ms >= 0),
    response_bytes INTEGER NOT NULL DEFAULT 0 CHECK (response_bytes >= 0),
    diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    observed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_tool_events_run
    ON pulse_ai_system_tool_events(pulse_ai_system_inquiry_run_id, observed_at);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_tool_events_status
    ON pulse_ai_system_tool_events(event_status, observed_at DESC);

CREATE OR REPLACE FUNCTION pulse_ai_054_touch_conversation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_touch_conversation_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_054_touch_conversation_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_conversation_updated_at
    ON pulse_ai_conversations;
CREATE TRIGGER trg_pulse_ai_054_conversation_updated_at
BEFORE UPDATE ON pulse_ai_conversations
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_touch_conversation();

CREATE OR REPLACE FUNCTION pulse_ai_054_touch_inquiry_run()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_touch_inquiry_run_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_054_touch_inquiry_run_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_inquiry_run_updated_at
    ON pulse_ai_system_inquiry_runs;
CREATE TRIGGER trg_pulse_ai_054_inquiry_run_updated_at
BEFORE UPDATE ON pulse_ai_system_inquiry_runs
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_touch_inquiry_run();

CREATE OR REPLACE FUNCTION pulse_ai_054_block_tool_event_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_tool_event_immutable$
BEGIN
    RAISE EXCEPTION 'Pulse AI system tool evidence is immutable.';
END;
$pulse_ai_054_tool_event_immutable$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_tool_events_immutable
    ON pulse_ai_system_tool_events;
CREATE TRIGGER trg_pulse_ai_054_tool_events_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_system_tool_events
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_block_tool_event_mutation();

CREATE OR REPLACE FUNCTION pulse_ai_054_increment_conversation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_increment_conversation_body$
BEGIN
    UPDATE pulse_ai_conversations
    SET message_count = message_count + 1,
        last_message_at = NEW.created_at,
        updated_at = NOW(),
        title = CASE
            WHEN NEW.role = 'user'
             AND (title = 'New Pulse AI conversation' OR BTRIM(title) = '')
            THEN LEFT(REGEXP_REPLACE(BTRIM(NEW.message_text), '\s+', ' ', 'g'), 240)
            ELSE title
        END
    WHERE pulse_ai_conversation_id = NEW.pulse_ai_conversation_id;
    RETURN NEW;
END;
$pulse_ai_054_increment_conversation_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_message_insert
    ON pulse_ai_conversation_messages;
CREATE TRIGGER trg_pulse_ai_054_message_insert
AFTER INSERT ON pulse_ai_conversation_messages
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_increment_conversation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('ASK_PULSE_AI_SYSTEM_INTELLIGENCE', 'Ask Pulse AI System Intelligence', '011', 'Ask detailed questions across authorized Pulse modules, runtime evidence, APIs, and architecture.'),
    ('VIEW_PULSE_AI_API_INVENTORY', 'View Pulse AI API Inventory', '011', 'View registered runtime API routes, methods, module ownership, and safe diagnostic capability.'),
    ('USE_PULSE_AI_SYSTEM_TROUBLESHOOTING', 'Use Pulse AI System Troubleshooting', '011', 'Run governed read-only diagnostic tools and receive source-grounded troubleshooting guidance.'),
    ('USE_PULSE_AI_ENHANCEMENT_ADVISOR', 'Use Pulse AI Enhancement Advisor', '011', 'Generate architecture-aware future enhancement blueprints without changing the system.'),
    ('VIEW_PULSE_AI_CONVERSATION_HISTORY', 'View Pulse AI Conversation History', '011', 'View the current user’s durable Pulse AI conversations and responses.'),
    ('RETEST_PULSE_AI_SAFE_API', 'Retest a Safe Pulse API', '011', 'Execute an explicitly confirmed read-only same-origin API retest and preserve diagnostic evidence.'),
    ('VIEW_PULSE_AI_SYSTEM_AUDIT', 'View Pulse AI System Audit', '011', 'Review system inquiry, tool, source-health, and correlation evidence.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_feature_catalog (
    feature_code,
    feature_name,
    module_code,
    route_anchor,
    required_permission_code,
    feature_description,
    display_order,
    is_active
)
VALUES
    ('PULSE_AI_SYSTEM_INTELLIGENCE', 'Pulse AI System Intelligence', '011', '#work-task-builder', 'ASK_PULSE_AI_SYSTEM_INTELLIGENCE', 'Detailed cross-system answers using authorized runtime, API, architecture, document, and business evidence.', 1110, TRUE),
    ('PULSE_AI_API_DISCOVERY', 'Pulse AI API Discovery', '011', '#work-task-builder', 'VIEW_PULSE_AI_API_INVENTORY', 'Live inventory of registered Pulse APIs with module ownership and safe retest boundaries.', 1120, TRUE),
    ('PULSE_AI_SYSTEM_TROUBLESHOOTING', 'Pulse AI System Troubleshooting', '011', '#work-task-builder', 'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING', 'Governed diagnostic reasoning across Module 013, Module 016, Module 078, Module 998, releases, defects, and dependencies.', 1130, TRUE),
    ('PULSE_AI_ENHANCEMENT_ADVISOR', 'Pulse AI Enhancement Advisor', '011', '#work-task-builder', 'USE_PULSE_AI_ENHANCEMENT_ADVISOR', 'Current-state-aware architecture and implementation blueprints for future Pulse enhancements.', 1140, TRUE),
    ('PULSE_AI_CONVERSATIONS', 'Pulse AI Durable Conversations', '011', '#work-task-builder', 'VIEW_PULSE_AI_CONVERSATION_HISTORY', 'Durable user-scoped conversations so questions and answers remain available after close, navigation, or refresh.', 1150, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Super Administrators, Administrators, and Project Team Coordinators receive
-- the complete Module 011 system-intelligence operating surface.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE UPPER(role.role_code) IN (
    'SUPER_ADMINISTRATOR', 'ADMINISTRATOR', 'PROJECT_TEAM_COORDINATOR'
)
  AND permission.permission_code IN (
    'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
    'VIEW_PULSE_AI_API_INVENTORY',
    'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
    'VIEW_PULSE_AI_CONVERSATION_HISTORY',
    'RETEST_PULSE_AI_SAFE_API',
    'VIEW_PULSE_AI_SYSTEM_AUDIT'
  )
ON CONFLICT DO NOTHING;

-- Engineering, Security, Release, and management leadership receive read-only
-- system intelligence and troubleshooting. Safe retest remains separately
-- assigned to elevated operational roles.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
    'VIEW_PULSE_AI_API_INVENTORY',
    'USE_PULSE_AI_SYSTEM_TROUBLESHOOTING',
    'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
    'VIEW_PULSE_AI_CONVERSATION_HISTORY',
    'VIEW_PULSE_AI_SYSTEM_AUDIT'
)
WHERE UPPER(role.role_code) IN (
    'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD', 'MANAGER',
    'SECURITY_ADMINISTRATOR', 'SECURITY_ANALYST', 'SECURITY_OPERATIONS',
    'RELEASE_MANAGER', 'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD', 'PM_TEAM_LEAD', 'EXECUTIVE'
)
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'RETEST_PULSE_AI_SAFE_API'
)
WHERE UPPER(role.role_code) IN (
    'ENGINEERING_LEAD', 'ENGINEERING_TEAM_LEAD',
    'SECURITY_ADMINISTRATOR', 'SECURITY_OPERATIONS', 'RELEASE_MANAGER'
)
ON CONFLICT DO NOTHING;

-- General delivery roles can ask questions, retain their own conversations, and
-- prepare future-enhancement proposals. Owning module APIs still enforce record
-- and field-level access before data is returned.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_SYSTEM_INTELLIGENCE',
    'USE_PULSE_AI_ENHANCEMENT_ADVISOR',
    'VIEW_PULSE_AI_CONVERSATION_HISTORY'
)
WHERE UPPER(role.role_code) IN (
    'ENGINEERING', 'ENGINEER', 'PROJECT_MANAGER', 'PROJECT_MANAGEMENT',
    'SOLUTION_ARCHITECT', 'SALES_ENGINEERING', 'SALES', 'INSIDE_SALES',
    'ACCOUNT_EXECUTIVE', 'ACCOUNTING', 'ACCOUNTING_BILLING', 'FINANCE'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '054_pulse_ai_system_intelligence_conversations',
    'Module 011 durable conversations, live API discovery, troubleshooting evidence, and future enhancement intelligence',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
