import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const { PGlite } = await import(process.env.PGLITE_MODULE || '@electric-sql/pglite');
const db = new PGlite();
const file = path => readFile(new URL('../' + path, import.meta.url), 'utf8');
const source = await file('src/backend/ProjectTime.Api/Modules/ProductionApprovalWorkModule.cs');
const migration = await file('database/migrations/132_time_approval_routing.sql');
await db.exec(migration.slice(migration.indexOf('CREATE OR REPLACE FUNCTION'), migration.indexOf('CREATE OR REPLACE VIEW')));
const id = n => `10000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
const engineer = id(1), manager = id(2), pm = id(3), pc = id(4), ptc = id(5), otherPm = id(6), sheet = id(10);
let checks = 0;
async function requires(entry, project, task = {}) {
 return (await db.query('SELECT time_requires_project_approval($1::jsonb,$2::jsonb,$3::jsonb) AS required', [JSON.stringify(entry),JSON.stringify(project),JSON.stringify(task)])).rows[0].required;
}
const project = {project_id:id(20),work_type:'Project',project_code:'PRJ-100',project_manager_user_id:pm};
for (const [name, e, p, t, expected] of [
 ['project with PM',{user_id:engineer},project,{},true],
 ['coordinator only',{user_id:engineer},{...project,project_manager_user_id:null,project_coordinator_user_id:pc},{},true],
 ['no reviewer',{user_id:engineer},{...project,project_manager_user_id:null},{},false],
 ['PM own time',{user_id:pm},project,{},false],
 ['PC own time',{user_id:pc},{...project,project_manager_user_id:null,project_coordinator_user_id:pc},{},false],
 ['nonproject',{user_id:engineer},{},{},false],
 ...['Service Request','Internal Task','Presales Task','Internal Project','Presales'].map(work_type=>[work_type,{user_id:engineer},{...project,work_type},{},false]),
 ...['SR-4','INT-4','PRES-4'].map(project_code=>[project_code,{user_id:engineer},{...project,project_code},{},false]),
 ...['service_request_task','internal_task','presales_task'].map(work_task_category=>[work_task_category,{user_id:engineer},project,{work_task_category},false]),
 ['SR task number',{user_id:engineer},project,{service_request_number:'SR-5'},false],
 ['SR entry reference',{user_id:engineer,service_request_id:id(90)},project,{},false]
]) { assert.equal(await requires(e,p,t),expected,name); checks++; }
await db.exec(`
 CREATE TABLE app_users(user_id uuid PRIMARY KEY,display_name text,email text,manager_email text);
 CREATE TABLE projects(project_id uuid PRIMARY KEY,project_manager_user_id uuid,project_coordinator_user_id uuid,project_code text,project_name text,work_type text);
 CREATE TABLE project_tasks(task_id uuid PRIMARY KEY,work_task_category text,service_request_number text);
 CREATE TABLE timesheets(timesheet_id uuid PRIMARY KEY,week_start_date date,week_end_date date);
 CREATE TABLE timesheet_day_statuses(timesheet_id uuid,user_id uuid,work_date date,status text,submitted_at timestamptz,updated_at timestamptz,created_at timestamptz);
 CREATE TABLE time_entries(time_entry_id uuid PRIMARY KEY,timesheet_id uuid,user_id uuid,work_date date,hours numeric,status text,project_id uuid,task_id uuid,updated_at timestamptz);
 INSERT INTO app_users VALUES('${engineer}','Engineer','engineer@example.invalid','manager@example.invalid'),('${pm}','PM','pm@example.invalid','manager@example.invalid');
 INSERT INTO projects VALUES('${id(20)}','${pm}',NULL,'PRJ-100','PM project','Project'),('${id(21)}',NULL,'${pc}','PRJ-101','PC project','Project'),('${id(22)}','${otherPm}',NULL,'PRJ-102','Other project','Project'),('${id(23)}','${pm}',NULL,'SR-100','Service request','Service Request');
 INSERT INTO timesheets VALUES('${sheet}','2026-09-27','2026-10-03');
 INSERT INTO timesheet_day_statuses VALUES('${sheet}','${engineer}','2026-09-30','submitted',NOW(),NOW(),NOW()),('${sheet}','${engineer}','2026-10-01','submitted',NOW(),NOW(),NOW());
 INSERT INTO time_entries VALUES
 ('${id(30)}','${sheet}','${engineer}','2026-09-30',2,'submitted','${id(20)}',NULL,NOW()),
 ('${id(31)}','${sheet}','${engineer}','2026-09-30',3,'submitted','${id(21)}',NULL,NOW()),
 ('${id(32)}','${sheet}','${engineer}','2026-09-30',4,'submitted','${id(22)}',NULL,NOW()),
 ('${id(33)}','${sheet}','${engineer}','2026-09-30',1,'submitted','${id(23)}',NULL,NOW()),
 ('${id(34)}','${sheet}','${engineer}','2026-09-30',1,'submitted',NULL,NULL,NOW()),
 ('${id(35)}','${sheet}','${engineer}','2026-10-01',2,'submitted','${id(20)}',NULL,NOW());
`);
function sql(method, ordinal = 0) {
 const body = source.slice(source.indexOf(`private static async Task${method}`));
 return [...body.matchAll(/new NpgsqlCommand\("""([\s\S]*?)""", connection/g)][ordinal][1];
}
async function query(text, values) {
 const names=[];
 const bound=text.replace(/@([a-z_]+)/g,(_,name)=>{if(!names.includes(name))names.push(name);return `$${names.indexOf(name)+1}`+(name==='week_start'||name==='week_end'?'::date':'');});
 return (await db.query(bound,names.map(name=>{assert.ok(name in values,`Missing parameter ${name}`);return values[name];}))).rows;
}
const pendingSql=sql('<List<ApprovalWorkItem>> LoadCandidatesAsync');
const defaults={stage_filter:'',week_start:null,week_end:null,effective_user_id:manager,actor_email:'manager@example.invalid',organization_scope:false,is_manager:true,is_project_manager:false,can_manager_approve:true,can_project_approve:false,can_ptc_final_approve:false};
const pending = overrides => query(pendingSql,{...defaults,...overrides});
assert.equal((await pending({week_start:'2026-09-01',week_end:'2026-09-30'})).length,1);checks++;
assert.equal((await pending({week_start:'2026-09-27',week_end:'2026-10-03'})).length,2);checks++;
assert.equal((await pending({effective_user_id:engineer,organization_scope:true})).length,0);checks++;
assert.equal((await pending({actor_email:'other-manager@example.invalid'})).length,0);checks++;
const pmAccess={effective_user_id:pm,is_manager:false,is_project_manager:true,can_manager_approve:false,can_project_approve:true};
assert.equal((await pending(pmAccess)).length,0,'PM cannot precede Manager');checks++;
await db.exec("UPDATE time_entries SET status='manager_approved'; UPDATE timesheet_day_statuses SET status='manager_approved'");
const pmRows=await pending(pmAccess);
assert.equal(pmRows.length,2);assert.ok(pmRows.every(row=>row.project_id===id(20)));checks++;
const tokenBefore = pmRows[0].review_token;
await db.exec(`UPDATE time_entries SET hours=2.5 WHERE time_entry_id='${id(30)}'`);
assert.notEqual((await pending(pmAccess))[0].review_token, tokenBefore, 'Editing reviewed hours invalidates the selection token'); checks++;
await db.exec(`UPDATE time_entries SET hours=2 WHERE time_entry_id='${id(30)}'`);
assert.equal(Number(pmRows[0].total_hours),2,'PM never sees mixed-day SR/leave/other projects');checks++;
const pcRows=await pending({...pmAccess,effective_user_id:pc});assert.equal(pcRows.length,1);assert.equal(pcRows[0].project_id,id(21));checks++;
assert.equal((await pending({...pmAccess,effective_user_id:id(999)})).length,0);checks++;
assert.equal((await pending({...pmAccess,effective_user_id:engineer,organization_scope:true})).length,0,'PTC/admin own-time exclusion');checks++;
const ptcAccess={effective_user_id:ptc,organization_scope:true,can_manager_approve:true,can_project_approve:true,can_ptc_final_approve:true};
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='pm').length,4);checks++;
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='ptc').length,0,'Cannot finalize with any required project outstanding');checks++;
// Execute the write-path lock/select SQL, not a test-only recreation of authorization.
const lockSql=sql('<bool> CompleteProjectManagerItemAsync');
const writeParams={timesheet_id:sheet,work_date:'2026-09-30',project_id:id(20),organization_scope:false,effective_user_id:pm};
assert.equal((await query(lockSql,writeParams)).length,1);checks++;
assert.equal((await query(lockSql,{...writeParams,project_id:id(21)})).length,0);checks++;
assert.equal((await query(lockSql,{...writeParams,effective_user_id:engineer,organization_scope:true})).length,0);checks++;
await db.exec(`UPDATE time_entries SET status='pm_approved' WHERE time_entry_id='${id(30)}'`);
assert.equal((await query(lockSql,writeParams)).length,0,'Replay cannot approve same scope twice');checks++;
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='ptc').length,0);checks++;
await db.exec("UPDATE time_entries SET status='pm_approved' WHERE project_id IS NOT NULL AND project_id <> '"+id(23)+"'");
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='ptc').length,2,'Completed required scopes release mixed days');checks++;
// The assigned PM's own time is manager-only; another project on that day still needs its reviewer.
await db.exec(`UPDATE timesheet_day_statuses SET user_id='${pm}'; UPDATE time_entries SET user_id='${pm}',status='manager_approved'`);
assert.equal((await pending({...pmAccess,effective_user_id:pm})).length,0);checks++;
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='pm'&&row.project_id===id(20)).length,0);checks++;
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='pm').length,2);checks++;
// Notifications must target the outstanding assigned reviewer, including PC-only projects.
await db.exec(`ALTER TABLE app_users ADD COLUMN is_active boolean DEFAULT true;
 INSERT INTO app_users(user_id,display_name,email) VALUES('${pc}','Coordinator','pc@example.invalid'),('${otherPm}','Other PM','otherpm@example.invalid');`);
const recipientSource = await file('src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs');
const notificationSql = recipientSource.slice(recipientSource.indexOf('private static async Task AddTimesheetProjectApproversAsync')).match(/new NpgsqlCommand\("""([\s\S]*?)""", connection/)[1];
const recipients = await query(notificationSql,{timesheet_id:sheet,work_date:'2026-09-30'});
assert.deepEqual(recipients.map(row=>row.user_id).sort(),[pc,otherPm].sort());checks++;
await db.exec(`UPDATE app_users SET is_active=false WHERE user_id='${pc}'`);
assert.equal((await query(notificationSql,{timesheet_id:sheet,work_date:'2026-09-30'})).length,1);checks++;
// Draft/returned time cannot slip through an accounting release of a mixed day.
await db.exec(`UPDATE time_entries SET status='draft' WHERE time_entry_id='${id(34)}'`);
assert.equal((await pending(ptcAccess)).filter(row=>row.stage==='ptc' && row.work_date==='2026-09-30').length,0);checks++;
console.log(`PASS ${checks} approval routing/scope SQL scenarios`);
await db.close();
