"""Real orchestration and retained workflow checks with synthetic cloud operations."""
import copy,hashlib,json,os,subprocess,sys,tempfile,unittest,importlib.util
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
        spec=importlib.util.spec_from_file_location('reviewed_order_projection',ROOT/'tests/pulse-activation-release/controller.py')
        projection=importlib.util.module_from_spec(spec);spec.loader.exec_module(projection)
        self.assertEqual(projection.normalize(current).decode(),original)
        current_pr_base=subprocess.check_output(['git','merge-base','origin/main','HEAD'],cwd=ROOT,text=True).strip()
        for path in ['scripts/validate-deployment-concurrency-governance.mjs','.github/workflows/module025-protected-uat-control.yml']:
            # Do not fail on reviewed main changes after the historical Laya cutover.
            # Still reject any edit to these protected controls in this PR.
            self.assertEqual((ROOT/path).read_bytes(),subprocess.check_output(['git','show',current_pr_base+':'+path],cwd=ROOT))
    def test_feature_merge_is_pinned_without_requesting_extra_token_permission(self):
        source=(D/'cutover.py').read_text()
        self.assertIn("'492d991c38fb87237beb570284403aee792e446c',SHA",source)
        self.assertNotIn("gh('pulls/",source)
        subprocess.run(['git','merge-base','--is-ancestor','492d991c38fb87237beb570284403aee792e446c','HEAD'],cwd=ROOT,check=True)
    def test_ci_database_waits_for_final_tcp_server(self):
        path='.github/workflows/celar-laya-integration.yml'
        current=(ROOT/path).read_text()
        original=subprocess.check_output(['git','show',BASE+':'+path],cwd=ROOT,text=True)
        old='pg_isready -U postgres -d laya_ci'
        new='pg_isready -h 127.0.0.1 -U postgres -d laya_ci'
        self.assertEqual(original.count(old),1)
        self.assertEqual(current,original.replace(old,new,1))
        self.assertIn('psql -v ON_ERROR_STOP=1 -f deployment/laya/schema.sql',current)
    def test_security_gates_precede_activation(self):
        a=yaml.safe_load((ROOT/WORKFLOW).read_text());job=a['jobs']['deploy']
        self.assertEqual(job['environment'],'test');self.assertEqual(a['concurrency']['queue'],'max');self.assertFalse(a['concurrency']['cancel-in-progress'])
        steps=job['steps'];names=[s.get('name','') for s in steps]
        ordered=['Seal server-confirmed deployment identity','Build private Pulse service images']+[f'Scan private Pulse {c} image' for c in ('documents','scanner','laya-gateway','laya')]+['Publish scanned private Pulse service images','Activate and verify private Pulse document and Laya services']
        positions=[names.index(n) for n in ordered];self.assertEqual(positions,sorted(positions))
        for name in ordered[1:]:
            step=steps[names.index(name)]
            self.assertIn("inputs.release_branch == 'main'",step['if']);self.assertIn("inputs.acceptance_scope == 'full'",step['if']);self.assertIn('success()',step['if'])
        self.assertLess(names.index('Activate and verify private Pulse document and Laya services'),names.index('Run protected-Test authenticated functional UAT'))
        self.assertLess(names.index('Verify normal Solution Architect browser and retained register'),names.index('Finalize private Pulse activation after full application acceptance'))
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
    def test_reviewed_service_lineage_is_upgrade_eligible_but_runtime_change_is_not_control_compatible(self):
        installed={'managedBy':'pulse-services-reviewed-cutover',
            'source':'55ac38319fa05b2c72f4744bd8c01ed5844f8099',
            'serviceFingerprint':'da52f4615c09abd20510c4856a8eee271f61e1d6cc6688dc52a20101f51b39ee'}
        self.assertTrue(images.reviewed_installed_source(installed))
        self.assertFalse(images.reviewed_control_only_compatible(installed,images.fingerprint()))
        self.assertFalse(images.reviewed_installed_source(installed|{'serviceFingerprint':'0'*64}))
        self.assertFalse(images.reviewed_installed_source(installed|{'source':'not-a-commit'}))

    def test_control_only_compatibility_allowlist_never_contains_runtime_image_inputs(self):
        docker_inputs=set()
        for dockerfile in D.glob('Dockerfile.*'):
            for line in dockerfile.read_text().splitlines():
                if not line.startswith('COPY '): continue
                for token in line.split()[1:]:
                    if token.startswith('--') or token.startswith('/'): continue
                    if token.startswith('deployment/'): docker_inputs.add(token)
        self.assertFalse(images.CONTROL_ONLY_COMPATIBLE_PATHS & docker_inputs)
        self.assertEqual(images.CONTROL_ONLY_COMPATIBLE_PATHS,{
            'deployment/pulse-services/post_activation_acceptance.py',
            'deployment/pulse-services/service_images.py'})
    def test_selected_reviewed_services_with_runtime_change_build_upgrade_images(self):
        with tempfile.TemporaryDirectory() as tmp:
            safe=Path(tmp)/'pulse-services-safe';safe.mkdir()
            source='a'*40;fp='b'*64
            old={c:images.ACR+'/pulse-services-'+c+'@sha256:'+'c'*64 for c in images.COMPONENTS}
            inspected={c:{'Id':'sha256:'+hashlib.sha256(c.encode()).hexdigest(),
                'Config':{'User':'65534:65534','Labels':{'org.opencontainers.image.revision':source}}}
                for c in images.COMPONENTS}
            calls=[]
            def existing(_fp,*,allow_upgrade=False):
                if not allow_upgrade: raise ValueError('service_upgrade_requires_review')
                return old
            with patch.object(images,'context',return_value=(source,safe)),patch.object(images,'fingerprint',return_value=fp),                 patch.object(images,'installed_selection',return_value=True),patch.object(images,'existing_images',side_effect=existing),                 patch.object(images,'command',side_effect=lambda args:(calls.append(args) or '')),                 patch.object(images,'inspect',side_effect=lambda c:inspected[c]):
                images.build()
            receipt=json.loads((safe/'build-identities.json').read_text())
            self.assertEqual(receipt['mode'],'upgrade_existing')
            self.assertEqual(receipt['installedImages'],old)
            self.assertEqual(len([c for c in calls if c[:2]==['docker','build']]),4)
            self.assertFalse(any(c[:2]==['docker','pull'] for c in calls))

    def test_all_private_service_runtimes_require_fixed_pcre_revision(self):
        for name in ('documents','scanner','laya-gateway','laya'):
            with self.subTest(image=name):
                docker=(D/('Dockerfile.'+name)).read_text()
                self.assertIn('libpcre2-8-0',docker)
                self.assertIn("10.42-1+deb12u2",docker)
                self.assertIn('dpkg --compare-versions',docker)

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
    def test_native_success_retains_rollback_until_full_application_uat(self):
        def switched():
            self.events.append('switch');(self.safe/'cutover.json').write_text(json.dumps({'status':'passed','sourceSha':self.source}))
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=False),patch.object(cutover,'prepare',side_effect=lambda:self.events.append('prepare')),patch.object(cutover,'switch',side_effect=switched),patch.object(phase,'remove_success_job',side_effect=lambda:self.events.append('cleanup')),patch.object(cutover,'rollback') as rollback:
            self.assertEqual(phase.execute(),0);rollback.assert_not_called()
        self.assertEqual(self.events,['prepare','switch']);self.assertTrue(self.private.exists())
        receipt=json.loads((self.safe/'activation-receipt.json').read_text())
        self.assertEqual(receipt['status'],'pending_application_uat');self.assertFalse(receipt['applicationUatPassed'])
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
    def test_reviewed_upgrade_uses_upgrade_path_without_initial_cutover(self):
        self.record['mode']='upgrade_existing';self.record['installedImages']={}
        (self.safe/'build-identities.json').write_text(json.dumps(self.record))
        (self.safe/'registry-images.json').write_text('{}')
        def upgraded():
            (self.safe/'cutover.json').write_text(json.dumps({'status':'passed','sourceSha':self.source,'serviceUpgrade':True}))
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=True),patch.object(cutover,'upgrade',side_effect=upgraded) as upgrade,             patch.object(cutover,'prepare') as prepare,patch.object(cutover,'switch') as switch,patch.object(cutover,'rollback') as rollback:
            self.assertEqual(phase.execute(),0)
        upgrade.assert_called_once();prepare.assert_not_called();switch.assert_not_called();rollback.assert_not_called()
        receipt=json.loads((self.safe/'activation-receipt.json').read_text())
        self.assertEqual(receipt['mode'],'upgrade_existing');self.assertTrue(receipt['servicesActivated'])
        self.assertEqual(receipt['status'],'pending_application_uat')

    def test_failed_reviewed_upgrade_attempts_exact_rollback(self):
        self.record['mode']='upgrade_existing';self.record['installedImages']={}
        (self.safe/'build-identities.json').write_text(json.dumps(self.record))
        a,b=self.mocks()
        with a,b,patch.object(phase,'installed_selection',return_value=True),             patch.object(cutover,'upgrade',side_effect=cutover.CutoverError('private_service_acceptance_failed')),             patch.object(cutover,'rollback') as rollback:
            self.assertEqual(phase.execute(),1)
        rollback.assert_called_once()

class RegistryPublication(unittest.TestCase):
    def manifest(self, identity):
        return {'schemaVersion': 2, 'mediaType':'application/vnd.docker.distribution.manifest.v2+json',
          'config': {'digest':identity,'size':9000,'mediaType':'application/vnd.docker.container.image.v1+json'},
          'layers':[{'digest':'sha256:'+'d'*64,'size':100,'mediaType':'application/vnd.docker.image.rootfs.diff.tar.gzip'}]}
    def test_publication_binds_registry_manifest_without_unpacking_images_again(self):
        with tempfile.TemporaryDirectory() as tmp:
            safe=Path(tmp);source='a'*40;fp='b'*64
            identities={c:'sha256:'+hashlib.sha256(c.encode()).hexdigest() for c in images.COMPONENTS}
            (safe/'build-identities.json').write_text(json.dumps({'source':source,'fingerprint':fp,'images':identities,'mode':'initial'}))
            for component,identity in identities.items():
                (safe/(component+'-scan.json')).write_text(json.dumps({'SchemaVersion':2,
                    'Metadata':{'ImageID':identity},'ArtifactType':'container_image','Results':[{'Target':'linux','Vulnerabilities':[]}]}))
            calls=[]
            def fake(args):
                calls.append(args)
                if args[:3]==['docker','image','inspect']:
                    component=args[3].removeprefix('pulse-services-').removesuffix(':cutover')
                    return json.dumps([{'Id':identities[component]}])
                if args[:2]==['docker','pull']:raise ValueError('redundant_image_unpack_failed')
                if args[:2] in (['docker','tag'],['docker','push']) or args[:3]==['az','acr','login']:return ''
                if args[:4]==['az','acr','repository','show']:return 'sha256:'+'c'*64
                if args[:4]==['az','acr','manifest','show']:
                    component=args[args.index('-n')+1].split('@')[0].removeprefix('pulse-services-')
                    return json.dumps(self.manifest(identities[component]))
                raise AssertionError('Unexpected external operation')
            with patch.object(images,'context',return_value=(source,safe)),patch.object(images,'fingerprint',return_value=fp),patch.object(images,'command',side_effect=fake),patch.dict(os.environ,{'GITHUB_RUN_ID':'123'}):
                images.publish()
            receipt=json.loads((safe/'registry-images.json').read_text())
            self.assertEqual(set(receipt),images.COMPONENTS)
            self.assertEqual(len([c for c in calls if c[:2]==['docker','push']]),4)
            self.assertFalse(any(c[:2]==['docker','pull'] for c in calls))
    def test_manifest_wrong_configuration_is_rejected(self):
        with self.assertRaises(ValueError):images.manifest_verified(self.manifest('sha256:'+'a'*64),'sha256:'+'b'*64)
    def test_manifest_requires_typed_bounded_descriptors(self):
        identity='sha256:'+'a'*64
        for mutation in ('schema','index','layers','config_type','digest','size','urls','count'):
            a=self.manifest(identity)
            if mutation=='schema':a['schemaVersion']=True
            elif mutation=='index':a['manifests']=[]
            elif mutation=='layers':a['layers']=[]
            elif mutation=='config_type':a['config']['mediaType']='unknown'
            elif mutation=='digest':a['layers'][0]['digest']='unsafe'
            elif mutation=='size':a['layers'][0]['size']=True
            elif mutation=='urls':a['layers'][0]['urls']=['https://untrusted.invalid']
            else:a['layers']*=257
            with self.subTest(mutation=mutation),self.assertRaises(ValueError):images.manifest_verified(a,identity)
    def test_oci_and_docker_manifests_match_exact_scanned_configuration(self):
        identity='sha256:'+'a'*64;a=self.manifest(identity)
        self.assertTrue(images.manifest_verified(a,identity))
        a['mediaType']='application/vnd.oci.image.manifest.v1+json';a['config']['mediaType']='application/vnd.oci.image.config.v1+json'
        a['layers'][0]['mediaType']='application/vnd.oci.image.layer.v1.tar+gzip'
        self.assertTrue(images.manifest_verified(a,identity))
    def test_failed_command_never_exports_secret_or_full_stderr(self):
        result=subprocess.CompletedProcess(['docker','push'],1,'','unauthorized SECRET_SENTINEL')
        with patch.object(images.subprocess,'run',return_value=result),self.assertRaises(ValueError) as caught:
            images.command(['docker','push','example.invalid/private'])
        self.assertEqual(str(caught.exception),'image_docker_push_unauthorized')
        self.assertNotIn('SECRET',str(caught.exception))
    def test_metadata_reads_retry_only_transient_codes(self):
        args=['az','acr','manifest','show','-r','acrphdtest7825cc','-n','pulse-services-laya@sha256:'+'a'*64]
        with patch.object(images,'command',side_effect=[ValueError('image_registry_manifest_not_visible'),'{"ok":true}']) as command,patch.object(images.time,'sleep'):
            self.assertEqual(images.read_registry(args),'{"ok":true}');self.assertEqual(command.call_count,2)
        with patch.object(images,'command',side_effect=ValueError('image_registry_manifest_unauthorized')) as command,patch.object(images.time,'sleep'),self.assertRaises(ValueError):
            images.read_registry(args)
        self.assertEqual(command.call_count,1)
    def test_metadata_retries_are_bounded(self):
        with patch.object(images,'command',side_effect=ValueError('image_registry_manifest_not_visible')) as command,patch.object(images.time,'sleep'),self.assertRaises(ValueError):
            images.read_registry(['az','acr','manifest','show'])
        self.assertEqual(command.call_count,3)
    def test_registry_helper_never_retries_mutations(self):
        with patch.object(images,'command') as command,self.assertRaises(ValueError):
            images.read_registry(['docker','push','anything'])
        command.assert_not_called()

if __name__=='__main__':unittest.main(verbosity=2)
