-- Pulse AI Module 011 — Phase 011E
-- Live system-operations copilot, API inventory investigations, immutable
-- operational citations, and future-enhancement draft plans.
--
-- This migration does not run a diagnostic, retest an API, deploy code, restart
-- a service, change infrastructure, configure a model, call a provider, or
-- authorize Pulse AI to perform production-changing remediation.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS pulse_ai_system_operations_investigations (
    pulse_ai_system_operations_investigation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    intent_code VARCHAR(80) NOT NULL,
    investigation_status VARCHAR(40) NOT NULL DEFAULT 'requested' CHECK (investigation_status IN (
        'requested', 'completed', 'partial', 'insufficient_evidence', 'blocked', 'failed', 'cancelled'
    )),
    sanitized_question TEXT NOT NULL DEFAULT '',
    question_sha256 VARCHAR(64) NOT NULL,
    classification_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    direct_conclusion TEXT NOT NULL DEFAULT '',
    answer_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    api_count INTEGER NOT NULL DEFAULT 0 CHECK (api_count >= 0),
    evidence_count INTEGER NOT NULL DEFAULT 0 CHECK (evidence_count >= 0),
    finding_count INTEGER NOT NULL DEFAULT 0 CHECK (finding_count >= 0),
    dependency_count INTEGER NOT NULL DEFAULT 0 CHECK (dependency_count >= 0),
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    release_sha VARCHAR(160) NOT NULL DEFAULT '',
    data_as_of TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_actual_user
    ON pulse_ai_system_operations_investigations(actual_user_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_effective_user
    ON pulse_ai_system_operations_investigations(effective_user_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_intent
    ON pulse_ai_system_operations_investigations(intent_code, investigation_status, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_correlation
    ON pulse_ai_system_operations_investigations(correlation_id);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_question_sha
    ON pulse_ai_system_operations_investigations(question_sha256);

CREATE TABLE IF NOT EXISTS pulse_ai_system_operations_evidence (
    pulse_ai_system_operations_evidence_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_system_operations_investigation_id UUID NOT NULL
        REFERENCES pulse_ai_system_operations_investigations(pulse_ai_system_operations_investigation_id)
        ON DELETE CASCADE,
    rank_order INTEGER NOT NULL CHECK (rank_order > 0),
    evidence_type VARCHAR(60) NOT NULL,
    source_module VARCHAR(20) NOT NULL DEFAULT '',
    source_name VARCHAR(300) NOT NULL DEFAULT '',
    api_id VARCHAR(300) NOT NULL DEFAULT '',
    method VARCHAR(16) NOT NULL DEFAULT '',
    path VARCHAR(500) NOT NULL DEFAULT '',
    evidence_status VARCHAR(40) NOT NULL DEFAULT '',
    status_code INTEGER NULL CHECK (status_code IS NULL OR status_code BETWEEN 0 AND 599),
    response_time_ms DOUBLE PRECISION NULL CHECK (response_time_ms IS NULL OR response_time_ms >= 0),
    error_code VARCHAR(160) NOT NULL DEFAULT '',
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    observed_at TIMESTAMPTZ NULL,
    release_sha VARCHAR(160) NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_system_operations_investigation_id, rank_order)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_evidence_run
    ON pulse_ai_system_operations_evidence(pulse_ai_system_operations_investigation_id, rank_order);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_evidence_api
    ON pulse_ai_system_operations_evidence(api_id, observed_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_evidence_path
    ON pulse_ai_system_operations_evidence(method, path, observed_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_system_operations_evidence_correlation
    ON pulse_ai_system_operations_evidence(correlation_id, observed_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_future_enhancement_plans (
    pulse_ai_future_enhancement_plan_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    plan_status VARCHAR(40) NOT NULL DEFAULT 'draft' CHECK (plan_status IN (
        'draft', 'under_review', 'approved', 'rejected', 'implemented', 'retired'
    )),
    title VARCHAR(300) NOT NULL,
    sanitized_request TEXT NOT NULL DEFAULT '',
    request_sha256 VARCHAR(64) NOT NULL,
    affected_modules_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    plan_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    decision_note TEXT NOT NULL DEFAULT '',
    reviewed_by UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    reviewed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_future_enhancement_user
    ON pulse_ai_future_enhancement_plans(actual_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_future_enhancement_status
    ON pulse_ai_future_enhancement_plans(plan_status, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_future_enhancement_request_sha
    ON pulse_ai_future_enhancement_plans(request_sha256);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_future_enhancement_modules
    ON pulse_ai_future_enhancement_plans USING GIN(affected_modules_json);

CREATE OR REPLACE FUNCTION pulse_ai_054_touch_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_touch_updated_at_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_054_touch_updated_at_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_investigations_updated_at
    ON pulse_ai_system_operations_investigations;
CREATE TRIGGER trg_pulse_ai_054_investigations_updated_at
BEFORE UPDATE ON pulse_ai_system_operations_investigations
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_touch_updated_at();

DROP TRIGGER IF EXISTS trg_pulse_ai_054_future_plans_updated_at
    ON pulse_ai_future_enhancement_plans;
CREATE TRIGGER trg_pulse_ai_054_future_plans_updated_at
BEFORE UPDATE ON pulse_ai_future_enhancement_plans
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_touch_updated_at();

CREATE OR REPLACE FUNCTION pulse_ai_054_block_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_054_evidence_immutable$
BEGIN
    RAISE EXCEPTION 'Pulse AI system operations evidence is immutable.';
END;
$pulse_ai_054_evidence_immutable$;

DROP TRIGGER IF EXISTS trg_pulse_ai_054_evidence_immutable
    ON pulse_ai_system_operations_evidence;
CREATE TRIGGER trg_pulse_ai_054_evidence_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_system_operations_evidence
FOR EACH ROW EXECUTE FUNCTION pulse_ai_054_block_evidence_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('ASK_PULSE_AI_SYSTEM_OPERATIONS', 'Ask Pulse AI System Operations', '011', 'Ask comprehensive live questions about registered APIs, health, failures, latency, correlation evidence, dependencies, workers, integrations, releases, and troubleshooting.'),
    ('VIEW_PULSE_AI_SYSTEM_OPERATIONS', 'View Pulse AI System Operations', '011', 'View the live API inventory, sanitized operational evidence, current runtime, dependencies, workers, integrations, and persistent diagnostic findings.'),
    ('RETEST_PULSE_AI_SAFE_API', 'Retest a Safe Pulse API', '011', 'Run an explicitly confirmed same-origin GET retest for an eligible API without reading its response body or performing a business mutation.'),
    ('VIEW_PULSE_AI_OPERATIONS_HISTORY', 'View Pulse AI Operations History', '011', 'Review the current user''s persisted system-operations investigations and immutable sanitized evidence.'),
    ('EXPORT_PULSE_AI_OPERATIONS_EVIDENCE', 'Export Pulse AI Operations Evidence', '011', 'Export sanitized API inventory and operations evidence without request bodies, query strings, raw logs, exception text, or secrets.'),
    ('PLAN_PULSE_AI_FUTURE_ENHANCEMENT', 'Plan a Pulse Future Enhancement', '011', 'Create a comprehensive draft plan for a future Pulse capability, including current state, gaps, affected modules and APIs, architecture, data, permissions, security, testing, risks, and rollout.')
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
    ('PULSE_AI_UNIFIED_LIVE_ANSWER', 'Pulse AI Unified Live Answer', '011', '#work-task-builder', 'ASK_PULSE_AI_HELP_SEARCH', 'Routes every question to governed product knowledge, authorized private RAG, live system operations, module knowledge, or future-enhancement planning and returns a direct comprehensive answer rather than only an execution plan.', 119, TRUE),
    ('PULSE_AI_SYSTEM_OPERATIONS_COPILOT', 'Pulse AI System Operations Copilot', '011', '#work-task-builder', 'ASK_PULSE_AI_SYSTEM_OPERATIONS', 'Discovers the running API inventory, correlates sanitized evidence, explains health and dependencies, prepares troubleshooting steps, and identifies explicitly supported safe retests.', 120, TRUE),
    ('PULSE_AI_FUTURE_ENHANCEMENT_PLANNER', 'Pulse AI Future Enhancement Planner', '011', '#work-task-builder', 'PLAN_PULSE_AI_FUTURE_ENHANCEMENT', 'Builds a comprehensive draft architecture and implementation plan for future Pulse enhancements using the module catalog and authorized live API evidence.', 121, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Full system-operations authority remains administrative and security scoped.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_SYSTEM_OPERATIONS',
    'VIEW_PULSE_AI_SYSTEM_OPERATIONS',
    'RETEST_PULSE_AI_SAFE_API',
    'VIEW_PULSE_AI_OPERATIONS_HISTORY',
    'EXPORT_PULSE_AI_OPERATIONS_EVIDENCE',
    'PLAN_PULSE_AI_FUTURE_ENHANCEMENT'
)
WHERE UPPER(role.role_code) IN (
    'SUPER_ADMINISTRATOR',
    'ADMINISTRATOR',
    'SYSTEM_ADMINISTRATOR',
    'SECURITY_ADMINISTRATOR',
    'SECURITY_OPERATIONS'
)
ON CONFLICT DO NOTHING;

-- Security Analysts receive read-only system evidence and investigation history.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_SYSTEM_OPERATIONS',
    'VIEW_PULSE_AI_SYSTEM_OPERATIONS',
    'VIEW_PULSE_AI_OPERATIONS_HISTORY',
    'EXPORT_PULSE_AI_OPERATIONS_EVIDENCE',
    'PLAN_PULSE_AI_FUTURE_ENHANCEMENT'
)
WHERE UPPER(role.role_code) IN ('SECURITY_ANALYST')
ON CONFLICT DO NOTHING;

-- Project, engineering, sales, finance, resource, and operations leaders may
-- plan future enhancements but do not receive privileged system API evidence.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'PLAN_PULSE_AI_FUTURE_ENHANCEMENT'
WHERE UPPER(role.role_code) IN (
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PM_TEAM_LEAD',
    'ENGINEERING_LEAD',
    'ENGINEERING_TEAM_LEAD',
    'SOLUTION_ARCHITECT',
    'RESOURCE_MANAGER',
    'OPERATIONS',
    'EXECUTIVE',
    'ACCOUNTING',
    'FINANCE',
    'SALES',
    'INSIDE_SALES'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '054_pulse_ai_system_operations_copilot',
    'Pulse AI live API inventory, system-operations investigations, immutable operational evidence, unified answers, and future-enhancement draft plans',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
