-- Module 012 remains readable for existing review roles, but policy writes are
-- reserved to a Super Administrator in their own non-View-As session.
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    cell.role_code,
    '012',
    action_row.action_code,
    'ORGANIZATION',
    'GRANT',
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'ownSessionRequired',
            cell.role_code = 'SUPER_ADMINISTRATOR',
        'viewAsWriteForbidden', TRUE,
        'finalSuperAdministratorProtection', TRUE,
        'nonBypassableSafetyControlsRemainSeparate', TRUE
    ),
    FALSE,
    action_row.action_code IN ('POLICY_PUBLISH','POLICY_RESTORE'),
    action_row.action_code <> 'MODULE_VIEW',
    cell.designation,
    module_row.permission_notes,
    TRUE
FROM projectpulse040_workbook_cells cell
JOIN scoped_role_policy_modules module_row
  ON module_row.module_code = '012'
CROSS JOIN LATERAL (
    SELECT unnest(
        CASE
            WHEN cell.role_code = 'SUPER_ADMINISTRATOR' THEN ARRAY[
                'MODULE_VIEW','POLICY_VIEW','POLICY_VALIDATE',
                'POLICY_PUBLISH','POLICY_RESTORE',
                'POLICY_AUDIT_VIEW','ACCESS_EXPLAIN'
            ]
            ELSE ARRAY['MODULE_VIEW','POLICY_VIEW']
        END
    ) AS action_code
) action_row
WHERE cell.module_code = '012'
  AND cell.designation NOT IN ('No Access','Not Set')
ON CONFLICT DO NOTHING;

INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    'SUPER_ADMINISTRATOR',
    '012',
    'NON_BYPASSABLE_SAFETY_BYPASS',
    'ORGANIZATION',
    'DENY',
    jsonb_build_object('nonBypassable', TRUE),
    FALSE,
    FALSE,
    TRUE,
    'Full Control',
    module_row.permission_notes,
    TRUE
FROM scoped_role_policy_modules module_row
WHERE module_row.module_code = '012'
ON CONFLICT DO NOTHING;
