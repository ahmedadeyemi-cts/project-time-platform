"""Source and deployment safety checks; no credentials or cloud operations."""
import ast,hashlib,importlib.util,json,re,subprocess,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];D=ROOT/'deployment/pulse-services'
class Contracts(unittest.TestCase):
    def test_pinned_checkpoint_and_weights(self):
        m=json.loads((D/'laya-model.json').read_text())
        self.assertEqual(m['revision'],'1c5edc17a7acd8701df6fc341c0d179f1c62c982')
        self.assertEqual({x['path'] for x in m['files']},{'model.safetensors','rl_agent_config.json','encoder/config.json','tokenizer/tokenizer.json','tokenizer/tokenizer_config.json'})
        for x in m['files']:
            self.assertRegex(x['sha256'],r'^[0-9a-f]{64}$');self.assertGreater(x['size'],0)
            self.assertNotIn('..',Path(x['path']).parts);self.assertFalse(x['path'].endswith(('.py','.pkl','.bin')))
    def test_fully_pinned_cpu_dependencies(self):
        s=(D/'requirements.lock').read_text()
        rows=[x for x in s.splitlines() if x and not x.startswith('#')]
        self.assertGreater(len(rows),20)
        for row in rows:self.assertRegex(row,r' --hash=sha256:[0-9a-f]{64}$')
        self.assertIn('2.14.0%2Bcpu',s);self.assertNotIn('nvidia-',s)
    def test_rootless_images_and_no_Oracle_client(self):
        for n in ['documents','scanner','laya','laya-gateway']:
            s=(D/f'Dockerfile.{n}').read_text()
            for line in s.splitlines():
                if line.startswith('FROM '):self.assertRegex(line,r'@sha256:[0-9a-f]{64}')
            self.assertIn('USER 65534:65534',s)
            self.assertNotIn('docker.sock',s)
        self.assertNotIn('model.safetensors',(D/'Dockerfile.laya-gateway').read_text())
    def test_real_model_runtime_not_heuristic(self):
        s=(D/'laya_worker.py').read_text()
        self.assertIn('laya.load(str(model_dir), device="cpu")',s)
        self.assertIn('result = self.agent.predict(state, QUESTIONS)',s)
        self.assertIn('harden(\'laya\')',s)
        self.assertNotIn('NOTIFY_SOCKET',s)
        self.assertIn('input_exceeds_model_budget',s);self.assertIn('"automation_approved": False',s)
        self.assertIn('"HF_HUB_OFFLINE": "1"',s)
    def test_existing_account_and_db_code_is_unchanged(self):
        changed=subprocess.check_output(['git','diff','--name-only','c8ac122653b2d948343727815dfc3279d98c9cc6'],cwd=ROOT,text=True).splitlines()
        self.assertFalse(any(p.startswith('database/') for p in changed))
        self.assertFalse(any(any(x in p for x in ('PasswordReset','LocalAccount','Session','SecurityHardeningModule')) for p in changed))
        spec=importlib.util.spec_from_file_location('reviewed_order_projection',ROOT/'tests/pulse-activation-release/controller.py')
        projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
        expected=subprocess.check_output(['git','show','6bf7c3303dec5f0aa136e52ce75bdd7b4b3b985f:.github/workflows/projectpulse-deploy-test.yml'],cwd=ROOT)
        self.assertEqual(projection.normalize((ROOT/'.github/workflows/projectpulse-deploy-test.yml').read_bytes()),expected)
        self.assertNotIn('.github/workflows/projectpulse-deploy-production.yml',changed)
        # PR1226 already changed queueing and passed installed Protected UAT.
        # Preserve those exact reviewed bytes rather than treating that inherited
        # repair as a mutation from this service PR. Unknown future edits fail.
        supervisor='.github/workflows/module025-protected-uat-control.yml'
        reviewed=subprocess.check_output(['git','show','ab9f39d3d1ccf33f7bffec779300a8bc900db629:'+supervisor],cwd=ROOT)
        self.assertEqual((ROOT/supervisor).read_bytes(),reviewed)
    def test_actual_Laya_contract_unchanged(self):
        names=['src/backend/ProjectTime.Api/Ai/LayaDecisionContract.cs','src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs','src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs','src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs','src/backend/ProjectTime.Api/Ai/LayaWorkerLease.cs']
        for p in names:
            expected=subprocess.check_output(['git','show','c8ac122653b2d948343727815dfc3279d98c9cc6:'+p],cwd=ROOT)
            self.assertEqual((ROOT/p).read_bytes(),expected)
    def test_classification_gateway_keeps_all_validation(self):
        self.assertEqual((D/'laya_protocol.py').read_bytes(),(ROOT/'deployment/oracle-celar/gateway/laya_decisions.py').read_bytes())
    def test_kernel_controls_cannot_be_opted_out(self):
        s=(D/'sandbox.c').read_text()
        for marker in ['geteuid()==0','PR_SET_NO_NEW_PRIVS','abi<3','SYS_landlock_restrict_self','SCMP_A0(SCMP_CMP_NE,AF_UNIX)']:self.assertIn(marker,s)
        self.assertNotIn('getenv(',s)
        self.assertIn('if(updater && path_rule(fd,"/var/lib/clamav",writes))',s)
        self.assertNotIn('if(scanner && path_rule(fd,"/var/lib/clamav",writes))',s)
if __name__=='__main__':unittest.main(verbosity=2)
