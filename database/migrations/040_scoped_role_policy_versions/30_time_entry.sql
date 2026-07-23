-- Module 001: scoped time actions and immutable correction workflow.
WITH module001 AS (
    SELECT cell.*, module_row.permission_notes
    FROM projectpulse040_workbook_cells cell
    JOIN scoped_role_policy_modules module_row USING (module_code)
    WHERE cell.module_code = '001'
      AND cell.designation NOT IN ('No Access','Not Set')
),
expanded AS (
    SELECT module001.*,
           unnest(
               CASE
                   WHEN designation = 'Custom' THEN ARRAY[
                       'MODULE_VIEW','TIME_VIEW','TIME_REASSIGN',
                       'TIME_CORRECT_ON_BEHALF','TIME_REOPEN',
                       'TIME_APPROVE','TIME_REJECT'
                   ]
                   WHEN designation = 'View' THEN ARRAY['MODULE_VIEW','TIME_VIEW']
                   WHEN designation = 'Create/Edit' THEN ARRAY[
                       'MODULE_VIEW','TIME_VIEW','TIME_EDIT_OWN','TIME_SUBMIT'
                   ]
                   WHEN designation = 'Approve' THEN ARRAY[
                       'MODULE_VIEW','TIME_VIEW','TIME_APPROVE','TIME_REJECT'
                   ]
                   ELSE ARRAY[
                       'MODULE_VIEW','TIME_VIEW','TIME_EDIT_OWN','TIME_SUBMIT',
                       'TIME_REOPEN','TIME_REASSIGN','TIME_CORRECT_ON_BEHALF',
                       'TIME_APPROVE','TIME_REJECT'
                   ]
               END
           ) AS action_code
    FROM module001
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
        'approvedTimeWorkflow', 'REOPEN_REASON_CORRECT_MOVE_REAPPROVE',
        'preserveOriginalAndRevisedValues', TRUE,
        'permanentDeletionForbidden', TRUE
    ),
    FALSE,
    action_code IN (
        'TIME_REASSIGN','TIME_CORRECT_ON_BEHALF',
        'TIME_REOPEN','TIME_APPROVE','TIME_REJECT'
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
    '001',
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
  ON module_row.module_code = '001'
CROSS JOIN (
    VALUES
        ('TIME_DELETE_PERMANENT'),
        ('USER_IMPERSONATE'),
        ('SYSTEM_CONFIGURE'),
        ('AUDIT_BYPASS')
) AS denied_action(action_code)
WHERE cell.module_code = '001'
  AND cell.designation NOT IN ('No Access','Not Set')
ON CONFLICT DO NOTHING;
