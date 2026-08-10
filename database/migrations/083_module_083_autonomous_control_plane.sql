-- Pulse migration 083
-- Module 083 bounded autonomous control plane: immutable policy versions,
-- durable dry-run orchestration, approvals, exact release manifests, adapter
-- registry, idempotency, blocked outbox, and append-only evidence.

BEGIN;

DO $pulse083_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL
       OR to_regclass('public.full_future_loop_items') IS NULL
       OR to_regclass('public.full_future_loop_events') IS NULL
       OR to_regclass('public.full_future_loop_artifacts') IS NULL THEN
        RAISE EXCEPTION 'Migration 083 requires canonical identity, RBAC, feature catalog, and Module 083 migration 082 foundations.';
    END IF;
END;
$pulse083_prerequisites$;

CREATE TABLE IF NOT EXISTS full_future_loop_automation_policies (
    policy_version_id UUID PRIMARY KEY,
    policy_version VARCHAR(100) NOT NULL UNIQUE,
    policy_document JSONB NOT NULL CHECK (jsonb_typeof(policy_document)='object'),
    policy_sha256 CHAR(64) NOT NULL CHECK (policy_sha256 ~ '^[0-9a-f]{64}$'),
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_state (
    state_id SMALLINT PRIMARY KEY CHECK (state_id=1),
    automation_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    global_kill_switch BOOLEAN NOT NULL DEFAULT TRUE,
    dry_run_only BOOLEAN NOT NULL DEFAULT TRUE CHECK (dry_run_only=TRUE),
    active_policy_version_id UUID NOT NULL REFERENCES full_future_loop_automation_policies(policy_version_id) ON DELETE RESTRICT,
    last_reason TEXT NOT NULL DEFAULT 'Enterprise fail-closed baseline',
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number>=1),
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_adapters (
    adapter_code VARCHAR(80) PRIMARY KEY,
    display_name VARCHAR(180) NOT NULL,
    capabilities JSONB NOT NULL DEFAULT '[]'::JSONB CHECK (jsonb_typeof(capabilities)='array'),
    credential_boundary TEXT NOT NULL,
    writes_externally BOOLEAN NOT NULL,
    adapter_mode VARCHAR(20) NOT NULL DEFAULT 'disabled' CHECK (adapter_mode IN ('disabled','dry_run')),
    is_ready BOOLEAN NOT NULL DEFAULT FALSE,
    circuit_open BOOLEAN NOT NULL DEFAULT FALSE,
    last_probe_at TIMESTAMPTZ NULL,
    last_successful_probe_at TIMESTAMPTZ NULL,
    failure_count INTEGER NOT NULL DEFAULT 0 CHECK (failure_count>=0),
    detail TEXT NOT NULL DEFAULT 'Not configured. External execution is disabled.',
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number>=1),
    created_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_full_future_loop_adapter_readiness CHECK (
        adapter_mode='disabled' OR (adapter_mode='dry_run' AND is_ready=FALSE)
    )
);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_runs (
    run_id UUID PRIMARY KEY,
    loop_id UUID NULL REFERENCES full_future_loop_items(loop_id) ON DELETE RESTRICT,
    idempotency_key VARCHAR(200) NOT NULL UNIQUE,
    correlation_id UUID NOT NULL UNIQUE,
    requested_operation VARCHAR(40) NOT NULL CHECK (requested_operation IN (
        'observe','classify','create_issue','dispatch_ci','run_canary','deploy',
        'verify','rollback','notify','propose_repair'
    )),
    target_environment VARCHAR(32) NOT NULL CHECK (target_environment IN ('canary','test','production')),
    repository VARCHAR(240) NOT NULL,
    source_commit CHAR(40) NOT NULL CHECK (source_commit ~ '^[0-9a-f]{40}$'),
    risk_class VARCHAR(20) NOT NULL CHECK (risk_class IN ('routine','normal','high','critical')),
    change_type VARCHAR(80) NOT NULL,
    policy_version_id UUID NOT NULL REFERENCES full_future_loop_automation_policies(policy_version_id) ON DELETE RESTRICT,
    disposition VARCHAR(32) NOT NULL CHECK (disposition IN ('auto_execute','approval_required','blocked')),
    decision_code VARCHAR(100) NOT NULL,
    run_status VARCHAR(32) NOT NULL CHECK (run_status IN (
        'blocked','approval_required','planned','dry_run_completed','cancelled'
    )),
    dry_run BOOLEAN NOT NULL DEFAULT TRUE CHECK (dry_run=TRUE),
    attempt_count INTEGER NOT NULL DEFAULT 1 CHECK (attempt_count>=1),
    maximum_attempts INTEGER NOT NULL CHECK (maximum_attempts BETWEEN 1 AND 10),
    lease_owner VARCHAR(180) NULL,
    lease_expires_at TIMESTAMPTZ NULL,
    deadline_at TIMESTAMPTZ NULL,
    request_snapshot JSONB NOT NULL CHECK (jsonb_typeof(request_snapshot)='object'),
    decision_snapshot JSONB NOT NULL CHECK (jsonb_typeof(decision_snapshot)='object'),
    requested_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    requested_at TIMESTAMPTZ NOT NULL,
    started_at TIMESTAMPTZ NULL,
    completed_at TIMESTAMPTZ NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_full_future_loop_run_completion CHECK (
        (run_status IN ('blocked','dry_run_completed','cancelled') AND completed_at IS NOT NULL)
        OR (run_status IN ('approval_required','planned'))
    )
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_runs_status
    ON full_future_loop_automation_runs(run_status,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_runs_loop
    ON full_future_loop_automation_runs(loop_id,created_at DESC);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_runs_release
    ON full_future_loop_automation_runs(repository,source_commit,target_environment);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_steps (
    step_id UUID PRIMARY KEY,
    run_id UUID NOT NULL REFERENCES full_future_loop_automation_runs(run_id) ON DELETE RESTRICT,
    step_code VARCHAR(100) NOT NULL,
    sequence_number INTEGER NOT NULL CHECK (sequence_number>=1),
    adapter_code VARCHAR(80) NULL REFERENCES full_future_loop_automation_adapters(adapter_code) ON DELETE RESTRICT,
    step_status VARCHAR(32) NOT NULL CHECK (step_status IN (
        'pending','completed','waiting_approval','skipped','dry_run_completed','failed','cancelled'
    )),
    attempt_number INTEGER NOT NULL DEFAULT 1 CHECK (attempt_number>=1),
    input_document JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(input_document)='object'),
    output_document JSONB NOT NULL DEFAULT '{}'::JSONB CHECK (jsonb_typeof(output_document)='object'),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(run_id,sequence_number),
    UNIQUE(run_id,step_code)
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_steps_run
    ON full_future_loop_automation_steps(run_id,sequence_number);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_approvals (
    approval_id UUID PRIMARY KEY,
    run_id UUID NOT NULL REFERENCES full_future_loop_automation_runs(run_id) ON DELETE RESTRICT,
    approval_type VARCHAR(100) NOT NULL,
    approval_status VARCHAR(20) NOT NULL DEFAULT 'pending' CHECK (approval_status IN ('pending','approved','rejected','cancelled')),
    requested_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    decided_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    decision_reason TEXT NOT NULL DEFAULT '',
    decided_at TIMESTAMPTZ NULL,
    revision_number INTEGER NOT NULL DEFAULT 1 CHECK (revision_number>=1),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(run_id,approval_type),
    CONSTRAINT ck_full_future_loop_approval_decision CHECK (
        (approval_status='pending' AND decided_by_user_id IS NULL AND decided_at IS NULL)
        OR (approval_status IN ('approved','rejected') AND decided_by_user_id IS NOT NULL AND decided_at IS NOT NULL AND btrim(decision_reason)<>'')
        OR approval_status='cancelled'
    )
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_approvals_queue
    ON full_future_loop_automation_approvals(approval_status,created_at);

CREATE TABLE IF NOT EXISTS full_future_loop_release_manifests (
    manifest_id UUID PRIMARY KEY,
    run_id UUID NOT NULL UNIQUE REFERENCES full_future_loop_automation_runs(run_id) ON DELETE RESTRICT,
    manifest_version VARCHAR(80) NOT NULL,
    repository VARCHAR(240) NOT NULL,
    source_commit CHAR(40) NOT NULL CHECK (source_commit ~ '^[0-9a-f]{40}$'),
    target_environment VARCHAR(32) NOT NULL CHECK (target_environment IN ('canary','test','production')),
    manifest_document JSONB NOT NULL CHECK (jsonb_typeof(manifest_document)='object'),
    manifest_sha256 CHAR(64) NOT NULL CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    is_read_only BOOLEAN NOT NULL DEFAULT TRUE CHECK (is_read_only=TRUE),
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    CONSTRAINT ck_full_future_loop_manifest_expiry CHECK (expires_at>created_at)
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_release_manifests_release
    ON full_future_loop_release_manifests(repository,source_commit,target_environment);

CREATE TABLE IF NOT EXISTS full_future_loop_automation_evidence (
    evidence_id UUID PRIMARY KEY,
    run_id UUID NULL REFERENCES full_future_loop_automation_runs(run_id) ON DELETE RESTRICT,
    loop_id UUID NULL REFERENCES full_future_loop_items(loop_id) ON DELETE RESTRICT,
    event_code VARCHAR(100) NOT NULL,
    severity VARCHAR(20) NOT NULL CHECK (severity IN ('information','notice','warning','critical')),
    actual_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    effective_actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    evidence_document JSONB NOT NULL CHECK (jsonb_typeof(evidence_document)='object'),
    occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_evidence_run
    ON full_future_loop_automation_evidence(run_id,occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_full_future_loop_automation_evidence_loop
    ON full_future_loop_automation_evidence(loop_id,occurred_at DESC);

CREATE TABLE IF NOT EXISTS full_future_loop_outbox (
    outbox_id UUID PRIMARY KEY,
    run_id UUID NOT NULL REFERENCES full_future_loop_automation_runs(run_id) ON DELETE RESTRICT,
    message_type VARCHAR(100) NOT NULL,
    adapter_code VARCHAR(80) NULL REFERENCES full_future_loop_automation_adapters(adapter_code) ON DELETE RESTRICT,
    idempotency_key VARCHAR(240) NOT NULL UNIQUE,
    payload JSONB NOT NULL CHECK (jsonb_typeof(payload)='object'),
    outbox_status VARCHAR(20) NOT NULL CHECK (outbox_status IN ('pending','blocked','dispatched','dead_letter','cancelled')),
    attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count>=0),
    available_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    dispatched_at TIMESTAMPTZ NULL,
    last_error TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT ck_full_future_loop_outbox_dispatch CHECK (
        (outbox_status='dispatched' AND dispatched_at IS NOT NULL)
        OR (outbox_status<>'dispatched' AND dispatched_at IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_full_future_loop_outbox_queue
    ON full_future_loop_outbox(outbox_status,available_at);

CREATE OR REPLACE FUNCTION pulse083_immutable_automation_evidence()
RETURNS TRIGGER LANGUAGE plpgsql AS $pulse083_immutable$
BEGIN
    RAISE EXCEPTION 'Module 083 autonomous policy versions, release manifests, and evidence are append-only.';
END;
$pulse083_immutable$;

DROP TRIGGER IF EXISTS trg_full_future_loop_automation_policies_immutable_083 ON full_future_loop_automation_policies;
CREATE TRIGGER trg_full_future_loop_automation_policies_immutable_083
BEFORE UPDATE OR DELETE ON full_future_loop_automation_policies
FOR EACH ROW EXECUTE FUNCTION pulse083_immutable_automation_evidence();

DROP TRIGGER IF EXISTS trg_full_future_loop_release_manifests_immutable_083 ON full_future_loop_release_manifests;
CREATE TRIGGER trg_full_future_loop_release_manifests_immutable_083
BEFORE UPDATE OR DELETE ON full_future_loop_release_manifests
FOR EACH ROW EXECUTE FUNCTION pulse083_immutable_automation_evidence();

DROP TRIGGER IF EXISTS trg_full_future_loop_automation_evidence_immutable_083 ON full_future_loop_automation_evidence;
CREATE TRIGGER trg_full_future_loop_automation_evidence_immutable_083
BEFORE UPDATE OR DELETE ON full_future_loop_automation_evidence
FOR EACH ROW EXECUTE FUNCTION pulse083_immutable_automation_evidence();

INSERT INTO full_future_loop_automation_policies(
    policy_version_id,policy_version,policy_document,policy_sha256,created_by_user_id,created_at)
VALUES(
    '08300000-0000-0000-0000-000000000001',
    'enterprise-default-v1',
    '{
      "enabled": false,
      "globalKillSwitch": true,
      "allowedRepositories": ["ahmedadeyemi-cts/project-time-platform"],
      "allowedEnvironments": ["canary", "test"],
      "allowedOperations": ["observe", "classify", "create_issue", "dispatch_ci", "run_canary", "deploy", "verify", "rollback", "notify", "propose_repair"],
      "allowAutomaticTestDeployment": true,
      "allowAutomaticTestRollback": true,
      "allowAutomaticProductionDeployment": false,
      "allowAutomaticProductionRollback": false,
      "requireProductionApproval": true,
      "requireMigrationApproval": true,
      "requireSecurityApproval": true,
      "requireInfrastructureApproval": true,
      "requireSecretChangeApproval": true,
      "maximumConcurrentRuns": 2,
      "maximumStepAttempts": 3,
      "maximumRunDurationMinutes": 120,
      "evidenceMaximumAgeMinutes": 1440,
      "approvedProductionChangeTypes": []
    }'::JSONB,
    '500959490893b41860c7d868307c54d44f4fbf52f1d519e39fb6ef88089edd69',
    NULL,
    NOW())
ON CONFLICT(policy_version_id) DO NOTHING;

INSERT INTO full_future_loop_automation_state(
    state_id,automation_enabled,global_kill_switch,dry_run_only,active_policy_version_id,
    last_reason,revision_number,updated_by_user_id,updated_at)
VALUES(
    1,FALSE,TRUE,TRUE,'08300000-0000-0000-0000-000000000001',
    'Enterprise fail-closed baseline; external execution is not installed.',1,NULL,NOW())
ON CONFLICT(state_id) DO NOTHING;

INSERT INTO full_future_loop_automation_adapters(
    adapter_code,display_name,capabilities,credential_boundary,writes_externally,
    adapter_mode,is_ready,circuit_open,failure_count,detail,revision_number,created_at,updated_at)
VALUES
  ('github','GitHub repository and workflow adapter','["repository_read","pull_request_read","checks_read","actions_read","issues_write","workflow_dispatch"]'::JSONB,'GitHub App with least-privilege installation',TRUE,'disabled',FALSE,FALSE,0,'Not configured. No GitHub request can be sent.',1,NOW(),NOW()),
  ('canary','Disposable canary execution adapter','["seed_scenario","execute_contracts","collect_evidence","prove_cleanup"]'::JSONB,'Protected reusable workflow or isolated runner',TRUE,'disabled',FALSE,FALSE,0,'Not configured. Canary dispatch is disabled.',1,NOW(),NOW()),
  ('azure_container_apps','Azure Container Apps deployment adapter','["read_environment","deploy_exact_digest","verify_revision","restore_exact_digest"]'::JSONB,'GitHub Environment OIDC identity',TRUE,'disabled',FALSE,FALSE,0,'Not configured. Azure mutation is disabled.',1,NOW(),NOW()),
  ('azure_observability','Azure Monitor and Application Insights evidence adapter','["health_read","slo_read","logs_read","release_identity_read"]'::JSONB,'Read-only managed or federated identity',FALSE,'disabled',FALSE,FALSE,0,'Not configured. Observability reads are disabled.',1,NOW(),NOW()),
  ('module_076','Pulse defect and private repair adapter','["defect_create","defect_update","repair_evidence_link"]'::JSONB,'Pulse service identity and Module 076 capability',TRUE,'disabled',FALSE,FALSE,0,'Not configured. Defect writes are disabled.',1,NOW(),NOW()),
  ('module_065','Pulse notification adapter','["notification_prepare","notification_send","delivery_evidence_read"]'::JSONB,'Module 065 governed connection',TRUE,'disabled',FALSE,FALSE,0,'Not configured. Notification writes are disabled.',1,NOW(),NOW()),
  ('celar_ai','Celar AI advisory adapter','["classify","summarize","recommend","draft_repair"]'::JSONB,'Module 011 through Module 064',FALSE,'disabled',FALSE,FALSE,0,'Not configured for autonomous orchestration. AI cannot act as authority.',1,NOW(),NOW())
ON CONFLICT(adapter_code) DO NOTHING;

CREATE TABLE IF NOT EXISTS full_future_loop_083_permissions_created(
    app_permission_id UUID PRIMARY KEY REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS full_future_loop_083_role_grants(
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id) ON DELETE RESTRICT,
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id) ON DELETE RESTRICT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY(app_role_id,app_permission_id)
);

WITH inserted AS (
    INSERT INTO app_permissions(permission_code,permission_name,module_code,permission_description)
    VALUES
      ('VIEW_FULL_FUTURE_LOOP_AUTOMATION_083','View Full Future Loop Automation','083','View autonomous readiness, policies, adapters, dry runs, approvals, manifests, and evidence.'),
      ('OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083','Operate Full Future Loop Dry Runs','083','Create idempotent autonomous policy simulations and durable dry-run plans without external execution.'),
      ('MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083','Manage Full Future Loop Automation','083','Manage the kill switch, dry-run runtime state, and disabled or dry-run adapter modes.'),
      ('APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083','Approve Full Future Loop Automation','083','Approve or reject gated automation runs under separation-of-duties controls.')
    ON CONFLICT(permission_code) DO NOTHING
    RETURNING app_permission_id,permission_code
)
INSERT INTO full_future_loop_083_permissions_created(app_permission_id,permission_code)
SELECT app_permission_id,permission_code FROM inserted ON CONFLICT DO NOTHING;

WITH desired(role_code,permission_code) AS (
    VALUES
      ('SUPER_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPER_ADMINISTRATOR','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPER_ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPER_ADMINISTRATOR','APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ADMINISTRATOR','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ADMINISTRATOR','APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SYSTEM_ADMINISTRATOR','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SYSTEM_ADMINISTRATOR','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SYSTEM_ADMINISTRATOR','MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SYSTEM_ADMINISTRATOR','APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('RELEASE_MANAGER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('RELEASE_MANAGER','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('RELEASE_MANAGER','MANAGE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('RELEASE_MANAGER','APPROVE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_TEAM_COORDINATOR','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_TEAM_COORDINATOR','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('MANAGER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('MANAGER','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_MANAGER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_MANAGER','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_LEAD','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_LEAD','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_TEAM_LEAD','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING_TEAM_LEAD','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_MANAGER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_MANAGER','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_MANAGEMENT','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('PROJECT_MANAGEMENT','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPPORT_MANAGER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPPORT_MANAGER','OPERATE_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('ENGINEERING','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SYSTEMS_ENGINEER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('NETWORK_ENGINEER','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SOLUTION_ARCHITECT','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SUPPORT','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('HELP_DESK','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('SERVICE_DESK','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('EXECUTIVE','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083'),
      ('EXECUTIVE_LEADERSHIP','VIEW_FULL_FUTURE_LOOP_AUTOMATION_083')
), candidates AS (
    SELECT role.app_role_id,permission.app_permission_id
    FROM desired
    JOIN app_roles role ON upper(role.role_code)=desired.role_code AND role.is_active=TRUE
    JOIN app_permissions permission ON permission.permission_code=desired.permission_code
    LEFT JOIN app_role_permissions existing
      ON existing.app_role_id=role.app_role_id AND existing.app_permission_id=permission.app_permission_id
    WHERE existing.app_role_permission_id IS NULL
), inserted AS (
    INSERT INTO app_role_permissions(app_role_id,app_permission_id,created_at)
    SELECT app_role_id,app_permission_id,NOW() FROM candidates
    ON CONFLICT(app_role_id,app_permission_id) DO NOTHING
    RETURNING app_role_id,app_permission_id
)
INSERT INTO full_future_loop_083_role_grants(app_role_id,app_permission_id)
SELECT app_role_id,app_permission_id FROM inserted ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog(
    feature_code,feature_name,module_code,route_anchor,required_permission_code,
    feature_description,display_order,is_active)
VALUES(
    'FULL_FUTURE_LOOP_AUTOMATION_083',
    'Full Future Loop Autonomous Control Plane',
    '083',
    '#full-future-loop',
    'VIEW_FULL_FUTURE_LOOP_AUTOMATION_083',
    'Fail-closed enterprise policy simulation, durable dry-run orchestration, approvals, immutable release manifests, adapter readiness, blocked outbox, and append-only evidence.',
    183,
    TRUE)
ON CONFLICT(feature_code) DO UPDATE SET
    feature_name=EXCLUDED.feature_name,
    module_code=EXCLUDED.module_code,
    route_anchor=EXCLUDED.route_anchor,
    required_permission_code=EXCLUDED.required_permission_code,
    feature_description=EXCLUDED.feature_description,
    display_order=EXCLUDED.display_order,
    is_active=TRUE,
    updated_at=NOW();

INSERT INTO schema_migrations(migration_id,description,applied_at)
VALUES(
    '083_module_083_autonomous_control_plane',
    'Create fail-closed Module 083 autonomous policy, durable dry-run orchestration, approvals, manifests, adapters, blocked outbox, and append-only evidence',
    NOW())
ON CONFLICT(migration_id) DO NOTHING;

COMMIT;
