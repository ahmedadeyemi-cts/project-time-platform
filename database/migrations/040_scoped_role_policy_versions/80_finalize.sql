INSERT INTO scoped_role_policy_audit_events (
    policy_version_id, event_code, actor_user_id, actor_email, reason,
    previous_state, new_state, event_metadata
)
SELECT
    '04000000-0000-0000-0000-000000000001'::uuid,
    'POLICY_BASELINE_PUBLISHED',
    (
        SELECT published_by_user_id
        FROM scoped_role_policy_versions
        WHERE policy_version_id = '04000000-0000-0000-0000-000000000001'::uuid
    ),
    COALESCE((
        SELECT u.email
        FROM scoped_role_policy_versions v
        JOIN app_users u ON u.user_id = v.published_by_user_id
        WHERE v.policy_version_id = '04000000-0000-0000-0000-000000000001'::uuid
    ), 'system'),
    'Initial workbook-approved scoped RBAC baseline.',
    '{}'::jsonb,
    jsonb_build_object(
        'versionNumber', 1,
        'grantCount', (
            SELECT COUNT(*)
            FROM scoped_role_policy_grants
            WHERE policy_version_id = '04000000-0000-0000-0000-000000000001'::uuid
        )
    ),
    jsonb_build_object(
        'sourceName', 'ProjectPulse_Module_Role_Permissions_Matrix(2).xlsx',
        'sourceSha256', 'a9d8d1549ad36634d0a84510326e2127e644c3d14a4be2877fb659ef4a56c02c',
        'notSetCellsPreserveLegacy', TRUE,
        'nonBypassableControlsRemainSeparate', TRUE
    )
WHERE NOT EXISTS (
    SELECT 1
    FROM scoped_role_policy_audit_events
    WHERE policy_version_id = '04000000-0000-0000-0000-000000000001'::uuid
      AND event_code = 'POLICY_BASELINE_PUBLISHED'
);

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '040_scoped_role_policy_versions',
    'Add versioned scoped RBAC grants for Modules 012 and 037 with workbook baseline',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
