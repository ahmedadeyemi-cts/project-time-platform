-- Explicit No Access is module-scoped and does not revoke platform login or
-- unrelated modules. Not Set rows are intentionally omitted.
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    cell.role_code,
    cell.module_code,
    'MODULE_ACCESS',
    cell.scope_code,
    'DENY',
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'designation', cell.designation,
        'moduleScopedOnly', TRUE
    ),
    FALSE,
    FALSE,
    TRUE,
    cell.designation,
    module_row.permission_notes,
    TRUE
FROM projectpulse040_workbook_cells cell
JOIN scoped_role_policy_modules module_row USING (module_code)
WHERE cell.designation = 'No Access'
ON CONFLICT DO NOTHING;

-- Standard designations expand into granular action rows.
WITH standard_cells AS (
    SELECT cell.*, module_row.permission_notes
    FROM projectpulse040_workbook_cells cell
    JOIN scoped_role_policy_modules module_row USING (module_code)
    WHERE cell.designation IN (
        'View','Create/Edit','Approve','Manage','Administer','Full Control'
    )
      AND cell.module_code NOT IN ('001','002','003','012','037')
),
expanded AS (
    SELECT
        standard_cells.*,
        unnest(
            CASE standard_cells.designation
                WHEN 'View' THEN ARRAY['MODULE_VIEW']
                WHEN 'Create/Edit' THEN ARRAY[
                    'MODULE_VIEW','RECORD_CREATE','RECORD_EDIT'
                ]
                WHEN 'Approve' THEN ARRAY[
                    'MODULE_VIEW','APPROVAL_VIEW',
                    'APPROVAL_APPROVE','APPROVAL_REJECT'
                ]
                WHEN 'Manage' THEN ARRAY[
                    'MODULE_VIEW','RECORD_CREATE','RECORD_EDIT',
                    'RECORD_ASSIGN','RECORD_REOPEN','WORKFLOW_MANAGE'
                ]
                WHEN 'Administer' THEN ARRAY[
                    'MODULE_VIEW','RECORD_CREATE','RECORD_EDIT',
                    'RECORD_ASSIGN','RECORD_REOPEN','WORKFLOW_MANAGE',
                    'MODULE_CONFIGURE','POLICY_DELEGATE','AUDIT_VIEW'
                ]
                ELSE ARRAY[
                    'MODULE_VIEW','RECORD_CREATE','RECORD_EDIT',
                    'RECORD_ASSIGN','RECORD_REOPEN','WORKFLOW_MANAGE',
                    'APPROVAL_VIEW','APPROVAL_APPROVE','APPROVAL_REJECT',
                    'MODULE_CONFIGURE','POLICY_DELEGATE','EXPORT_DATA',
                    'AUDIT_VIEW','AUDIT_RECORD','DELEGATED_ACTION'
                ]
            END
        ) AS action_code
    FROM standard_cells
)
INSERT INTO scoped_role_policy_grants (
    policy_version_id, role_code, module_code, action_code, scope_code,
    grant_effect, conditions, delegated_authority, reason_required,
    audit_required, source_designation, source_notes, is_active
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    expanded.role_code,
    expanded.module_code,
    expanded.action_code,
    expanded.scope_code,
    'GRANT',
    jsonb_build_object(
        'source', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'designation', expanded.designation,
        'nonBypassableSafetyControlsRemainSeparate', TRUE
    ),
    expanded.action_code IN ('POLICY_DELEGATE','DELEGATED_ACTION'),
    expanded.action_code IN ('RECORD_REOPEN','DELEGATED_ACTION'),
    expanded.action_code <> 'MODULE_VIEW',
    expanded.designation,
    expanded.permission_notes,
    TRUE
FROM expanded
ON CONFLICT DO NOTHING;
