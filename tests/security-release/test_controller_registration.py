"""Only evidence publication changes; execution and authorization remain identical."""
from pathlib import Path
from copy import deepcopy
import hashlib
import importlib.util
import json
import os
import subprocess
import unittest
from unittest.mock import patch
import yaml

ROOT = Path(__file__).resolve().parents[2]
CONTROLLER = '.github/workflows/projectpulse-deploy-test.yml'
BASE = 'a562371a0bbed74e881c40a4c246928dac9ec72e'
REGISTRATION = json.loads((ROOT/'tests/security-release/controller_registration.json').read_text())

def load(name):
    spec=importlib.util.spec_from_file_location(name, ROOT/'scripts/release-test'/name)
    result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result);return result

def old_bytes():
    return subprocess.check_output(['git','show',BASE+':'+CONTROLLER],cwd=ROOT)

def verify_publication_only(current):
    original=yaml.safe_load(old_bytes()); secured=yaml.safe_load(current)
    steps=secured['jobs']['deploy']['steps']
    preparation=[s for s in steps if s.get('id')=='safe_uat_evidence']
    assert len(preparation)==1
    prepare=preparation[0]
    assert prepare['working-directory']=="${{ inputs.release_branch == 'main' && 'release' || 'control' }}"
    assert prepare['shell']=='bash'
    assert prepare['run']=='python3 scripts/security/publish-safe-uat-evidence.py "$EVIDENCE_DIR" "$RUNNER_TEMP/publish-safe-uat"'
    upload=next(s for s in steps if s.get('name')=='Upload protected-Test deployment evidence')
    before=next(s for s in original['jobs']['deploy']['steps'] if s.get('name')==upload['name'])
    expected=deepcopy(before)
    expected['if']="${{ (inputs.qualification_provider == '' || inputs.qualification_provider == 'none') && (always() && steps.safe_uat_evidence.outcome == 'success') }}"
    expected['with']['path']='${{ runner.temp }}/publish-safe-uat/*.json'
    expected['with']['retention-days']=3
    assert upload==expected
    assert prepare['if']==before['if']
    steps.remove(prepare);steps[steps.index(upload)]=before
    assert secured==original, 'Execution, permission, concurrency or authorization changed'

class ControllerTests(unittest.TestCase):
    def test_exact_registered_workflow_and_evidence_only_delta(self):
        data=(ROOT/CONTROLLER).read_bytes()
        self.assertEqual(hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest(), REGISTRATION['controllerBlob'])
        self.assertEqual(hashlib.sha256(data).hexdigest(),REGISTRATION['controllerSha256'])
        verify_publication_only(data)
    def test_gate_and_source_changes_rejected(self):
        data=(ROOT/CONTROLLER).read_text()
        for old,new in [('environment: test','environment: production'),('cancel-in-progress: false','cancel-in-progress: true'),("== 'refs/heads/main'","!= 'refs/heads/main'"),('timeout-minutes: 240','timeout-minutes: 241')]:
            self.assertIn(old,data)
            with self.assertRaises(AssertionError): verify_publication_only(data.replace(old,new,1))
    def test_registrations_change_only_literal_content_digest_checks(self):
        expected={f'scripts/release-test/{n}' for n in ('recover-pr1139-uat-orphan.py','recover-pr1140-uat-orphan.py','recover-pr1140-migration-retry-orphan.py','verify-pr1151-uat-supersession.py','verify-pr1204-uat-supersession.py','verify-module025-quarantine-controller.py')}
        self.assertEqual(set(REGISTRATION['patches']),expected)
        for file,edits in REGISTRATION['patches'].items():
            old=subprocess.check_output(['git','show',BASE+':'+file],cwd=ROOT,text=True)
            for before,after in edits:
                self.assertEqual(old.count(before),1);old=old.replace(before,after,1)
            self.assertEqual((ROOT/file).read_text(),old)
    def test_all_five_recovery_contexts_accept_only_exact_current_controller(self):
        current='b'*40
        for file in REGISTRATION['patches']:
            if file.endswith('verify-module025-quarantine-controller.py'): continue
            m=load(Path(file).name)
            class Api:
                def read(self,*_): return {'object':{'sha':current}}
            env={'GITHUB_REPOSITORY':m.REPOSITORY,'GITHUB_REF':'refs/heads/main','GITHUB_EVENT_NAME':'push','GITHUB_SHA':current,'GITHUB_WORKFLOW_REF':f'{m.REPOSITORY}/{m.SUPERVISOR}@refs/heads/main'}
            for blob in [REGISTRATION['controllerBlob'],'0'*40]:
                def git(*args):
                    if args==('rev-parse','HEAD'): return current
                    if args==('rev-parse',f'{current}:{m.DEPLOYMENT}'): return blob
                    if args[0]=='rev-parse': return m.DEPLOYMENT_BLOB
                    return ''
                with patch.dict(os.environ,env,clear=True),patch.object(m,'git',side_effect=git):
                    if blob==REGISTRATION['controllerBlob']: self.assertEqual(m.verify_context(Api()),current)
                    else:
                        with self.assertRaises(RuntimeError): m.verify_context(Api())
    def test_known_quarantine_base_and_byte_changes(self):
        m=load('verify-module025-quarantine-controller.py')
        current=(ROOT/CONTROLLER).read_bytes()
        self.assertTrue(m.permitted(old_bytes(),current))
        self.assertFalse(m.permitted(b'unknown base',current))
        self.assertFalse(m.permitted(old_bytes(),current+b'\n'))

if __name__=='__main__': unittest.main()
