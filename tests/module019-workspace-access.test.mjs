import assert from 'node:assert/strict';
import fs from 'node:fs';
import { pathToFileURL } from 'node:url';
const { PGlite } = await import(pathToFileURL(process.env.MODULE019_PGLITE_PATH || '/tmp/module019-validation/node_modules/@electric-sql/pglite/dist/index.js'));
const db = new PGlite();
const source = fs.readFileSync(new URL('../src/backend/ProjectTime.Api/Modules/ProjectWorkspaceModule019Repair.cs', import.meta.url), 'utf8');
const id = n => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
// Execute the actual list/download SQL, including all authorization predicates.
async function run(method, access, extra = {}) {
  const values = { user_id: id(11), team_name: '', department_name: '', is_broad_scope: false, can_view_managed_projects: false, can_view_team_scope: false, hide_closed_projects: true, ...access, ...extra };
  const keys = [];
  const query = source.split(` ${method}(`).at(-1).split('const string sql = """')[1].split('""";')[0].replace(/@(\w+)/g, (_, name) => {
    assert.ok(name in values, `Missing SQL fixture parameter ${name}`);
    if (!keys.includes(name)) keys.push(name);
    const type = typeof values[name] === 'boolean' ? 'boolean' : name.endsWith('_id') ? 'uuid' : 'text';
    return `$${keys.indexOf(name) + 1}::${type}`;
  });
  return (await db.query(query, keys.map(key => values[key]))).rows;
}
await db.exec(`
CREATE TABLE app_users(user_id uuid, display_name text, email text, team_name text, department_name text, department text, is_active boolean DEFAULT TRUE);
CREATE TABLE projectpulse_team_scope_assignments(scoped_user_id uuid, team_name text, department_name text, is_active boolean);
CREATE TABLE clients(client_id uuid, client_name text);
CREATE TABLE projects(project_id uuid, project_code text, project_name text, status text DEFAULT 'active', start_date date, end_date date, billable boolean DEFAULT TRUE, client_id uuid, project_manager_user_id uuid, account_executive_user_id uuid, solution_architect_user_id uuid, created_at timestamp DEFAULT NOW());
CREATE TABLE project_tasks(task_id uuid, project_id uuid, is_active boolean);
CREATE TABLE project_assignments(project_assignment_id uuid, project_id uuid, user_id uuid, effective_start_date date, effective_end_date date);
CREATE TABLE engineering_resource_requests(engineering_resource_request_id uuid, project_id uuid, project_intake_request_id uuid, request_number text, request_status text DEFAULT 'assigned', fulfilled_by_user_id uuid, assigned_pm_user_id uuid, requested_function text DEFAULT 'Build', requested_hours numeric DEFAULT 8, priority text DEFAULT 'normal', created_at timestamp DEFAULT NOW());
CREATE TABLE engineering_resource_request_assignments(engineering_resource_request_id uuid, user_id uuid);
CREATE TABLE project_intake_requests(project_intake_request_id uuid, request_number text, request_title text, assigned_pm_user_id uuid);
CREATE TABLE project_intake_documents(project_intake_document_id uuid, project_id uuid, project_intake_request_id uuid, document_type text DEFAULT 'sow', document_category text DEFAULT 'sow', original_file_name text DEFAULT 'SOW.docx', content_type text DEFAULT 'application/octet-stream', size_bytes bigint DEFAULT 4, engineering_visible boolean DEFAULT FALSE, ai_timesheet_context_enabled boolean DEFAULT FALSE, extraction_status text DEFAULT 'not_started', upload_source text DEFAULT 'manual', uploaded_at timestamp DEFAULT NOW(), is_active boolean DEFAULT TRUE, storage_path text DEFAULT 'intake/SOW.docx');
INSERT INTO app_users(user_id,display_name,email,team_name) VALUES
('${id(11)}','Engineer A','a@example.test','A'), ('${id(12)}','Engineer B','b@example.test','B'),
('${id(14)}','Project Manager','pm@example.test','B'), ('${id(15)}','Lead A','lead@example.test','A'),
('${id(16)}','Other lead','other@example.test','C');
INSERT INTO projectpulse_team_scope_assignments VALUES ('${id(15)}','B',NULL,TRUE), ('${id(16)}','B',NULL,FALSE);
INSERT INTO projects(project_id,project_code,project_name,project_manager_user_id) VALUES
('${id(1)}','P1','Direct project',NULL), ('${id(2)}','P2','Other team project',NULL),
('${id(3)}','P3','Closed project',NULL), ('${id(4)}','P4','Managed project','${id(14)}'),
('${id(5)}','P5','Future assignment',NULL), ('${id(6)}','P6','Expired assignment',NULL), ('${id(7)}','P7','Cancelled request',NULL);
UPDATE projects SET status='closed' WHERE project_id='${id(3)}';
INSERT INTO project_assignments VALUES
('${id(31)}','${id(1)}','${id(11)}',CURRENT_DATE-1,NULL),
('${id(32)}','${id(2)}','${id(12)}',CURRENT_DATE-1,NULL),
('${id(33)}','${id(3)}','${id(11)}',CURRENT_DATE-1,NULL),
('${id(35)}','${id(5)}','${id(11)}',CURRENT_DATE+1,NULL),
('${id(36)}','${id(6)}','${id(11)}',CURRENT_DATE-10,CURRENT_DATE-1);
INSERT INTO project_intake_requests VALUES ('${id(52)}','INT-52','Linked intake',NULL), ('${id(53)}','INT-53','Standalone intake',NULL);
INSERT INTO engineering_resource_requests(engineering_resource_request_id,project_id,project_intake_request_id,request_number,fulfilled_by_user_id,request_status) VALUES
('${id(41)}','${id(2)}','${id(52)}','SR-41','${id(12)}','assigned'),
('${id(42)}',NULL,'${id(53)}','SR-42','${id(11)}','assigned'),
('${id(43)}','${id(7)}',NULL,'SR-43','${id(11)}','cancelled');
INSERT INTO project_intake_documents(project_intake_document_id,project_id,project_intake_request_id,engineering_visible) VALUES
('${id(61)}','${id(1)}',NULL,FALSE), ('${id(62)}','${id(2)}',NULL,TRUE),
('${id(63)}','${id(2)}',NULL,FALSE), ('${id(64)}',NULL,'${id(52)}',TRUE),
('${id(65)}',NULL,'${id(53)}',FALSE), ('${id(66)}','${id(3)}',NULL,TRUE),
('${id(67)}','${id(4)}',NULL,TRUE), ('${id(68)}','${id(1)}',NULL,TRUE),
('${id(69)}','${id(1)}',NULL,TRUE);
UPDATE project_intake_documents SET is_active=FALSE WHERE project_intake_document_id='${id(68)}';
UPDATE project_intake_documents SET upload_source='celar_ai_chat_attachment' WHERE project_intake_document_id='${id(69)}';
`);
const ids = rows => rows.map(row => row.id).sort();
const cases = [
  ['Engineer, direct project/request', { user_id: id(11), team_name: 'A' }, [1], [61,65]],
  ['Engineer, other team', { user_id: id(12), team_name: 'B' }, [2], [62,63,64]],
  ['Lead, own and additional teams', { user_id: id(15), team_name: 'A', can_view_team_scope: true }, [1,2,4], [62,64,67]],
  ['Manager, department/team scope', { user_id: id(16), team_name: 'B', can_view_team_scope: true }, [2,4], [62,64,67]],
  ['Lead, inactive additional team', { user_id: id(16), team_name: 'C', can_view_team_scope: true }, [], []],
  ['Project manager, managed scope', { user_id: id(14), can_view_managed_projects: true, hide_closed_projects: false }, [4], [67]],
  ['Unassigned engineer', { user_id: id(16), team_name: 'B' }, [], []],
  ['Administrator/coordinator/executive broad scope', { user_id: id(16), is_broad_scope: true, can_view_managed_projects: true, can_view_team_scope: true, hide_closed_projects: false }, [1,2,4,5,6,7], [61,62,63,64,65,66,67]]
];
let assertions = 0;
for (const [label, access, projects, documents] of cases) {
  assert.deepEqual(ids(await run('LoadProjectsAsync', access)), projects.map(id).sort(), `${label}: projects`); assertions++;
  assert.deepEqual(ids(await run('LoadDocumentsAsync', access)), documents.map(id).sort(), `${label}: document list`); assertions++;
  for (let n=61; n<=69; n++) {
    assert.equal((await run('DownloadDocumentAsync', access, { document_id: id(n) })).length, documents.includes(n) ? 1 : 0, `${label}: download ${n} must match list permission`); assertions++;
  }
}
const leadRequests = await run('LoadResourceRequestsAsync', { user_id: id(15), team_name: 'A', can_view_team_scope: true });
assert.ok(leadRequests.some(row => row.request_number === 'SR-41'), 'additional-team request is visible'); assertions++;
assert.equal((await run('LoadResourceRequestsAsync', { user_id: id(16), team_name: 'C', can_view_team_scope: true })).length, 0, 'inactive scope grants no requests'); assertions++;
// Metadata must remain complete beyond the old 100-project / 250-record cutoffs.
await db.exec(`
INSERT INTO projects(project_id,project_code,project_name) SELECT lpad(n::text,32,'0')::uuid, 'BULK-'||n, 'Large portfolio' FROM generate_series(1000,1100) n;
INSERT INTO project_intake_documents(project_intake_document_id,project_id) SELECT lpad(n::text,32,'0')::uuid, '${id(1)}'::uuid FROM generate_series(2000,2250) n;
INSERT INTO engineering_resource_requests(engineering_resource_request_id,request_number,fulfilled_by_user_id) SELECT lpad(n::text,32,'0')::uuid, 'SR-'||n, '${id(11)}'::uuid FROM generate_series(3000,3250) n;
`);
assert.equal((await run('LoadProjectsAsync', { is_broad_scope: true })).length, 107); assertions++;
assert.equal((await run('LoadDocumentsAsync', { user_id: id(11) })).length, 253); assertions++;
assert.equal((await run('LoadResourceRequestsAsync', { user_id: id(11) })).length, 253); assertions++;
await db.close();
console.log(`Module 019 PostgreSQL access scenarios: PASS (${assertions} assertions; scoped lists, matching download permission, larger portfolios)`);
