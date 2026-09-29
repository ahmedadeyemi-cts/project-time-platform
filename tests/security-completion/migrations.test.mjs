import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';
const require=createRequire(process.env.SECURITY_TEST_DEPENDENCIES || new URL('./package.json',import.meta.url));
const { PGlite }=require('@electric-sql/pglite');
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const fixture=path=>read(path).split("psql_exec <<'SQL'\n")[1].split('\nSQL')[0].replace('CREATE EXTENSION IF NOT EXISTS pgcrypto;','');
let checks=0;
async function scalar(db,sql){return Object.values((await db.query(sql)).rows[0])[0];}
{
 const db=await PGlite.create();
 await db.exec(fixture('tests/test-super-administrator-permanent-full-control-migration-062.sh'));
 await db.exec("UPDATE app_roles SET is_active=FALSE,is_system_role=FALSE WHERE role_code='ADMINISTRATOR'");
 const migration=read('database/migrations/062_super_administrator_permanent_full_control.sql');
 await db.exec(migration);
 await db.exec("UPDATE app_user_role_assignments SET is_active=FALSE WHERE user_id='10000000-0000-0000-0000-000000000001' AND app_role_id=(SELECT app_role_id FROM app_roles WHERE role_code='SUPER_ADMINISTRATOR')");
 await db.exec(migration);
 assert.equal(await scalar(db,"SELECT count(*)::int FROM app_user_role_assignments WHERE user_id='10000000-0000-0000-0000-000000000001' AND app_role_id=(SELECT app_role_id FROM app_roles WHERE role_code='SUPER_ADMINISTRATOR') AND is_active"),0);checks++;
 await db.exec("UPDATE app_users SET is_active=FALSE WHERE user_id='10000000-0000-0000-0000-000000000001'");
 await db.exec(migration);
 assert.equal(await scalar(db,"SELECT is_active FROM app_users WHERE user_id='10000000-0000-0000-0000-000000000001'"),false);checks++;
 await db.exec(read('database/rollback/062_super_administrator_permanent_full_control_rollback.sql'));
 assert.equal(await scalar(db,"SELECT is_active FROM app_roles WHERE role_code='ADMINISTRATOR'"),false);checks++;
 assert.equal(await scalar(db,"SELECT is_system_role FROM app_roles WHERE role_code='ADMINISTRATOR'"),false);checks++;
 await db.close();
}
{
 const db=await PGlite.create();
 await db.exec(fixture('tests/test-project-management-billing-role-access-migration-063.sh'));
 const migration=read('database/migrations/063_project_management_billing_role_access_repair.sql');
 await db.exec(migration);
 await db.exec("DELETE FROM app_role_permissions rp USING app_roles r,app_permissions p WHERE rp.app_role_id=r.app_role_id AND rp.app_permission_id=p.app_permission_id AND r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='APPROVE_TIME'");
 await db.exec(migration);
 assert.equal(await scalar(db,"SELECT count(*)::int FROM app_role_permissions rp JOIN app_roles r ON r.app_role_id=rp.app_role_id JOIN app_permissions p ON p.app_permission_id=rp.app_permission_id WHERE r.role_code='PROJECT_MANAGEMENT' AND p.permission_code='APPROVE_TIME'"),0);checks++;
 await db.close();
}
{
 const db=await PGlite.create();
 await db.exec(`CREATE TABLE app_users(user_id uuid PRIMARY KEY,is_active boolean); CREATE TABLE email_notification_outbox(id uuid);
 CREATE TABLE app_roles(app_role_id uuid,role_code text,is_active boolean); CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id uuid,is_active boolean); CREATE TABLE app_permissions(app_permission_id uuid,permission_code text);
 CREATE TABLE app_role_permissions(app_role_id uuid,app_permission_id uuid);
 CREATE TABLE schema_migrations(migration_id text PRIMARY KEY,description text);
 CREATE TABLE work_register_documents(work_register_document_id uuid,visibility text);
 CREATE TABLE project_intake_documents(project_intake_document_id uuid,work_register_document_id uuid,engineering_visible boolean,original_file_name text,document_type text,document_category text);
 CREATE FUNCTION projectpulse073_is_working_day(candidate date) RETURNS boolean LANGUAGE SQL AS 'SELECT extract(isodow from candidate)<6';
 INSERT INTO work_register_documents VALUES('10000000-0000-0000-0000-000000000001','ptc_admin_only');
 INSERT INTO project_intake_documents VALUES('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',TRUE,'private SOW.pdf','sow','sow');`);
 await db.exec(read('database/migrations/130_security_integrity_boundaries.sql'));
 await db.exec(read('scripts/security/verify-security-integrity-130.sql'));checks++;
 assert.equal(await scalar(db,'SELECT engineering_visible FROM project_intake_documents'),false);checks++;
 assert.equal(await scalar(db,'SELECT count(*)::int FROM security_integrity_repair_events'),1);checks++;
 await db.exec('UPDATE project_intake_documents SET engineering_visible=TRUE');
 await assert.rejects(()=>db.exec(read('scripts/security/verify-security-integrity-130.sql')));checks++;
 await db.exec('ROLLBACK');
 await db.exec(read('database/migrations/130_security_integrity_boundaries.sql'));
 await db.exec(read('scripts/security/verify-security-integrity-130.sql'));checks++;
 assert.equal(await scalar(db,'SELECT engineering_visible FROM project_intake_documents'),false);checks++;
 assert.equal(await scalar(db,'SELECT count(*)::int FROM security_integrity_repair_events'),1);checks++;
 await db.exec(`INSERT INTO app_users VALUES('30000000-0000-0000-0000-000000000001',TRUE),('30000000-0000-0000-0000-000000000002',TRUE);
 INSERT INTO app_roles VALUES('40000000-0000-0000-0000-000000000001','ACCOUNT_EXECUTIVE',TRUE);
 INSERT INTO app_user_role_assignments VALUES('30000000-0000-0000-0000-000000000001','40000000-0000-0000-0000-000000000001',TRUE);`);
 const stakeholder=id=>`SELECT projectpulse055d4d_get_or_create_stakeholder_user('${id}','ACCOUNT_EXECUTIVE','','','',NULL)`;
 assert.equal(await scalar(db,stakeholder('30000000-0000-0000-0000-000000000001')),'30000000-0000-0000-0000-000000000001');checks++;
 for(const input of ['Any Display Name','30000000-0000-0000-0000-000000000002']) { await assert.rejects(()=>db.exec(stakeholder(input)));checks++; }
 await db.exec("UPDATE app_user_role_assignments SET is_active=FALSE");
 await assert.rejects(()=>db.exec(stakeholder('30000000-0000-0000-0000-000000000001')));checks++;
 assert.equal(await scalar(db,'SELECT count(*)::int FROM app_user_role_assignments WHERE is_active'),0);checks++;
 assert.equal(await scalar(db,'SELECT count(*)::int FROM app_users'),2);checks++;
 assert.equal(await scalar(db,"SELECT projectpulse073_working_day_delta('2026-09-25','2026-09-28')"),1);checks++;
 assert.equal(await scalar(db,"SELECT projectpulse073_working_day_duration('2026-09-25','2026-09-28')"),2);checks++;
 for(const sql of ["SELECT projectpulse073_working_day_delta('2026-09-28','9999-12-31')","SELECT projectpulse073_working_day_duration('2026-09-28','9999-12-31')","SELECT projectpulse073_add_working_days('2026-09-28',2147483647)","SELECT projectpulse073_add_working_days('2026-09-28',-2147483648)"]){
  const start=Date.now();await assert.rejects(()=>db.exec(sql));assert.ok(Date.now()-start<1000,'Unbounded input must fail before looping');checks++;
 }
 await db.close();
}
console.log(`SECURITY_MIGRATION_REGRESSIONS=PASS assertions=${checks}`);
