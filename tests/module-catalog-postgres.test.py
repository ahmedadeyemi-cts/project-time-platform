#!/usr/bin/env python3
"""Real PostgreSQL reconciliation: preservation, new-module denial, idempotence, atomic conflict."""
import json, os, subprocess, tempfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
assert os.environ.get('PGDATABASE','').startswith('module_catalog_test'), 'Use a dedicated module_catalog_test database'
def sql(text,ok=True):
    r=subprocess.run(['psql','-X','-qAt','-v','ON_ERROR_STOP=1'],input=text,text=True,capture_output=True)
    if ok and r.returncode: raise AssertionError(r.stderr)
    if not ok: assert r.returncode and 'MODULE_CATALOG_ROUTE_CONFLICT' in r.stderr,r.stderr
    return r.stdout.strip()
foundation=(root/'database/migrations/040_scoped_role_policy_versions/00_schema.sql').read_text()
foundation=foundation[foundation.index('CREATE TABLE IF NOT EXISTS scoped_role_policy_modules'):foundation.index('CREATE TABLE IF NOT EXISTS scoped_approval_stage_events')]
sql('''CREATE TABLE schema_migrations(migration_id text primary key,description text,applied_at timestamptz);
CREATE TABLE app_users(user_id uuid primary key);
CREATE TABLE app_roles(role_code text primary key,is_active boolean);
INSERT INTO app_roles VALUES('SOLUTION_ARCHITECT',true),('ENGINEER',true),('ENGINEERING',true),('ADMINISTRATOR',true),('SUPER_ADMINISTRATOR',true);
'''+foundation+'''
ALTER TABLE scoped_role_policy_modules ADD COLUMN owner_user_id uuid, ADD COLUMN owner_revision_number integer NOT NULL DEFAULT 0;
INSERT INTO scoped_role_policy_actions(action_code,action_description) VALUES('MODULE_ACCESS','Access');
INSERT INTO scoped_role_policy_scopes(scope_code,scope_description) VALUES('ORGANIZATION','All');
INSERT INTO scoped_role_policy_modules(module_code,module_name,route_scope,current_state,permission_notes,is_active,owner_user_id,owner_revision_number) VALUES
('025','SOW Generator + Claude Review Workflow','sow-generator','Installed','preserve note',true,'11111111-1111-1111-1111-111111111111',7),
('024','Retired name','sales-intake','Retired','preserve retirement',false,NULL,0),
('006','Legacy title','psa-modules','Installed','preserve source',true,NULL,0),
('900','Customer custom module','custom-route','Installed','custom',true,NULL,0);
INSERT INTO scoped_role_policy_versions(policy_version_id,version_number,policy_name,policy_status,source_name,source_sha256)
VALUES('22222222-2222-2222-2222-222222222222',7,'fixture','PUBLISHED','fixture','fixture');
INSERT INTO scoped_role_policy_grants(policy_version_id,role_code,module_code,action_code,scope_code,grant_effect,source_designation,conditions)
SELECT policy_version_id,'SOLUTION_ARCHITECT','025','MODULE_ACCESS','ORGANIZATION','DENY','No Access','{"keep":{"nested":true}}'::jsonb FROM scoped_role_policy_versions;
''')
with tempfile.TemporaryDirectory() as td:
    target=Path(td)/'catalog.sql'
    subprocess.run(['node','scripts/release-test/reconcile-module-catalog.mjs','a'*40,str(target)],cwd=root,check=True)
    migration=target.read_text()
    original=sql("SELECT to_jsonb(g)-'scoped_role_policy_grant_id'-'policy_version_id'-'created_at' FROM scoped_role_policy_grants g")
    sql(migration)
    assert sql("SELECT module_name FROM scoped_role_policy_modules WHERE module_code='025'")=='SOW & GSD Workspace'
    assert sql("SELECT owner_revision_number||'|'||permission_notes||'|'||is_active FROM scoped_role_policy_modules WHERE module_code='025'")=='7|preserve note|true'
    assert sql("SELECT current_state||'|'||is_active FROM scoped_role_policy_modules WHERE module_code='024'")=='Retired|false'
    assert sql("SELECT module_name||'|'||route_scope FROM scoped_role_policy_modules WHERE module_code='900'")=='Customer custom module|custom-route'
    assert sql("SELECT route_scope FROM scoped_role_policy_modules WHERE module_code='006'")=='toyota-hyundai-pipelines'
    assert sql("SELECT to_jsonb(g)-'scoped_role_policy_grant_id'-'policy_version_id'-'created_at' FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND module_code='025'")==original
    assert sql("SELECT is_active FROM scoped_role_policy_modules WHERE module_code='001'")=='f'
    assert sql("SELECT count(*) FROM scoped_role_policy_grants g JOIN scoped_role_policy_versions v USING(policy_version_id) WHERE v.policy_status='PUBLISHED' AND module_code='001' AND grant_effect='DENY'")=='2'
    assert sql("SELECT count(*) FROM scoped_role_policy_grants WHERE role_code IN ('SUPER_ADMINISTRATOR','ADMINISTRATOR','ENGINEER')")=='0'
    counts=sql('SELECT (SELECT count(*) FROM scoped_role_policy_audit_events)||\'|\'||(SELECT count(*) FROM scoped_role_policy_versions)')
    sql(migration)
    assert sql('SELECT (SELECT count(*) FROM scoped_role_policy_audit_events)||\'|\'||(SELECT count(*) FROM scoped_role_policy_versions)')==counts
    sql("UPDATE scoped_role_policy_modules SET route_scope='unrecognized-route',module_name='Unchanged on failure' WHERE module_code='025';")
    sql(migration,ok=False)
    assert sql("SELECT module_name FROM scoped_role_policy_modules WHERE module_code='025'")=='Unchanged on failure'
    sql("UPDATE scoped_role_policy_modules SET route_scope='sow-generator' WHERE module_code='025'; UPDATE scoped_role_policy_modules SET route_scope='sow-generator' WHERE module_code='900';")
    sql(migration,ok=False)
    assert sql('SELECT (SELECT count(*) FROM scoped_role_policy_audit_events)||\'|\'||(SELECT count(*) FROM scoped_role_policy_versions)')==counts
print('MODULE_CATALOG_POSTGRES=PASS preservation default_deny idempotence retired custom collision_rollback')
