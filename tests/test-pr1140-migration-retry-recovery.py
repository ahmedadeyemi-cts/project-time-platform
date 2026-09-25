"""Reuse the previously reviewed recovery negative cases for one new identity."""
import importlib.util
from pathlib import Path
import hashlib
import re
import unittest
ROOT=Path(__file__).resolve().parents[1]
def load(name,path):
 spec=importlib.util.spec_from_file_location(name,ROOT/path)
 module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module
previous=load('previous_recovery_tests','tests/test-pr1139-uat-recovery.py')
current=load('migration_retry_recovery','scripts/release-test/recover-pr1140-migration-retry-orphan.py')
class RecoveryTests(previous.RecoveryTests):
 def setUp(self):
  old=previous.recovery
  previous.recovery=current
  self.addCleanup(setattr,previous,'recovery',old)
 def test_supervisor_has_only_exact_addition(self):
  data=previous.subprocess.check_output(["git","show","436010a2838c94e7b94251fbdada16453b917dcf:"+current.SUPERVISOR],cwd=ROOT,text=True)
  pattern=r"^            # MIGRATION_RETRY_RECOVERY_BEGIN\n.*?^            # MIGRATION_RETRY_RECOVERY_END\n"
  blocks=re.findall(pattern,data,re.M|re.S)
  self.assertEqual(len(blocks),1)
  self.assertIn("35761573008",blocks[0])
  self.assertIn("|| fail",blocks[0])
  original=re.sub(pattern,"",data,flags=re.M|re.S).encode()
  digest=hashlib.sha1(b"blob "+str(len(original)).encode()+b"\0"+original).hexdigest()
  self.assertEqual(digest,'f80eda11ed5da1fe81b2785567bf96e114691a6f')
 def test_no_alternate_deployment_or_force_operation(self):
  text=(ROOT/'scripts/release-test/recover-pr1140-migration-retry-orphan.py').read_text()
  for prohibited in ('"/force-cancel"','"/dispatches"','"DELETE"','"approve"'):
   self.assertNotIn(prohibited,text)
 def test_legacy_scope_accepts_only_the_exact_successor_additions(self):
  scope=load('legacy_scope','tests/pr1140-uat-recovery-scope.py')
  for anchor,addition,marker,extension in [
   (scope.SUPERVISOR_ANCHOR,scope.SUPERVISOR_ADDITION,'            # PR1140_UAT_RECOVERY_END\n','            # MIGRATION_RETRY_RECOVERY_BEGIN\n            if [[ "$run_id" == \'35761573008\' ]]; then\n              python3 scripts/release-test/recover-pr1140-migration-retry-orphan.py \\\n                || fail \'Migration retry orphan recovery did not meet its exact safety contract.\'\n              quarantined_runs+=("$run_id")\n              continue\n            fi\n            # MIGRATION_RETRY_RECOVERY_END\n'),
   (scope.REGISTRY_ANCHOR,scope.REGISTRY_ADDITION,'# PR1140_RECOVERY_SCOPE_BEGIN\n','# MIGRATION_THROTTLE_SCOPE_BEGIN\nif [[ "$HEAD_BRANCH" == fix/uat-migration-throttle-recovery-20260922 ]]; then\n  python3 tests/uat-migration-throttle-scope.py\n  python3 tests/test-azure-migration-throttle.py\n  python3 tests/test-pr1140-migration-retry-recovery.py\n  node tests/validate-systemwide-image-build-controller.mjs\n  return\nfi\n# MIGRATION_THROTTLE_SCOPE_END\n')]:
   before='before\n'+anchor+'after\n'
   legacy=before.replace(anchor,addition+anchor,1)
   expected=legacy.replace(marker,extension+marker,1)
   scope.verify_insertion(before,legacy,anchor,addition)
   scope.verify_insertion(before,expected,anchor,addition)
   for bad in (expected+"# unrelated\n",expected.replace(extension,extension*2),expected.replace('35761573008','35761573009') if '35761573008' in expected else expected.replace('python3 tests/uat-migration','true tests/uat-migration')):
    with self.assertRaises(AssertionError):scope.verify_insertion(before,bad,anchor,addition)
 def test_new_pin_does_not_reuse_old_release(self):
  self.assertEqual(current.RUN_ID,35761573008)
  self.assertEqual(current.OLD_SHA,'e81b458f77c6af114c06585475386568a060450d')
  self.assertEqual(current.CREATED,'2026-09-22T17:34:19Z')
if __name__=='__main__':unittest.main()
