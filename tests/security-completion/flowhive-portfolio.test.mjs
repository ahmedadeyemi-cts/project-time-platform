import { readFileSync } from 'node:fs';
import { PGlite } from '@electric-sql/pglite';
import assert from 'node:assert/strict';

// Execute the actual application SQL, including all scope subqueries.
const source = readFileSync(new URL('../../src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs', import.meta.url), 'utf8');
const sqlFor = name => source.slice(source.indexOf(`>> ${name}(`)).split('const string sql = """')[1].split('""";')[0];
const id = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const db = await PGlite.create();
let checks = 0;
try {
  await db.exec(`
    CREATE TABLE app_users(user_id uuid PRIMARY KEY, display_name text, email text, is_active boolean, team_name text, department_name text);
    CREATE TABLE clients(client_id uuid PRIMARY KEY, client_name text);
    CREATE TABLE projects(project_id uuid PRIMARY KEY, project_code text, project_name text, status text, start_date date, end_date date, created_at timestamptz, client_id uuid, project_manager_user_id uuid, account_executive_user_id uuid, solution_architect_user_id uuid);
    CREATE TABLE project_tasks(task_id uuid PRIMARY KEY, project_id uuid, task_code text, task_name text, task_description text, billable boolean, is_active boolean);
    CREATE TABLE project_assignments(project_assignment_id uuid PRIMARY KEY, project_id uuid, task_id uuid, user_id uuid, effective_start_date date, effective_end_date date, module001a_closeout_status text, assigned_hours numeric, allocation_percent numeric);
    CREATE TABLE project_intake_documents(project_id uuid, is_active boolean, engineering_visible boolean);
    CREATE TABLE reporting_relationships(employee_user_id uuid, manager_user_id uuid, team_lead_user_id uuid, effective_start_date date, effective_end_date date);
    CREATE TABLE projectpulse_team_scope_assignments(scoped_user_id uuid, is_active boolean, team_name text, department_name text);
    CREATE TABLE project_planning_collaborators(project_id uuid, user_id uuid, module_code text, is_active boolean, effective_start_date date, effective_end_date date);
    CREATE TABLE time_entries(task_id uuid, hours numeric, status text);
    INSERT INTO app_users VALUES ('${id(1)}','Engineer','engineer@example.invalid',true,'Delivery',''),('${id(2)}','Manager','manager@example.invalid',true,'','');
    INSERT INTO projects(project_id,project_code,project_name,status,created_at,project_manager_user_id) VALUES('${id(10)}','A','Fixture','active',now(),'${id(2)}');
    INSERT INTO project_tasks VALUES('${id(20)}','${id(10)}','T','Fixture task','',true,true);
    INSERT INTO project_assignments VALUES('${id(30)}','${id(10)}','${id(20)}','${id(1)}',current_date-1,NULL,'active',8,100);
  `);
  async function query(name, overrides = {}) {
    const values = { user_id: id(1), team_name: '', department_name: '', is_broad_scope: false, can_view_team_scope: false, can_view_all_scoped_tasks: false, ...overrides };
    const names = [];
    const text = sqlFor(name).replace(/@([a-z_]+)/g, (_, n) => { if (!names.includes(n)) names.push(n); return '$' + (names.indexOf(n) + 1); });
    return (await db.query(text, names.map(n => values[n]))).rows;
  }
  for (const method of ['LoadProjectsAsync','LoadTasksAsync','LoadAssignmentsAsync']) {
    assert.equal((await query(method, { user_id: id(2), can_view_all_scoped_tasks: true })).length, 1); checks++;
  }
  for (const method of ['LoadProjectsAsync','LoadTasksAsync']) {
    assert.equal((await query(method)).length, 1); checks++;
    assert.equal((await query(method, { user_id: id(99) })).length, 0); checks++;
    assert.equal((await query(method, { user_id: id(99), can_view_team_scope: true, team_name: 'Delivery', can_view_all_scoped_tasks: true })).length, 1); checks++;
    for (const update of ["effective_end_date=current_date-1", "effective_start_date=current_date+1", "module001a_closeout_status='ptc_final_closed'"]) {
      await db.exec(`UPDATE project_assignments SET ${update}`);
      assert.equal((await query(method)).length, 0); checks++;
      assert.equal((await query(method, { user_id: id(99), can_view_team_scope: true, team_name: 'Delivery', can_view_all_scoped_tasks: true })).length, 0); checks++;
      await db.exec("UPDATE project_assignments SET effective_start_date=current_date-1,effective_end_date=NULL,module001a_closeout_status='active'");
    }
  }
  console.log(`FLOWHIVE_PORTFOLIO_SQL=PASS assertions=${checks}`);
} finally { await db.close(); }
