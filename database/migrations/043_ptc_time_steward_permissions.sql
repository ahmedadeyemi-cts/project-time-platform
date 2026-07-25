-- ProjectPulse Module 001 Project Team Coordinator time-steward foundation.
-- Adds governed operational actions without granting submission-on-behalf,
-- permanent deletion, impersonation, or platform configuration.
BEGIN;

DO $projectpulse043_prerequisite$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '042_module_availability_controls'
    ) THEN
        RAISE EXCEPTION
            'Migration 043 requires 042_module_availability_controls first.';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '040_scoped_role_policy_versions'
    ) THEN
        RAISE EXCEPTION
            'Migration 043 requires 040_scoped_role_policy_versions first.';
    END IF;
END;
$projectpulse043_prerequisite$;

INSERT INTO scoped_role_policy_actions (
    action_code, action_description, is_non_bypassable, is_active
)
VALUES
    ('TIME_VIEW_ON_BEHALF', 'Select users and view their time-management workspace without impersonating them.', FALSE, TRUE),
    ('TIME_UNSUBMIT', 'Return submitted or approved time to draft for correction and reapproval.', FALSE, TRUE),
    ('TIME_DELETE_ON_BEHALF', 'Remove an incorrect draft entry while preserving immutable audit evidence.', FALSE, TRUE),
    ('TIME_TASK_CREATE', 'Create a replacement project task for a governed time correction.', FALSE, TRUE),
    ('TIME_TASK_ASSIGN', 'Assign a replacement project task to the selected user.', FALSE, TRUE)
ON CONFLICT (action_code) DO UPDATE
SET action_description = EXCLUDED.action_description,
    is_non_bypassable = EXCLUDED.is_non_bypassable,
    is_active = TRUE;

CREATE TABLE IF NOT EXISTS scoped_time_management_events (
    scoped_time_management_event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    action_code TEXT NOT NULL REFERENCES scoped_role_policy_actions(action_code),
    actor_user_id UUID NOT NULL REFERENCES app_users(user_id),
    target_user_id UUID NOT NULL REFERENCES app_users(user_id),
    timesheet_id UUID NULL REFERENCES timesheets(timesheet_id),
    time_entry_id UUID NULL,
    project_id UUID NULL REFERENCES projects(project_id),
    task_id UUID NULL REFERENCES project_tasks(task_id),
    reason TEXT NOT NULL CHECK (length(trim(reason)) > 0),
    original_values JSONB NOT NULL DEFAULT '{}'::jsonb,
    revised_values JSONB NOT NULL DEFAULT '{}'::jsonb,
    event_metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS ix_scoped_time_management_events_target
ON scoped_time_management_events (target_user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_scoped_time_management_events_timesheet
ON scoped_time_management_events (timesheet_id, created_at DESC)
WHERE timesheet_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_projectpulse043_time_management_audit_immutable
ON scoped_time_management_events;
CREATE TRIGGER trg_projectpulse043_time_management_audit_immutable
BEFORE UPDATE OR DELETE ON scoped_time_management_events
FOR EACH ROW EXECUTE FUNCTION projectpulse040_block_immutable_audit_mutation();

ALTER TABLE module001_timesheet_entry_associations
    DROP CONSTRAINT IF EXISTS chk_module001_association_source;
ALTER TABLE module001_timesheet_entry_associations
    ADD CONSTRAINT chk_module001_association_source CHECK (
        association_source IN (
            'EXISTING_ENTRY','WORK_QUEUE','TIMER','CALENDAR','PTC_TIME_STEWARD'
        )
    );

DO $projectpulse043_publish_policy$
DECLARE
    v_current_policy UUID;
    v_new_policy UUID;
    v_next_version INTEGER;
BEGIN
    IF EXISTS (
        SELECT 1
        FROM scoped_role_policy_versions
        WHERE source_name = '043_ptc_time_steward_permissions'
    ) THEN
        RETURN;
    END IF;

    SELECT policy_version_id
    INTO v_current_policy
    FROM scoped_role_policy_versions
    WHERE policy_status = 'PUBLISHED'
    ORDER BY version_number DESC
    LIMIT 1
    FOR UPDATE;

    IF v_current_policy IS NULL THEN
        RAISE EXCEPTION 'Migration 043 requires one currently published scoped role policy.';
    END IF;

    SELECT COALESCE(MAX(version_number), 0) + 1
    INTO v_next_version
    FROM scoped_role_policy_versions;

    v_new_policy := gen_random_uuid();

    INSERT INTO scoped_role_policy_versions (
        policy_version_id,
        version_number,
        policy_name,
        policy_status,
        source_name,
        source_sha256,
        policy_notes,
        created_by_user_id
    )
    VALUES (
        v_new_policy,
        v_next_version,
        'ProjectPulse PTC Time Steward Policy',
        'DRAFT',
        '043_ptc_time_steward_permissions',
        'migration-043-ptc-time-steward',
        'Clones the previously published policy and adds audited Project Team Coordinator time stewardship without submission-on-behalf.',
        NULL
    );

    INSERT INTO scoped_role_policy_grants (
        policy_version_id, role_code, module_code, action_code, scope_code,
        grant_effect, conditions, delegated_authority, reason_required,
        audit_required, source_designation, source_notes, is_active
    )
    SELECT
        v_new_policy, role_code, module_code, action_code, scope_code,
        grant_effect, conditions, delegated_authority, reason_required,
        audit_required, source_designation, source_notes, is_active
    FROM scoped_role_policy_grants
    WHERE policy_version_id = v_current_policy;

    INSERT INTO scoped_role_policy_grants (
        policy_version_id, role_code, module_code, action_code, scope_code,
        grant_effect, conditions, delegated_authority, reason_required,
        audit_required, source_designation, source_notes, is_active
    )
    SELECT
        v_new_policy,
        'PROJECT_TEAM_COORDINATOR',
        '001',
        action_row.action_code,
        'ORGANIZATION',
        'GRANT',
        jsonb_build_object(
            'source', '043_ptc_time_steward_permissions',
            'designation', 'Manage',
            'permissionLevel', 'Manage',
            'operationalTimeSteward', TRUE,
            'submitOnBehalfAllowed', FALSE,
            'immutableAuditRequired', TRUE
        ),
        TRUE,
        action_row.reason_required,
        action_row.action_code <> 'MODULE_VIEW',
        'Manage',
        'PTC_TIME_STEWARD_043',
        TRUE
    FROM (
        VALUES
            ('MODULE_VIEW', FALSE),
            ('TIME_VIEW', FALSE),
            ('TIME_VIEW_ON_BEHALF', FALSE),
            ('TIME_UNSUBMIT', TRUE),
            ('TIME_REOPEN', TRUE),
            ('TIME_CORRECT_ON_BEHALF', TRUE),
            ('TIME_REASSIGN', TRUE),
            ('TIME_DELETE_ON_BEHALF', TRUE),
            ('TIME_TASK_CREATE', TRUE),
            ('TIME_TASK_ASSIGN', TRUE),
            ('AUDIT_VIEW', FALSE),
            ('AUDIT_RECORD', TRUE)
    ) AS action_row(action_code, reason_required)
    ON CONFLICT (
        policy_version_id, role_code, module_code,
        action_code, scope_code, grant_effect
    ) DO UPDATE
    SET conditions = EXCLUDED.conditions,
        delegated_authority = EXCLUDED.delegated_authority,
        reason_required = EXCLUDED.reason_required,
        audit_required = EXCLUDED.audit_required,
        source_designation = EXCLUDED.source_designation,
        source_notes = EXCLUDED.source_notes,
        is_active = TRUE;

    INSERT INTO scoped_role_policy_grants (
        policy_version_id, role_code, module_code, action_code, scope_code,
        grant_effect, conditions, delegated_authority, reason_required,
        audit_required, source_designation, source_notes, is_active
    )
    SELECT
        v_new_policy,
        'PROJECT_TEAM_COORDINATOR',
        '001',
        denial.action_code,
        'ORGANIZATION',
        'DENY',
        jsonb_build_object(
            'source', '043_ptc_time_steward_permissions',
            'operationalTimeStewardBoundary', TRUE,
            'explanation', denial.explanation
        ),
        FALSE,
        FALSE,
        TRUE,
        'Manage',
        'PTC_TIME_STEWARD_043',
        TRUE
    FROM (
        VALUES
            ('TIME_SUBMIT', 'The PTC manages time for others but never submits another user''s timesheet.'),
            ('TIME_DELETE_PERMANENT', 'Time may be removed only through governed deletion with immutable audit evidence.'),
            ('USER_IMPERSONATE', 'PTC time management uses explicit target-user operations and does not impersonate the user.'),
            ('SYSTEM_CONFIGURE', 'Operational time stewardship does not include platform configuration.')
    ) AS denial(action_code, explanation)
    ON CONFLICT (
        policy_version_id, role_code, module_code,
        action_code, scope_code, grant_effect
    ) DO UPDATE
    SET conditions = EXCLUDED.conditions,
        delegated_authority = FALSE,
        reason_required = FALSE,
        audit_required = TRUE,
        source_designation = EXCLUDED.source_designation,
        source_notes = EXCLUDED.source_notes,
        is_active = TRUE;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'RETIRED', retired_at = NOW()
    WHERE policy_version_id = v_current_policy;

    UPDATE scoped_role_policy_versions
    SET policy_status = 'PUBLISHED', published_at = NOW()
    WHERE policy_version_id = v_new_policy;
END;
$projectpulse043_publish_policy$;

DO $projectpulse043_runtime_grants$
DECLARE
    runtime_role TEXT;
BEGIN
    FOREACH runtime_role IN ARRAY ARRAY['ptp_app', 'projectpulse_app']
    LOOP
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = runtime_role) THEN
            EXECUTE format('GRANT SELECT, INSERT ON TABLE scoped_time_management_events TO %I', runtime_role);
        END IF;
    END LOOP;
END;
$projectpulse043_runtime_grants$;

COMMENT ON TABLE scoped_time_management_events IS
    'Immutable evidence for Project Team Coordinator and Super Administrator time-management actions performed for another user.';

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '043_ptc_time_steward_permissions',
    'Add audited Project Team Coordinator time stewardship without submission-on-behalf or permanent deletion',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
