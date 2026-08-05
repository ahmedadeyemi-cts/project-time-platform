-- Focused post-migration validation for 070_module_033_project_forge.
-- Run with ON_ERROR_STOP=1 after the complete ProjectPulse migration chain.
-- This script is read-only and creates no project, task, person, or sample data.

BEGIN TRANSACTION READ ONLY;

DO $projectpulse070_table_assertions$
DECLARE
    expected_table TEXT;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id = '070_module_033_project_forge'
    ) THEN
        RAISE EXCEPTION 'Migration 070 evidence is missing.';
    END IF;

    FOREACH expected_table IN ARRAY ARRAY[
        'project_forge_plans',
        'project_forge_plan_tasks',
        'project_forge_plan_assignments',
        'project_forge_task_dependencies',
        'project_forge_task_details',
        'project_forge_audit_events'
    ] LOOP
        IF to_regclass('public.' || expected_table) IS NULL THEN
            RAISE EXCEPTION 'Expected Project Forge table % is missing.', expected_table;
        END IF;
    END LOOP;

    IF EXISTS (
        SELECT 1
        FROM pg_class relation
        JOIN pg_namespace namespace ON namespace.oid = relation.relnamespace
        WHERE namespace.nspname = 'public'
          AND relation.relname LIKE 'project_forge%outbox%'
    ) THEN
        RAISE EXCEPTION 'Project Forge must use enterprise_notification_events and must not create a competing outbox.';
    END IF;
END;
$projectpulse070_table_assertions$;

DO $projectpulse070_column_assertions$
DECLARE
    missing_columns TEXT;
BEGIN
    WITH expected(table_name, column_name) AS (
        VALUES
            ('project_forge_plans', 'plan_id'),
            ('project_forge_plans', 'project_id'),
            ('project_forge_plans', 'plan_status'),
            ('project_forge_plans', 'ai_evidence'),
            ('project_forge_plans', 'revision_number'),
            ('project_forge_plan_tasks', 'plan_task_id'),
            ('project_forge_plan_tasks', 'plan_id'),
            ('project_forge_plan_tasks', 'canonical_task_id'),
            ('project_forge_plan_tasks', 'estimated_hours'),
            ('project_forge_plan_tasks', 'recurrence_rule'),
            ('project_forge_plan_assignments', 'plan_assignment_id'),
            ('project_forge_plan_assignments', 'assignment_type'),
            ('project_forge_plan_assignments', 'review_status'),
            ('project_forge_task_dependencies', 'predecessor_plan_task_id'),
            ('project_forge_task_dependencies', 'successor_plan_task_id'),
            ('project_forge_task_details', 'task_id'),
            ('project_forge_task_details', 'source_plan_task_id'),
            ('project_forge_task_details', 'revision_number'),
            ('project_forge_audit_events', 'prior_state'),
            ('project_forge_audit_events', 'new_state'),
            ('project_forge_audit_events', 'actual_actor_user_id'),
            ('project_forge_audit_events', 'effective_actor_user_id')
    ), missing AS (
        SELECT expected.table_name || '.' || expected.column_name AS qualified_name
        FROM expected
        LEFT JOIN information_schema.columns column_definition
          ON column_definition.table_schema = 'public'
         AND column_definition.table_name = expected.table_name
         AND column_definition.column_name = expected.column_name
        WHERE column_definition.column_name IS NULL
    )
    SELECT string_agg(qualified_name, ', ' ORDER BY qualified_name)
    INTO missing_columns
    FROM missing;

    IF missing_columns IS NOT NULL THEN
        RAISE EXCEPTION 'Project Forge columns missing: %', missing_columns;
    END IF;

    IF (
        SELECT is_nullable
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'project_forge_plan_tasks'
          AND column_name = 'canonical_task_id'
    ) <> 'YES' THEN
        RAISE EXCEPTION 'Project Forge AI drafts must not require canonical_task_id before review and adoption.';
    END IF;
END;
$projectpulse070_column_assertions$;

DO $projectpulse070_trigger_assertions$
DECLARE
    missing_trigger_count INTEGER;
BEGIN
    WITH expected(trigger_name) AS (
        VALUES
            ('trg_project_forge_plans_revision'),
            ('trg_project_forge_plan_tasks_validate'),
            ('trg_project_forge_plan_tasks_revision'),
            ('trg_project_forge_plan_assignments_validate'),
            ('trg_project_forge_dependencies_validate'),
            ('trg_project_forge_task_details_validate'),
            ('trg_project_forge_plans_audit'),
            ('trg_project_forge_plan_tasks_audit'),
            ('trg_project_forge_plan_assignments_audit'),
            ('trg_project_forge_dependencies_audit'),
            ('trg_project_forge_task_details_audit'),
            ('trg_project_forge_audit_events_immutable')
    )
    SELECT COUNT(*)
    INTO missing_trigger_count
    FROM expected
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_trigger trigger_definition
        WHERE trigger_definition.tgname = expected.trigger_name
          AND trigger_definition.tgenabled <> 'D'
    );

    IF missing_trigger_count <> 0 THEN
        RAISE EXCEPTION 'Project Forge has % missing or disabled integrity/audit triggers.', missing_trigger_count;
    END IF;
END;
$projectpulse070_trigger_assertions$;

DO $projectpulse070_permission_assertions$
DECLARE
    missing_permission_count INTEGER;
    missing_grant_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_permission_count
    FROM unnest(ARRAY[
        'VIEW_PROJECT_FORGE_033',
        'MANAGE_PROJECT_FORGE_033',
        'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033',
        'USE_PROJECT_FORGE_AI_033'
    ]) expected(permission_code)
    WHERE NOT EXISTS (
        SELECT 1 FROM app_permissions permission
        WHERE permission.permission_code = expected.permission_code
          AND permission.module_code = '033'
    );

    IF missing_permission_count <> 0 THEN
        RAISE EXCEPTION 'Project Forge has % missing Module 033 permissions.', missing_permission_count;
    END IF;

    WITH desired(role_code, permission_code) AS (
        VALUES
            ('SUPER_ADMINISTRATOR', 'VIEW_PROJECT_FORGE_033'),
            ('SUPER_ADMINISTRATOR', 'MANAGE_PROJECT_FORGE_033'),
            ('SUPER_ADMINISTRATOR', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('SUPER_ADMINISTRATOR', 'USE_PROJECT_FORGE_AI_033'),
            ('ADMINISTRATOR', 'VIEW_PROJECT_FORGE_033'),
            ('ADMINISTRATOR', 'MANAGE_PROJECT_FORGE_033'),
            ('ADMINISTRATOR', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('ADMINISTRATOR', 'USE_PROJECT_FORGE_AI_033'),
            ('PROJECT_MANAGER', 'VIEW_PROJECT_FORGE_033'),
            ('PROJECT_MANAGER', 'MANAGE_PROJECT_FORGE_033'),
            ('PROJECT_MANAGER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('PROJECT_MANAGER', 'USE_PROJECT_FORGE_AI_033'),
            ('PROJECT_MANAGEMENT', 'VIEW_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT', 'MANAGE_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('PROJECT_MANAGEMENT', 'USE_PROJECT_FORGE_AI_033'),
            ('PROJECT_MANAGEMENT_LEAD', 'VIEW_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT_LEAD', 'MANAGE_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('PROJECT_MANAGEMENT_LEAD', 'USE_PROJECT_FORGE_AI_033'),
            ('PROJECT_MANAGEMENT_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT_TEAM_LEAD', 'MANAGE_PROJECT_FORGE_033'),
            ('PROJECT_MANAGEMENT_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('PROJECT_MANAGEMENT_TEAM_LEAD', 'USE_PROJECT_FORGE_AI_033'),
            ('PM_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
            ('PM_TEAM_LEAD', 'MANAGE_PROJECT_FORGE_033'),
            ('PM_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('PM_TEAM_LEAD', 'USE_PROJECT_FORGE_AI_033'),
            ('ENGINEERING', 'VIEW_PROJECT_FORGE_033'),
            ('ENGINEERING', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('ENGINEER', 'VIEW_PROJECT_FORGE_033'),
            ('ENGINEER', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('ENGINEERING_LEAD', 'VIEW_PROJECT_FORGE_033'),
            ('ENGINEERING_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033'),
            ('ENGINEERING_TEAM_LEAD', 'VIEW_PROJECT_FORGE_033'),
            ('ENGINEERING_TEAM_LEAD', 'EDIT_ASSIGNED_PROJECT_FORGE_ESTIMATES_033')
    ), installed_roles AS (
        SELECT role.app_role_id, upper(role.role_code) AS role_code
        FROM app_roles role
        WHERE role.is_active = TRUE
    )
    SELECT COUNT(*)
    INTO missing_grant_count
    FROM desired
    JOIN installed_roles role ON role.role_code = desired.role_code
    JOIN app_permissions permission ON permission.permission_code = desired.permission_code
    WHERE NOT EXISTS (
        SELECT 1 FROM app_role_permissions relationship
        WHERE relationship.app_role_id = role.app_role_id
          AND relationship.app_permission_id = permission.app_permission_id
    );

    IF missing_grant_count <> 0 THEN
        RAISE EXCEPTION 'Project Forge has % missing grants for installed scoped roles.', missing_grant_count;
    END IF;
END;
$projectpulse070_permission_assertions$;

DO $projectpulse070_integration_assertions$
DECLARE
    missing_policy_count INTEGER;
BEGIN
    SELECT COUNT(*)
    INTO missing_policy_count
    FROM unnest(ARRAY[
        'PROJECT_FORGE_REVIEW_ASSIGNED',
        'PROJECT_FORGE_TASK_ASSIGNED',
        'PROJECT_FORGE_TASK_UPDATED',
        'PROJECT_FORGE_PLAN_UPDATED'
    ]) expected(policy_code)
    WHERE NOT EXISTS (
        SELECT 1
        FROM enterprise_notification_policies policy
        WHERE policy.policy_code = expected.policy_code
          AND policy.source_module = '033'
          AND policy.recipient_strategy = 'project_team'
          AND policy.producer_contract = 'module_033_native_event'
    );

    IF missing_policy_count <> 0 THEN
        RAISE EXCEPTION 'Project Forge has % missing or invalid Module 065 notification policies.', missing_policy_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM ai_capability_routes route
        WHERE route.feature_code = 'project_forge_plan_estimate'
          AND route.external_context_policy = 'sanitized_generic_only'
          AND route.route_targets = '["celar_ai","claude","openai","local_template"]'::JSONB
    ) THEN
        RAISE EXCEPTION 'Project Forge Module 064 AI capability route is missing or unsafe.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM app_feature_catalog feature
        WHERE feature.feature_code = 'PROJECT_FORGE'
          AND feature.module_code = '033'
          AND feature.required_permission_code = 'VIEW_PROJECT_FORGE_033'
          AND feature.is_active = TRUE
    ) THEN
        RAISE EXCEPTION 'Project Forge feature catalog registration is missing.';
    END IF;
END;
$projectpulse070_integration_assertions$;

ROLLBACK;

SELECT 'MODULE_033_PROJECT_FORGE_MIGRATION=PASS' AS validation_result;
