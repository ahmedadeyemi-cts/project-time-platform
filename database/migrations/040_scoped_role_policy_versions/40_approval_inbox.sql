-- Module 002: separate Manager, Project Manager, and PTC-final stages.
WITH module002 AS (
    SELECT cell.*, module_row.permission_notes
    FROM projectpulse040_workbook_cells cell
    JOIN scoped_role_policy_modules module_row USING (module_code)
    WHERE cell.module_code = '002'
      AND cell.designation NOT IN ('No Access','Not Set')
),
expanded AS (
    SELECT module002.*,
           unnest(
               CASE
                   WHEN designation = 'Custom' THEN ARRAY[
                       'MODULE_VIEW','APPROVAL_VIEW',
                       'APPROVAL_VIEW_MANAGER','APPROVAL_VIEW_PROJECT_MANAGER',
                       'APPROVAL_VIEW_PTC_FINAL',
                       'APPROVAL_APPROVE_PTC_FINAL',
                       'APPROVAL_REJECT_PTC_FINAL',
                       'APPROVAL_DELEGATE_MANAGER',
                       'APPROVAL_DELEGATE_PROJECT_MANAGER',
                       'APPROVAL_RETURN_FOR_CORRECTION'
                   ]
                   WHEN role_code IN ('MANAGER','ENGINEERING_LEAD') THEN ARRAY[
                       'MODULE_VIEW','APPROVAL_VIEW',
                       'APPROVAL_VIEW_MANAGER',
                       'APPROVAL_APPROVE_MANAGER',
                       'APPROVAL_REJECT_MANAGER'
                   ]
                   WHEN role_code IN (
                       'PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD'
                   ) THEN ARRAY[
                       'MODULE_VIEW','APPROVAL_VIEW',
                       'APPROVAL_VIEW_PROJECT_MANAGER',
                       'APPROVAL_APPROVE_PROJECT_MANAGER',
                       'APPROVAL_REJECT_PROJECT_MANAGER'
                   ]
                   ELSE ARRAY[
                       'MODULE_VIEW','APPROVAL_VIEW',
                       'APPROVAL_APPROVE','APPROVAL_REJECT'
                   ]
               END
           ) AS action_code
    FROM module002
)
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    role_code,
    module_code,
    action_code,
    scope_code,
    'GRANT',
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'designation', designation,
        'separateApprovalStages',
            jsonb_build_array('MANAGER','PROJECT_MANAGER','PTC_FINAL'),
        'passwordResetApprovalExcludedForProjectRoles', TRUE,
        'delegatedApprovalAuditRequired', TRUE
    ),
    action_code IN (
        'APPROVAL_DELEGATE_MANAGER',
        'APPROVAL_DELEGATE_PROJECT_MANAGER'
    ),
    action_code IN (
        'APPROVAL_REJECT_MANAGER',
        'APPROVAL_REJECT_PROJECT_MANAGER',
        'APPROVAL_APPROVE_PTC_FINAL',
        'APPROVAL_REJECT_PTC_FINAL',
        'APPROVAL_DELEGATE_MANAGER',
        'APPROVAL_DELEGATE_PROJECT_MANAGER',
        'APPROVAL_RETURN_FOR_CORRECTION'
    ),
    action_code <> 'MODULE_VIEW',
    designation,
    permission_notes,
    TRUE
FROM expanded
ON CONFLICT DO NOTHING;

INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    cell.role_code,
    '002',
    denied_action.action_code,
    cell.scope_code,
    'DENY',
    jsonb_build_object('nonBypassable', TRUE),
    FALSE,
    FALSE,
    TRUE,
    cell.designation,
    module_row.permission_notes,
    TRUE
FROM projectpulse040_workbook_cells cell
JOIN scoped_role_policy_modules module_row
  ON module_row.module_code = '002'
CROSS JOIN (
    VALUES
        ('APPROVAL_DELETE_PERMANENT'),
        ('APPROVAL_HISTORY_EDIT'),
        ('APPROVAL_SYSTEM_CONFIGURE')
) AS denied_action(action_code)
WHERE cell.module_code = '002'
  AND cell.designation NOT IN ('No Access','Not Set')
ON CONFLICT DO NOTHING;

-- Project roles may approve project time but do not inherit password-reset approval.
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    role_code,
    '002',
    'PASSWORD_RESET_APPROVE',
    scope_code,
    'DENY',
    jsonb_build_object('projectApprovalDoesNotGrantPasswordReset', TRUE),
    FALSE,
    FALSE,
    TRUE,
    designation,
    permission_notes,
    TRUE
FROM (
    SELECT cell.*, module_row.permission_notes
    FROM projectpulse040_workbook_cells cell
    JOIN scoped_role_policy_modules module_row USING (module_code)
    WHERE cell.module_code = '002'
      AND cell.role_code IN (
          'PROJECT_MANAGEMENT','PROJECT_MANAGEMENT_LEAD'
      )
) project_roles
ON CONFLICT DO NOTHING;
