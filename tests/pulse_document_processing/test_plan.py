"""Read-only scope, container topology and ID-only closure-ledger tests."""
import copy,hashlib,json,re,subprocess,unittest
from pathlib import Path
import yaml
ROOT=Path(__file__).resolve().parents[2]
BASE='044d261140c1d64d864104c0a8c1a5563b5c5e0c'
SERVICE=ROOT/'deployment/pulse-document-processing'
REGISTER=ROOT/'docs/security/security-closeout-register-20260930.json'
WORKFLOW='.github/workflows/pulse-document-containers-ci.yml'
DOCS={'docs/security/security-closeout-plan-20260930.md','docs/security/security-closeout-register-20260930.json','docs/security/foundry-transition-plan-20260930.md'}
def scope_ok(paths):
    for p in paths:
        if '..' in Path(p).parts or Path(p).is_absolute(): raise ValueError('Unsafe path')
        if p not in DOCS|{WORKFLOW} and not p.startswith(('deployment/pulse-document-processing/','tests/pulse_document_processing/')):
            raise ValueError('Existing application/account/release authority must remain unchanged')
def validate_register(a):
    assert a['preserveLocalSuperAdministrator'] is True
    assert a['newAzureVmRequired'] is False and a['productionChangesAuthorized'] is False
    assert a['defaultStatus']=='pending_evidence'
    rows=a['originalFindings']; ids=[r[1] for r in rows]
    assert len(rows)==149 and len(set(ids))==149
    assert [int(r[0]) for r in rows]==list(range(1,150))
    assert hashlib.sha256('\n'.join(ids).encode()).hexdigest()=='d66eee2d47c3c4502e13830f51be90ca30325f5d70875f476c23c3729ce7cb05'
    assert all(len(r)==4 and re.fullmatch(r'\d{7}',r[1]) and r[3] in {f'W{i:02}' for i in range(1,8)} for r in rows)
    assert {r['id'] for r in a['additionalLocations']}=={'A001','A002'}
    for key,e in a['findingEvidence'].items():
        assert key in ids and isinstance(e,dict) and e.get('implementationPrs')
        assert e.get('environment') in {'test','production'}
        assert re.fullmatch('[0-9a-f]{40}',e.get('sourceSha',''))
        assert re.fullmatch('sha256:[0-9a-f]{64}',e.get('imageDigest',''))
        assert e.get('positiveReceipt') and e.get('negativeReceipt') and e.get('residualScope') is not None
    for key,e in a['securityDispositions'].items():
        assert key in a['findingEvidence'] and e.get('reviewReference') and e.get('decision')
class PlanTests(unittest.TestCase):
    def test_source_boundary(self):
        paths=subprocess.check_output(['git','diff','--name-only',BASE],cwd=ROOT,text=True).splitlines()
        paths+=subprocess.check_output(['git','ls-files','--others','--exclude-standard'],cwd=ROOT,text=True).splitlines()
        self.assertTrue(paths);scope_ok(paths)
    def test_rejects_existing_authority_changes(self):
        for p in ['src/backend/ProjectTime.Api/Program.cs','database/migrations/new.sql','.github/workflows/projectpulse-deploy-test.yml','deployment/oracle-celar/gateway/gateway.py','../escape']:
            with self.subTest(path=p),self.assertRaises(ValueError):scope_ok([p])
    def test_complete_inventory_no_closure(self):
        a=json.loads(REGISTER.read_text());validate_register(a)
        self.assertEqual(a['findingEvidence'],{});self.assertEqual(a['securityDispositions'],{})
        severities=[r[2] for r in a['originalFindings']]
        self.assertEqual({s:severities.count(s) for s in set(severities)},{'CRITICAL':3,'HIGH':65,'MEDIUM':52,'LOW':29})
    def test_invalid_closure_inventory_and_account_changes_rejected(self):
        a=json.loads(REGISTER.read_text())
        for kind in ['missing','duplicate','evidence','closure','account']:
            b=copy.deepcopy(a)
            if kind=='missing':b['originalFindings'].pop()
            if kind=='duplicate':b['originalFindings'][1][1]=b['originalFindings'][0][1]
            if kind=='evidence':b['findingEvidence']['4758971']={}
            if kind=='closure':b['securityDispositions']['4758971']={'decision':'closed'}
            if kind=='account':b['preserveLocalSuperAdministrator']=False
            with self.subTest(kind=kind),self.assertRaises(AssertionError):validate_register(b)
    def test_private_nonroot_opt_in_topology(self):
        a=yaml.safe_load((SERVICE/'compose.yml').read_text())
        for s in a['services'].values():
            self.assertEqual(s['profiles'],['pulse-documents']);self.assertEqual(s['user'],'65534:65534')
            self.assertTrue(s['read_only']);self.assertEqual(s['cap_drop'],['ALL'])
            self.assertFalse(s.get('ports'));self.assertFalse(s.get('privileged'))
            self.assertNotEqual(s.get('network_mode'),'host');self.assertNotIn('docker.sock',str(s))
            self.assertEqual(len(s['tmpfs']),1);self.assertIn('noexec,nosuid,nodev',s['tmpfs'][0])
        self.assertTrue(a['networks']['documents_private']['internal'])
        self.assertEqual(a['services']['clamav']['network_mode'],'none')
        self.assertNotIn('document_service_token',str(a['services']['signature_updater']))
        self.assertNotIn('clamav_signatures',str(a['services']['documents']['volumes']))
    def test_scanner_limits(self):
        s=(SERVICE/'clamd.conf').read_text();self.assertNotRegex(s,r'(?m)^TCP')
        for v in ['AlertExceedsMax yes','AlertEncrypted yes','LocalSocketMode 660','StreamMaxLength 32M']:self.assertIn(v,s)
        self.assertIn('TestDatabases yes',(SERVICE/'freshclam.conf').read_text())
    def test_pinned_bases_nonroot_images(self):
        for name in ['Dockerfile.worker','Dockerfile.clamav']:
            s=(SERVICE/name).read_text();self.assertRegex(s,r'FROM debian:[\w-]+@sha256:[0-9a-f]{64}')
            self.assertIn('USER 65534:65534',s);self.assertNotIn('COPY . ',s)
    def test_ci_cannot_deploy(self):
        s=(ROOT/WORKFLOW).read_text();a=yaml.safe_load(s)
        self.assertEqual(a['permissions'],{'contents':'read'})
        for forbidden in ['pull_request_target','id-token:','environment:','secrets.','az login','docker push','gh release']:self.assertNotIn(forbidden,s)
        for job in a['jobs'].values():
            for step in job['steps']:
                if 'uses' in step:self.assertRegex(step['uses'],r'@[0-9a-f]{40}$')
if __name__=='__main__':unittest.main(verbosity=2)
