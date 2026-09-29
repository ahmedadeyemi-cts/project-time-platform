import { readFileSync } from 'node:fs';
import { PGlite } from '@electric-sql/pglite';
import assert from 'node:assert/strict';
const db=await PGlite.create();
const policy=readFileSync(new URL('../../scripts/security/provision-runtime-database-role.sql',import.meta.url),'utf8');
let checks=0;
try {
 await db.exec(`CREATE TABLE app_users(id int); CREATE TABLE schema_migrations(id int);
 CREATE TABLE module025_sow_gsd_versions(id int); CREATE TABLE security_credential_transition_backups(id int);
 CREATE TABLE action_events(id int); CREATE FUNCTION block_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'immutable'; END $$;
 CREATE TRIGGER immutable_events BEFORE UPDATE OR DELETE ON action_events FOR EACH ROW EXECUTE FUNCTION block_audit_mutation();`);
 await db.exec(policy); await db.exec(policy);
 for (const [table,privilege,expected] of [['app_users','SELECT',true],['app_users','UPDATE',true],['app_users','TRUNCATE',false],['schema_migrations','SELECT',true],['schema_migrations','INSERT',false],['module025_sow_gsd_versions','INSERT',true],['module025_sow_gsd_versions','UPDATE',false],['action_events','DELETE',false],['security_credential_transition_backups','SELECT',false]]) {
  assert.equal((await db.query(`SELECT has_table_privilege('ptp_runtime','${table}','${privilege}') AS allowed`)).rows[0].allowed,expected);checks++;
 }
 assert.equal((await db.query("SELECT rolcanlogin OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls AS elevated FROM pg_roles WHERE rolname='ptp_runtime'")).rows[0].elevated,false);checks++;
 await db.exec('ALTER ROLE ptp_runtime CREATEROLE');
 await assert.rejects(()=>db.exec(policy));checks++;
 await db.exec('ROLLBACK');
 console.log(`SECURITY_RUNTIME_ROLE_POLICY=PASS assertions=${checks}`);
} finally { await db.close(); }
