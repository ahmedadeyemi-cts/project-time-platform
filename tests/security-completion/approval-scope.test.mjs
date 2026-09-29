import { readFileSync } from 'node:fs';
import { PGlite } from '@electric-sql/pglite';
import assert from 'node:assert/strict';
const source=readFileSync(new URL('../../src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs',import.meta.url),'utf8');
const start=source.indexOf('private static async Task<List<TimeApprovalItem>> LoadTimeApprovalsAsync');
const sql=source.slice(start).split('new NpgsqlCommand("""')[1].split('""", connection)')[0];
const db=await PGlite.create();
const id=n=>`10000000-0000-0000-0000-${String(n).padStart(12,'0')}`;
let checks=0;
try {
 await db.exec(`CREATE TABLE app_users(user_id uuid,display_name text,email text,manager_email text);
 CREATE TABLE projects(project_id uuid,project_manager_user_id uuid,project_coordinator_user_id uuid,project_code text,project_name text);
 CREATE TABLE timesheet_day_statuses(timesheet_id uuid,user_id uuid,work_date date,status text,submitted_at timestamptz,manager_decision_comment text);
 CREATE TABLE non_project_time_categories(non_project_time_category_id uuid,category_code text,category_name text);
 CREATE TABLE time_entries(time_entry_id uuid,timesheet_id uuid,work_date date,time_type text,hours numeric,description text,project_id uuid,non_project_time_category_id uuid,created_at timestamptz);
 INSERT INTO app_users VALUES('${id(1)}','Engineer','engineer@example.invalid','manager@example.invalid');
 INSERT INTO projects VALUES('${id(10)}','${id(2)}',NULL,'OWN','Owned project'),('${id(11)}','${id(3)}',NULL,'OTHER','Unrelated project');
 INSERT INTO non_project_time_categories VALUES('${id(20)}','PTO','Private leave');
 INSERT INTO timesheet_day_statuses VALUES('${id(30)}','${id(1)}','2026-09-29','submitted',NOW(),'');
 INSERT INTO time_entries VALUES
 ('${id(40)}','${id(30)}','2026-09-29','normal',2,'Owned description','${id(10)}',NULL,NOW()),
 ('${id(41)}','${id(30)}','2026-09-29','normal',5,'Confidential other project','${id(11)}',NULL,NOW()),
 ('${id(42)}','${id(30)}','2026-09-29','normal',1,'Private leave',NULL,'${id(20)}',NOW());`);
 async function query(overrides={}) {
  const values={actor_user_id:id(2),actor_email:'pm@example.invalid',can_view_all:false,is_manager:false,is_project_manager:true,date_from:'2026-09-29',date_to:'2026-09-29',include_all:false,search:'',...overrides};
  const names=[];const text=sql.replace(/@([a-z_]+)/g,(_,name)=>{if(!names.includes(name))names.push(name);return '$'+(names.indexOf(name)+1);});
  return (await db.query(text,names.map(n=>values[n]),{rowMode:'array'})).rows;
 }
 const pm=await query(); assert.equal(pm.length,1);checks++;
 // Duplicate aggregate column names are intentional in the production SQL;
 // use the positional result rather than a lossy object mapping.
 assert.equal(Number(pm[0][9]),2);checks++;
 assert.equal(Number(pm[0][10]),1);checks++;
 const details=JSON.parse(pm[0][15]);assert.equal(details.length,1);assert.equal(details[0].projectId,id(10));checks++;
 assert.equal((await query({search:'OTHER'})).length,0);checks++;
 assert.equal((await query({actor_user_id:id(99)})).length,0);checks++;
 for(const access of [{can_view_all:true},{is_manager:true,actor_email:'MANAGER@example.invalid'}]) {
  const rows=await query(access);assert.equal(Number(rows[0][9]),8);assert.equal(JSON.parse(rows[0][15]).length,3);checks++;
 }
 console.log(`SECURITY_APPROVAL_SCOPE=PASS assertions=${checks}`);
} finally {await db.close();}
