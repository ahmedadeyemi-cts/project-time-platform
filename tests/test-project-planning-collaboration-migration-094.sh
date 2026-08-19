#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MIGRATION="$ROOT/database/migrations/094_project_planning_collaboration_access.sql"
ROLLBACK="$ROOT/database/rollback/094_project_planning_collaboration_access_rollback.sql"
POSTGRES_IMAGE="${POSTGRES_IMAGE:-postgres:16-alpine}"
CONTAINER="projectpulse-m094-${GITHUB_RUN_ID:-local}-${RANDOM}"
PORT="${PROJECTPULSE_M094_POSTGRES_PORT:-55494}"
PASSWORD="projectpulse-m094-test"
DATABASE="projectpulse_m094"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

[[ -f "$MIGRATION" ]] || { echo "Missing $MIGRATION" >&2; exit 1; }
[[ -f "$ROLLBACK" ]] || { echo "Missing $ROLLBACK" >&2; exit 1; }

docker run -d --name "$CONTAINER" \
  -e POSTGRES_PASSWORD="$PASSWORD" \
  -e POSTGRES_DB="$DATABASE" \
  -p "127.0.0.1:${PORT}:5432" \
  "$POSTGRES_IMAGE" >/dev/null

for attempt in $(seq 1 90); do
  if PGPASSWORD="$PASSWORD" psql \
      "host=127.0.0.1 port=$PORT user=postgres dbname=$DATABASE sslmode=disable" \
      -Atqc 'SELECT 1' >/dev/null 2>&1; then
    break
  fi
  [[ "$attempt" -lt 90 ]] || { echo 'PostgreSQL did not become ready.' >&2; exit 1; }
  sleep 1
done

PSQL=(psql "host=127.0.0.1 port=$PORT user=postgres dbname=$DATABASE sslmode=disable" -X -v ON_ERROR_STOP=1)
export PGPASSWORD="$PASSWORD"

"${PSQL[@]}" <<'SQL'
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE app_users (
    user_id UUID PRIMARY KEY,
    email TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    manager_user_id UUID NULL,
    reports_to_user_id UUID NULL,
    supervisor_user_id UUID NULL,
    engineering_lead_user_id UUID NULL
);

CREATE TABLE app_roles (
    app_role_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_code TEXT NOT NULL UNIQUE,
    role_name TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE app_permissions (
    app_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    permission_code VARCHAR(100) NOT NULL UNIQUE,
    permission_name TEXT NOT NULL,
    module_code TEXT NOT NULL,
    permission_description TEXT NOT NULL
);

CREATE TABLE app_user_role_assignments (
    app_user_role_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    UNIQUE(user_id, app_role_id)
);

CREATE TABLE app_role_permissions (
    app_role_permission_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    app_role_id UUID NOT NULL REFERENCES app_roles(app_role_id),
    app_permission_id UUID NOT NULL REFERENCES app_permissions(app_permission_id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(app_role_id, app_permission_id)
);

CREATE TABLE projects (
    project_id UUID PRIMARY KEY,
    project_code TEXT NOT NULL UNIQUE,
    project_name TEXT NOT NULL,
    customer_name TEXT NOT NULL DEFAULT '',
    project_manager_user_id UUID NULL REFERENCES app_users(user_id),
    account_executive_user_id UUID NULL REFERENCES app_users(user_id),
    solution_architect_user_id UUID NULL REFERENCES app_users(user_id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE project_tasks (
    task_id UUID PRIMARY KEY,
    project_id UUID NOT NULL REFERENCES projects(project_id),
    task_code TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE project_assignments (
    project_assignment_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id UUID NOT NULL REFERENCES project_tasks(task_id),
    user_id UUID NOT NULL REFERENCES app_users(user_id),
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    effective_start_date DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_end_date DATE NULL
);

INSERT INTO app_roles(role_code, role_name) VALUES
  ('PROJECT_MANAGER','Project Manager'),
  ('PROJECT_MANAGER_LEAD','Project Manager Lead'),
  ('ENGINEER','Engineer'),
  ('ENGINEERING_LEAD','Engineering Lead'),
  ('ACCOUNT_EXECUTIVE','Account Executive'),
  ('SOLUTION_ARCHITECT','Solution Architect'),
  ('SUPER_ADMINISTRATOR','Super Administrator');

INSERT INTO app_permissions(permission_code, permission_name, module_code, permission_description) VALUES
  ('VIEW_PROJECT_FORGE_033','View Project Forge','033','fixture'),
  ('VIEW_PROJECT_FLOWHIVE_066','View FlowHive','066','fixture'),
  ('VIEW_FLOWHIVE_066','View FlowHive','066','fixture'),
  ('VIEW_FLOWHIVE_FINANCIALS_066','View FlowHive financials','066','fixture'),
  ('CREATE_FLOWHIVE_CUSTOMER_SHARE_066','Create FlowHive customer share','066','fixture');

INSERT INTO app_users(user_id,email,display_name) VALUES
  ('10000000-0000-4000-8000-000000000001','pm@example.test','Project Manager'),
  ('10000000-0000-4000-8000-000000000002','pmlead@example.test','PM Lead'),
  ('10000000-0000-4000-8000-000000000003','engineer@example.test','Engineer'),
  ('10000000-0000-4000-8000-000000000004','lead@example.test','Engineering Lead'),
  ('10000000-0000-4000-8000-000000000005','ae@example.test','Account Executive'),
  ('10000000-0000-4000-8000-000000000006','sa@example.test','Solution Architect'),
  ('10000000-0000-4000-8000-000000000007','outsider@example.test','Outsider'),
  ('10000000-0000-4000-8000-000000000008','admin@example.test','Super Administrator');

UPDATE app_users
SET manager_user_id = '10000000-0000-4000-8000-000000000002'
WHERE user_id = '10000000-0000-4000-8000-000000000001';
UPDATE app_users
SET engineering_lead_user_id = '10000000-0000-4000-8000-000000000004'
WHERE user_id = '10000000-0000-4000-8000-000000000003';

INSERT INTO app_user_role_assignments(user_id, app_role_id)
SELECT fixture.user_id, role.app_role_id
FROM (VALUES
  ('10000000-0000-4000-8000-000000000001'::UUID,'PROJECT_MANAGER'),
  ('10000000-0000-4000-8000-000000000002'::UUID,'PROJECT_MANAGER_LEAD'),
  ('10000000-0000-4000-8000-000000000003'::UUID,'ENGINEER'),
  ('10000000-0000-4000-8000-000000000004'::UUID,'ENGINEERING_LEAD'),
  ('10000000-0000-4000-8000-000000000005'::UUID,'ACCOUNT_EXECUTIVE'),
  ('10000000-0000-4000-8000-000000000006'::UUID,'SOLUTION_ARCHITECT'),
  ('10000000-0000-4000-8000-000000000008'::UUID,'SUPER_ADMINISTRATOR')
) fixture(user_id, role_code)
JOIN app_roles role ON role.role_code = fixture.role_code;

INSERT INTO projects(
    project_id,project_code,project_name,customer_name,project_manager_user_id,
    account_executive_user_id,solution_architect_user_id)
VALUES
  ('20000000-0000-4000-8000-000000000001','PRO-001','Associated Project','Customer A',
   '10000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000005','10000000-0000-4000-8000-000000000006'),
  ('20000000-0000-4000-8000-000000000002','PRO-002','Unrelated Project','Customer B',NULL,NULL,NULL);

INSERT INTO project_tasks(task_id,project_id,task_code) VALUES
  ('30000000-0000-4000-8000-000000000001','20000000-0000-4000-8000-000000000001','TASK-001');
INSERT INTO project_assignments(task_id,user_id)
VALUES('30000000-0000-4000-8000-000000000001','10000000-0000-4000-8000-000000000003');
SQL

"${PSQL[@]}" -f "$MIGRATION"
"${PSQL[@]}" -f "$MIGRATION"

"${PSQL[@]}" <<'SQL'
DO $assertions$
DECLARE
    project_a UUID := '20000000-0000-4000-8000-000000000001';
    project_b UUID := '20000000-0000-4000-8000-000000000002';
BEGIN
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000001') <> 'assigned_project_manager' THEN
        RAISE EXCEPTION 'Assigned PM scope failed.';
    END IF;
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000002') <> 'project_manager_lead_scope' THEN
        RAISE EXCEPTION 'PM Lead scope failed.';
    END IF;
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000003') <> 'active_project_assignment' THEN
        RAISE EXCEPTION 'Engineer assignment scope failed.';
    END IF;
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000004') <> 'engineering_lead_team_scope' THEN
        RAISE EXCEPTION 'Engineering Lead team scope failed.';
    END IF;
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000005') <> 'assigned_account_executive' THEN
        RAISE EXCEPTION 'Account Executive scope failed.';
    END IF;
    IF projectpulse094_project_scope_reason(project_a,'10000000-0000-4000-8000-000000000006') <> 'assigned_solution_architect' THEN
        RAISE EXCEPTION 'Solution Architect scope failed.';
    END IF;
    IF projectpulse094_can_view_project(project_a,'10000000-0000-4000-8000-000000000007') THEN
        RAISE EXCEPTION 'Unassociated user received project access.';
    END IF;
    IF projectpulse094_can_view_project(project_b,'10000000-0000-4000-8000-000000000003') THEN
        RAISE EXCEPTION 'Engineer received cross-project access.';
    END IF;
    IF NOT projectpulse094_can_edit_planner(project_a,'10000000-0000-4000-8000-000000000003') THEN
        RAISE EXCEPTION 'Assigned Engineer planner edit failed.';
    END IF;
    IF NOT projectpulse094_can_edit_planner(project_a,'10000000-0000-4000-8000-000000000004') THEN
        RAISE EXCEPTION 'Engineering Lead planner edit failed.';
    END IF;
    IF projectpulse094_can_edit_planner(project_a,'10000000-0000-4000-8000-000000000005')
       OR projectpulse094_can_edit_planner(project_a,'10000000-0000-4000-8000-000000000006') THEN
        RAISE EXCEPTION 'AE or SA received planner edit access.';
    END IF;
    IF NOT projectpulse094_can_administer_planner(project_a,'10000000-0000-4000-8000-000000000001') THEN
        RAISE EXCEPTION 'Assigned PM administration failed.';
    END IF;
    IF projectpulse094_can_administer_planner(project_a,'10000000-0000-4000-8000-000000000003') THEN
        RAISE EXCEPTION 'Engineer received PM administration.';
    END IF;
END;
$assertions$;

INSERT INTO project_planning_collaborators(
    project_id,user_id,collaboration_role,access_level,reason,created_by_user_id,updated_by_user_id)
VALUES(
    '20000000-0000-4000-8000-000000000002',
    '10000000-0000-4000-8000-000000000003',
    'engineer',
    'edit',
    'Explicit cross-functional planning assignment',
    '10000000-0000-4000-8000-000000000001',
    '10000000-0000-4000-8000-000000000001');

DO $collaborator_assertions$
BEGIN
    IF projectpulse094_project_scope_reason(
        '20000000-0000-4000-8000-000000000002',
        '10000000-0000-4000-8000-000000000003') <> 'planning_collaborator_edit' THEN
        RAISE EXCEPTION 'Explicit collaborator scope failed.';
    END IF;
    IF NOT projectpulse094_can_edit_planner(
        '20000000-0000-4000-8000-000000000002',
        '10000000-0000-4000-8000-000000000003') THEN
        RAISE EXCEPTION 'Explicit collaborator planner edit failed.';
    END IF;
    IF (SELECT COUNT(*) FROM project_planning_collaboration_audit_events) <> 1 THEN
        RAISE EXCEPTION 'Collaborator audit evidence was not created.';
    END IF;
END;
$collaborator_assertions$;
SQL

set +e
rollback_output="$("${PSQL[@]}" -f "$ROLLBACK" 2>&1)"
rollback_status=$?
set -e
if [[ "$rollback_status" -eq 0 ]]; then
  echo 'Guarded rollback unexpectedly succeeded after operational evidence.' >&2
  exit 1
fi
grep -q 'Rollback 094 refused: project-planning collaborator records exist.' <<<"$rollback_output" || {
  echo "$rollback_output" >&2
  echo 'Guarded rollback did not report the expected collaborator-evidence refusal.' >&2
  exit 1
}

echo 'Migration 094 project-planning collaboration regression passed.'
