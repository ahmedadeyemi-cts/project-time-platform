"""Offline command-boundary tests; no Azure credentials or network requests."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[1]
HELPER=ROOT/'scripts/release-test/azure-migration-throttle.sh'
AZ='''#!/usr/bin/env python3
import json,os,sys
from pathlib import Path
p=Path(os.environ['FIXTURE'])
a=json.loads((p/'responses').read_text())
c=p/'calls'
n=len(c.read_text().splitlines()) if c.exists() else 0
with c.open('a') as f:f.write(json.dumps(sys.argv[1:])+'\\n')
r=a[min(n,len(a)-1)]
print(r.get('out',''),end='')
print(r.get('err',''),file=sys.stderr,end='')
raise SystemExit(r.get('code',0))
'''
SLEEP='''#!/usr/bin/env python3
import os,sys
from pathlib import Path
with (Path(os.environ['FIXTURE'])/'sleeps').open('a') as f:f.write(sys.argv[1]+'\\n')
'''
class RetryTests(unittest.TestCase):
 def run_case(self,responses,args=None):
  with tempfile.TemporaryDirectory() as tmp:
   p=Path(tmp)
   for name,script in [('az',AZ),('sleep',SLEEP)]:
    (p/name).write_text(script);(p/name).chmod(0o700)
   (p/'responses').write_text(json.dumps(responses))
   env={**os.environ,'FIXTURE':tmp,'PATH':tmp+':'+os.environ['PATH']}
   command=args or ['api_read','az','containerapp','show','-g','synthetic','-n','test']
   result=subprocess.run(['bash','-c','source "$1"; shift; azure_migration_retry "$@"','test',str(HELPER),*command],env=env,text=True,capture_output=True,timeout=10)
   calls=[json.loads(x) for x in (p/'calls').read_text().splitlines()] if (p/'calls').exists() else []
   sleeps=(p/'sleeps').read_text().splitlines() if (p/'sleeps').exists() else []
   return result,calls,sleeps
 def test_success_output_and_arguments_preserved(self):
  r,c,s=self.run_case([{'out':'{"ok":true}'}]);self.assertEqual(r.returncode,0);self.assertEqual(r.stdout,'{"ok":true}');self.assertEqual(len(c),1);self.assertEqual(s,[])
 def test_throttled_read_retries_without_partial_output(self):
  r,c,s=self.run_case([{'code':1,'out':'partial','err':'ERROR: Too Many Requests'},{'out':'complete'}]);self.assertEqual(r.returncode,0);self.assertEqual(r.stdout,'complete');self.assertEqual(len(c),2);self.assertEqual(s,['15']);self.assertEqual(c[0],c[1])
 def test_exact_idempotent_put_is_eligible(self):
  r,c,s=self.run_case([{'code':1,'err':'(TooManyRequests)'},{'out':'done'}],['job_put','az','rest','--method','put','--uri','https://management.azure.com/synthetic','--body','@fixture']);self.assertEqual(r.returncode,0);self.assertEqual(len(c),2)
 def test_readiness_identity_token_and_list_operations(self):
  commands=[['account_read','az','account','show','--query','id'],['identity_read','az','identity','show','--ids','fixture'],['token_read','az','account','get-access-token','--query','accessToken'],['executions_read','az','containerapp','job','execution','list','-g','fixture']]
  for cmd in commands:
   with self.subTest(command=cmd):
    r,c,s=self.run_case([{'code':1,'err':'ERROR: TooManyRequests'},{'out':'secret-result'}],cmd);self.assertEqual(r.returncode,0);self.assertEqual(len(c),2);self.assertNotIn('secret-result',r.stderr)
 def test_four_attempt_limit(self):
  r,c,s=self.run_case([{'code':9,'err':'ERROR: Too Many Requests'}]);self.assertEqual(r.returncode,9);self.assertEqual(len(c),4);self.assertEqual(s,['15','30','60']);self.assertIn('THROTTLE_EXHAUSTED',r.stderr)
 def test_server_retry_after_is_respected(self):
  r,c,s=self.run_case([{'code':1,'err':'ERROR: TooManyRequests\nRetry-After: 45'},{'out':'ok'}]);self.assertEqual(r.returncode,0);self.assertEqual(s,['45'])
 def test_large_server_delay_fails_without_early_retry(self):
  for value in ['121','1200','99999999999999999999999999999999']:
   r,c,s=self.run_case([{'code':1,'err':'ERROR: TooManyRequests\nRetry-After: '+value}]);self.assertEqual(r.returncode,1);self.assertEqual(len(c),1);self.assertEqual(s,[])
 def test_permissions_authentication_and_ambiguous_failures_not_retried(self):
  for error in ['ERROR: Forbidden (403)','ERROR: Unauthorized (401)','ERROR: NotFound (404)','ERROR: connection reset','ERROR: generic 500']:
   r,c,s=self.run_case([{'code':7,'err':error}]);self.assertEqual(r.returncode,7);self.assertEqual(len(c),1);self.assertEqual(s,[])
 def test_non_idempotent_commands_are_rejected_before_execution(self):
  for args in [['job_put','az','rest','--method','post','--uri','fixture'],['api_read','az','containerapp','delete','--name','fixture'],['executions_read','az','containerapp','job','start','-n','fixture'],['start','az','containerapp','job','start']]:
   r,c,s=self.run_case([{'out':'must not execute'}],args);self.assertEqual(r.returncode,64);self.assertEqual(c,[]);self.assertEqual(s,[])
 def test_runner_preserves_start_cleanup_ownership_and_sql(self):
  source=(ROOT/'scripts/release-test/run-flowhive-authority-migration-094-job.sh').read_text()
  self.assertIn('EXECUTION_NAME="$(az containerapp job start',source)
  self.assertIn('az containerapp job delete',source)
  self.assertIn('validate_job_ownership "$JOB_RESPONSE"',source)
  self.assertIn('PREFLIGHT_CONFIRMED_404=1',source)
  self.assertIn('azure_migration_retry job_put az rest --method put',source)
  self.assertNotIn('azure_migration_retry job_start',source)
  subprocess.run(['bash','-n',str(HELPER)],check=True)
  subprocess.run(['bash','-n',str(ROOT/'scripts/release-test/run-flowhive-authority-migration-094-job.sh')],check=True)
if __name__=='__main__': unittest.main()
