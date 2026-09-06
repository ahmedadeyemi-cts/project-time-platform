"""Exercise exact candidate migration payload and entrypoint in disposable PostgreSQL.

The fixture refuses a database other than the CI-only identity. No Test/Production
app credentials are used, and all rows below are synthetic.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

root=Path(__file__).resolve().parents[1]
source=Path(os.environ['FLOWHIVE_CANDIDATE_ROOT']).resolve()
assert os.environ.get('PGHOST')=='127.0.0.1'
assert os.environ.get('PGDATABASE')=='flowhive_migrations_test'
assert os.environ.get('PGUSER')=='flowhive'
assert os.environ.get('GITHUB_ACTIONS')=='true'
approval=json.loads((root/'.github/flowhive-psa-protected-test-candidate.json').read_text())
assert subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()==approval['sha']


def sql(text,success=True):
    result=subprocess.run(['psql','-X','-At','-v','ON_ERROR_STOP=1'],input=text,text=True,capture_output=True)
    assert (result.returncode==0)==success, 'Migration behavior disagreed with expected transaction outcome.'
    return result.stdout.strip()


def block(file,start,end='\n);'):
    text=(source/file).read_text(); a=text.index(start); b=text.index(end,a)
    return text[a:b+len(end)]

sql('''CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE TABLE schema_migrations(migration_id TEXT PRIMARY KEY,description TEXT,applied_at TIMESTAMPTZ DEFAULT NOW());
CREATE TABLE projects(project_id UUID PRIMARY KEY);
CREATE TABLE app_users(user_id UUID PRIMARY KEY);
CREATE TABLE app_permissions(permission_code TEXT PRIMARY KEY,permission_name TEXT,module_code TEXT,permission_description TEXT);
CREATE TABLE project_flowhive_plans(plan_id UUID PRIMARY KEY);
CREATE TABLE project_notification_dispatches(project_notification_dispatch_id UUID PRIMARY KEY);
CREATE TABLE project_flowhive_customer_shares(share_id UUID PRIMARY KEY);
CREATE TABLE project_flowhive_raid_items(raid_item_id UUID PRIMARY KEY,project_id UUID NOT NULL REFERENCES projects,
    updated_by_user_id UUID NOT NULL REFERENCES app_users,title TEXT,status TEXT);
INSERT INTO schema_migrations(migration_id) VALUES('086_module_066_flowhive_enterprise_pm');
INSERT INTO projects VALUES('11111111-1111-4111-8111-111111111111');
INSERT INTO app_users VALUES('22222222-2222-4222-8222-222222222222');''')
sql(block('database/migrations/086_module_066_flowhive_enterprise_pm.sql','CREATE TABLE IF NOT EXISTS project_flowhive_working_copies ('))
sql(block('database/migrations/095_project_planning_collaboration_access.sql','CREATE TABLE IF NOT EXISTS project_flowhive_ai_planner_runs ('))
sql('''INSERT INTO project_flowhive_ai_planner_runs(run_id,project_id,status,phase,requested_plan,actual_actor_user_id,effective_actor_user_id)
 VALUES('33333333-3333-4333-8333-333333333333','11111111-1111-4111-8111-111111111111','generating','old-contract','{}',
 '22222222-2222-4222-8222-222222222222','22222222-2222-4222-8222-222222222222');''')
# This fixed directory is the image entrypoint's real layout, prepared by CI only.
payload=Path('/opt/projectpulse/release')
assert payload.is_dir()
(payload/'database/migrations').mkdir(parents=True,exist_ok=True)
checks=[]
for item in approval['migrations']:
    rel='database/migrations/'+item['file']; data=(source/rel).read_bytes()
    assert hashlib.sha256(data).hexdigest()==item['sha256']
    (payload/rel).write_bytes(data);checks.append(item['sha256']+'  '+rel)
entry=(root/'scripts/release-test/apply-flowhive-psa-migrations.sh').read_bytes()
(payload/'entrypoint.sh').write_bytes(entry)
checks.append(hashlib.sha256(entry).hexdigest()+'  entrypoint.sh')
(payload/'SHA256SUMS').write_text('\n'.join(checks)+'\n')
(payload/'release-commit').write_text(approval['sha']+'\n')
env={**os.environ,'MAIN_RELEASE_EXPECTED_RELEASE_COMMIT':approval['sha'],'MAIN_RELEASE_MIGRATION_MODE':'apply',
     'PROJECTPULSE_TEST_DATABASE_NAME':'flowhive_migrations_test'}
for attempt in range(2):
    subprocess.run(['bash',str(payload/'entrypoint.sh')],env=env,check=True)
assert sql("SELECT phase FROM project_flowhive_ai_planner_runs WHERE run_id='33333333-3333-4333-8333-333333333333'")=='execution_upgrade_required'
# Exact execution-image guards fail closed without modifying the database.
for field,value in [('MAIN_RELEASE_EXPECTED_RELEASE_COMMIT','0'*40),('PGDATABASE','not_the_test_database'),('MAIN_RELEASE_MIGRATION_MODE','rollback')]:
    result=subprocess.run(['bash',str(payload/'entrypoint.sh')],env={**env,field:value},capture_output=True)
    assert result.returncode!=0
sql('''INSERT INTO project_flowhive_raid_items VALUES('44444444-4444-4444-8444-444444444444',
'11111111-1111-4111-8111-111111111111','22222222-2222-4222-8222-222222222222','Synthetic RAID item','open');
UPDATE project_flowhive_raid_items SET status='resolved';
DELETE FROM project_flowhive_raid_items;''')
assert sql("SELECT string_agg(action_code,',' ORDER BY occurred_at) FROM project_flowhive_raid_events")=='created,updated,deleted'
for text in ["UPDATE project_flowhive_raid_events SET action_code='created'",'DELETE FROM project_flowhive_raid_events']:
    sql(text,success=False)
assert sql('SELECT count(*) FROM project_flowhive_raid_events')=='3'
sql((source/'database/rollback/103_module_066_flowhive_enterprise_psa_revamp_rollback.sql').read_text(),success=False)
assert sql('SELECT count(*) FROM project_flowhive_raid_events')=='3'
sql('''INSERT INTO project_flowhive_ai_planner_runs(run_id,project_id,status,phase,requested_plan,actual_actor_user_id,effective_actor_user_id,
execution_contract,deadline_at) VALUES('55555555-5555-4555-8555-555555555555','11111111-1111-4111-8111-111111111111','queued','queued','{}',
'22222222-2222-4222-8222-222222222222','22222222-2222-4222-8222-222222222222','flowhive-bounded-execution-v1-20260906',NOW()+INTERVAL '5 minutes');''')
for text in ["UPDATE project_flowhive_ai_planner_runs SET deadline_at=deadline_at+INTERVAL '1 hour' WHERE execution_contract<>''",
             "UPDATE project_flowhive_ai_planner_runs SET attempt_count=3 WHERE execution_contract<>''"]:
    sql(text,success=False)
sql((source/'database/rollback/104_flowhive_bounded_ai_execution_rollback.sql').read_text(),success=False)
subprocess.run(['bash',str(payload/'entrypoint.sh')],env={**env,'MAIN_RELEASE_MIGRATION_MODE':'verify'},check=True)
# Prove disabled execution fence and altered migration bytes are detected before acceptance.
sql('ALTER TABLE project_flowhive_ai_planner_runs DISABLE TRIGGER trg_flowhive_104_execution_fence;')
assert subprocess.run(['bash',str(payload/'entrypoint.sh')],env={**env,'MAIN_RELEASE_MIGRATION_MODE':'verify'},capture_output=True).returncode!=0
sql('ALTER TABLE project_flowhive_ai_planner_runs ENABLE TRIGGER trg_flowhive_104_execution_fence;')
(payload/'database/migrations'/approval['migrations'][0]['file']).write_text('-- corrupt payload\n')
assert subprocess.run(['bash',str(payload/'entrypoint.sh')],env=env,capture_output=True).returncode!=0
print('FLOWHIVE_PSA_MIGRATION_ENTRYPOINT_BEHAVIOR=PASS isolatedPostgres=true liveDeployment=false')
