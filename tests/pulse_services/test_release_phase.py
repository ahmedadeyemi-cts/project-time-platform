"""Real orchestration and retained workflow checks with synthetic cloud operations."""
import copy,hashlib,json,os,subprocess,sys,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
import yaml
ROOT=Path(__file__).resolve().parents[2];D=ROOT/'deployment/pulse-services'
sys.path.insert(0,str(D))
import release_phase as phase
import service_images as images
import cutover
BASE='6bf7c3303dec5f0aa136e52ce75bdd7b4b3b985f'
WORKFLOW='.github/workflows/projectpulse-deploy-test.yml'
START='      - name: Build private Pulse service images\n'
END='      - name: Restore exact prior Test images after application failure\n'

class Workflow(unittest.TestCase):
    def test_only_additive_activation_changes(self):
        current=(ROOT/WORKFLOW).read_text()
        original=subprocess.check_output(['git','show',BASE+':'+WORKFLOW],cwd=ROOT,text=True)
        self.assertEqual(current.count(START),1);self.assertEqual(current.count(END),1)
        lo=current.index(START);hi=current.index(END,lo)
        self.assertEqual(current[:lo]+current[hi:],original)
        for path in ['scripts/validate-deployment-concurrency-governance.mjs','.github/workflows/module025-protected-uat-control.yml']:
            self.assertEqual((ROOT/path).read_bytes(),subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT))
    def test_security_gates_precede_activation(self):
        a=yaml.safe_load((ROOT/WORKFLOW).read_text());job=a['jobs']['deploy']
        self.assertEqual(job['environment'],'test');self.assertEqual(a['concurrency']['queue'],'max');self.assertFalse(a['concurrency']['cancel-in-progress'])
        steps=job['steps'];names=[s.get('name','') for s in steps]
        ordered=['Verify normal Solution Architect browser and retained register','Build private Pulse service images']+[f'Scan private Pulse {c} image' for c in ('documents','scanner','laya-gateway','laya')]+['Publish scanned private Pulse service images','Activate and verify private Pulse document and Laya services']
        positions=[names.index(n) for n in ordered];self.assertEqual(positions,sorted(positions))
        for name in ordered[1:]:
            step=steps[names.index(name)]
            self.assertIn("inputs.release_branch == 'main'",step['if']);self.assertIn("inputs.acceptance_scope == 'full'",step['if']);self.assertIn('success()',step['if'])
        for step in steps:
            if step.get('name','').startswith('Scan private Pulse'):
                self.assertEqual(str(step['with']['exit-code']),'1');self.assertIn('CRITICAL',step['with']['severity']);self.assertRegex(step['uses'],r'@[0-9a-f]{40}$')
    def test_activation_only_publishes_safe_evidence(self):
        a=yaml.safe_load((ROOT/WORKFLOW).read_text());steps=a['jobs']['deploy']['steps']
        step=next(x for x in steps if x.get('name')=='Publish private Pulse service activation receipt')
        self.assertEqual(step['with']['path'],'${{ runner.temp }}/pulse-services-safe')
        self.assertNotIn('private',step['with']['path'].split('/')[-1])

class ScanBinding(unittest.TestCase):
    def valid(self):return {'SchemaVersion':2,'Metadata':{'ImageID':'sha256:'+64*'a'},'ArtifactType':'container_image','Results':[{'Target':'debian','Vulnerabilities':[]}]}
    def test_matching_report(self):self.assertTrue(images.scan_verified(self.valid(),'sha256:'+64*'a'))
    def test_missing_scan_report_or_wrong_image(self):
        for change in ['metadata','version','results','artifact']:
            r=self.valid()
            if change=='metadata':r['Metadata']['ImageID']='sha256:'+64*'b'
            elif change=='version':r['SchemaVersion']=1
            elif change=='results':r['Results']=[]
            else:r['ArtifactType']='filesystem'
            with self.subTest(change=change),self.assertRaises(ValueError):images.scan_verified(r,'sha256:'+64*'a')
    def test_fixable_high_and_critical_rejected(self):
        for severity in ['HIGH','CRITICAL']:
            r=self.valid();r['Results'][0]['Vulnerabilities']=[{'Severity':severity,'FixedVersion':'fixed'}]
            with self.assertRaises(ValueError):images.scan_verified(r,'sha256:'+64*'a')
    def test_repeat_selected_mode_is_explicit(self):
        for entries,expected in [([],False),([{'name':p+'MODE','value':'pulse_container'} for p in cutover.PREFIXES],True)]:
            with patch.object(images,'command',return_value=json.dumps(entries)):self.assertEqual(images.installed_selection(),expected)
        with patch.object(images,'command',return_value=json.dumps([{'name':cutover.PREFIXES[0]+'MODE','value':'pulse_container'}])):
            with self.assertRaises(ValueError):images.installed_selection()

class Orchestration(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        root=Path(self.tmp.name);self.safe=root/'safe';self.safe.mkdir();self.private=root/'private';self.private.mkdir()
        self.source='a'*40;self.run='123';self.fp='b'*64
        cutover.PRIVATE=self.private;cutover.SAFE=self.safe;cutover.SHA=self.source;cutover.RUN=self.run
        self.record={'source':self.source,'fingerprint':self.fp,'mode':'initial'}
        (self.safe/'build-identities.json').write_text(json.dumps(self.record))
        self.events=[]
    def mocks(self):
        return patch.object(phase,'initialize',return_value=(self.source,self.run,self.safe,self.private)),patch.object(phase,'fingerprint',return_value=self.fp)
    def test_success_runs_prepare_switch_and_cleanup_in_order(self):
        def switched():
            self.events.append('switch');(self.safe/'cutover.json').write_text(json.dumps({'status':'passed','sourceSha':self.source}))
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=False),patch.object(cutover,'prepare',side_effect=lambda:self.events.append('prepare')),patch.object(cutover,'switch',side_effect=switched),patch.object(phase,'remove_success_job',side_effect=lambda:self.events.append('cleanup')),patch.object(cutover,'rollback') as rollback:
            self.assertEqual(phase.execute(),0);rollback.assert_not_called()
        self.assertEqual(self.events,['prepare','switch','cleanup']);self.assertFalse(self.private.exists())
        self.assertTrue(json.loads((self.safe/'activation-receipt.json').read_text())['servicesActivated'])
    def test_failed_staging_never_switches_and_attempts_rollback(self):
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=False),patch.object(cutover,'prepare',side_effect=ValueError('native_acceptance_failed')),patch.object(cutover,'switch') as switch,patch.object(cutover,'rollback') as rollback:
            self.assertEqual(phase.execute(),1);switch.assert_not_called();rollback.assert_called_once()
        result=json.loads((self.safe/'activation-receipt.json').read_text());self.assertFalse(result['servicesActivated']);self.assertTrue(result['rollbackCompleted'])
    def test_switch_failure_does_not_report_success(self):
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=False),patch.object(cutover,'prepare'),patch.object(cutover,'switch',side_effect=ValueError('admin_check_failed')),patch.object(cutover,'rollback',side_effect=ValueError('restore_failed')):
            self.assertEqual(phase.execute(),1)
        result=json.loads((self.safe/'activation-receipt.json').read_text());self.assertEqual(result['status'],'failed');self.assertFalse(result['rollbackCompleted'])
    def test_untrusted_or_malformed_build_never_calls_cloud_mutation(self):
        for path in ['fingerprint','source']:
            self.record[path]='wrong';(self.safe/'build-identities.json').write_text(json.dumps(self.record))
            a,b=self.mocks()
            with a,b,patch.object(cutover,'prepare') as prepare,patch.object(cutover,'switch') as switch,patch.object(cutover,'rollback') as rollback:
                self.assertEqual(phase.execute(),1);prepare.assert_not_called();switch.assert_not_called();rollback.assert_not_called()
            if not self.private.exists():self.private.mkdir()
    def test_exception_text_is_not_exported(self):
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=False),patch.object(cutover,'prepare',side_effect=RuntimeError('SECRET_SENTINEL value')),patch.object(cutover,'rollback'):
            self.assertEqual(phase.execute(),1)
        self.assertNotIn('SECRET_SENTINEL',(self.safe/'activation-receipt.json').read_text())

if __name__=='__main__':unittest.main(verbosity=2)
