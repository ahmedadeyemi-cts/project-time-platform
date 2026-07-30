-- Pulse AI Module 011 — Phase 011D
-- Private RAG orchestration, detailed answer evidence, citations, feedback, and
-- immutable retrieval audit for Timesheet, Help/Search, and FlowHive.
--
-- This migration does not configure a model endpoint, call a provider, change
-- Module 064, deploy an environment, or authorize autonomous business writes.

BEGIN;

CREATE TABLE IF NOT EXISTS pulse_ai_answer_runs (
    pulse_ai_answer_run_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    feature_code VARCHAR(120) NOT NULL,
    purpose_code VARCHAR(120) NOT NULL,
    answer_status VARCHAR(40) NOT NULL DEFAULT 'requested' CHECK (answer_status IN (
        'requested', 'retrieving', 'generating', 'completed', 'partial',
        'insufficient_evidence', 'blocked', 'failed', 'cancelled'
    )),
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    project_code VARCHAR(120) NOT NULL DEFAULT '',
    question_text TEXT NOT NULL DEFAULT '',
    question_sha256 VARCHAR(64) NOT NULL,
    request_filters_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    detail_level VARCHAR(40) NOT NULL DEFAULT 'comprehensive',
    private_model_provider VARCHAR(200) NOT NULL DEFAULT '',
    private_model_name VARCHAR(240) NOT NULL DEFAULT '',
    prompt_contract_version VARCHAR(160) NOT NULL,
    retrieval_contract_version VARCHAR(160) NOT NULL,
    retrieval_mode VARCHAR(60) NOT NULL DEFAULT 'lexical' CHECK (retrieval_mode IN (
        'none', 'lexical', 'semantic', 'hybrid', 'direct_knowledge', 'hybrid_plus_direct_knowledge'
    )),
    retrieved_chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (retrieved_chunk_count >= 0),
    cited_source_count INTEGER NOT NULL DEFAULT 0 CHECK (cited_source_count >= 0),
    source_document_count INTEGER NOT NULL DEFAULT 0 CHECK (source_document_count >= 0),
    source_version_count INTEGER NOT NULL DEFAULT 0 CHECK (source_version_count >= 0),
    input_character_count INTEGER NOT NULL DEFAULT 0 CHECK (input_character_count >= 0),
    output_character_count INTEGER NOT NULL DEFAULT 0 CHECK (output_character_count >= 0),
    confidence_score NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (confidence_score BETWEEN 0 AND 1),
    coverage_score NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (coverage_score BETWEEN 0 AND 1),
    citation_coverage_score NUMERIC(5,4) NOT NULL DEFAULT 0 CHECK (citation_coverage_score BETWEEN 0 AND 1),
    answer_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    warning_codes JSONB NOT NULL DEFAULT '[]'::JSONB,
    missing_evidence JSONB NOT NULL DEFAULT '[]'::JSONB,
    conflicts_json JSONB NOT NULL DEFAULT '[]'::JSONB,
    source_health_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    privacy_evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    diagnostic_message TEXT NOT NULL DEFAULT '',
    data_as_of TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_runs_effective_user
    ON pulse_ai_answer_runs(effective_user_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_runs_project
    ON pulse_ai_answer_runs(project_id, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_runs_feature
    ON pulse_ai_answer_runs(feature_code, answer_status, requested_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_runs_correlation
    ON pulse_ai_answer_runs(correlation_id);

CREATE TABLE IF NOT EXISTS pulse_ai_answer_citations (
    pulse_ai_answer_citation_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_answer_run_id UUID NOT NULL REFERENCES pulse_ai_answer_runs(pulse_ai_answer_run_id) ON DELETE CASCADE,
    chunk_id VARCHAR(64) NULL REFERENCES pulse_ai_document_chunks(chunk_id) ON DELETE SET NULL,
    project_intake_document_id UUID NULL REFERENCES project_intake_documents(project_intake_document_id) ON DELETE SET NULL,
    pulse_ai_document_version_id UUID NULL REFERENCES pulse_ai_document_versions(pulse_ai_document_version_id) ON DELETE SET NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    source_type VARCHAR(80) NOT NULL DEFAULT 'project_document',
    source_module VARCHAR(20) NOT NULL DEFAULT '011',
    document_category VARCHAR(80) NOT NULL DEFAULT '',
    document_version VARCHAR(300) NOT NULL DEFAULT '',
    original_file_name TEXT NOT NULL DEFAULT '',
    citation_anchor VARCHAR(500) NOT NULL DEFAULT '',
    page_number INTEGER NULL CHECK (page_number IS NULL OR page_number > 0),
    sheet_name VARCHAR(300) NULL,
    rank_order INTEGER NOT NULL CHECK (rank_order > 0),
    lexical_score NUMERIC(12,8) NOT NULL DEFAULT 0,
    semantic_score NUMERIC(12,8) NOT NULL DEFAULT 0,
    combined_score NUMERIC(12,8) NOT NULL DEFAULT 0,
    source_sha256 VARCHAR(64) NOT NULL DEFAULT '',
    text_sha256 VARCHAR(64) NOT NULL DEFAULT '',
    source_processed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(pulse_ai_answer_run_id, rank_order)
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_citations_run
    ON pulse_ai_answer_citations(pulse_ai_answer_run_id, rank_order);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_citations_document
    ON pulse_ai_answer_citations(project_intake_document_id, created_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_answer_feedback (
    pulse_ai_answer_feedback_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_answer_run_id UUID NOT NULL REFERENCES pulse_ai_answer_runs(pulse_ai_answer_run_id) ON DELETE CASCADE,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    feedback_type VARCHAR(40) NOT NULL CHECK (feedback_type IN (
        'accepted', 'accepted_with_edits', 'rejected', 'incorrect',
        'incomplete', 'unsafe', 'unauthorized_source', 'other'
    )),
    feedback_reason TEXT NOT NULL DEFAULT '',
    corrected_answer_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    training_candidate BOOLEAN NOT NULL DEFAULT FALSE,
    training_review_status VARCHAR(40) NOT NULL DEFAULT 'not_reviewed' CHECK (training_review_status IN (
        'not_reviewed', 'approved', 'rejected', 'needs_redaction', 'duplicate'
    )),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_feedback_run
    ON pulse_ai_answer_feedback(pulse_ai_answer_run_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_answer_feedback_training
    ON pulse_ai_answer_feedback(training_candidate, training_review_status, created_at DESC);

CREATE TABLE IF NOT EXISTS pulse_ai_retrieval_events (
    pulse_ai_retrieval_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pulse_ai_answer_run_id UUID NULL REFERENCES pulse_ai_answer_runs(pulse_ai_answer_run_id) ON DELETE SET NULL,
    actual_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    effective_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    feature_code VARCHAR(120) NOT NULL,
    event_code VARCHAR(120) NOT NULL,
    event_status VARCHAR(40) NOT NULL CHECK (event_status IN (
        'requested', 'succeeded', 'partial', 'blocked', 'failed', 'cancelled'
    )),
    retrieval_mode VARCHAR(60) NOT NULL DEFAULT 'none',
    candidate_count INTEGER NOT NULL DEFAULT 0 CHECK (candidate_count >= 0),
    authorized_candidate_count INTEGER NOT NULL DEFAULT 0 CHECK (authorized_candidate_count >= 0),
    returned_chunk_count INTEGER NOT NULL DEFAULT 0 CHECK (returned_chunk_count >= 0),
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    evidence_json JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_pulse_ai_retrieval_events_run
    ON pulse_ai_retrieval_events(pulse_ai_answer_run_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_retrieval_events_effective_user
    ON pulse_ai_retrieval_events(effective_user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_pulse_ai_retrieval_events_feature
    ON pulse_ai_retrieval_events(feature_code, event_status, created_at DESC);

CREATE OR REPLACE FUNCTION pulse_ai_053_touch_answer_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_053_touch_answer_updated_at_body$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$pulse_ai_053_touch_answer_updated_at_body$;

DROP TRIGGER IF EXISTS trg_pulse_ai_053_answer_runs_updated_at
    ON pulse_ai_answer_runs;
CREATE TRIGGER trg_pulse_ai_053_answer_runs_updated_at
BEFORE UPDATE ON pulse_ai_answer_runs
FOR EACH ROW EXECUTE FUNCTION pulse_ai_053_touch_answer_updated_at();

CREATE OR REPLACE FUNCTION pulse_ai_053_block_retrieval_event_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $pulse_ai_053_retrieval_event_immutable$
BEGIN
    RAISE EXCEPTION 'Pulse AI retrieval event evidence is immutable.';
END;
$pulse_ai_053_retrieval_event_immutable$;

DROP TRIGGER IF EXISTS trg_pulse_ai_053_retrieval_events_immutable
    ON pulse_ai_retrieval_events;
CREATE TRIGGER trg_pulse_ai_053_retrieval_events_immutable
BEFORE UPDATE OR DELETE ON pulse_ai_retrieval_events
FOR EACH ROW EXECUTE FUNCTION pulse_ai_053_block_retrieval_event_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('ASK_PULSE_AI_HELP_SEARCH', 'Ask Pulse AI Help and Search', '011', 'Ask detailed product, project, document, workflow, operational, reporting, and permitted financial questions using private retrieval and governed tools.'),
    ('USE_PULSE_AI_TIMESHEET_GROUNDING', 'Use Pulse AI Timesheet Grounding', '011', 'Generate private document-grounded Timesheet suggestions for work within the current assignment and document scope.'),
    ('USE_PULSE_AI_FLOWHIVE_PLANNING', 'Use Pulse AI FlowHive Planning', '011', 'Generate a cited draft WBS, milestones, dependencies, risks, assumptions, and timeline inputs for authorized projects.'),
    ('VIEW_PULSE_AI_ANSWER_AUDIT', 'View Pulse AI Answer Audit', '011', 'View private answer-run metadata, source citations, confidence, feedback, and sanitized retrieval evidence within the current scope.'),
    ('SUBMIT_PULSE_AI_FEEDBACK', 'Submit Pulse AI Feedback', '011', 'Accept, edit, reject, or report a Pulse AI answer without automatically turning the response into training data.')
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
    ('PULSE_AI_PRIVATE_HELP_SEARCH', 'Pulse AI Private Help and Search', '011', '#work-task-builder', 'ASK_PULSE_AI_HELP_SEARCH', 'Detailed private product, project-document, workflow, operational, reporting, and governed-system answers with citations and current authorization.', 116, TRUE),
    ('PULSE_AI_PRIVATE_TIMESHEET_GROUNDING', 'Pulse AI Private Timesheet Grounding', '011', '#timesheet', 'USE_PULSE_AI_TIMESHEET_GROUNDING', 'Engineer-reviewed Timesheet suggestions grounded in authorized current SOW, GSD, tasks, requests, and project documents.', 117, TRUE),
    ('PULSE_AI_PRIVATE_FLOWHIVE_PLANNING', 'Pulse AI Private FlowHive Planning', '066', '#project-flowhive', 'USE_PULSE_AI_FLOWHIVE_PLANNING', 'Cited private WBS, dependency, milestone, risk, assumption, and timeline draft generation for PM and Engineering review.', 118, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

-- Super Administrator and Administrator receive all private answer capabilities.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_HELP_SEARCH',
    'USE_PULSE_AI_TIMESHEET_GROUNDING',
    'USE_PULSE_AI_FLOWHIVE_PLANNING',
    'VIEW_PULSE_AI_ANSWER_AUDIT',
    'SUBMIT_PULSE_AI_FEEDBACK'
)
WHERE UPPER(role.role_code) IN ('SUPER_ADMINISTRATOR', 'ADMINISTRATOR')
ON CONFLICT DO NOTHING;

-- Project Team Coordinators and Project Management can use project intelligence
-- but remain limited by current project, document, module, and record scope.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_HELP_SEARCH',
    'USE_PULSE_AI_FLOWHIVE_PLANNING',
    'VIEW_PULSE_AI_ANSWER_AUDIT',
    'SUBMIT_PULSE_AI_FEEDBACK'
)
WHERE UPPER(role.role_code) IN (
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PM_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

-- Engineering users receive Timesheet grounding, Help/Search, and feedback.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_HELP_SEARCH',
    'USE_PULSE_AI_TIMESHEET_GROUNDING',
    'SUBMIT_PULSE_AI_FEEDBACK'
)
WHERE UPPER(role.role_code) IN (
    'ENGINEER',
    'ENGINEERING',
    'ENGINEERING_LEAD',
    'ENGINEERING_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

-- Other authenticated business roles can use product Help/Search and provide
-- feedback only when the module/action policy grants Module 011 access.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code IN (
    'ASK_PULSE_AI_HELP_SEARCH',
    'SUBMIT_PULSE_AI_FEEDBACK'
)
WHERE UPPER(role.role_code) IN (
    'EXECUTIVE', 'ACCOUNTING', 'FINANCE', 'SALES', 'INSIDE_SALES',
    'SOLUTION_ARCHITECT', 'RESOURCE_MANAGER', 'OPERATIONS'
)
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '053_pulse_ai_private_rag_orchestration',
    'Pulse AI private RAG answer runs, citations, feedback, immutable retrieval evidence, and permissions for Timesheet, Help/Search, and FlowHive',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
