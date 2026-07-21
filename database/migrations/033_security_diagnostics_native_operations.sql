-- ProjectPulse migration 033: native security operations and diagnostic sessions.
-- Source-only until separately reviewed and applied to the target database.
BEGIN;

DO $$
BEGIN
    IF to_regclass('public.projectpulse_module_audit_events') IS NULL THEN
        RAISE EXCEPTION 'Migration 031 must be applied before migration 033.';
    END IF;
END $$;

ALTER TABLE projectpulse_module_audit_events
    DROP CONSTRAINT IF EXISTS ck_projectpulse_module_audit_module;

ALTER TABLE projectpulse_module_audit_events
    ADD CONSTRAINT ck_projectpulse_module_audit_module
    CHECK (module_number IN (
        '064','065','066','067','068','069','070','071','072','073','074',
        '075','076','077','078','079','080','997','998'
    ));

CREATE TABLE IF NOT EXISTS projectpulse_security_alerts
(
    alert_id uuid PRIMARY KEY,
    source_code varchar(75) NOT NULL,
    source_event_id varchar(200) NOT NULL,
    alert_type varchar(100) NOT NULL,
    title varchar(250) NOT NULL,
    summary varchar(1000) NOT NULL,
    severity varchar(20) NOT NULL,
    confidence smallint NOT NULL DEFAULT 50,
    status varchar(30) NOT NULL DEFAULT 'open',
    subject_user_id uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    source_ip varchar(100) NULL,
    resource_type varchar(100) NULL,
    resource_id varchar(200) NULL,
    correlation_key varchar(250) NULL,
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    observed_at timestamptz NOT NULL,
    first_seen_at timestamptz NOT NULL DEFAULT now(),
    last_seen_at timestamptz NOT NULL DEFAULT now(),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_projectpulse_security_alert_source UNIQUE (source_code, source_event_id),
    CONSTRAINT ck_projectpulse_security_alert_severity
        CHECK (severity IN ('informational','low','medium','high','critical')),
    CONSTRAINT ck_projectpulse_security_alert_confidence CHECK (confidence BETWEEN 0 AND 100),
    CONSTRAINT ck_projectpulse_security_alert_status
        CHECK (status IN ('open','triaged','investigating','contained','resolved','dismissed')),
    CONSTRAINT ck_projectpulse_security_alert_evidence CHECK (jsonb_typeof(evidence_json) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_security_alert_queue
    ON projectpulse_security_alerts(status, severity, last_seen_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_security_incidents
(
    incident_id uuid PRIMARY KEY,
    incident_number bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
    source_alert_id uuid NULL REFERENCES projectpulse_security_alerts(alert_id) ON DELETE SET NULL,
    title varchar(250) NOT NULL,
    description varchar(4000) NOT NULL,
    severity varchar(20) NOT NULL,
    status varchar(30) NOT NULL DEFAULT 'declared',
    owner_user_id uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    declared_by uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledged_by uuid NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    diagnostic_session_id uuid NULL,
    declared_at timestamptz NOT NULL DEFAULT now(),
    acknowledged_at timestamptz NULL,
    closed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_security_incident_severity
        CHECK (severity IN ('low','medium','high','critical')),
    CONSTRAINT ck_projectpulse_security_incident_status
        CHECK (status IN ('declared','acknowledged','investigating','containment_pending','contained','eradication','recovery','review','closed'))
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_security_incident_queue
    ON projectpulse_security_incidents(status, severity, updated_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_security_incident_events
(
    event_id uuid PRIMARY KEY,
    incident_id uuid NOT NULL REFERENCES projectpulse_security_incidents(incident_id) ON DELETE CASCADE,
    action_code varchar(100) NOT NULL,
    actor_user_id uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    note varchar(2000) NULL,
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    occurred_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_security_incident_event_evidence CHECK (jsonb_typeof(evidence_json) = 'object')
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_security_incident_timeline
    ON projectpulse_security_incident_events(incident_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_security_response_requests
(
    response_request_id uuid PRIMARY KEY,
    incident_id uuid NOT NULL REFERENCES projectpulse_security_incidents(incident_id) ON DELETE CASCADE,
    action_code varchar(75) NOT NULL,
    target_reference varchar(250) NOT NULL,
    reason varchar(2000) NOT NULL,
    state varchar(30) NOT NULL DEFAULT 'awaiting_approval',
    requested_by uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    approved_by uuid NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    executed_by uuid NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    requested_at timestamptz NOT NULL DEFAULT now(),
    approved_at timestamptz NULL,
    executed_at timestamptz NULL,
    result_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_projectpulse_security_response_action
        CHECK (action_code IN ('revoke_session','suspend_user','restrict_role','quarantine_integration','block_indicator')),
    CONSTRAINT ck_projectpulse_security_response_state
        CHECK (state IN ('awaiting_approval','approved','denied','executed','failed','cancelled')),
    CONSTRAINT ck_projectpulse_security_response_result CHECK (jsonb_typeof(result_json) = 'object'),
    CONSTRAINT ck_projectpulse_security_response_separation
        CHECK (approved_by IS NULL OR approved_by <> requested_by)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_security_response_queue
    ON projectpulse_security_response_requests(state, requested_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_diagnostic_sessions
(
    diagnostic_session_id uuid PRIMARY KEY,
    incident_id uuid NULL REFERENCES projectpulse_security_incidents(incident_id) ON DELETE SET NULL,
    target_kind varchar(75) NOT NULL,
    target_reference varchar(250) NOT NULL,
    status varchar(30) NOT NULL DEFAULT 'running',
    severity varchar(20) NOT NULL DEFAULT 'informational',
    summary varchar(2000) NULL,
    requested_by uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    closed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_diagnostic_target
        CHECK (target_kind IN ('platform','api','web','database','identity','integration','deployment','incident')),
    CONSTRAINT ck_projectpulse_diagnostic_status
        CHECK (status IN ('running','completed','attention_required','failed','closed')),
    CONSTRAINT ck_projectpulse_diagnostic_severity
        CHECK (severity IN ('informational','low','medium','high','critical'))
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_diagnostic_sessions_queue
    ON projectpulse_diagnostic_sessions(status, updated_at DESC);

CREATE TABLE IF NOT EXISTS projectpulse_diagnostic_findings
(
    diagnostic_finding_id uuid PRIMARY KEY,
    diagnostic_session_id uuid NOT NULL REFERENCES projectpulse_diagnostic_sessions(diagnostic_session_id) ON DELETE CASCADE,
    check_code varchar(100) NOT NULL,
    category varchar(75) NOT NULL,
    status varchar(30) NOT NULL,
    severity varchar(20) NOT NULL,
    summary varchar(1000) NOT NULL,
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    observed_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_projectpulse_diagnostic_finding_status
        CHECK (status IN ('healthy','warning','failed','unknown','not_applicable')),
    CONSTRAINT ck_projectpulse_diagnostic_finding_severity
        CHECK (severity IN ('informational','low','medium','high','critical')),
    CONSTRAINT ck_projectpulse_diagnostic_finding_evidence CHECK (jsonb_typeof(evidence_json) = 'object'),
    CONSTRAINT ux_projectpulse_diagnostic_finding UNIQUE (diagnostic_session_id, check_code)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_diagnostic_findings_session
    ON projectpulse_diagnostic_findings(diagnostic_session_id, observed_at);

CREATE TABLE IF NOT EXISTS projectpulse_remediation_requests
(
    remediation_request_id uuid PRIMARY KEY,
    diagnostic_session_id uuid NOT NULL REFERENCES projectpulse_diagnostic_sessions(diagnostic_session_id) ON DELETE CASCADE,
    runbook_code varchar(100) NOT NULL,
    action_code varchar(100) NOT NULL,
    target_reference varchar(250) NOT NULL,
    state varchar(30) NOT NULL DEFAULT 'awaiting_approval',
    requested_by uuid NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    approved_by uuid NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    executed_by uuid NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    plan_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    requested_at timestamptz NOT NULL DEFAULT now(),
    approved_at timestamptz NULL,
    executed_at timestamptz NULL,
    verified_at timestamptz NULL,
    closed_at timestamptz NULL,
    CONSTRAINT ck_projectpulse_remediation_state
        CHECK (state IN ('awaiting_approval','approved','staged','executed','verified','rolled_back','closed','denied','failed')),
    CONSTRAINT ck_projectpulse_remediation_plan CHECK (jsonb_typeof(plan_json) = 'object'),
    CONSTRAINT ck_projectpulse_remediation_result CHECK (jsonb_typeof(result_json) = 'object'),
    CONSTRAINT ck_projectpulse_remediation_separation
        CHECK (approved_by IS NULL OR approved_by <> requested_by)
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_remediation_queue
    ON projectpulse_remediation_requests(state, requested_at DESC);

ALTER TABLE projectpulse_security_incidents
    ADD CONSTRAINT fk_projectpulse_security_incident_diagnostic
    FOREIGN KEY (diagnostic_session_id)
    REFERENCES projectpulse_diagnostic_sessions(diagnostic_session_id)
    ON DELETE SET NULL;

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    ('VIEW_SECURITY_OPERATIONS', 'View Security Operations', 'security', 'View ProjectPulse security signals, alerts, incidents, and response evidence.'),
    ('MANAGE_SECURITY_RESPONSE', 'Manage Security Response', 'security', 'Declare and manage incidents and request governed containment.'),
    ('VIEW_SYSTEM_DIAGNOSTICS', 'View System Diagnostics', 'diagnostics', 'Run and review ProjectPulse diagnostic sessions and evidence.'),
    ('MANAGE_SYSTEM_REMEDIATION', 'Manage System Remediation', 'diagnostics', 'Prepare, approve, and verify governed remediation requests.')
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT r.app_role_id, p.app_permission_id
FROM app_roles r
JOIN app_permissions p ON p.permission_code = ANY(ARRAY[
    'VIEW_SECURITY_OPERATIONS','MANAGE_SECURITY_RESPONSE',
    'VIEW_SYSTEM_DIAGNOSTICS','MANAGE_SYSTEM_REMEDIATION'
])
WHERE r.role_code IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR')
ON CONFLICT DO NOTHING;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '033_security_diagnostics_native_operations',
    'Native Module 997 incidents and Module 998 diagnostic/remediation sessions',
    now()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
