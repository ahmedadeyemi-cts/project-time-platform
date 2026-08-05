-- Focused post-migration validation for 073_module_033_project_forge_interactive.
-- Run with ON_ERROR_STOP=1 after migrations 070, 071, 072, and 073. This script is read-only.

BEGIN TRANSACTION READ ONLY;

DO $projectpulse073_contract_test$
DECLARE
    duration_check TEXT;
    lag_check TEXT;
    missing_trigger_count INTEGER;
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM schema_migrations
        WHERE migration_id='073_module_033_project_forge_interactive'
    ) THEN
        RAISE EXCEPTION 'Migration 073 evidence is missing.';
    END IF;

    IF to_regclass('public.project_task_dependencies') IS NULL THEN
        RAISE EXCEPTION 'Missing canonical Project Forge dependency table.';
    END IF;

    IF NOT EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='project_tasks' AND column_name='revision_number'
    ) OR NOT EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='project_assignments' AND column_name='is_primary_assignee'
    ) OR NOT EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='project_forge_plan_assignments' AND column_name='reviewed_task_revision'
    ) OR NOT EXISTS(
        SELECT 1 FROM information_schema.columns
        WHERE table_schema='public' AND table_name='project_forge_task_details' AND column_name='duration_working_days'
    ) THEN
        RAISE EXCEPTION 'Missing Project Forge concurrency, assignment, review, or schedule columns.';
    END IF;

    IF to_regprocedure('public.projectpulse073_add_working_days(date,integer)') IS NULL
       OR to_regprocedure('public.projectpulse073_working_day_delta(date,date)') IS NULL
       OR to_regprocedure('public.projectpulse073_working_day_duration(date,date)') IS NULL THEN
        RAISE EXCEPTION 'Missing holiday-aware Project Forge scheduling functions.';
    END IF;

    SELECT pg_get_constraintdef(oid) INTO duration_check
    FROM pg_constraint
    WHERE conrelid='project_forge_plan_tasks'::regclass
      AND conname='project_forge_plan_tasks_duration_working_days_check';
    SELECT pg_get_constraintdef(oid) INTO lag_check
    FROM pg_constraint
    WHERE conrelid='project_forge_task_dependencies'::regclass
      AND conname='project_forge_task_dependencies_lag_working_days_check';
    IF duration_check NOT LIKE '%730%' OR lag_check NOT LIKE '%365%' THEN
        RAISE EXCEPTION 'Project Forge duration/lag constraints do not match the interactive contract.';
    END IF;

    WITH expected(trigger_name) AS (
        VALUES
            ('trg_project_tasks_revision_073'),
            ('trg_project_assignments_revision_073'),
            ('trg_project_task_dependencies_validate_073'),
            ('trg_project_task_dependencies_revision_073'),
            ('trg_project_task_dependencies_audit_073'),
            ('trg_project_forge_task_details_parent_073')
    )
    SELECT COUNT(*) INTO missing_trigger_count
    FROM expected
    WHERE NOT EXISTS(
        SELECT 1 FROM pg_trigger trigger_definition
        WHERE trigger_definition.tgname=expected.trigger_name
          AND trigger_definition.tgenabled<>'D'
    );
    IF missing_trigger_count<>0 THEN
        RAISE EXCEPTION 'Project Forge interactive schema has % missing or disabled triggers.', missing_trigger_count;
    END IF;

    IF NOT EXISTS(
        SELECT 1 FROM app_permissions
        WHERE permission_code='UPDATE_ASSIGNED_PROJECT_FORGE_TASK_STATUS_033'
          AND module_code='033'
    ) THEN
        RAISE EXCEPTION 'Missing assigned-Engineer workflow permission.';
    END IF;

    IF EXISTS(
        SELECT 1
        FROM pg_class relation
        JOIN pg_namespace namespace ON namespace.oid=relation.relnamespace
        WHERE namespace.nspname='public'
          AND relation.relname LIKE 'project_forge%outbox%'
    ) THEN
        RAISE EXCEPTION 'Project Forge must use Module 065 and must not create a competing notification outbox.';
    END IF;
END;
$projectpulse073_contract_test$;

ROLLBACK;

SELECT 'MODULE_033_PROJECT_FORGE_INTERACTIVE_MIGRATION_073=PASS' AS validation_result;
