import assert from 'node:assert/strict';
import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
// PGlite executes the unmodified PostgreSQL SELECTs in an isolated fixture database.
const { PGlite } = await import(pathToFileURL(process.env.MODULE019_PGLITE_PATH || '/tmp/module019-validation/node_modules/@electric-sql/pglite/dist/index.js'));
const db = new PGlite();
const source = fs.readFileSync(new URL('../src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs', import.meta.url), 'utf8');
function queryFor(method) {
  return source.split(` ${method}(`).at(-1).split('const string sql = """')[1].split('""";')[0].replaceAll('@visible_project_ids', '$1::uuid[]');
}
const id = n => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
await db.exec(`
CREATE TABLE projects(project_id uuid, project_code text, project_name text, status text);
CREATE TABLE app_users(user_id uuid, display_name text, email text, team_name text, department_name text, department text);
CREATE TABLE project_tasks(task_id uuid, task_code text, task_name text);
CREATE TABLE project_assignments(project_assignment_id uuid, project_id uuid, user_id uuid, task_id uuid, assigned_hours numeric, effective_start_date date, effective_end_date date, allocation_percent numeric);
CREATE TABLE engineering_resource_requests(engineering_resource_request_id uuid, project_id uuid, request_status text);
CREATE TABLE engineering_resource_request_assignments(engineering_resource_request_id uuid, user_id uuid, allocated_hours numeric);
CREATE TABLE time_entries(user_id uuid, project_id uuid, task_id uuid, hours numeric, status text);
INSERT INTO projects VALUES ('${id(1)}','P1','Assigned work','active'), ('${id(2)}','P2','Outside scope','active');
INSERT INTO app_users VALUES ('${id(11)}','One','one@example.test','','',''), ('${id(12)}','Two','two@example.test','','','');
INSERT INTO project_tasks VALUES ('${id(21)}','PLAN','Plan'), ('${id(22)}','BUILD','Build');
INSERT INTO project_assignments VALUES
('${id(31)}','${id(1)}','${id(11)}','${id(21)}',4,CURRENT_DATE-1,NULL,100),
('${id(32)}','${id(1)}','${id(11)}','${id(22)}',0,CURRENT_DATE-1,NULL,100),
('${id(33)}','${id(1)}','${id(12)}','${id(21)}',5,CURRENT_DATE-1,NULL,100),
('${id(34)}','${id(2)}','${id(12)}','${id(21)}',999,CURRENT_DATE-1,NULL,100),
('${id(35)}','${id(1)}','${id(11)}','${id(22)}',100,CURRENT_DATE+1,NULL,100);
INSERT INTO engineering_resource_requests VALUES ('${id(41)}','${id(1)}','assigned');
INSERT INTO engineering_resource_request_assignments VALUES ('${id(41)}','${id(11)}',10);
INSERT INTO time_entries VALUES
('${id(11)}','${id(1)}','${id(21)}',3,'approved'),
('${id(11)}','${id(1)}','${id(21)}',99,'pm_declined'),
('${id(12)}','${id(1)}','${id(21)}',2,'submitted'),
('${id(13)}','${id(1)}',NULL,4,'approved'),
('${id(12)}','${id(2)}','${id(21)}',999,'approved');
`);
const assignments = (await db.query(queryFor('LoadAssignmentsAsync'), [[id(1)]])).rows;
assert.equal(assignments.length, 3, 'all current teammates, no future or outside-scope assignment');
assert.equal(assignments.reduce((n, row) => n + Number(row.assigned_hours), 0), 15, 'request fallback does not multiply or double count explicit allocations');
assert.equal(Number(assignments.find(row => row.id === id(31)).used_hours), 3, 'declined hours excluded');
assert.equal(Number(assignments.find(row => row.id === id(32)).assigned_hours), 6, 'only unallocated balance distributed to missing task allocation');
assert.equal((await db.query(queryFor('LoadAssignmentsAsync'), [[]])).rows.length, 0, 'empty authorized IDs fail closed');
const time = (await db.query(queryFor('LoadTeamHoursAsync'), [[id(1)]])).rows;
assert.equal(time.length, 3, 'former teammate/taskless usage included');
assert.equal(time.reduce((n, row) => n + Number(row.sum), 0), 9, 'project usage excludes declined and outside-scope time');
assert.equal((await db.query(queryFor('LoadTeamHoursAsync'), [[]])).rows.length, 0);
await db.close();
console.log('Module 019 PostgreSQL query scenarios: PASS (8 assertions)');
