-- ProjectPulse migration 064
-- Module 065 enterprise notification orchestration.
--
-- This migration extends the durable Module 032 / Module 065 dispatch foundation
-- created by migration 050. It does not introduce another SMTP provider, store a
-- credential, or send a message while the migration is applied.

BEGIN;

DO $projectpulse064_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 064 requires public.schema_migrations.';
    END IF;
    IF to_regclass('public.project_notification_dispatches') IS NULL
       OR to_regclass('public.project_notification_dispatch_recipients') IS NULL
       OR to_regclass('public.project_notification_delivery_attempts') IS NULL THEN
        RAISE EXCEPTION 'Migration 064 requires migration 050 project-notification dispatch storage.';
    END IF;
    IF to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Migration 064 requires the ProjectPulse identity and RBAC schema.';
    END IF;
END;
$projectpulse064_prerequisites$;

CREATE TABLE IF NOT EXISTS enterprise_notification_policies (
    enterprise_notification_policy_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_code VARCHAR(160) NOT NULL UNIQUE,
    policy_name VARCHAR(240) NOT NULL,
    category VARCHAR(80) NOT NULL CHECK (category IN (
        'time', 'utilization', 'expense', 'project', 'financial', 'closeout',
        'identity', 'qualification', 'oncall', 'defect', 'operations', 'security'
    )),
    source_module VARCHAR(20) NOT NULL,
    event_code VARCHAR(180) NOT NULL,
    trigger_mode VARCHAR(40) NOT NULL CHECK (trigger_mode IN (
        'event', 'scheduled', 'threshold', 'escalation', 'native_worker'
    )),
    recipient_strategy VARCHAR(160) NOT NULL,
    trigger_configuration JSONB NOT NULL DEFAULT '{}'::JSONB,
    recipient_configuration JSONB NOT NULL DEFAULT '{}'::JSONB,
    severity VARCHAR(24) NOT NULL DEFAULT 'informational' CHECK (severity IN (
        'informational', 'warning', 'high', 'critical'
    )),
    delivery_boundary VARCHAR(40) NOT NULL DEFAULT 'test_only' CHECK (delivery_boundary IN (
        'test_only', 'production_governed', 'locked'
    )),
    acknowledgement_required BOOLEAN NOT NULL DEFAULT FALSE,
    acknowledgement_escalation_minutes INTEGER NULL CHECK (
        acknowledgement_escalation_minutes IS NULL
        OR acknowledgement_escalation_minutes BETWEEN 1 AND 43200
    ),
    subject_template TEXT NOT NULL,
    text_template TEXT NOT NULL,
    owner_module VARCHAR(20) NOT NULL DEFAULT '065',
    producer_contract VARCHAR(160) NOT NULL,
    source_state VARCHAR(40) NOT NULL DEFAULT 'contract_ready' CHECK (source_state IN (
        'scanner', 'signed_event', 'native_worker', 'contract_ready'
    )),
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_enterprise_notification_policies_category
    ON enterprise_notification_policies(category, enabled, source_module);

CREATE TABLE IF NOT EXISTS enterprise_notification_events (
    enterprise_notification_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    policy_code VARCHAR(160) NOT NULL REFERENCES enterprise_notification_policies(policy_code) ON DELETE RESTRICT,
    source_module VARCHAR(20) NOT NULL,
    source_event_id VARCHAR(320) NOT NULL,
    idempotency_key VARCHAR(420) NOT NULL UNIQUE,
    entity_type VARCHAR(120) NOT NULL DEFAULT '',
    entity_id UUID NULL,
    project_id UUID NULL REFERENCES projects(project_id) ON DELETE SET NULL,
    subject_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    payload JSONB NOT NULL DEFAULT '{}'::JSONB,
    ingestion_source VARCHAR(40) NOT NULL CHECK (ingestion_source IN (
        'authoritative_scanner', 'signed_api', 'native_bridge', 'manual_preview'
    )),
    event_status VARCHAR(40) NOT NULL DEFAULT 'pending' CHECK (event_status IN (
        'pending', 'processing', 'dispatched', 'suppressed', 'failed'
    )),
    dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    last_error_code VARCHAR(160) NOT NULL DEFAULT '',
    last_error_message TEXT NOT NULL DEFAULT '',
    processed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(policy_code, source_event_id)
);

CREATE INDEX IF NOT EXISTS ix_enterprise_notification_events_due
    ON enterprise_notification_events(event_status, available_at, created_at)
    WHERE event_status IN ('pending', 'failed');
CREATE INDEX IF NOT EXISTS ix_enterprise_notification_events_subject
    ON enterprise_notification_events(subject_user_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_enterprise_notification_events_project
    ON enterprise_notification_events(project_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS enterprise_notification_event_history (
    enterprise_notification_event_history_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    enterprise_notification_event_id UUID NOT NULL REFERENCES enterprise_notification_events(enterprise_notification_event_id) ON DELETE RESTRICT,
    history_code VARCHAR(120) NOT NULL,
    event_status VARCHAR(40) NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
    history_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_enterprise_notification_event_history_event
    ON enterprise_notification_event_history(enterprise_notification_event_id, created_at DESC);

CREATE TABLE IF NOT EXISTS enterprise_notification_acknowledgements (
    enterprise_notification_acknowledgement_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    enterprise_notification_event_id UUID NOT NULL REFERENCES enterprise_notification_events(enterprise_notification_event_id) ON DELETE RESTRICT,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledged_by_actual_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledgement_statement TEXT NOT NULL DEFAULT '',
    acknowledged_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(enterprise_notification_event_id, user_id)
);

CREATE TABLE IF NOT EXISTS enterprise_notification_source_checkpoints (
    source_code VARCHAR(160) PRIMARY KEY,
    source_module VARCHAR(20) NOT NULL,
    last_scan_started_at TIMESTAMPTZ NULL,
    last_scan_completed_at TIMESTAMPTZ NULL,
    last_successful_at TIMESTAMPTZ NULL,
    last_status VARCHAR(40) NOT NULL DEFAULT 'not_run' CHECK (last_status IN (
        'not_run', 'running', 'healthy', 'partial', 'unavailable', 'failed'
    )),
    last_diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
    last_message TEXT NOT NULL DEFAULT '',
    records_observed INTEGER NOT NULL DEFAULT 0 CHECK (records_observed >= 0),
    events_created INTEGER NOT NULL DEFAULT 0 CHECK (events_created >= 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS enterprise_notification_run_history (
    enterprise_notification_run_history_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    run_type VARCHAR(60) NOT NULL CHECK (run_type IN (
        'scheduled_worker', 'manual_run', 'signed_event', 'preview'
    )),
    started_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    run_status VARCHAR(40) NOT NULL DEFAULT 'running' CHECK (run_status IN (
        'running', 'completed', 'partial', 'failed'
    )),
    observed_count INTEGER NOT NULL DEFAULT 0 CHECK (observed_count >= 0),
    created_count INTEGER NOT NULL DEFAULT 0 CHECK (created_count >= 0),
    dispatched_count INTEGER NOT NULL DEFAULT 0 CHECK (dispatched_count >= 0),
    queued_count INTEGER NOT NULL DEFAULT 0 CHECK (queued_count >= 0),
    suppressed_count INTEGER NOT NULL DEFAULT 0 CHECK (suppressed_count >= 0),
    failed_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_count >= 0),
    source_states JSONB NOT NULL DEFAULT '[]'::JSONB,
    diagnostic_code VARCHAR(160) NOT NULL DEFAULT '',
    correlation_id VARCHAR(180) NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS ix_enterprise_notification_run_history_started
    ON enterprise_notification_run_history(started_at DESC);

CREATE TABLE IF NOT EXISTS enterprise_notification_policy_audit (
    enterprise_notification_policy_audit_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    enterprise_notification_policy_id UUID NOT NULL REFERENCES enterprise_notification_policies(enterprise_notification_policy_id) ON DELETE RESTRICT,
    action_code VARCHAR(120) NOT NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    change_reason TEXT NOT NULL,
    prior_state JSONB NULL,
    new_state JSONB NULL,
    correlation_id VARCHAR(180) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Migration evidence ensures rollback removes only RBAC relationships and catalog
-- rows created by migration 064. These evidence rows are immutable.
CREATE TABLE IF NOT EXISTS enterprise_notification_064_permissions_created (
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(160) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS enterprise_notification_064_role_grants (
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id, app_permission_id)
);

CREATE TABLE IF NOT EXISTS enterprise_notification_064_feature_changes (
    feature_code VARCHAR(160) PRIMARY KEY,
    created_by_migration BOOLEAN NOT NULL,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE OR REPLACE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse064_immutable$
BEGIN
    RAISE EXCEPTION 'Enterprise notification orchestration evidence is immutable.';
END;
$projectpulse064_immutable$;

DROP TRIGGER IF EXISTS trg_enterprise_notification_event_history_immutable
    ON enterprise_notification_event_history;
CREATE TRIGGER trg_enterprise_notification_event_history_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_event_history
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_enterprise_notification_acknowledgements_immutable
    ON enterprise_notification_acknowledgements;
CREATE TRIGGER trg_enterprise_notification_acknowledgements_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_acknowledgements
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_enterprise_notification_run_history_immutable
    ON enterprise_notification_run_history;
CREATE TRIGGER trg_enterprise_notification_run_history_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_run_history
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_enterprise_notification_policy_audit_immutable
    ON enterprise_notification_policy_audit;
CREATE TRIGGER trg_enterprise_notification_policy_audit_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_policy_audit
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_enterprise_notification_064_permissions_immutable
    ON enterprise_notification_064_permissions_created;
CREATE TRIGGER trg_enterprise_notification_064_permissions_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_064_permissions_created
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

DROP TRIGGER IF EXISTS trg_enterprise_notification_064_role_grants_immutable
    ON enterprise_notification_064_role_grants;
CREATE TRIGGER trg_enterprise_notification_064_role_grants_immutable
BEFORE UPDATE OR DELETE ON enterprise_notification_064_role_grants
FOR EACH ROW EXECUTE FUNCTION projectpulse064_block_enterprise_notification_evidence_mutation();

-- Seed the complete enterprise notification policy inventory. Reapplication does
-- not overwrite administrator changes to enabled state, delivery boundary,
-- templates, timing, or recipient configuration.
INSERT INTO enterprise_notification_policies (
    policy_code, policy_name, category, source_module, event_code,
    trigger_mode, recipient_strategy, trigger_configuration,
    recipient_configuration, severity, acknowledgement_required,
    acknowledgement_escalation_minutes, subject_template, text_template,
    producer_contract, source_state
)
VALUES
    ('TIME_SUBMISSION_CONFIRMATION', 'Timesheet submission confirmation', 'time', '001', 'timesheet_submitted', 'event', 'timesheet_engineer', '{"scan":"timesheet_day_statuses","status":"submitted"}', '{"to":["engineer"]}', 'informational', FALSE, NULL, 'ProjectPulse: Timesheet submitted — {{workDate}}', 'Your time for {{workDate}} was submitted successfully and is now in the approval workflow.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_MANAGER_APPROVAL_REQUEST', 'Manager approval request', 'time', '002', 'manager_approval_requested', 'event', 'timesheet_manager', '{"scan":"timesheet_day_statuses","status":"submitted"}', '{"to":["direct_manager"],"fallback":["ptc"]}', 'informational', FALSE, NULL, 'ProjectPulse: Time ready for manager approval — {{engineerName}}', '{{engineerName}} submitted {{totalHours}} hour(s) for {{workDate}}. Open Approval Center to review it.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_PM_APPROVAL_REQUEST', 'Project Manager approval request', 'time', '002', 'pm_approval_requested', 'event', 'timesheet_project_managers', '{"scan":"timesheet_day_statuses","status":"manager_approved"}', '{"to":["project_managers"],"fallback":["ptc"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project time ready for approval — {{engineerName}}', '{{engineerName}} has manager-approved project time for {{workDate}} that is ready for Project Management review.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_PTC_FINAL_APPROVAL_REQUEST', 'PTC final approval request', 'time', '002', 'ptc_final_approval_requested', 'event', 'ptc_role_group', '{"scan":"timesheet_day_statuses","status":"pm_approved"}', '{"to":["project_team_coordinator"]}', 'informational', FALSE, NULL, 'ProjectPulse: Time ready for final approval — {{engineerName}}', '{{engineerName}} has PM-approved time for {{workDate}} that is ready for final PTC approval.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_FULLY_APPROVED', 'Timesheet fully approved', 'time', '002', 'timesheet_fully_approved', 'event', 'timesheet_engineer', '{"scan":"timesheet_day_statuses","status":["accounting_ready","reconciled","locked"]}', '{"to":["engineer"]}', 'informational', FALSE, NULL, 'ProjectPulse: Timesheet approval complete — {{workDate}}', 'Your time for {{workDate}} completed the approval workflow. Current status: {{status}}.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_REJECTED', 'Timesheet rejected or returned', 'time', '002', 'timesheet_rejected', 'event', 'timesheet_engineer', '{"scan":"timesheet_day_statuses","status":["manager_declined","pm_declined"]}', '{"to":["engineer"]}', 'high', FALSE, NULL, 'ProjectPulse: Time returned for correction — {{workDate}}', '{{reviewerName}} returned your time for {{workDate}}. Reason: {{decisionComment}}. Review, correct, and resubmit it when required.', 'authoritative_timesheet_scanner', 'scanner'),
    ('TIME_APPROVAL_OVERDUE_3_DAYS', 'Approval pending for three days', 'time', '002', 'timesheet_approval_overdue', 'escalation', 'timesheet_current_approvers', '{"ageDays":3,"repeatDays":3,"statuses":["submitted","manager_approved","pm_approved"]}', '{"to":["current_stage_approver"]}', 'warning', FALSE, NULL, 'ProjectPulse: Approval overdue — {{engineerName}} / {{workDate}}', 'This approval has been pending for {{ageDays}} day(s). Current stage: {{status}}. Open Approval Center to complete the review.', 'authoritative_timesheet_scanner', 'scanner'),
    ('UTILIZATION_MINIMUM_70_REACHED', 'Quarterly minimum utilization reached', 'utilization', '003', 'utilization_threshold_reached', 'threshold', 'subject_user', '{"threshold":70,"unit":"percent","period":"quarter"}', '{"to":["engineer"]}', 'informational', FALSE, NULL, 'ProjectPulse: Quarterly utilization minimum reached', 'Your quarter-to-date utilization reached {{utilizationPercent}}%, meeting the 70% minimum requirement.', 'module_003_signed_event', 'signed_event'),
    ('UTILIZATION_MONTH_END', 'Month-end quarterly utilization summary', 'utilization', '003', 'utilization_month_end', 'scheduled', 'subject_user', '{"cadence":"monthly","when":"month_end","localTime":"18:00","timezone":"America/Chicago"}', '{"to":["engineer"]}', 'informational', FALSE, NULL, 'ProjectPulse: Month-end utilization summary — {{monthName}}', 'Quarter-to-date utilization: {{utilizationPercent}}%. Current target: {{targetPercent}}%. Next target: {{nextTarget}}.', 'module_003_signed_event', 'signed_event'),
    ('EXPENSE_UPLOAD_CONFIRMATION', 'Expense upload confirmation', 'expense', '005', 'expense_uploaded', 'event', 'expense_owner', '{"scan":"project_expense_uploads","event":"UPLOAD_CREATED"}', '{"to":["expense_owner"],"cc":["project_manager"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project expenses uploaded — {{projectCode}}', '{{lineCount}} expense line(s) totaling {{totalAmount}} were uploaded for {{projectCode}} {{projectName}}.', 'module_005_native_bridge', 'scanner'),
    ('EXPENSE_PM_REVIEW_REQUEST', 'Expense review request', 'expense', '005', 'expense_review_requested', 'event', 'project_manager', '{"event":"expense_uploaded"}', '{"to":["project_manager"]}', 'informational', FALSE, NULL, 'ProjectPulse: Expenses ready for review — {{projectCode}}', 'Project expenses totaling {{totalAmount}} are ready for review for {{projectCode}} {{projectName}}.', 'module_005_native_bridge', 'scanner'),
    ('EXPENSE_APPROVED', 'Expense approved', 'expense', '005', 'expense_approved', 'event', 'expense_owner', '{}', '{"to":["expense_owner"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project expense approved — {{projectCode}}', 'Your project expense for {{projectCode}} was approved by {{reviewerName}}.', 'module_005_signed_event', 'signed_event'),
    ('EXPENSE_REJECTED', 'Expense rejected', 'expense', '005', 'expense_rejected', 'event', 'expense_owner', '{}', '{"to":["expense_owner"]}', 'high', FALSE, NULL, 'ProjectPulse: Project expense returned — {{projectCode}}', '{{reviewerName}} returned your project expense. Reason: {{decisionComment}}.', 'module_005_signed_event', 'signed_event'),
    ('PROJECT_ASSIGNMENT_CHANGED', 'Project assignment change', 'project', '019', 'project_assignment_changed', 'event', 'project_team', '{"coalescingMinutes":5}', '{"to":["affected_users","project_manager","ptc"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project assignment updated — {{projectCode}}', 'Assignment changes for {{projectCode}} {{projectName}} were consolidated over five minutes. Review the updated project team and workload.', 'module_019_signed_event', 'signed_event'),
    ('PROJECT_DOCUMENT_AVAILABLE', 'Project document available', 'project', '019', 'project_document_available', 'event', 'project_team', '{}', '{"to":["project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: New project document — {{projectCode}}', '{{documentName}} is now available for {{projectCode}} {{projectName}}.', 'module_019_signed_event', 'signed_event'),
    ('PROJECT_DELIVERY_MILESTONE', 'Project delivery or milestone notification', 'project', '019', 'project_milestone', 'scheduled', 'project_team', '{"relativeTo":"milestoneDate"}', '{"to":["project_team"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project milestone — {{projectCode}}', '{{milestoneName}} for {{projectCode}} is scheduled for {{milestoneDate}}.', 'module_019_signed_event', 'signed_event'),
    ('PROJECT_COST_ALERT', 'Project cost and budget alert', 'financial', '022', 'project_cost_alert', 'native_worker', 'project_team', '{"owner":"migration_050_group4"}', '{"to":["derived_project_roles"]}', 'high', FALSE, NULL, 'ProjectPulse: Project cost alert — {{projectCode}}', '{{alertSummary}}', 'group_4_project_notification_worker', 'native_worker'),
    ('MONDAY_TIME_COMPLIANCE', 'Monday time-compliance reminder', 'time', '023', 'monday_time_compliance', 'scheduled', 'manager_and_ptc', '{"cadence":"weekly","dayOfWeek":1,"localTime":"08:00","timezone":"America/Chicago"}', '{"to":["engineer"],"cc":["manager","ptc"]}', 'warning', FALSE, NULL, 'ProjectPulse: Time compliance reminder', '{{complianceSummary}}', 'module_023_signed_event', 'signed_event'),
    ('ANALYTICS_REPORT_READY', 'Analytics report ready', 'financial', '030', 'analytics_report_ready', 'event', 'report_requester', '{}', '{"to":["requester"]}', 'informational', FALSE, NULL, 'ProjectPulse: Analytics report ready — {{reportName}}', 'Your {{reportName}} report is ready. {{reportSummary}}', 'module_030_signed_event', 'signed_event'),
    ('ANALYTICS_REPORT_FAILED', 'Analytics report failed', 'financial', '030', 'analytics_report_failed', 'event', 'report_requester_and_admin', '{}', '{"to":["requester"],"cc":["ptc","administrator"]}', 'high', FALSE, NULL, 'ProjectPulse: Analytics report failed — {{reportName}}', '{{reportName}} could not be completed. Diagnostic code: {{diagnosticCode}}.', 'module_030_signed_event', 'signed_event'),
    ('BILLING_READINESS_CHANGED', 'Billing readiness changed', 'financial', '039', 'billing_readiness_changed', 'event', 'billing_project_team', '{}', '{"to":["billing","project_manager","ptc"]}', 'informational', FALSE, NULL, 'ProjectPulse: Billing readiness updated — {{projectCode}}', 'Billing readiness for {{projectCode}} changed to {{status}}.', 'module_039_signed_event', 'signed_event'),
    ('INVOICE_CREATED', 'Invoice created', 'financial', '042', 'invoice_created', 'event', 'billing_project_team', '{}', '{"to":["billing","project_manager"]}', 'informational', FALSE, NULL, 'ProjectPulse: Invoice created — {{invoiceNumber}}', 'Invoice {{invoiceNumber}} was created for {{projectCode}} {{projectName}}.', 'module_042_signed_event', 'signed_event'),
    ('INVOICE_SENT', 'Invoice sent', 'financial', '042', 'invoice_sent', 'event', 'billing_project_team', '{}', '{"to":["billing","project_manager"]}', 'informational', FALSE, NULL, 'ProjectPulse: Invoice sent — {{invoiceNumber}}', 'Invoice {{invoiceNumber}} for {{projectCode}} was sent successfully.', 'module_042_signed_event', 'signed_event'),
    ('INVOICE_FAILED', 'Invoice delivery failed', 'financial', '042', 'invoice_failed', 'event', 'billing_project_team', '{}', '{"to":["billing","ptc","administrator"]}', 'critical', FALSE, NULL, 'ProjectPulse: Invoice processing failed — {{invoiceNumber}}', 'Invoice {{invoiceNumber}} failed. Diagnostic code: {{diagnosticCode}}.', 'module_042_signed_event', 'signed_event'),
    ('CLOSEOUT_STARTED', 'Project closeout started', 'closeout', '040', 'closeout_started', 'event', 'project_team', '{}', '{"to":["project_manager","ptc","billing"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project closeout started — {{projectCode}}', 'Closeout started for {{projectCode}} {{projectName}}.', 'module_040_signed_event', 'signed_event'),
    ('CLOSEOUT_ACTION_REQUIRED', 'Project closeout action required', 'closeout', '041', 'closeout_action_required', 'native_worker', 'project_team', '{"owner":"group_4_closeout_compatibility"}', '{"to":["derived_project_roles"]}', 'warning', FALSE, NULL, 'ProjectPulse: Closeout action required — {{projectCode}}', '{{closeoutSummary}}', 'group_4_closeout_notification_worker', 'native_worker'),
    ('CLOSEOUT_COMPLETED', 'Project closeout completed', 'closeout', '040', 'closeout_completed', 'event', 'project_team', '{}', '{"to":["project_manager","ptc","billing","account_executive"]}', 'informational', FALSE, NULL, 'ProjectPulse: Project closeout complete — {{projectCode}}', 'Closeout completed for {{projectCode}} {{projectName}}.', 'module_040_signed_event', 'signed_event'),
    ('ENTRA_SECRET_EXPIRATION', 'Microsoft Integration client-secret expiration', 'identity', '065', 'entra_secret_expiration', 'native_worker', 'entra_expiration_recipients', '{"owner":"module_065_expiration_governance","startsDaysBefore":30,"criticalDays":7}', '{"to":["snapshotted_ptc_recipients"]}', 'critical', TRUE, 10080, 'ProjectPulse: Microsoft Integration credential expiration', 'Open Module 065 to review and acknowledge the Microsoft Integration credential-expiration reminder.', 'module_065_expiration_governance_worker', 'native_worker'),
    ('QUALIFICATION_EXPIRING', 'Qualification or certification expiring', 'qualification', '069', 'qualification_expiring', 'scheduled', 'qualification_owner_and_manager', '{"relativeTo":"effective_end_date","offsetDays":[90,60,30,14,7,1,0]}', '{"to":["qualification_owner"],"cc":["manager"]}', 'warning', FALSE, NULL, 'ProjectPulse: Qualification expiring — {{qualificationName}}', '{{qualificationName}} expires on {{expirationDate}} ({{daysRemaining}} day(s) remaining).', 'module_069_authoritative_scanner', 'scanner'),
    ('QUALIFICATION_EXPIRED_WEEKLY', 'Expired qualification weekly reminder', 'qualification', '069', 'qualification_expired', 'scheduled', 'qualification_owner_and_manager', '{"cadence":"weekly","dayOfWeek":1,"localTime":"08:00","timezone":"America/Chicago"}', '{"to":["qualification_owner"],"cc":["manager"]}', 'high', FALSE, NULL, 'ProjectPulse: Qualification expired — {{qualificationName}}', '{{qualificationName}} expired on {{expirationDate}}. Update Module 069 when renewal evidence is available.', 'module_069_authoritative_scanner', 'scanner'),
    ('ONCALL_WEEK_BEFORE', 'On-call assignment one-week reminder', 'oncall', '071', 'oncall_week_before', 'scheduled', 'oncall_assignee', '{"relativeTo":"start_at","offsetDays":[7]}', '{"to":["assignee"],"cc":["manager"]}', 'informational', TRUE, 4320, 'ProjectPulse: On-call assignment begins next week', 'Your on-call assignment begins {{startAt}} and ends {{endAt}}. Acknowledge the assignment in ProjectPulse.', 'module_071_signed_event', 'signed_event'),
    ('ONCALL_DAY_OF', 'On-call assignment day-of reminder', 'oncall', '071', 'oncall_day_of', 'scheduled', 'oncall_assignee', '{"relativeTo":"start_at","offsetDays":[0]}', '{"to":["assignee"],"cc":["manager"]}', 'high', TRUE, 240, 'ProjectPulse: On-call assignment begins today', 'Your on-call assignment begins today at {{startAt}}. Acknowledge the assignment in ProjectPulse.', 'module_071_signed_event', 'signed_event'),
    ('ONCALL_ACK_ESCALATION', 'On-call acknowledgement escalation', 'oncall', '071', 'oncall_ack_overdue', 'escalation', 'oncall_manager_and_ptc', '{"afterMinutes":240}', '{"to":["manager","ptc"],"cc":["assignee"]}', 'critical', TRUE, 240, 'ProjectPulse: On-call acknowledgement overdue', '{{assigneeName}} has not acknowledged the on-call assignment beginning {{startAt}}.', 'module_071_signed_event', 'signed_event'),
    ('DEFECT_OPENED', 'Defect opened', 'defect', '076', 'defect_opened', 'event', 'defect_assignee_and_managers', '{}', '{"to":["assignee","manager_role_group"]}', 'high', FALSE, NULL, 'ProjectPulse: Defect opened — {{defectId}}', '{{defectTitle}} was opened with priority {{priority}}.', 'module_076_signed_event', 'signed_event'),
    ('DEFECT_ASSIGNED', 'Defect assigned', 'defect', '076', 'defect_assigned', 'event', 'defect_assignee', '{}', '{"to":["assignee"]}', 'informational', FALSE, NULL, 'ProjectPulse: Defect assigned — {{defectId}}', '{{defectId}} was assigned to you. Priority: {{priority}}.', 'module_076_signed_event', 'signed_event'),
    ('DEFECT_RESOLVED', 'Defect resolved', 'defect', '076', 'defect_resolved', 'event', 'defect_reporter', '{}', '{"to":["reporter"]}', 'informational', FALSE, NULL, 'ProjectPulse: Defect resolved — {{defectId}}', '{{defectId}} was resolved. {{resolutionSummary}}', 'module_076_signed_event', 'signed_event'),
    ('SYSTEM_OUTAGE', 'System outage', 'operations', '997', 'system_outage', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'critical', FALSE, NULL, 'ProjectPulse: System outage — {{serviceName}}', '{{serviceName}} is unavailable. {{incidentSummary}}', 'signed_operational_event_api', 'signed_event'),
    ('SYSTEM_RECOVERY', 'System recovery', 'operations', '997', 'system_recovery', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'informational', FALSE, NULL, 'ProjectPulse: Service recovered — {{serviceName}}', '{{serviceName}} recovered at {{recoveredAt}}.', 'signed_operational_event_api', 'signed_event'),
    ('BACKUP_FAILED', 'Backup failed', 'operations', '997', 'backup_failed', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'critical', FALSE, NULL, 'ProjectPulse: Backup failed — {{serviceName}}', 'The {{backupType}} backup for {{serviceName}} failed. Diagnostic code: {{diagnosticCode}}.', 'signed_operational_event_api', 'signed_event'),
    ('BACKUP_RECOVERED', 'Backup recovered', 'operations', '997', 'backup_recovered', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'informational', FALSE, NULL, 'ProjectPulse: Backup recovered — {{serviceName}}', 'The {{backupType}} backup for {{serviceName}} completed successfully.', 'signed_operational_event_api', 'signed_event'),
    ('SERVICE_HEALTH_DEGRADED', 'Service health degraded', 'operations', '078', 'service_health_degraded', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'high', FALSE, NULL, 'ProjectPulse: Service health degraded — {{serviceName}}', '{{serviceName}} health changed to {{status}}. {{incidentSummary}}', 'signed_operational_event_api', 'signed_event'),
    ('SERVICE_HEALTH_RECOVERED', 'Service health recovered', 'operations', '078', 'service_health_recovered', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'informational', FALSE, NULL, 'ProjectPulse: Service health recovered — {{serviceName}}', '{{serviceName}} returned to a healthy state.', 'signed_operational_event_api', 'signed_event'),
    ('REPLICATION_LAG', 'Replication lag threshold exceeded', 'operations', '078', 'replication_lag', 'threshold', 'operations_stakeholders', '{"metric":"replicationLagSeconds"}', '{"to":["ptc","administrator","service_owners"]}', 'critical', FALSE, NULL, 'ProjectPulse: Replication lag — {{serviceName}}', 'Replication lag reached {{replicationLagSeconds}} seconds for {{serviceName}}.', 'signed_operational_event_api', 'signed_event'),
    ('REPLICATION_RECOVERED', 'Replication recovered', 'operations', '078', 'replication_recovered', 'event', 'operations_stakeholders', '{}', '{"to":["ptc","administrator","service_owners"]}', 'informational', FALSE, NULL, 'ProjectPulse: Replication recovered — {{serviceName}}', 'Replication for {{serviceName}} returned within the approved threshold.', 'signed_operational_event_api', 'signed_event'),
    ('CRM_SYNC_FAILED', 'CRM synchronization failed', 'operations', '026', 'crm_sync_failed', 'event', 'integration_stakeholders', '{}', '{"to":["ptc","administrator","integration_owners"]}', 'critical', FALSE, NULL, 'ProjectPulse: CRM synchronization failed', '{{integrationName}} synchronization failed. Diagnostic code: {{diagnosticCode}}.', 'signed_operational_event_api', 'signed_event'),
    ('CRM_SYNC_RECOVERED', 'CRM synchronization recovered', 'operations', '026', 'crm_sync_recovered', 'event', 'integration_stakeholders', '{}', '{"to":["ptc","administrator","integration_owners"]}', 'informational', FALSE, NULL, 'ProjectPulse: CRM synchronization recovered', '{{integrationName}} synchronization completed successfully.', 'signed_operational_event_api', 'signed_event'),
    ('SECURITY_EVENT', 'Security event requiring notification', 'security', '997', 'security_event', 'event', 'security_stakeholders', '{}', '{"to":["security_owners","administrator"]}', 'critical', FALSE, NULL, 'ProjectPulse: Security event — {{eventName}}', '{{eventSummary}} Correlation ID: {{correlationId}}.', 'signed_security_event_api', 'signed_event')
ON CONFLICT (policy_code) DO NOTHING;

WITH inserted_permissions AS (
    INSERT INTO app_permissions (
        permission_code,
        permission_name,
        module_code,
        permission_description
    )
    VALUES
        ('VIEW_ENTERPRISE_NOTIFICATIONS_065', 'View Enterprise Notification Orchestration', '065', 'View notification policies, producer coverage, schedules, Module 065 readiness, dispatch evidence, acknowledgements, and source diagnostics.'),
        ('MANAGE_ENTERPRISE_NOTIFICATIONS_065', 'Manage Enterprise Notification Orchestration', '065', 'Change enterprise notification policy state, boundary, templates, timing, and recipient strategy without exposing mail credentials.'),
        ('RUN_ENTERPRISE_NOTIFICATIONS_065', 'Run Enterprise Notification Orchestration', '065', 'Run authoritative source scans and process due enterprise notification events through the governed Module 065 adapter.')
    ON CONFLICT (permission_code) DO NOTHING
    RETURNING app_permission_id, permission_code
)
INSERT INTO enterprise_notification_064_permissions_created (
    app_permission_id,
    permission_code
)
SELECT app_permission_id, permission_code
FROM inserted_permissions
ON CONFLICT DO NOTHING;

WITH desired_grants AS (
    SELECT role.app_role_id, permission.app_permission_id
    FROM app_roles role
    JOIN app_permissions permission
      ON permission.permission_code IN (
          'VIEW_ENTERPRISE_NOTIFICATIONS_065',
          'MANAGE_ENTERPRISE_NOTIFICATIONS_065',
          'RUN_ENTERPRISE_NOTIFICATIONS_065'
      )
    WHERE role.is_active = TRUE
      AND upper(role.role_code) IN (
          'PROJECT_TEAM_COORDINATOR',
          'PROJECT_COORDINATOR',
          'PTC',
          'SUPER_ADMINISTRATOR',
          'ADMINISTRATOR'
      )
), inserted_grants AS (
    INSERT INTO app_role_permissions (app_role_id, app_permission_id)
    SELECT desired.app_role_id, desired.app_permission_id
    FROM desired_grants desired
    WHERE NOT EXISTS (
        SELECT 1
        FROM app_role_permissions existing
        WHERE existing.app_role_id = desired.app_role_id
          AND existing.app_permission_id = desired.app_permission_id
    )
    ON CONFLICT DO NOTHING
    RETURNING app_role_id, app_permission_id
)
INSERT INTO enterprise_notification_064_role_grants (
    app_role_id,
    app_permission_id
)
SELECT app_role_id, app_permission_id
FROM inserted_grants
ON CONFLICT DO NOTHING;

DO $projectpulse064_feature$
DECLARE
    existed BOOLEAN;
BEGIN
    IF to_regclass('public.app_feature_catalog') IS NULL THEN
        RETURN;
    END IF;

    SELECT EXISTS (
        SELECT 1 FROM app_feature_catalog
        WHERE feature_code = 'ENTERPRISE_NOTIFICATION_ORCHESTRATION'
    ) INTO existed;

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
    VALUES (
        'ENTERPRISE_NOTIFICATION_ORCHESTRATION',
        'Enterprise Notification Orchestration',
        '065',
        '#entra-secret-administration',
        'VIEW_ENTERPRISE_NOTIFICATIONS_065',
        'Central inventory, policy, source diagnostics, acknowledgement, and Module 065 delivery control for platform notifications.',
        655,
        TRUE
    )
    ON CONFLICT (feature_code) DO UPDATE
    SET feature_name = EXCLUDED.feature_name,
        module_code = EXCLUDED.module_code,
        route_anchor = EXCLUDED.route_anchor,
        required_permission_code = EXCLUDED.required_permission_code,
        feature_description = EXCLUDED.feature_description,
        display_order = EXCLUDED.display_order,
        is_active = TRUE,
        updated_at = NOW();

    INSERT INTO enterprise_notification_064_feature_changes (
        feature_code,
        created_by_migration
    )
    VALUES ('ENTERPRISE_NOTIFICATION_ORCHESTRATION', NOT existed)
    ON CONFLICT (feature_code) DO NOTHING;
END;
$projectpulse064_feature$;

CREATE OR REPLACE VIEW enterprise_notification_inventory AS
SELECT
    policy.policy_code,
    policy.policy_name,
    policy.category,
    policy.source_module,
    policy.event_code,
    policy.trigger_mode,
    policy.recipient_strategy,
    policy.severity,
    policy.delivery_boundary,
    policy.acknowledgement_required,
    policy.owner_module,
    policy.producer_contract,
    policy.source_state,
    policy.enabled,
    policy.updated_at,
    'module_065'::TEXT AS delivery_authority,
    FALSE AS direct_smtp_authorized,
    FALSE AS direct_brevo_authorized
FROM enterprise_notification_policies policy;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '064_module_065_enterprise_notification_orchestration',
    'Enterprise notification policy inventory, durable event ingestion, source checkpoints, acknowledgements, run evidence, and exclusive Module 065 delivery authority',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;