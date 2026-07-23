-- Module 003 is always read-only; scope differs by role.
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    cell.role_code,
    '003',
    action_row.action_code,
    cell.scope_code,
    action_row.grant_effect,
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'designation', cell.designation,
        'readOnly', TRUE,
        'utilizationMutationForbidden', TRUE
    ),
    FALSE,
    FALSE,
    TRUE,
    cell.designation,
    module_row.permission_notes,
    TRUE
FROM projectpulse040_workbook_cells cell
JOIN scoped_role_policy_modules module_row
  ON module_row.module_code = '003'
CROSS JOIN (
    VALUES
        ('MODULE_VIEW','GRANT'),
        ('UTILIZATION_VIEW','GRANT'),
        ('UTILIZATION_EDIT','DENY')
) AS action_row(action_code, grant_effect)
WHERE cell.module_code = '003'
  AND cell.designation NOT IN ('No Access','Not Set')
ON CONFLICT DO NOTHING;
