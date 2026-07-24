-- Module 037 is strictly read-only for every role that has matrix access.
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    cell.role_code,
    '037',
    action_row.action_code,
    'ORGANIZATION',
    'GRANT',
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'readOnly', TRUE,
        'writeEndpointForbidden', TRUE
    ),
    FALSE,
    FALSE,
    FALSE,
    cell.designation,
    module_row.permission_notes,
    TRUE
FROM projectpulse040_workbook_cells cell
JOIN scoped_role_policy_modules module_row
  ON module_row.module_code = '037'
CROSS JOIN (
    VALUES
        ('MODULE_VIEW'),
        ('MATRIX_VIEW'),
        ('MATRIX_EXPORT'),
        ('ACCESS_EXPLAIN')
) AS action_row(action_code)
WHERE cell.module_code = '037'
  AND cell.designation NOT IN ('No Access','Not Set')
ON CONFLICT DO NOTHING;
