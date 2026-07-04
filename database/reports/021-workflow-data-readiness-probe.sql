-- 021E Workflow Data Readiness Probe
-- Run against the target ProjectPulse database during production readiness validation.
-- This probe does not modify data.

CREATE TEMP TABLE IF NOT EXISTS workflow_data_readiness_probe (
    area text,
    table_name text,
    table_exists boolean,
    row_count bigint
);

TRUNCATE workflow_data_readiness_probe;

DO $$
DECLARE
    table_area text;
    table_item text;
    row_total bigint;
BEGIN
    FOR table_area, table_item IN
        VALUES
        ('Customer Directory', 'public.customers'),
        ('Customer Directory', 'public.customer_contacts'),
        ('Customer Directory', 'public.customer_locations'),
        ('Project Intake', 'public.project_intake_requests'),
        ('Project Intake', 'public.project_intakes'),
        ('Project Intake', 'public.project_intake_supporting_documents'),
        ('Project Intake', 'public.project_intake_work_tasks'),
        ('Resource Assignment', 'public.resource_assignments'),
        ('Resource Assignment', 'public.project_resource_assignments'),
        ('Resource Assignment', 'public.project_allocations'),
        ('Resource Assignment', 'public.app_users'),
        ('Approval Workflow', 'public.manager_approval_requests'),
        ('Approval Workflow', 'public.time_approval_requests'),
        ('Approval Workflow', 'public.time_entries'),
        ('Approval Workflow', 'public.time_workflow_locks'),
        ('Export Package', 'public.time_workflow_exports'),
        ('Export Package', 'public.time_export_packages'),
        ('Export Package', 'public.time_export_package_items'),
        ('Export Package', 'public.time_entries'),
        ('Audit Evidence', 'public.audit_logs'),
        ('Audit Evidence', 'public.audit_events'),
        ('Audit Evidence', 'public.system_email_provider_test_events'),
        ('Production Readiness Command Center', 'public.dashboard_module_visibility_expectations'),
        ('Production Readiness Command Center', 'public.app_users'),
        ('Production Readiness Command Center', 'public.projects'),
        ('Production Readiness Command Center', 'public.time_entries'),
        ('Production Readiness Command Center', 'public.audit_logs')
    LOOP
        IF to_regclass(table_item) IS NULL THEN
            INSERT INTO workflow_data_readiness_probe(area, table_name, table_exists, row_count)
            VALUES (table_area, table_item, false, NULL);
        ELSE
            EXECUTE format('SELECT COUNT(*)::bigint FROM %s', table_item) INTO row_total;
            INSERT INTO workflow_data_readiness_probe(area, table_name, table_exists, row_count)
            VALUES (table_area, table_item, true, row_total);
        END IF;
    END LOOP;
END $$;

SELECT *
FROM workflow_data_readiness_probe
ORDER BY area, table_name;