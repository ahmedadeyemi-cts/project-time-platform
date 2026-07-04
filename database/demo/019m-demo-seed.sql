-- Project Health Dashboard August demo seed data
-- Staging/demo only. Do not run in production without review.

DO $$
DECLARE
    v_manager_id uuid;
    v_engineer_id uuid;
    v_ptc_id uuid;
    v_team_id uuid;
BEGIN
    INSERT INTO app_users (email, display_name, job_title, department, department_name, team_name, manager_email, is_active, login_enabled)
    VALUES
      ('demo.manager@ussignal.local', 'Demo Manager', 'Manager of Professional Services', 'Professional Services', 'Professional Services', 'Demo Delivery', NULL, TRUE, TRUE),
      ('demo.engineer@ussignal.local', 'Demo Engineer', 'Systems Engineer', 'Professional Services', 'Systems Engineering', 'Demo Delivery', 'demo.manager@ussignal.local', TRUE, TRUE),
      ('project.team.coordinator@ussignal.local', 'Project Team Coordinator', 'Project Team Coordinator', 'Project Management Office', 'Project Management Office', 'Project Management', NULL, TRUE, TRUE)
    ON CONFLICT (email) DO UPDATE
    SET
      display_name = EXCLUDED.display_name,
      job_title = EXCLUDED.job_title,
      department = EXCLUDED.department,
      department_name = EXCLUDED.department_name,
      team_name = EXCLUDED.team_name,
      manager_email = EXCLUDED.manager_email,
      is_active = TRUE,
      login_enabled = TRUE,
      updated_at = NOW();

    SELECT user_id INTO v_manager_id FROM app_users WHERE email = 'demo.manager@ussignal.local';
    SELECT user_id INTO v_engineer_id FROM app_users WHERE email = 'demo.engineer@ussignal.local';
    SELECT user_id INTO v_ptc_id FROM app_users WHERE email = 'project.team.coordinator@ussignal.local';

    INSERT INTO teams (team_name, team_description, is_active)
    VALUES ('Demo Delivery', 'Demo team for August Project Health Dashboard walkthrough.', TRUE)
    ON CONFLICT (team_name) DO UPDATE
    SET team_description = EXCLUDED.team_description,
        is_active = TRUE,
        updated_at = NOW();

    SELECT team_id INTO v_team_id FROM teams WHERE team_name = 'Demo Delivery';

    INSERT INTO team_memberships (team_id, user_id, effective_start_date)
    VALUES
      (v_team_id, v_manager_id, CURRENT_DATE),
      (v_team_id, v_engineer_id, CURRENT_DATE)
    ON CONFLICT (team_id, user_id, effective_start_date) DO NOTHING;

    INSERT INTO reporting_relationships (employee_user_id, manager_user_id, effective_start_date)
    VALUES (v_engineer_id, v_manager_id, CURRENT_DATE)
    ON CONFLICT (employee_user_id, effective_start_date) DO NOTHING;

    INSERT INTO timesheets (user_id, week_start_date, week_end_date, status)
    VALUES
      (v_engineer_id, DATE '2026-06-28', DATE '2026-07-04', 'draft')
    ON CONFLICT (user_id, week_start_date) DO UPDATE
    SET status = 'draft',
        submitted_at = NULL,
        updated_at = NOW();

    INSERT INTO projects (project_code, project_name, project_description, project_manager_user_id, status, start_date, billable)
    VALUES
      ('DEMO-PSA-001', 'Demo Client PSA Implementation', 'Sample project for August demo workflow.', v_manager_id, 'active', CURRENT_DATE, TRUE),
      ('DEMO-OPS-002', 'Demo Operations Readiness', 'Sample internal readiness project for reporting demo.', v_manager_id, 'active', CURRENT_DATE, FALSE)
    ON CONFLICT (project_code) DO UPDATE
    SET project_name = EXCLUDED.project_name,
        project_description = EXCLUDED.project_description,
        project_manager_user_id = EXCLUDED.project_manager_user_id,
        status = EXCLUDED.status,
        updated_at = NOW();

    INSERT INTO company_holidays (holiday_date, holiday_name, holiday_code, holiday_type, is_floating_holiday, auto_populate_hours, is_active)
    VALUES
      (DATE '2026-07-03', 'Independence Day', 'HOLIDAY', 'company_paid', FALSE, 8.00, TRUE),
      (DATE '2026-09-07', 'Labor Day', 'HOLIDAY', 'company_paid', FALSE, 8.00, TRUE)
    ON CONFLICT (holiday_date) DO UPDATE
    SET holiday_name = EXCLUDED.holiday_name,
        holiday_code = EXCLUDED.holiday_code,
        holiday_type = EXCLUDED.holiday_type,
        is_floating_holiday = EXCLUDED.is_floating_holiday,
        auto_populate_hours = EXCLUDED.auto_populate_hours,
        is_active = TRUE,
        updated_at = NOW();
END $$;

-- Expense, Emburse Certify, and report demo records should be added after those tables are finalized.
