-- ProjectPulse August Demo Users
-- Staging/demo only. Creates app users, roles, role assignments, teams,
-- reporting relationships, and time compliance demo cases.

DO $$
DECLARE
    v_admin_user_id uuid;

    v_manager_role_id uuid;
    v_engineer_role_id uuid;
    v_project_management_role_id uuid;
    v_ptc_role_id uuid;
    v_accounting_role_id uuid;
    v_executive_role_id uuid;

    v_view_time_compliance_permission_id uuid;

    v_ahmed_manager_id uuid;
    v_matthew_manager_id uuid;
    v_steve_pm_id uuid;
    v_header_pm_id uuid;
    v_kari_ptc_id uuid;
    v_jason_engineer_id uuid;
    v_jeremy_engineer_id uuid;
    v_kevin_engineer_id uuid;
    v_darren_exec_id uuid;
    v_juli_accounting_id uuid;

    v_collab_team_id uuid;
    v_systems_team_id uuid;
    v_project_management_team_id uuid;
    v_accounting_team_id uuid;
    v_executive_team_id uuid;

    v_week_start date := DATE '2026-06-21';
    v_week_end date := DATE '2026-06-27';
BEGIN
    SELECT user_id
    INTO v_admin_user_id
    FROM app_users
    WHERE email IN ('ahmed.adeyemi@ussignal.local', 'ahmed.adeyemi@ussignal.com')
    ORDER BY CASE WHEN email = 'ahmed.adeyemi@ussignal.local' THEN 0 ELSE 1 END
    LIMIT 1;

    -- Ensure demo-specific permissions and roles exist.
    INSERT INTO app_permissions (permission_code, permission_name, module_code, permission_description)
    VALUES
      ('VIEW_TIME_COMPLIANCE', 'View time compliance', 'notifications', 'View Time Compliance and Notification Center dry-run previews.'),
      ('MANAGE_TIME_COMPLIANCE_NOTIFICATIONS', 'Manage time compliance notifications', 'notifications', 'Manage Time Compliance settings, templates, previews, and future notification sends.')
    ON CONFLICT (permission_code) DO UPDATE
    SET permission_name = EXCLUDED.permission_name,
        module_code = EXCLUDED.module_code,
        permission_description = EXCLUDED.permission_description;

    INSERT INTO app_roles (role_code, role_name, role_description, is_system_role, is_active, display_order)
    VALUES
      ('ACCOUNTING', 'Accounting', 'Accounting role for reconciliation, expenses, reporting, and billing readiness demo workflows.', TRUE, TRUE, 55),
      ('EXECUTIVE', 'Executive', 'Executive role for reporting, accountability dashboards, and demo read-only leadership views.', TRUE, TRUE, 65)
    ON CONFLICT (role_code) DO UPDATE
    SET role_name = EXCLUDED.role_name,
        role_description = EXCLUDED.role_description,
        is_active = TRUE,
        display_order = EXCLUDED.display_order,
        updated_at = NOW();

    SELECT app_role_id INTO v_manager_role_id FROM app_roles WHERE role_code = 'MANAGER';
    SELECT app_role_id INTO v_engineer_role_id FROM app_roles WHERE role_code = 'ENGINEER';
    SELECT app_role_id INTO v_project_management_role_id FROM app_roles WHERE role_code = 'PROJECT_MANAGEMENT';
    SELECT app_role_id INTO v_ptc_role_id FROM app_roles WHERE role_code = 'PROJECT_TEAM_COORDINATOR';
    SELECT app_role_id INTO v_accounting_role_id FROM app_roles WHERE role_code = 'ACCOUNTING';
    SELECT app_role_id INTO v_executive_role_id FROM app_roles WHERE role_code = 'EXECUTIVE';

    SELECT app_permission_id INTO v_view_time_compliance_permission_id
    FROM app_permissions
    WHERE permission_code = 'VIEW_TIME_COMPLIANCE';

    -- Give Time Compliance visibility to demo management/coordinator roles.
    INSERT INTO app_role_permissions (app_role_id, app_permission_id)
    SELECT role_id, v_view_time_compliance_permission_id
    FROM (
        VALUES
            (v_manager_role_id),
            (v_project_management_role_id),
            (v_ptc_role_id),
            (v_accounting_role_id),
            (v_executive_role_id)
    ) AS roles(role_id)
    WHERE role_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM app_role_permissions existing
        WHERE existing.app_role_id = roles.role_id
          AND existing.app_permission_id = v_view_time_compliance_permission_id
      );

    -- Accounting role permissions.
    INSERT INTO app_role_permissions (app_role_id, app_permission_id)
    SELECT v_accounting_role_id, p.app_permission_id
    FROM app_permissions p
    WHERE p.permission_code IN (
        'VIEW_DASHBOARD',
        'VIEW_EXPENSES',
        'MANAGE_EXPENSES',
        'VIEW_ACCOUNT_RECONCILIATION',
        'MANAGE_ACCOUNT_RECONCILIATION',
        'VIEW_REPORTS',
        'VIEW_AUDIT_TRAIL',
        'VIEW_TIME_COMPLIANCE'
    )
      AND v_accounting_role_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM app_role_permissions existing
        WHERE existing.app_role_id = v_accounting_role_id
          AND existing.app_permission_id = p.app_permission_id
      );

    -- Executive role permissions.
    INSERT INTO app_role_permissions (app_role_id, app_permission_id)
    SELECT v_executive_role_id, p.app_permission_id
    FROM app_permissions p
    WHERE p.permission_code IN (
        'VIEW_DASHBOARD',
        'VIEW_EXECUTIVE_REPORTING',
        'VIEW_REPORTS',
        'VIEW_TEAM_UTILIZATION',
        'VIEW_TIME_COMPLIANCE'
    )
      AND v_executive_role_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM app_role_permissions existing
        WHERE existing.app_role_id = v_executive_role_id
          AND existing.app_permission_id = p.app_permission_id
      );

    -- Demo users.
    INSERT INTO app_users (email, display_name, job_title, department, department_name, team_name, manager_email, is_active, login_enabled)
    VALUES
      ('ahmed.adeyemi01@ussignal.local', 'Ahmed Adeyemi01', 'Manager of Professional Services', 'Professional Services', 'Collaboration Engineering', 'Collaboration Engineering', NULL, TRUE, TRUE),
      ('matthew.lenoble01@ussignal.local', 'Matthew Lenoble01', 'Manager of Professional Services', 'Professional Services', 'Systems Engineering', 'Systems Engineering', NULL, TRUE, TRUE),
      ('steve.kopischke@ussignal.local', 'Steve Kopischke', 'Project Manager', 'Project Management Office', 'Project Management', 'Project Management', NULL, TRUE, TRUE),
      ('header.schrock@ussignal.local', 'Header Schrock', 'Project Manager', 'Project Management Office', 'Project Management', 'Project Management', NULL, TRUE, TRUE),
      ('kari.wilkening@ussignal.local', 'Kari Wilkening', 'Project Team Coordinator', 'Project Management Office', 'Project Management / Accounting Support', 'Project Management', NULL, TRUE, TRUE),
      ('jason.mosier@ussignal.local', 'Jason Mosier', 'Collaboration Engineer', 'Professional Services', 'Collaboration Engineering', 'Collaboration Engineering', 'ahmed.adeyemi01@ussignal.local', TRUE, TRUE),
      ('jeremy.holt@ussignal.local', 'Jeremy Holt', 'Systems Engineer', 'Professional Services', 'Systems Engineering', 'Systems Engineering', 'matthew.lenoble01@ussignal.local', TRUE, TRUE),
      ('kevin.damish@ussignal.local', 'Kevin Damish', 'Systems Engineer', 'Professional Services', 'Systems Engineering', 'Systems Engineering', 'matthew.lenoble01@ussignal.local', TRUE, TRUE),
      ('darren.olson@ussignal.local', 'Darren Olson', 'Executive', 'Executive Leadership', 'Executive Leadership', 'Executive Leadership', NULL, TRUE, TRUE),
      ('juli.cambron@ussignal.local', 'Juli Cambron', 'Accounting', 'Accounting', 'Accounting', 'Accounting', NULL, TRUE, TRUE)
    ON CONFLICT (email) DO UPDATE
    SET display_name = EXCLUDED.display_name,
        job_title = EXCLUDED.job_title,
        department = EXCLUDED.department,
        department_name = EXCLUDED.department_name,
        team_name = EXCLUDED.team_name,
        manager_email = EXCLUDED.manager_email,
        is_active = TRUE,
        login_enabled = TRUE,
        updated_at = NOW();

    SELECT user_id INTO v_ahmed_manager_id FROM app_users WHERE email = 'ahmed.adeyemi01@ussignal.local';
    SELECT user_id INTO v_matthew_manager_id FROM app_users WHERE email = 'matthew.lenoble01@ussignal.local';
    SELECT user_id INTO v_steve_pm_id FROM app_users WHERE email = 'steve.kopischke@ussignal.local';
    SELECT user_id INTO v_header_pm_id FROM app_users WHERE email = 'header.schrock@ussignal.local';
    SELECT user_id INTO v_kari_ptc_id FROM app_users WHERE email = 'kari.wilkening@ussignal.local';
    SELECT user_id INTO v_jason_engineer_id FROM app_users WHERE email = 'jason.mosier@ussignal.local';
    SELECT user_id INTO v_jeremy_engineer_id FROM app_users WHERE email = 'jeremy.holt@ussignal.local';
    SELECT user_id INTO v_kevin_engineer_id FROM app_users WHERE email = 'kevin.damish@ussignal.local';
    SELECT user_id INTO v_darren_exec_id FROM app_users WHERE email = 'darren.olson@ussignal.local';
    SELECT user_id INTO v_juli_accounting_id FROM app_users WHERE email = 'juli.cambron@ussignal.local';

    -- Role assignments.
    INSERT INTO app_user_role_assignments (user_id, app_role_id, assigned_by_user_id, assignment_reason, is_active)
    SELECT user_id, role_id, v_admin_user_id, 'August demo seed role assignment', TRUE
    FROM (
        VALUES
          (v_ahmed_manager_id, v_manager_role_id),
          (v_matthew_manager_id, v_manager_role_id),
          (v_steve_pm_id, v_project_management_role_id),
          (v_header_pm_id, v_project_management_role_id),
          (v_kari_ptc_id, v_ptc_role_id),
          (v_kari_ptc_id, v_accounting_role_id),
          (v_jason_engineer_id, v_engineer_role_id),
          (v_jeremy_engineer_id, v_engineer_role_id),
          (v_kevin_engineer_id, v_engineer_role_id),
          (v_darren_exec_id, v_executive_role_id),
          (v_juli_accounting_id, v_accounting_role_id)
    ) AS assignments(user_id, role_id)
    WHERE user_id IS NOT NULL
      AND role_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM app_user_role_assignments existing
        WHERE existing.user_id = assignments.user_id
          AND existing.app_role_id = assignments.role_id
          AND existing.is_active = TRUE
      );

    -- Teams.
    INSERT INTO teams (team_name, team_description, is_active)
    VALUES
      ('Collaboration Engineering', 'August demo collaboration engineering team.', TRUE),
      ('Systems Engineering', 'August demo systems engineering team.', TRUE),
      ('Project Management', 'August demo project management and coordination team.', TRUE),
      ('Accounting', 'August demo accounting and reconciliation team.', TRUE),
      ('Executive Leadership', 'August demo executive leadership team.', TRUE)
    ON CONFLICT (team_name) DO UPDATE
    SET team_description = EXCLUDED.team_description,
        is_active = TRUE,
        updated_at = NOW();

    SELECT team_id INTO v_collab_team_id FROM teams WHERE team_name = 'Collaboration Engineering';
    SELECT team_id INTO v_systems_team_id FROM teams WHERE team_name = 'Systems Engineering';
    SELECT team_id INTO v_project_management_team_id FROM teams WHERE team_name = 'Project Management';
    SELECT team_id INTO v_accounting_team_id FROM teams WHERE team_name = 'Accounting';
    SELECT team_id INTO v_executive_team_id FROM teams WHERE team_name = 'Executive Leadership';

    INSERT INTO team_memberships (team_id, user_id, effective_start_date)
    SELECT team_id, user_id, CURRENT_DATE
    FROM (
        VALUES
          (v_collab_team_id, v_ahmed_manager_id),
          (v_collab_team_id, v_jason_engineer_id),
          (v_systems_team_id, v_matthew_manager_id),
          (v_systems_team_id, v_jeremy_engineer_id),
          (v_systems_team_id, v_kevin_engineer_id),
          (v_project_management_team_id, v_steve_pm_id),
          (v_project_management_team_id, v_header_pm_id),
          (v_project_management_team_id, v_kari_ptc_id),
          (v_accounting_team_id, v_kari_ptc_id),
          (v_accounting_team_id, v_juli_accounting_id),
          (v_executive_team_id, v_darren_exec_id)
    ) AS memberships(team_id, user_id)
    WHERE team_id IS NOT NULL
      AND user_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM team_memberships existing
        WHERE existing.team_id = memberships.team_id
          AND existing.user_id = memberships.user_id
          AND existing.effective_start_date = CURRENT_DATE
      );

    -- Reporting relationships.
    INSERT INTO reporting_relationships (employee_user_id, manager_user_id, effective_start_date)
    SELECT employee_id, manager_id, CURRENT_DATE
    FROM (
        VALUES
          (v_jason_engineer_id, v_ahmed_manager_id),
          (v_jeremy_engineer_id, v_matthew_manager_id),
          (v_kevin_engineer_id, v_matthew_manager_id)
    ) AS relationships(employee_id, manager_id)
    WHERE employee_id IS NOT NULL
      AND manager_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM reporting_relationships existing
        WHERE existing.employee_user_id = relationships.employee_id
          AND existing.effective_start_date = CURRENT_DATE
      );

    -- Time Compliance demo cases for week 2026-06-21.
    -- Jason: missing entirely.
    DELETE FROM timesheets
    WHERE user_id = v_jason_engineer_id
      AND week_start_date = v_week_start;

    -- Jeremy: draft with partial hours.
    INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
    VALUES (v_jeremy_engineer_id, v_week_start, v_week_end, 'draft', NULL)
    ON CONFLICT (user_id, week_start_date) DO UPDATE
    SET status = 'draft',
        submitted_at = NULL,
        updated_at = NOW();

    -- Kevin: submitted and should not appear as missing.
    INSERT INTO timesheets (user_id, week_start_date, week_end_date, status, submitted_at)
    VALUES (v_kevin_engineer_id, v_week_start, v_week_end, 'submitted', NOW())
    ON CONFLICT (user_id, week_start_date) DO UPDATE
    SET status = 'submitted',
        submitted_at = NOW(),
        updated_at = NOW();

    INSERT INTO audit_logs (actor_user_id, action, entity_type, entity_id, new_value)
    VALUES (
        v_admin_user_id,
        'august_demo_users_seeded',
        'demo_seed',
        NULL,
        jsonb_build_object(
            'users', 10,
            'roles', ARRAY['MANAGER','PROJECT_MANAGEMENT','PROJECT_TEAM_COORDINATOR','ENGINEER','ACCOUNTING','EXECUTIVE'],
            'weekStart', v_week_start
        )
    );
END $$;
