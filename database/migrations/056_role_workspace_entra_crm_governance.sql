-- ProjectPulse migration 056
-- Canonical role workspaces, Module 065 client-secret expiration governance,
-- and Module 026 persistent OAuth renewal.
--
-- This migration stores no client-secret, access-token, or refresh-token value.
-- It does not call Microsoft Graph, send mail, call a CRM provider, deploy an
-- environment, or change Azure resources. Runtime delivery and provider calls
-- remain separately governed by existing Module 065 and Module 026 controls.

BEGIN;

DO $prerequisites$
BEGIN
    IF to_regclass('public.app_roles') IS NULL
       OR to_regclass('public.app_permissions') IS NULL
       OR to_regclass('public.app_role_permissions') IS NULL
       OR to_regclass('public.app_users') IS NULL
       OR to_regclass('public.app_user_role_assignments') IS NULL
       OR to_regclass('public.app_feature_catalog') IS NULL THEN
        RAISE EXCEPTION 'Migration 056 requires the role-based access-control foundation.';
    END IF;

    IF to_regclass('public.crm_integration_providers') IS NULL
       OR to_regclass('public.crm_integration_credentials') IS NULL THEN
        RAISE EXCEPTION 'Migration 056 requires Module 026 migration 034.';
    END IF;

    IF to_regclass('public.project_notification_dispatches') IS NULL
       OR to_regclass('public.project_notification_dispatch_recipients') IS NULL THEN
        RAISE EXCEPTION 'Migration 056 requires project-notification migration 050.';
    END IF;
END;
$prerequisites$;

CREATE TABLE IF NOT EXISTS entra_secret_expiration_profile_versions (
    profile_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    generation INTEGER NOT NULL UNIQUE CHECK (generation > 0),
    application_name VARCHAR(200) NOT NULL,
    environment_name VARCHAR(40) NOT NULL CHECK (environment_name IN ('development', 'test', 'production')),
    secret_label VARCHAR(200) NOT NULL,
    secret_version VARCHAR(120) NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    reminder_start_days INTEGER NOT NULL DEFAULT 30 CHECK (reminder_start_days BETWEEN 7 AND 365),
    critical_start_days INTEGER NOT NULL DEFAULT 7 CHECK (critical_start_days BETWEEN 1 AND 30),
    reminder_interval_hours INTEGER NOT NULL DEFAULT 24 CHECK (reminder_interval_hours BETWEEN 1 AND 168),
    change_reason TEXT NOT NULL,
    created_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CHECK (critical_start_days <= reminder_start_days)
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_profiles_expires
    ON entra_secret_expiration_profile_versions(expires_at, generation DESC);

CREATE TABLE IF NOT EXISTS entra_secret_expiration_state (
    singleton_key BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK (singleton_key),
    active_profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    updated_by_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS entra_secret_expiration_recipients (
    profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    display_name VARCHAR(320) NOT NULL,
    email VARCHAR(320) NOT NULL,
    role_code VARCHAR(100) NOT NULL,
    snapshotted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (profile_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_recipients_email
    ON entra_secret_expiration_recipients(profile_id, lower(email));

CREATE TABLE IF NOT EXISTS entra_secret_expiration_acknowledgements (
    acknowledgement_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledged_by_actual_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    acknowledgement_statement TEXT NOT NULL,
    acknowledged_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (profile_id, user_id)
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_acknowledgements_profile
    ON entra_secret_expiration_acknowledgements(profile_id, acknowledged_at DESC);

CREATE TABLE IF NOT EXISTS entra_secret_expiration_reminder_claims (
    reminder_claim_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    reminder_bucket BIGINT NOT NULL,
    claim_status VARCHAR(40) NOT NULL DEFAULT 'claimed' CHECK (claim_status IN (
        'claimed', 'sent', 'queued', 'suppressed', 'held', 'failed', 'disabled'
    )),
    dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    claimed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at TIMESTAMPTZ NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (profile_id, user_id, reminder_bucket)
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_claims_due
    ON entra_secret_expiration_reminder_claims(profile_id, user_id, claimed_at DESC);
CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_claims_dispatch
    ON entra_secret_expiration_reminder_claims(dispatch_id)
    WHERE dispatch_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS entra_secret_expiration_reminder_events (
    reminder_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reminder_claim_id UUID NOT NULL REFERENCES entra_secret_expiration_reminder_claims(reminder_claim_id) ON DELETE RESTRICT,
    profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    dispatch_id UUID NULL REFERENCES project_notification_dispatches(project_notification_dispatch_id) ON DELETE SET NULL,
    event_code VARCHAR(120) NOT NULL,
    delivery_status VARCHAR(40) NOT NULL,
    provider_source VARCHAR(80) NOT NULL DEFAULT '',
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_reminder_events_profile
    ON entra_secret_expiration_reminder_events(profile_id, user_id, created_at DESC);

CREATE TABLE IF NOT EXISTS entra_secret_expiration_audit_events (
    audit_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    profile_id UUID NOT NULL REFERENCES entra_secret_expiration_profile_versions(profile_id) ON DELETE RESTRICT,
    event_code VARCHAR(100) NOT NULL,
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id) ON DELETE RESTRICT,
    actor_email VARCHAR(320) NOT NULL,
    event_reason TEXT NOT NULL DEFAULT '',
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    correlation_id VARCHAR(160) NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_audit_profile
    ON entra_secret_expiration_audit_events(profile_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_entra_secret_expiration_audit_correlation
    ON entra_secret_expiration_audit_events(correlation_id)
    WHERE correlation_id <> '';

CREATE TABLE IF NOT EXISTS crm_integration_token_refresh_events (
    refresh_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_key VARCHAR(60) NOT NULL REFERENCES crm_integration_providers(provider_key) ON DELETE RESTRICT,
    refresh_trigger VARCHAR(40) NOT NULL CHECK (refresh_trigger IN ('manual', 'background', 'connection_test', 'unknown')),
    refresh_status VARCHAR(80) NOT NULL,
    diagnostic_code VARCHAR(120) NOT NULL DEFAULT '',
    provider_http_status INTEGER NULL CHECK (provider_http_status IS NULL OR provider_http_status BETWEEN 100 AND 599),
    next_expires_at TIMESTAMPTZ NULL,
    actor_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    event_metadata JSONB NOT NULL DEFAULT '{}'::JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_crm_integration_token_refresh_provider
    ON crm_integration_token_refresh_events(provider_key, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_crm_integration_token_refresh_status
    ON crm_integration_token_refresh_events(refresh_status, created_at DESC);

-- Track only the permission rows changed by migration 056 so rollback does not
-- disturb later administrator decisions or unrelated role policy.
CREATE TABLE IF NOT EXISTS role_workspace_permission_changes_056 (
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    change_kind VARCHAR(20) NOT NULL CHECK (change_kind IN ('granted', 'removed')),
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (role_code, permission_code, change_kind)
);

CREATE OR REPLACE FUNCTION projectpulse_056_block_immutable_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $projectpulse_056_immutable$
BEGIN
    RAISE EXCEPTION 'ProjectPulse migration 056 evidence is immutable.';
END;
$projectpulse_056_immutable$;

DROP TRIGGER IF EXISTS trg_entra_secret_expiration_profiles_immutable
    ON entra_secret_expiration_profile_versions;
CREATE TRIGGER trg_entra_secret_expiration_profiles_immutable
BEFORE UPDATE OR DELETE ON entra_secret_expiration_profile_versions
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_entra_secret_expiration_recipients_immutable
    ON entra_secret_expiration_recipients;
CREATE TRIGGER trg_entra_secret_expiration_recipients_immutable
BEFORE UPDATE OR DELETE ON entra_secret_expiration_recipients
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_entra_secret_expiration_acknowledgements_immutable
    ON entra_secret_expiration_acknowledgements;
CREATE TRIGGER trg_entra_secret_expiration_acknowledgements_immutable
BEFORE UPDATE OR DELETE ON entra_secret_expiration_acknowledgements
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_entra_secret_expiration_reminder_events_immutable
    ON entra_secret_expiration_reminder_events;
CREATE TRIGGER trg_entra_secret_expiration_reminder_events_immutable
BEFORE UPDATE OR DELETE ON entra_secret_expiration_reminder_events
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_entra_secret_expiration_audit_events_immutable
    ON entra_secret_expiration_audit_events;
CREATE TRIGGER trg_entra_secret_expiration_audit_events_immutable
BEFORE UPDATE OR DELETE ON entra_secret_expiration_audit_events
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_crm_integration_token_refresh_events_immutable
    ON crm_integration_token_refresh_events;
CREATE TRIGGER trg_crm_integration_token_refresh_events_immutable
BEFORE UPDATE OR DELETE ON crm_integration_token_refresh_events
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

DROP TRIGGER IF EXISTS trg_role_workspace_permission_changes_056_immutable
    ON role_workspace_permission_changes_056;
CREATE TRIGGER trg_role_workspace_permission_changes_056_immutable
BEFORE UPDATE OR DELETE ON role_workspace_permission_changes_056
FOR EACH ROW EXECUTE FUNCTION projectpulse_056_block_immutable_mutation();

INSERT INTO app_permissions (
    permission_code,
    permission_name,
    module_code,
    permission_description
)
VALUES
    ('VIEW_ENTRA_SECRET_EXPIRATION', 'View Entra Client Secret Expiration', '065', 'View non-secret client-secret expiration metadata, reminder state, recipient acknowledgement status, and critical-warning readiness.'),
    ('MANAGE_ENTRA_SECRET_EXPIRATION', 'Manage Entra Client Secret Expiration', '065', 'Publish a new non-secret expiration profile and evaluate governed reminders. This permission never grants access to the secret value.'),
    ('ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION', 'Acknowledge Entra Client Secret Expiration', '065', 'Record the current recipient acknowledgement for an active client-secret expiration profile.')
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
    ('ENTRA_SECRET_EXPIRATION_GOVERNANCE', 'Entra Client Secret Expiration Governance', '065', '#entra-secret-administration', 'VIEW_ENTRA_SECRET_EXPIRATION', 'Non-secret expiration metadata, Project Team Coordinator reminders and individual acknowledgement, immutable evidence, and the seven-day organization warning.', 651, TRUE),
    ('CRM_ERP_OAUTH_PERSISTENCE', 'CRM and ERP OAuth Persistence', '026', '#crm-integration', 'VIEW_INTEGRATIONS_026', 'Server-side encrypted OAuth renewal status and governed persistent connector operation.', 262, TRUE)
ON CONFLICT (feature_code) DO UPDATE
SET feature_name = EXCLUDED.feature_name,
    module_code = EXCLUDED.module_code,
    route_anchor = EXCLUDED.route_anchor,
    required_permission_code = EXCLUDED.required_permission_code,
    feature_description = EXCLUDED.feature_description,
    display_order = EXCLUDED.display_order,
    is_active = TRUE,
    updated_at = NOW();

CREATE TEMP TABLE projectpulse_056_role_module_baseline (
    role_code VARCHAR(75) NOT NULL,
    module_code VARCHAR(75) NOT NULL,
    PRIMARY KEY (role_code, module_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_056_role_module_baseline (role_code, module_code)
VALUES
    -- Project Management workspace.
    ('PROJECT_MANAGER', '002'), ('PROJECT_MANAGER', '018'), ('PROJECT_MANAGER', '019'), ('PROJECT_MANAGER', '020'),
    ('PROJECT_MANAGER', '021'), ('PROJECT_MANAGER', '022'), ('PROJECT_MANAGER', '027'), ('PROJECT_MANAGER', '030'),
    ('PROJECT_MANAGER', '040'), ('PROJECT_MANAGER', '041'), ('PROJECT_MANAGER', '055C'), ('PROJECT_MANAGER', '060'),
    ('PROJECT_MANAGER', '066'), ('PROJECT_MANAGER', '999'),
    ('PROJECT_MANAGEMENT', '002'), ('PROJECT_MANAGEMENT', '018'), ('PROJECT_MANAGEMENT', '019'), ('PROJECT_MANAGEMENT', '020'),
    ('PROJECT_MANAGEMENT', '021'), ('PROJECT_MANAGEMENT', '022'), ('PROJECT_MANAGEMENT', '027'), ('PROJECT_MANAGEMENT', '030'),
    ('PROJECT_MANAGEMENT', '040'), ('PROJECT_MANAGEMENT', '041'), ('PROJECT_MANAGEMENT', '055C'), ('PROJECT_MANAGEMENT', '060'),
    ('PROJECT_MANAGEMENT', '066'), ('PROJECT_MANAGEMENT', '999'),
    ('PROJECT_MANAGEMENT_LEAD', '002'), ('PROJECT_MANAGEMENT_LEAD', '018'), ('PROJECT_MANAGEMENT_LEAD', '019'), ('PROJECT_MANAGEMENT_LEAD', '020'),
    ('PROJECT_MANAGEMENT_LEAD', '021'), ('PROJECT_MANAGEMENT_LEAD', '022'), ('PROJECT_MANAGEMENT_LEAD', '027'), ('PROJECT_MANAGEMENT_LEAD', '030'),
    ('PROJECT_MANAGEMENT_LEAD', '040'), ('PROJECT_MANAGEMENT_LEAD', '041'), ('PROJECT_MANAGEMENT_LEAD', '055C'), ('PROJECT_MANAGEMENT_LEAD', '060'),
    ('PROJECT_MANAGEMENT_LEAD', '066'), ('PROJECT_MANAGEMENT_LEAD', '999'),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', '002'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '018'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '019'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '020'),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', '021'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '022'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '027'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '030'),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', '040'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '041'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '055C'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '060'),
    ('PROJECT_MANAGEMENT_TEAM_LEAD', '066'), ('PROJECT_MANAGEMENT_TEAM_LEAD', '999'),
    ('PM_TEAM_LEAD', '002'), ('PM_TEAM_LEAD', '018'), ('PM_TEAM_LEAD', '019'), ('PM_TEAM_LEAD', '020'),
    ('PM_TEAM_LEAD', '021'), ('PM_TEAM_LEAD', '022'), ('PM_TEAM_LEAD', '027'), ('PM_TEAM_LEAD', '030'),
    ('PM_TEAM_LEAD', '040'), ('PM_TEAM_LEAD', '041'), ('PM_TEAM_LEAD', '055C'), ('PM_TEAM_LEAD', '060'),
    ('PM_TEAM_LEAD', '066'), ('PM_TEAM_LEAD', '999'),

    -- Accounting and Billing workspace.
    ('ACCOUNTING', '007'), ('ACCOUNTING', '008'), ('ACCOUNTING', '021'), ('ACCOUNTING', '030'),
    ('ACCOUNTING', '038'), ('ACCOUNTING', '039'), ('ACCOUNTING', '040'), ('ACCOUNTING', '041'),
    ('ACCOUNTING', '042'), ('ACCOUNTING', '060'), ('ACCOUNTING', '999'),
    ('ACCOUNTING_BILLING', '007'), ('ACCOUNTING_BILLING', '008'), ('ACCOUNTING_BILLING', '021'), ('ACCOUNTING_BILLING', '030'),
    ('ACCOUNTING_BILLING', '038'), ('ACCOUNTING_BILLING', '039'), ('ACCOUNTING_BILLING', '040'), ('ACCOUNTING_BILLING', '041'),
    ('ACCOUNTING_BILLING', '042'), ('ACCOUNTING_BILLING', '060'), ('ACCOUNTING_BILLING', '999'),
    ('BILLING', '007'), ('BILLING', '008'), ('BILLING', '021'), ('BILLING', '030'),
    ('BILLING', '038'), ('BILLING', '039'), ('BILLING', '040'), ('BILLING', '041'),
    ('BILLING', '042'), ('BILLING', '060'), ('BILLING', '999'),
    ('FINANCE', '007'), ('FINANCE', '008'), ('FINANCE', '021'), ('FINANCE', '030'),
    ('FINANCE', '038'), ('FINANCE', '039'), ('FINANCE', '040'), ('FINANCE', '041'),
    ('FINANCE', '042'), ('FINANCE', '060'), ('FINANCE', '999'),

    -- Sales and Inside Sales / Resale workspace.
    ('SALES', '020'), ('SALES', '021'), ('SALES', '024'), ('SALES', '025'), ('SALES', '026'),
    ('SALES', '027'), ('SALES', '030'), ('SALES', '036'), ('SALES', '060'), ('SALES', '063'),
    ('SALES', '073'), ('SALES', '074'), ('SALES', '999'),
    ('INSIDE_SALES', '020'), ('INSIDE_SALES', '021'), ('INSIDE_SALES', '024'), ('INSIDE_SALES', '025'), ('INSIDE_SALES', '026'),
    ('INSIDE_SALES', '027'), ('INSIDE_SALES', '030'), ('INSIDE_SALES', '036'), ('INSIDE_SALES', '060'), ('INSIDE_SALES', '063'),
    ('INSIDE_SALES', '073'), ('INSIDE_SALES', '074'), ('INSIDE_SALES', '999'),
    ('RESALE', '020'), ('RESALE', '021'), ('RESALE', '024'), ('RESALE', '025'), ('RESALE', '026'),
    ('RESALE', '027'), ('RESALE', '030'), ('RESALE', '036'), ('RESALE', '060'), ('RESALE', '063'),
    ('RESALE', '073'), ('RESALE', '074'), ('RESALE', '999'),
    ('ACCOUNT_EXECUTIVE', '020'), ('ACCOUNT_EXECUTIVE', '021'), ('ACCOUNT_EXECUTIVE', '024'), ('ACCOUNT_EXECUTIVE', '025'), ('ACCOUNT_EXECUTIVE', '026'),
    ('ACCOUNT_EXECUTIVE', '027'), ('ACCOUNT_EXECUTIVE', '030'), ('ACCOUNT_EXECUTIVE', '036'), ('ACCOUNT_EXECUTIVE', '060'), ('ACCOUNT_EXECUTIVE', '063'),
    ('ACCOUNT_EXECUTIVE', '073'), ('ACCOUNT_EXECUTIVE', '074'), ('ACCOUNT_EXECUTIVE', '999'),
    ('ACCOUNT_EXECUTIVES', '020'), ('ACCOUNT_EXECUTIVES', '021'), ('ACCOUNT_EXECUTIVES', '024'), ('ACCOUNT_EXECUTIVES', '025'), ('ACCOUNT_EXECUTIVES', '026'),
    ('ACCOUNT_EXECUTIVES', '027'), ('ACCOUNT_EXECUTIVES', '030'), ('ACCOUNT_EXECUTIVES', '036'), ('ACCOUNT_EXECUTIVES', '060'), ('ACCOUNT_EXECUTIVES', '063'),
    ('ACCOUNT_EXECUTIVES', '073'), ('ACCOUNT_EXECUTIVES', '074'), ('ACCOUNT_EXECUTIVES', '999'),
    ('SALES_MANAGER', '020'), ('SALES_MANAGER', '021'), ('SALES_MANAGER', '024'), ('SALES_MANAGER', '025'), ('SALES_MANAGER', '026'),
    ('SALES_MANAGER', '027'), ('SALES_MANAGER', '030'), ('SALES_MANAGER', '036'), ('SALES_MANAGER', '060'), ('SALES_MANAGER', '063'),
    ('SALES_MANAGER', '073'), ('SALES_MANAGER', '074'), ('SALES_MANAGER', '999');

CREATE TEMP TABLE projectpulse_056_candidate_grants (
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    PRIMARY KEY (role_code, permission_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_056_candidate_grants (role_code, permission_code)
SELECT DISTINCT baseline.role_code, permission.permission_code
FROM projectpulse_056_role_module_baseline baseline
JOIN app_permissions permission
  ON upper(permission.module_code) = upper(baseline.module_code)
WHERE upper(permission.permission_code) LIKE 'VIEW\_%' ESCAPE '\'
   OR upper(permission.permission_code) LIKE 'READ\_%' ESCAPE '\'
   OR upper(permission.permission_code) LIKE 'EXPORT\_%' ESCAPE '\'
   OR upper(permission.permission_code) IN ('MODULE_ACCESS', 'MODULE_VIEW', 'ACCESS_EXPLAIN')
UNION
SELECT DISTINCT baseline.role_code, feature.required_permission_code
FROM projectpulse_056_role_module_baseline baseline
JOIN app_feature_catalog feature
  ON upper(feature.module_code) = upper(baseline.module_code)
WHERE feature.is_active = TRUE
  AND feature.required_permission_code IS NOT NULL
  AND (
      upper(feature.required_permission_code) LIKE 'VIEW\_%' ESCAPE '\'
      OR upper(feature.required_permission_code) LIKE 'READ\_%' ESCAPE '\'
      OR upper(feature.required_permission_code) LIKE 'EXPORT\_%' ESCAPE '\'
      OR upper(feature.required_permission_code) IN ('MODULE_ACCESS', 'MODULE_VIEW', 'ACCESS_EXPLAIN')
  )
ON CONFLICT DO NOTHING;

-- Required legacy permissions whose historical module_code predates numeric
-- module identifiers.
INSERT INTO projectpulse_056_candidate_grants (role_code, permission_code)
SELECT role_code, permission_code
FROM (
    VALUES
        ('PROJECT_MANAGER', 'VIEW_APPROVAL_INBOX'), ('PROJECT_MANAGER', 'APPROVE_TIME'), ('PROJECT_MANAGER', 'REJECT_TIME'), ('PROJECT_MANAGER', 'PROJECT_TIME_APPROVAL'),
        ('PROJECT_MANAGER', 'VIEW_PROJECT_INTAKE'), ('PROJECT_MANAGER', 'MANAGE_PROJECT_INTAKE'), ('PROJECT_MANAGER', 'VIEW_RESOURCE_SCHEDULING'), ('PROJECT_MANAGER', 'MANAGE_RESOURCE_SCHEDULING'), ('PROJECT_MANAGER', 'VIEW_REPORTS'),
        ('PROJECT_MANAGEMENT', 'VIEW_APPROVAL_INBOX'), ('PROJECT_MANAGEMENT', 'APPROVE_TIME'), ('PROJECT_MANAGEMENT', 'REJECT_TIME'), ('PROJECT_MANAGEMENT', 'PROJECT_TIME_APPROVAL'),
        ('PROJECT_MANAGEMENT', 'VIEW_PROJECT_INTAKE'), ('PROJECT_MANAGEMENT', 'MANAGE_PROJECT_INTAKE'), ('PROJECT_MANAGEMENT', 'VIEW_RESOURCE_SCHEDULING'), ('PROJECT_MANAGEMENT', 'MANAGE_RESOURCE_SCHEDULING'), ('PROJECT_MANAGEMENT', 'VIEW_REPORTS'),
        ('PROJECT_MANAGEMENT_LEAD', 'VIEW_APPROVAL_INBOX'), ('PROJECT_MANAGEMENT_LEAD', 'APPROVE_TIME'), ('PROJECT_MANAGEMENT_LEAD', 'REJECT_TIME'), ('PROJECT_MANAGEMENT_LEAD', 'PROJECT_TIME_APPROVAL'),
        ('PROJECT_MANAGEMENT_TEAM_LEAD', 'VIEW_APPROVAL_INBOX'), ('PROJECT_MANAGEMENT_TEAM_LEAD', 'APPROVE_TIME'), ('PROJECT_MANAGEMENT_TEAM_LEAD', 'REJECT_TIME'), ('PROJECT_MANAGEMENT_TEAM_LEAD', 'PROJECT_TIME_APPROVAL'),
        ('PM_TEAM_LEAD', 'VIEW_APPROVAL_INBOX'), ('PM_TEAM_LEAD', 'APPROVE_TIME'), ('PM_TEAM_LEAD', 'REJECT_TIME'), ('PM_TEAM_LEAD', 'PROJECT_TIME_APPROVAL'),
        ('ACCOUNTING', 'VIEW_ACCOUNT_RECONCILIATION'), ('ACCOUNTING', 'MANAGE_ACCOUNT_RECONCILIATION'), ('ACCOUNTING', 'VIEW_AUDIT_TRAIL'), ('ACCOUNTING', 'VIEW_REPORTS'),
        ('ACCOUNTING_BILLING', 'VIEW_ACCOUNT_RECONCILIATION'), ('ACCOUNTING_BILLING', 'MANAGE_ACCOUNT_RECONCILIATION'), ('ACCOUNTING_BILLING', 'VIEW_AUDIT_TRAIL'), ('ACCOUNTING_BILLING', 'VIEW_REPORTS'),
        ('BILLING', 'VIEW_ACCOUNT_RECONCILIATION'), ('BILLING', 'MANAGE_ACCOUNT_RECONCILIATION'), ('BILLING', 'VIEW_AUDIT_TRAIL'), ('BILLING', 'VIEW_REPORTS'),
        ('FINANCE', 'VIEW_ACCOUNT_RECONCILIATION'), ('FINANCE', 'MANAGE_ACCOUNT_RECONCILIATION'), ('FINANCE', 'VIEW_AUDIT_TRAIL'), ('FINANCE', 'VIEW_REPORTS'),
        ('SALES', 'VIEW_PROJECT_INTAKE'), ('SALES', 'VIEW_CUSTOMERS'), ('SALES', 'VIEW_REPORTS'), ('SALES', 'VIEW_INTEGRATIONS_026'),
        ('INSIDE_SALES', 'VIEW_PROJECT_INTAKE'), ('INSIDE_SALES', 'VIEW_CUSTOMERS'), ('INSIDE_SALES', 'VIEW_REPORTS'), ('INSIDE_SALES', 'VIEW_INTEGRATIONS_026'),
        ('RESALE', 'VIEW_PROJECT_INTAKE'), ('RESALE', 'VIEW_CUSTOMERS'), ('RESALE', 'VIEW_REPORTS'), ('RESALE', 'VIEW_INTEGRATIONS_026'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_PROJECT_INTAKE'), ('ACCOUNT_EXECUTIVE', 'VIEW_CUSTOMERS'), ('ACCOUNT_EXECUTIVE', 'VIEW_REPORTS'), ('ACCOUNT_EXECUTIVE', 'VIEW_INTEGRATIONS_026'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_PROJECT_INTAKE'), ('ACCOUNT_EXECUTIVES', 'VIEW_CUSTOMERS'), ('ACCOUNT_EXECUTIVES', 'VIEW_REPORTS'), ('ACCOUNT_EXECUTIVES', 'VIEW_INTEGRATIONS_026'),
        ('SALES_MANAGER', 'VIEW_PROJECT_INTAKE'), ('SALES_MANAGER', 'VIEW_CUSTOMERS'), ('SALES_MANAGER', 'VIEW_REPORTS'), ('SALES_MANAGER', 'VIEW_INTEGRATIONS_026')
) explicit_grants(role_code, permission_code)
JOIN app_permissions permission USING (permission_code)
ON CONFLICT DO NOTHING;

-- Module 065 governance and Module 026 administrator authority.
INSERT INTO projectpulse_056_candidate_grants (role_code, permission_code)
SELECT role_code, permission_code
FROM (
    VALUES
        ('SUPER_ADMINISTRATOR', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_ENTRA_SECRET_EXPIRATION'),
        ('SUPER_ADMINISTRATOR', 'MANAGE_INTEGRATIONS_026'),
        ('ADMINISTRATOR', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('ADMINISTRATOR', 'MANAGE_ENTRA_SECRET_EXPIRATION'),
        ('ADMINISTRATOR', 'MANAGE_INTEGRATIONS_026'),
        ('INTEGRATION_ADMINISTRATOR', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('INTEGRATION_ADMINISTRATOR', 'MANAGE_ENTRA_SECRET_EXPIRATION'),
        ('INTEGRATION_ADMINISTRATOR', 'MANAGE_INTEGRATIONS_026'),
        ('PROJECT_TEAM_COORDINATOR', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('PROJECT_TEAM_COORDINATOR', 'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION'),
        ('PROJECT_COORDINATOR', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('PROJECT_COORDINATOR', 'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION'),
        ('PTC', 'VIEW_ENTRA_SECRET_EXPIRATION'),
        ('PTC', 'ACKNOWLEDGE_ENTRA_SECRET_EXPIRATION')
) governance_grants(role_code, permission_code)
JOIN app_permissions permission USING (permission_code)
ON CONFLICT DO NOTHING;

INSERT INTO role_workspace_permission_changes_056 (role_code, permission_code, change_kind)
SELECT role.role_code, permission.permission_code, 'granted'
FROM projectpulse_056_candidate_grants candidate
JOIN app_roles role
  ON upper(role.role_code) = upper(candidate.role_code)
 AND role.is_active = TRUE
JOIN app_permissions permission
  ON permission.permission_code = candidate.permission_code
LEFT JOIN app_role_permissions existing
  ON existing.app_role_id = role.app_role_id
 AND existing.app_permission_id = permission.app_permission_id
WHERE existing.app_role_permission_id IS NULL
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM projectpulse_056_candidate_grants candidate
JOIN app_roles role
  ON upper(role.role_code) = upper(candidate.role_code)
 AND role.is_active = TRUE
JOIN app_permissions permission
  ON permission.permission_code = candidate.permission_code
ON CONFLICT DO NOTHING;

CREATE TEMP TABLE projectpulse_056_business_denials (
    role_code VARCHAR(75) NOT NULL,
    permission_code VARCHAR(100) NOT NULL,
    PRIMARY KEY (role_code, permission_code)
) ON COMMIT DROP;

INSERT INTO projectpulse_056_business_denials (role_code, permission_code)
SELECT role_code, permission_code
FROM (
    VALUES
        ('ACCOUNTING', 'VIEW_TIME_ENTRY'), ('ACCOUNTING', 'EDIT_OWN_TIME'), ('ACCOUNTING', 'SUBMIT_OWN_TIME'), ('ACCOUNTING', 'MANAGE_TIME'),
        ('ACCOUNTING', 'EDIT_HISTORICAL_TIME'), ('ACCOUNTING', 'VIEW_APPROVAL_INBOX'), ('ACCOUNTING', 'APPROVE_TIME'), ('ACCOUNTING', 'REJECT_TIME'), ('ACCOUNTING', 'UNLOCK_TIME'), ('ACCOUNTING', 'PROJECT_TIME_APPROVAL'),
        ('ACCOUNTING', 'VIEW_OWN_UTILIZATION'), ('ACCOUNTING', 'VIEW_TEAM_UTILIZATION'), ('ACCOUNTING', 'VIEW_INDIVIDUAL_UTILIZATION'), ('ACCOUNTING', 'VIEW_HOLIDAYS'), ('ACCOUNTING', 'MANAGE_HOLIDAYS'),
        ('ACCOUNTING', 'VIEW_PROJECT_ALLOCATION_INFO'), ('ACCOUNTING', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('ACCOUNTING', 'VIEW_PROJECT_WORKLOAD'), ('ACCOUNTING', 'VIEW_PROJECT_WORKSPACE'),
        ('ACCOUNTING', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('ACCOUNTING', 'MANAGE_PROJECT_DOCUMENTS'), ('ACCOUNTING', 'VIEW_RESOURCE_SCHEDULING'), ('ACCOUNTING', 'MANAGE_RESOURCE_SCHEDULING'),
        ('ACCOUNTING_BILLING', 'VIEW_TIME_ENTRY'), ('ACCOUNTING_BILLING', 'EDIT_OWN_TIME'), ('ACCOUNTING_BILLING', 'SUBMIT_OWN_TIME'), ('ACCOUNTING_BILLING', 'MANAGE_TIME'),
        ('ACCOUNTING_BILLING', 'EDIT_HISTORICAL_TIME'), ('ACCOUNTING_BILLING', 'VIEW_APPROVAL_INBOX'), ('ACCOUNTING_BILLING', 'APPROVE_TIME'), ('ACCOUNTING_BILLING', 'REJECT_TIME'), ('ACCOUNTING_BILLING', 'UNLOCK_TIME'), ('ACCOUNTING_BILLING', 'PROJECT_TIME_APPROVAL'),
        ('ACCOUNTING_BILLING', 'VIEW_OWN_UTILIZATION'), ('ACCOUNTING_BILLING', 'VIEW_TEAM_UTILIZATION'), ('ACCOUNTING_BILLING', 'VIEW_INDIVIDUAL_UTILIZATION'), ('ACCOUNTING_BILLING', 'VIEW_HOLIDAYS'), ('ACCOUNTING_BILLING', 'MANAGE_HOLIDAYS'),
        ('ACCOUNTING_BILLING', 'VIEW_PROJECT_ALLOCATION_INFO'), ('ACCOUNTING_BILLING', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('ACCOUNTING_BILLING', 'VIEW_PROJECT_WORKLOAD'), ('ACCOUNTING_BILLING', 'VIEW_PROJECT_WORKSPACE'),
        ('ACCOUNTING_BILLING', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('ACCOUNTING_BILLING', 'MANAGE_PROJECT_DOCUMENTS'), ('ACCOUNTING_BILLING', 'VIEW_RESOURCE_SCHEDULING'), ('ACCOUNTING_BILLING', 'MANAGE_RESOURCE_SCHEDULING'),
        ('BILLING', 'VIEW_TIME_ENTRY'), ('BILLING', 'EDIT_OWN_TIME'), ('BILLING', 'SUBMIT_OWN_TIME'), ('BILLING', 'MANAGE_TIME'),
        ('BILLING', 'EDIT_HISTORICAL_TIME'), ('BILLING', 'VIEW_APPROVAL_INBOX'), ('BILLING', 'APPROVE_TIME'), ('BILLING', 'REJECT_TIME'), ('BILLING', 'UNLOCK_TIME'), ('BILLING', 'PROJECT_TIME_APPROVAL'),
        ('BILLING', 'VIEW_OWN_UTILIZATION'), ('BILLING', 'VIEW_TEAM_UTILIZATION'), ('BILLING', 'VIEW_INDIVIDUAL_UTILIZATION'), ('BILLING', 'VIEW_HOLIDAYS'), ('BILLING', 'MANAGE_HOLIDAYS'),
        ('BILLING', 'VIEW_PROJECT_ALLOCATION_INFO'), ('BILLING', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('BILLING', 'VIEW_PROJECT_WORKLOAD'), ('BILLING', 'VIEW_PROJECT_WORKSPACE'),
        ('BILLING', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('BILLING', 'MANAGE_PROJECT_DOCUMENTS'), ('BILLING', 'VIEW_RESOURCE_SCHEDULING'), ('BILLING', 'MANAGE_RESOURCE_SCHEDULING'),
        ('FINANCE', 'VIEW_TIME_ENTRY'), ('FINANCE', 'EDIT_OWN_TIME'), ('FINANCE', 'SUBMIT_OWN_TIME'), ('FINANCE', 'MANAGE_TIME'),
        ('FINANCE', 'EDIT_HISTORICAL_TIME'), ('FINANCE', 'VIEW_APPROVAL_INBOX'), ('FINANCE', 'APPROVE_TIME'), ('FINANCE', 'REJECT_TIME'), ('FINANCE', 'UNLOCK_TIME'), ('FINANCE', 'PROJECT_TIME_APPROVAL'),
        ('FINANCE', 'VIEW_OWN_UTILIZATION'), ('FINANCE', 'VIEW_TEAM_UTILIZATION'), ('FINANCE', 'VIEW_INDIVIDUAL_UTILIZATION'), ('FINANCE', 'VIEW_HOLIDAYS'), ('FINANCE', 'MANAGE_HOLIDAYS'),
        ('FINANCE', 'VIEW_PROJECT_ALLOCATION_INFO'), ('FINANCE', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('FINANCE', 'VIEW_PROJECT_WORKLOAD'), ('FINANCE', 'VIEW_PROJECT_WORKSPACE'),
        ('FINANCE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('FINANCE', 'MANAGE_PROJECT_DOCUMENTS'), ('FINANCE', 'VIEW_RESOURCE_SCHEDULING'), ('FINANCE', 'MANAGE_RESOURCE_SCHEDULING'),
        ('SALES', 'VIEW_TIME_ENTRY'), ('SALES', 'EDIT_OWN_TIME'), ('SALES', 'SUBMIT_OWN_TIME'), ('SALES', 'MANAGE_TIME'), ('SALES', 'EDIT_HISTORICAL_TIME'),
        ('SALES', 'VIEW_APPROVAL_INBOX'), ('SALES', 'APPROVE_TIME'), ('SALES', 'REJECT_TIME'), ('SALES', 'UNLOCK_TIME'), ('SALES', 'PROJECT_TIME_APPROVAL'),
        ('SALES', 'VIEW_OWN_UTILIZATION'), ('SALES', 'VIEW_TEAM_UTILIZATION'), ('SALES', 'VIEW_INDIVIDUAL_UTILIZATION'), ('SALES', 'VIEW_HOLIDAYS'), ('SALES', 'MANAGE_HOLIDAYS'),
        ('SALES', 'VIEW_PROJECT_ALLOCATION_INFO'), ('SALES', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('SALES', 'VIEW_PROJECT_WORKLOAD'), ('SALES', 'VIEW_PROJECT_WORKSPACE'),
        ('SALES', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('SALES', 'MANAGE_PROJECT_DOCUMENTS'), ('SALES', 'VIEW_RESOURCE_SCHEDULING'), ('SALES', 'MANAGE_RESOURCE_SCHEDULING'),
        ('INSIDE_SALES', 'VIEW_TIME_ENTRY'), ('INSIDE_SALES', 'EDIT_OWN_TIME'), ('INSIDE_SALES', 'SUBMIT_OWN_TIME'), ('INSIDE_SALES', 'MANAGE_TIME'), ('INSIDE_SALES', 'EDIT_HISTORICAL_TIME'),
        ('INSIDE_SALES', 'VIEW_APPROVAL_INBOX'), ('INSIDE_SALES', 'APPROVE_TIME'), ('INSIDE_SALES', 'REJECT_TIME'), ('INSIDE_SALES', 'UNLOCK_TIME'), ('INSIDE_SALES', 'PROJECT_TIME_APPROVAL'),
        ('INSIDE_SALES', 'VIEW_OWN_UTILIZATION'), ('INSIDE_SALES', 'VIEW_TEAM_UTILIZATION'), ('INSIDE_SALES', 'VIEW_INDIVIDUAL_UTILIZATION'), ('INSIDE_SALES', 'VIEW_HOLIDAYS'), ('INSIDE_SALES', 'MANAGE_HOLIDAYS'),
        ('INSIDE_SALES', 'VIEW_PROJECT_ALLOCATION_INFO'), ('INSIDE_SALES', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('INSIDE_SALES', 'VIEW_PROJECT_WORKLOAD'), ('INSIDE_SALES', 'VIEW_PROJECT_WORKSPACE'),
        ('INSIDE_SALES', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('INSIDE_SALES', 'MANAGE_PROJECT_DOCUMENTS'), ('INSIDE_SALES', 'VIEW_RESOURCE_SCHEDULING'), ('INSIDE_SALES', 'MANAGE_RESOURCE_SCHEDULING'),
        ('RESALE', 'VIEW_TIME_ENTRY'), ('RESALE', 'EDIT_OWN_TIME'), ('RESALE', 'SUBMIT_OWN_TIME'), ('RESALE', 'MANAGE_TIME'), ('RESALE', 'EDIT_HISTORICAL_TIME'),
        ('RESALE', 'VIEW_APPROVAL_INBOX'), ('RESALE', 'APPROVE_TIME'), ('RESALE', 'REJECT_TIME'), ('RESALE', 'UNLOCK_TIME'), ('RESALE', 'PROJECT_TIME_APPROVAL'),
        ('RESALE', 'VIEW_OWN_UTILIZATION'), ('RESALE', 'VIEW_TEAM_UTILIZATION'), ('RESALE', 'VIEW_INDIVIDUAL_UTILIZATION'), ('RESALE', 'VIEW_HOLIDAYS'), ('RESALE', 'MANAGE_HOLIDAYS'),
        ('RESALE', 'VIEW_PROJECT_ALLOCATION_INFO'), ('RESALE', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('RESALE', 'VIEW_PROJECT_WORKLOAD'), ('RESALE', 'VIEW_PROJECT_WORKSPACE'),
        ('RESALE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('RESALE', 'MANAGE_PROJECT_DOCUMENTS'), ('RESALE', 'VIEW_RESOURCE_SCHEDULING'), ('RESALE', 'MANAGE_RESOURCE_SCHEDULING'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_TIME_ENTRY'), ('ACCOUNT_EXECUTIVE', 'EDIT_OWN_TIME'), ('ACCOUNT_EXECUTIVE', 'SUBMIT_OWN_TIME'), ('ACCOUNT_EXECUTIVE', 'MANAGE_TIME'), ('ACCOUNT_EXECUTIVE', 'EDIT_HISTORICAL_TIME'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_APPROVAL_INBOX'), ('ACCOUNT_EXECUTIVE', 'APPROVE_TIME'), ('ACCOUNT_EXECUTIVE', 'REJECT_TIME'), ('ACCOUNT_EXECUTIVE', 'UNLOCK_TIME'), ('ACCOUNT_EXECUTIVE', 'PROJECT_TIME_APPROVAL'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_OWN_UTILIZATION'), ('ACCOUNT_EXECUTIVE', 'VIEW_TEAM_UTILIZATION'), ('ACCOUNT_EXECUTIVE', 'VIEW_INDIVIDUAL_UTILIZATION'), ('ACCOUNT_EXECUTIVE', 'VIEW_HOLIDAYS'), ('ACCOUNT_EXECUTIVE', 'MANAGE_HOLIDAYS'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_PROJECT_ALLOCATION_INFO'), ('ACCOUNT_EXECUTIVE', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('ACCOUNT_EXECUTIVE', 'VIEW_PROJECT_WORKLOAD'), ('ACCOUNT_EXECUTIVE', 'VIEW_PROJECT_WORKSPACE'),
        ('ACCOUNT_EXECUTIVE', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('ACCOUNT_EXECUTIVE', 'MANAGE_PROJECT_DOCUMENTS'), ('ACCOUNT_EXECUTIVE', 'VIEW_RESOURCE_SCHEDULING'), ('ACCOUNT_EXECUTIVE', 'MANAGE_RESOURCE_SCHEDULING'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_TIME_ENTRY'), ('ACCOUNT_EXECUTIVES', 'EDIT_OWN_TIME'), ('ACCOUNT_EXECUTIVES', 'SUBMIT_OWN_TIME'), ('ACCOUNT_EXECUTIVES', 'MANAGE_TIME'), ('ACCOUNT_EXECUTIVES', 'EDIT_HISTORICAL_TIME'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_APPROVAL_INBOX'), ('ACCOUNT_EXECUTIVES', 'APPROVE_TIME'), ('ACCOUNT_EXECUTIVES', 'REJECT_TIME'), ('ACCOUNT_EXECUTIVES', 'UNLOCK_TIME'), ('ACCOUNT_EXECUTIVES', 'PROJECT_TIME_APPROVAL'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_OWN_UTILIZATION'), ('ACCOUNT_EXECUTIVES', 'VIEW_TEAM_UTILIZATION'), ('ACCOUNT_EXECUTIVES', 'VIEW_INDIVIDUAL_UTILIZATION'), ('ACCOUNT_EXECUTIVES', 'VIEW_HOLIDAYS'), ('ACCOUNT_EXECUTIVES', 'MANAGE_HOLIDAYS'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_PROJECT_ALLOCATION_INFO'), ('ACCOUNT_EXECUTIVES', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('ACCOUNT_EXECUTIVES', 'VIEW_PROJECT_WORKLOAD'), ('ACCOUNT_EXECUTIVES', 'VIEW_PROJECT_WORKSPACE'),
        ('ACCOUNT_EXECUTIVES', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('ACCOUNT_EXECUTIVES', 'MANAGE_PROJECT_DOCUMENTS'), ('ACCOUNT_EXECUTIVES', 'VIEW_RESOURCE_SCHEDULING'), ('ACCOUNT_EXECUTIVES', 'MANAGE_RESOURCE_SCHEDULING'),
        ('SALES_MANAGER', 'VIEW_TIME_ENTRY'), ('SALES_MANAGER', 'EDIT_OWN_TIME'), ('SALES_MANAGER', 'SUBMIT_OWN_TIME'), ('SALES_MANAGER', 'MANAGE_TIME'), ('SALES_MANAGER', 'EDIT_HISTORICAL_TIME'),
        ('SALES_MANAGER', 'VIEW_APPROVAL_INBOX'), ('SALES_MANAGER', 'APPROVE_TIME'), ('SALES_MANAGER', 'REJECT_TIME'), ('SALES_MANAGER', 'UNLOCK_TIME'), ('SALES_MANAGER', 'PROJECT_TIME_APPROVAL'),
        ('SALES_MANAGER', 'VIEW_OWN_UTILIZATION'), ('SALES_MANAGER', 'VIEW_TEAM_UTILIZATION'), ('SALES_MANAGER', 'VIEW_INDIVIDUAL_UTILIZATION'), ('SALES_MANAGER', 'VIEW_HOLIDAYS'), ('SALES_MANAGER', 'MANAGE_HOLIDAYS'),
        ('SALES_MANAGER', 'VIEW_PROJECT_ALLOCATION_INFO'), ('SALES_MANAGER', 'MANAGE_PROJECT_ALLOCATION_INFO'), ('SALES_MANAGER', 'VIEW_PROJECT_WORKLOAD'), ('SALES_MANAGER', 'VIEW_PROJECT_WORKSPACE'),
        ('SALES_MANAGER', 'VIEW_ENGINEERING_PROJECT_DOCUMENTS'), ('SALES_MANAGER', 'MANAGE_PROJECT_DOCUMENTS'), ('SALES_MANAGER', 'VIEW_RESOURCE_SCHEDULING'), ('SALES_MANAGER', 'MANAGE_RESOURCE_SCHEDULING')
) denied(role_code, permission_code)
JOIN app_permissions permission USING (permission_code)
ON CONFLICT DO NOTHING;

INSERT INTO role_workspace_permission_changes_056 (role_code, permission_code, change_kind)
SELECT role.role_code, permission.permission_code, 'removed'
FROM projectpulse_056_business_denials denied
JOIN app_roles role
  ON upper(role.role_code) = upper(denied.role_code)
JOIN app_permissions permission
  ON permission.permission_code = denied.permission_code
JOIN app_role_permissions existing
  ON existing.app_role_id = role.app_role_id
 AND existing.app_permission_id = permission.app_permission_id
ON CONFLICT DO NOTHING;

DELETE FROM app_role_permissions relationship
USING projectpulse_056_business_denials denied,
      app_roles role,
      app_permissions permission
WHERE upper(role.role_code) = upper(denied.role_code)
  AND permission.permission_code = denied.permission_code
  AND relationship.app_role_id = role.app_role_id
  AND relationship.app_permission_id = permission.app_permission_id;

-- Super Administrator remains irreducible Full Control across the complete
-- permission catalog, including permissions introduced by future migrations.
INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
CROSS JOIN app_permissions permission
WHERE upper(role.role_code) = 'SUPER_ADMINISTRATOR'
  AND role.is_active = TRUE
ON CONFLICT DO NOTHING;

-- Direct migration-056 invariant: the role workspace migration itself must never
-- introduce connector-administration or platform-administration authority for
-- Accounting, Billing, Sales, Inside Sales, or Resale role families.
DO $assert_056_least_privilege$
DECLARE
    unsafe_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO unsafe_count
    FROM role_workspace_permission_changes_056 change
    WHERE change.change_kind = 'granted'
      AND upper(change.role_code) IN (
          'ACCOUNTING', 'ACCOUNTING_BILLING', 'BILLING', 'FINANCE',
          'SALES', 'INSIDE_SALES', 'RESALE',
          'ACCOUNT_EXECUTIVE', 'ACCOUNT_EXECUTIVES', 'SALES_MANAGER'
      )
      AND upper(change.permission_code) IN (
          'MANAGE_INTEGRATIONS_026', 'MANAGE_ALL', 'SYSTEM_ADMINISTRATION'
      );

    IF unsafe_count <> 0 THEN
        RAISE EXCEPTION 'Migration 056 least-privilege invariant failed: % unsafe business-role grants were introduced.', unsafe_count;
    END IF;
END;
$assert_056_least_privilege$;

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '056_role_workspace_entra_crm_governance',
    'Canonical Project Management, Accounting/Billing, Sales/Inside Sales workspaces; Module 065 expiration reminders and acknowledgement; Module 026 persistent OAuth renewal and administrator authority',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
