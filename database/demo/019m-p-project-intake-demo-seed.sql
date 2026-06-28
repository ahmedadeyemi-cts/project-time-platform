-- 019M-P demo data
-- Creates realistic intake, project, task, capacity, qualifications, and engineering resource request records.

DO $$
DECLARE
    v_internal_client_id uuid;
    v_demo_client_id uuid;

    v_steve_id uuid;
    v_header_id uuid;
    v_kari_id uuid;
    v_jason_id uuid;
    v_jeremy_id uuid;
    v_kevin_id uuid;
    v_ahmed_manager_id uuid;
    v_matthew_manager_id uuid;

    v_intake_1_id uuid;
    v_intake_2_id uuid;
    v_project_1_id uuid;
    v_project_2_id uuid;
    v_task_design_id uuid;
    v_task_impl_id uuid;
    v_task_cutover_id uuid;
BEGIN
    SELECT user_id INTO v_steve_id FROM app_users WHERE email = 'steve.kopischke@ussignal.local';
    SELECT user_id INTO v_header_id FROM app_users WHERE email = 'header.schrock@ussignal.local';
    SELECT user_id INTO v_kari_id FROM app_users WHERE email = 'kari.wilkening@ussignal.local';
    SELECT user_id INTO v_jason_id FROM app_users WHERE email = 'jason.mosier@ussignal.local';
    SELECT user_id INTO v_jeremy_id FROM app_users WHERE email = 'jeremy.holt@ussignal.local';
    SELECT user_id INTO v_kevin_id FROM app_users WHERE email = 'kevin.damish@ussignal.local';
    SELECT user_id INTO v_ahmed_manager_id FROM app_users WHERE email = 'ahmed.adeyemi01@ussignal.local';
    SELECT user_id INTO v_matthew_manager_id FROM app_users WHERE email = 'matthew.lenoble01@ussignal.local';

    INSERT INTO clients (client_name, client_code, is_active)
    VALUES
      ('US Signal Internal', 'USS', TRUE),
      ('Great Lakes Healthcare', 'GLH', TRUE),
      ('Summit Manufacturing', 'SMFG', TRUE)
    ON CONFLICT (client_name) DO UPDATE
    SET client_code = EXCLUDED.client_code,
        is_active = TRUE,
        updated_at = NOW();

    SELECT client_id INTO v_internal_client_id FROM clients WHERE client_name = 'US Signal Internal';
    SELECT client_id INTO v_demo_client_id FROM clients WHERE client_name = 'Great Lakes Healthcare';

    INSERT INTO project_intake_requests (
        request_number,
        client_name,
        opportunity_reference,
        request_title,
        request_description,
        requested_by_user_id,
        assigned_pm_user_id,
        intake_status,
        priority,
        target_start_date,
        target_completion_date,
        estimated_hours
    )
    VALUES
      (
        'INTAKE-2026-001',
        'Great Lakes Healthcare',
        'OPP-GLH-CC-001',
        'Contact Center Modernization',
        'Implement contact center modernization with call routing, reporting, and readiness tasks.',
        v_kari_id,
        v_steve_id,
        'triage',
        'high',
        DATE '2026-07-13',
        DATE '2026-09-18',
        320.00
      ),
      (
        'INTAKE-2026-002',
        'Summit Manufacturing',
        'OPP-SMFG-NET-002',
        'Network Refresh Planning',
        'Plan and staff a phased network refresh covering discovery, design, implementation, and cutover.',
        v_kari_id,
        v_header_id,
        'approved',
        'normal',
        DATE '2026-07-20',
        DATE '2026-10-30',
        420.00
      )
    ON CONFLICT (request_number) DO UPDATE
    SET client_name = EXCLUDED.client_name,
        opportunity_reference = EXCLUDED.opportunity_reference,
        request_title = EXCLUDED.request_title,
        request_description = EXCLUDED.request_description,
        requested_by_user_id = EXCLUDED.requested_by_user_id,
        assigned_pm_user_id = EXCLUDED.assigned_pm_user_id,
        intake_status = EXCLUDED.intake_status,
        priority = EXCLUDED.priority,
        target_start_date = EXCLUDED.target_start_date,
        target_completion_date = EXCLUDED.target_completion_date,
        estimated_hours = EXCLUDED.estimated_hours,
        updated_at = NOW();

    SELECT project_intake_request_id INTO v_intake_1_id FROM project_intake_requests WHERE request_number = 'INTAKE-2026-001';
    SELECT project_intake_request_id INTO v_intake_2_id FROM project_intake_requests WHERE request_number = 'INTAKE-2026-002';

    INSERT INTO projects (
        client_id,
        project_code,
        project_name,
        project_description,
        project_manager_user_id,
        status,
        start_date,
        end_date,
        billable
    )
    VALUES
      (
        v_demo_client_id,
        'GLH-CC-2026',
        'Great Lakes Healthcare Contact Center Modernization',
        'Demo project created from project intake for resource assignment walkthrough.',
        v_steve_id,
        'active',
        DATE '2026-07-13',
        DATE '2026-09-18',
        TRUE
      ),
      (
        v_internal_client_id,
        'USS-DEMO-INTAKE',
        'ProjectPulse Intake and Resource Workflow',
        'Internal ProjectPulse demo workspace for intake, engineering requests, capacity, and assignment readiness.',
        v_header_id,
        'active',
        DATE '2026-07-06',
        DATE '2026-08-31',
        FALSE
      )
    ON CONFLICT (project_code) DO UPDATE
    SET client_id = EXCLUDED.client_id,
        project_name = EXCLUDED.project_name,
        project_description = EXCLUDED.project_description,
        project_manager_user_id = EXCLUDED.project_manager_user_id,
        status = EXCLUDED.status,
        start_date = EXCLUDED.start_date,
        end_date = EXCLUDED.end_date,
        billable = EXCLUDED.billable,
        updated_at = NOW();

    SELECT project_id INTO v_project_1_id FROM projects WHERE project_code = 'GLH-CC-2026';
    SELECT project_id INTO v_project_2_id FROM projects WHERE project_code = 'USS-DEMO-INTAKE';

    INSERT INTO project_tasks (project_id, task_code, task_name, task_description, billable, is_active, utilization_bucket, utilization_requires_approval)
    VALUES
      (v_project_1_id, 'DISCOVERY', 'Discovery & Current State', 'Discovery, requirements validation, and current-state assessment.', TRUE, TRUE, 'billable', TRUE),
      (v_project_1_id, 'DESIGN', 'Solution Design', 'Target-state contact center design and implementation plan.', TRUE, TRUE, 'billable', TRUE),
      (v_project_1_id, 'IMPLEMENT', 'Implementation', 'Configuration, testing, migration, and deployment activities.', TRUE, TRUE, 'billable', TRUE),
      (v_project_1_id, 'CUTOVER', 'Cutover & Hypercare', 'Production cutover and post-go-live hypercare.', TRUE, TRUE, 'billable', TRUE)
    ON CONFLICT (project_id, task_code) DO UPDATE
    SET task_name = EXCLUDED.task_name,
        task_description = EXCLUDED.task_description,
        billable = EXCLUDED.billable,
        is_active = TRUE,
        utilization_bucket = EXCLUDED.utilization_bucket,
        utilization_requires_approval = EXCLUDED.utilization_requires_approval,
        updated_at = NOW();

    SELECT task_id INTO v_task_design_id FROM project_tasks WHERE project_id = v_project_1_id AND task_code = 'DESIGN';
    SELECT task_id INTO v_task_impl_id FROM project_tasks WHERE project_id = v_project_1_id AND task_code = 'IMPLEMENT';
    SELECT task_id INTO v_task_cutover_id FROM project_tasks WHERE project_id = v_project_1_id AND task_code = 'CUTOVER';

    INSERT INTO project_assignments (
        project_id,
        task_id,
        user_id,
        assigned_by_user_id,
        effective_start_date,
        effective_end_date,
        allocation_percent
    )
    VALUES
      (v_project_1_id, v_task_design_id, v_jason_id, v_steve_id, DATE '2026-07-13', DATE '2026-08-14', 50.00),
      (v_project_1_id, v_task_impl_id, v_jeremy_id, v_steve_id, DATE '2026-07-27', DATE '2026-09-11', 75.00),
      (v_project_1_id, v_task_cutover_id, v_kevin_id, v_steve_id, DATE '2026-09-07', DATE '2026-09-18', 50.00)
    ON CONFLICT (project_id, task_id, user_id, effective_start_date) DO UPDATE
    SET assigned_by_user_id = EXCLUDED.assigned_by_user_id,
        effective_end_date = EXCLUDED.effective_end_date,
        allocation_percent = EXCLUDED.allocation_percent;

    INSERT INTO resource_profiles (user_id, resource_number, resource_type, primary_function, time_zone, availability_status)
    VALUES
      (v_jason_id, 'RES-JASON', 'full_time', 'Collaboration Engineering', 'America/Chicago', 'online'),
      (v_jeremy_id, 'RES-JEREMY', 'full_time', 'Systems Engineering', 'America/Chicago', 'online'),
      (v_kevin_id, 'RES-KEVIN', 'full_time', 'Systems Engineering', 'America/Chicago', 'online')
    ON CONFLICT (user_id) DO UPDATE
    SET resource_number = EXCLUDED.resource_number,
        resource_type = EXCLUDED.resource_type,
        primary_function = EXCLUDED.primary_function,
        time_zone = EXCLUDED.time_zone,
        availability_status = EXCLUDED.availability_status,
        updated_at = NOW();

    INSERT INTO resource_functions (user_id, function_name, is_primary, effective_start_date)
    VALUES
      (v_jason_id, 'Collaboration Engineering', TRUE, CURRENT_DATE),
      (v_jeremy_id, 'Systems Engineering', TRUE, CURRENT_DATE),
      (v_kevin_id, 'Systems Engineering', TRUE, CURRENT_DATE)
    ON CONFLICT (user_id, function_name, effective_start_date) DO NOTHING;

    INSERT INTO resource_qualifications (user_id, qualification_category, qualification_name, competency, years_of_experience, effective_start_date)
    VALUES
      (v_jason_id, 'Collaboration', 'Contact Center', 'advanced', 8.0, CURRENT_DATE),
      (v_jason_id, 'Collaboration', 'Cisco UC', 'advanced', 10.0, CURRENT_DATE),
      (v_jeremy_id, 'Systems', 'Windows Server', 'advanced', 7.0, CURRENT_DATE),
      (v_jeremy_id, 'Systems', 'VMware', 'intermediate', 5.0, CURRENT_DATE),
      (v_kevin_id, 'Systems', 'Automation', 'advanced', 9.0, CURRENT_DATE),
      (v_kevin_id, 'Systems', 'Monitoring', 'advanced', 8.0, CURRENT_DATE)
    ON CONFLICT DO NOTHING;

    INSERT INTO resource_capacity_plans (user_id, week_start_date, available_hours, assigned_hours, planned_utilization_percent, capacity_status)
    VALUES
      (v_jason_id, DATE '2026-07-13', 40.00, 20.00, 50.00, 'available'),
      (v_jeremy_id, DATE '2026-07-13', 40.00, 32.00, 80.00, 'near_capacity'),
      (v_kevin_id, DATE '2026-07-13', 40.00, 16.00, 40.00, 'available'),
      (v_jason_id, DATE '2026-07-20', 40.00, 24.00, 60.00, 'available'),
      (v_jeremy_id, DATE '2026-07-20', 40.00, 36.00, 90.00, 'near_capacity'),
      (v_kevin_id, DATE '2026-07-20', 40.00, 20.00, 50.00, 'available')
    ON CONFLICT (user_id, week_start_date) DO UPDATE
    SET available_hours = EXCLUDED.available_hours,
        assigned_hours = EXCLUDED.assigned_hours,
        planned_utilization_percent = EXCLUDED.planned_utilization_percent,
        capacity_status = EXCLUDED.capacity_status,
        updated_at = NOW();

    INSERT INTO engineering_resource_requests (
        request_number,
        project_intake_request_id,
        project_id,
        requested_by_user_id,
        assigned_pm_user_id,
        requested_function,
        skill_requirements,
        requested_hours,
        target_start_date,
        target_end_date,
        priority,
        request_status,
        fulfilled_by_user_id,
        assignment_notes
    )
    VALUES
      (
        'ERR-2026-001',
        v_intake_1_id,
        v_project_1_id,
        v_kari_id,
        v_steve_id,
        'Collaboration Engineering',
        'Contact Center, Cisco UC, call routing, reporting readiness',
        120.00,
        DATE '2026-07-13',
        DATE '2026-08-14',
        'high',
        'assigned',
        v_jason_id,
        'Jason assigned for discovery and solution design.'
      ),
      (
        'ERR-2026-002',
        v_intake_1_id,
        v_project_1_id,
        v_kari_id,
        v_steve_id,
        'Systems Engineering',
        'Windows Server, integration readiness, monitoring',
        160.00,
        DATE '2026-07-27',
        DATE '2026-09-11',
        'high',
        'requested',
        NULL,
        'Manager review needed before assignment.'
      ),
      (
        'ERR-2026-003',
        v_intake_2_id,
        NULL,
        v_kari_id,
        v_header_id,
        'Systems Engineering',
        'Network-adjacent systems readiness, automation, cutover support',
        80.00,
        DATE '2026-07-20',
        DATE '2026-08-21',
        'normal',
        'triage',
        NULL,
        'Pending intake approval and project creation.'
      )
    ON CONFLICT (request_number) DO UPDATE
    SET project_intake_request_id = EXCLUDED.project_intake_request_id,
        project_id = EXCLUDED.project_id,
        requested_by_user_id = EXCLUDED.requested_by_user_id,
        assigned_pm_user_id = EXCLUDED.assigned_pm_user_id,
        requested_function = EXCLUDED.requested_function,
        skill_requirements = EXCLUDED.skill_requirements,
        requested_hours = EXCLUDED.requested_hours,
        target_start_date = EXCLUDED.target_start_date,
        target_end_date = EXCLUDED.target_end_date,
        priority = EXCLUDED.priority,
        request_status = EXCLUDED.request_status,
        fulfilled_by_user_id = EXCLUDED.fulfilled_by_user_id,
        assignment_notes = EXCLUDED.assignment_notes,
        updated_at = NOW();

    INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id)
    VALUES (v_kari_id, 'project_intake_demo_seeded', 'demo_seed', NULL);
END $$;
