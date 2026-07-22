-- ProjectPulse Modules 055C and 055D
-- Separates Work Register editing from creation, establishes exact role grants,
-- and guarantees durable audit storage. Source-only until separately approved.

BEGIN;

ALTER TABLE work_register_intake_packages
    ADD COLUMN IF NOT EXISTS source_mode VARCHAR(60);

UPDATE work_register_intake_packages
SET source_mode = 'gsd_sow_upload'
WHERE source_mode IS NULL
   OR btrim(source_mode) = '';

ALTER TABLE work_register_intake_packages
    ALTER COLUMN source_mode SET DEFAULT 'gsd_sow_upload',
    ALTER COLUMN source_mode SET NOT NULL;

CREATE TABLE IF NOT EXISTS work_register_change_history (
    work_register_change_history_id UUID PRIMARY KEY,
    source_table VARCHAR(120) NOT NULL,
    work_id UUID NOT NULL,
    action VARCHAR(120) NOT NULL,
    change_summary TEXT NOT NULL DEFAULT '',
    changed_fields_csv TEXT NOT NULL DEFAULT '',
    changed_by_user_id UUID NULL REFERENCES app_users(user_id) ON DELETE SET NULL,
    old_value_json JSONB NULL,
    new_value_json JSONB NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE work_register_change_history
    ADD COLUMN IF NOT EXISTS changed_by_user_id UUID NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint constraint_record
        WHERE constraint_record.conrelid = 'work_register_change_history'::regclass
          AND constraint_record.confrelid = 'app_users'::regclass
          AND constraint_record.contype = 'f'
          AND constraint_record.conkey = ARRAY[
              (
                  SELECT attribute_record.attnum::smallint
                  FROM pg_attribute attribute_record
                  WHERE attribute_record.attrelid = 'work_register_change_history'::regclass
                    AND attribute_record.attname = 'changed_by_user_id'
                    AND NOT attribute_record.attisdropped
              )
          ]
    ) THEN
        ALTER TABLE work_register_change_history
            ADD CONSTRAINT fk_work_register_change_history_changed_by_user
            FOREIGN KEY (changed_by_user_id)
            REFERENCES app_users(user_id)
            ON DELETE SET NULL;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_work_register_change_history_work
    ON work_register_change_history(source_table, work_id);

CREATE INDEX IF NOT EXISTS idx_work_register_change_history_changed_at
    ON work_register_change_history(changed_at DESC);

INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
VALUES
    (
        'EDIT_WORK_REGISTER_055C',
        'Manage Existing Projects',
        '055C',
        'Manage existing projects, tasks, assignments, documents, purchase orders, lifecycle fields, closeout entry points, and billing requests with audit history.'
    ),
    (
        'CREATE_WORK_REGISTER_055D',
        'Create New Project',
        '055D',
        'Create new projects from controlled GSD or SELL intake with durable Work Register audit history.'
    )
ON CONFLICT (permission_code) DO UPDATE
SET permission_name = EXCLUDED.permission_name,
    module_code = EXCLUDED.module_code,
    permission_description = EXCLUDED.permission_description;

DELETE FROM app_role_permissions
WHERE app_permission_id IN (
    SELECT app_permission_id
    FROM app_permissions
    WHERE permission_code IN ('EDIT_WORK_REGISTER_055C', 'CREATE_WORK_REGISTER_055D')
);

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'EDIT_WORK_REGISTER_055C'
WHERE upper(role.role_code) IN (
    'PROJECT_TEAM_COORDINATOR',
    'PROJECT_MANAGER',
    'PROJECT_MANAGEMENT',
    'PROJECT_MANAGEMENT_LEAD',
    'PROJECT_MANAGEMENT_TEAM_LEAD',
    'PM_TEAM_LEAD'
)
ON CONFLICT DO NOTHING;

INSERT INTO app_role_permissions (app_role_id, app_permission_id)
SELECT role.app_role_id, permission.app_permission_id
FROM app_roles role
JOIN app_permissions permission ON permission.permission_code = 'CREATE_WORK_REGISTER_055D'
WHERE upper(role.role_code) = 'PROJECT_TEAM_COORDINATOR'
ON CONFLICT DO NOTHING;

INSERT INTO app_feature_catalog (
    feature_code, feature_name, module_code, route_anchor,
    required_permission_code, feature_description, display_order
)
VALUES
    (
        'EDIT_WORK_REGISTER_055C',
        'Manage Existing Projects',
        '055C',
        '#work-register',
        'EDIT_WORK_REGISTER_055C',
        'Search and manage existing projects. All saved mutations are recorded in the Audit tab.',
        553
    ),
    (
        'CREATE_WORK_REGISTER_055D',
        'Create New Project',
        '055D',
        '#create-work-register',
        'CREATE_WORK_REGISTER_055D',
        'Create a new project from GSD or SELL. SELL remains authoritative for project name and Actual Rate / Pricing / Rate Review.',
        554
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

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '035_work_register_055c_055d_split',
    'Split Module 055C editing from PTC-only Module 055D creation with GSD/SELL intake and audit controls',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
