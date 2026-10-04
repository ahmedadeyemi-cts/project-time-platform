"""Real finalization code, fake cloud calls; no live account or document mutations."""
import copy
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import yaml

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'deployment/pulse-services'))
import canonical_release as gate
import post_activation_acceptance as final
import cutover
import release_phase
from test_activation_integration import context, records, SHA, RUN


def completed_records():
    run,jobs=records()
    for step in jobs['jobs'][0]['steps']:
        step.update(status='completed',conclusion='success')
    jobs['jobs'][0]['steps'][-1].update(status='in_progress',conclusion=None)
    return run,jobs

class OrderGates(unittest.TestCase):
    def test_native_stage_never_accepts_missing_future_application_gate(self):
        for name in gate.APPLICATION_UAT_STEPS:
            run,jobs=records()
            jobs['jobs'][0]['steps']=[s for s in jobs['jobs'][0]['steps'] if s['name']!=name]
            with self.subTest(name=name),self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_native_stage_does_not_reuse_prior_application_acceptance(self):
        run,jobs=records()
        step=next(s for s in jobs['jobs'][0]['steps'] if s['name']==gate.APPLICATION_UAT_STEPS[0])
        step.update(status='completed',conclusion='success')
        with self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_gate_order_cannot_put_application_tests_before_cutover(self):
        run,jobs=records();steps=jobs['jobs'][0]['steps']
        activation=next(s for s in steps if s['name']==gate.ACTIVATION_STEP)
        steps.remove(activation);steps.insert(len(steps)-1,activation)
        with self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN)
    def test_only_full_success_reaches_final_acceptance(self):
        self.assertTrue(gate.validate_run(*completed_records(),SHA,RUN,final=True))
        for name in gate.APPLICATION_UAT_STEPS:
            for outcome in ('failure','skipped','cancelled',None):
                run,jobs=completed_records()
                next(s for s in jobs['jobs'][0]['steps'] if s['name']==name)['conclusion']=outcome
                with self.subTest(name=name,outcome=outcome),self.assertRaises(ValueError):
                    gate.validate_run(run,jobs,SHA,RUN,final=True)
    def test_finalizer_observation_retries_lagging_job_state(self):
        run,jobs=completed_records()
        lagging=copy.deepcopy(jobs)
        finalizer=next(s for s in lagging['jobs'][0]['steps'] if s['name']==gate.FINALIZATION_STEP)
        finalizer.update(status='pending',conclusion=None)
        previous=lagging['jobs'][0]['steps'][-2]
        previous.update(status='in_progress',conclusion=None)
        with patch.object(cutover,'gh',side_effect=[run,lagging,run,jobs]) as gh,patch.object(final.time,'sleep') as sleep:
            observed=final.observe_canonical_finalization(SHA,RUN,attempts=2,delay_seconds=0)
        self.assertEqual(observed,(run,jobs));self.assertEqual(gh.call_count,4);sleep.assert_called_once()

    def test_finalizer_observation_never_retries_identity_mismatch(self):
        run,jobs=completed_records();run['path']='untrusted.yml'
        with patch.object(cutover,'gh',side_effect=[run,jobs]) as gh,patch.object(final.time,'sleep') as sleep,self.assertRaises(cutover.CutoverError):
            final.observe_canonical_finalization(SHA,RUN,attempts=5,delay_seconds=0)
        self.assertEqual(gh.call_count,2);sleep.assert_not_called()

    def test_finalizer_observation_retries_transient_github_read(self):
        run,jobs=completed_records()
        with patch.object(cutover,'gh',side_effect=[cutover.CutoverError('command_failed_gh'),run,jobs]) as gh,patch.object(final.time,'sleep') as sleep:
            observed=final.observe_canonical_finalization(SHA,RUN,attempts=2,delay_seconds=0)
        self.assertEqual(observed,(run,jobs));self.assertEqual(gh.call_count,3);sleep.assert_called_once()

    def test_authentic_failed_uat_can_only_enter_cleanup_identity_gate(self):
        run,jobs=completed_records()
        next(s for s in jobs['jobs'][0]['steps'] if s['name']==gate.APPLICATION_UAT_STEPS[0])['conclusion']='failure'
        final.canonical_finalization(run,jobs,SHA,RUN)
        with self.assertRaises(ValueError):gate.validate_run(run,jobs,SHA,RUN,final=True)
        for field,value in [('id',1),('head_sha','b'*40),('path','other.yml'),('event','push'),('run_attempt',2)]:
            bad=copy.deepcopy(run);bad[field]=value
            with self.subTest(field=field),self.assertRaises(cutover.CutoverError):final.canonical_finalization(bad,jobs,SHA,RUN)
    def test_document_parser_images_require_patched_expat(self):
        for component in ('documents','scanner','laya-gateway'):
            source=(ROOT/'deployment/pulse-services'/('Dockerfile.'+component)).read_text()
            self.assertIn('COPY --from=expat /libexpat1-pulse.deb',source)
            self.assertIn('/tmp/libexpat1-pulse.deb',source)
            self.assertIn("e.EXPAT_VERSION == 'expat_2.8.5'",source)
            build=(ROOT/'deployment/pulse-services/build-expat-package.sh').read_text()
            self.assertIn('make check',build)
            self.assertIn('1e727b8933ec51a77a9a9d9afcf8e688bce45d907c13e36ab7393fe36e703182',build)
            self.assertIn('Version: 2.8.5-0pulse1',build)
            self.assertIn('USER 65534:65534',source)
    def test_failure_finalizer_is_always_and_api_rollback_is_mandatory(self):
        doc=yaml.safe_load((ROOT/'.github/workflows/projectpulse-deploy-test.yml').read_text())
        steps=doc['jobs']['deploy']['steps']
        last=next(s for s in steps if s.get('name')==gate.FINALIZATION_STEP)
        self.assertIn('always()',last['if'])
        rollback=next(s for s in steps if s.get('name')=='Restore exact prior Test images after application failure')
        self.assertIn("steps.pulse_private_services_final.outcome == 'failure'",rollback['if'])
        self.assertIn('old_api_image',rollback['run']);self.assertIn('old_web_image',rollback['run'])
        self.assertEqual(doc['permissions'],{'id-token':'write','contents':'read','actions':'read'})

class Finalization(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root=Path(self.tmp.name);self.safe=self.root/'safe';self.safe.mkdir()
        self.private=self.root/'pulse-services-private';self.private.mkdir()
        self.env=context()|{'GITHUB_RUN_ID':RUN,'RUNNER_TEMP':str(self.root),'PULSE_PRIVATE_EVIDENCE':str(self.private)}
        self.images={'documents':'synthetic-docs','laya':'synthetic-laya'}
        self.receipt={'sourceSha':SHA,'runId':RUN,'status':'pending_application_uat','servicesActivated':True,'mode':'initial_activation'}
        (self.safe/'activation-receipt.json').write_text(json.dumps(self.receipt))
        (self.safe/'build-identities.json').write_text(json.dumps({'fingerprint':'f'*64}))
        (self.safe/'registry-images.json').write_text(json.dumps(self.images))
        (self.private/'preflight.json').write_text(json.dumps({'adminIdentity':'same-local-admin'}))
        self.run,self.jobs=completed_records()
    def call(self,*,admin='same-local-admin',rollback_error=False):
        with patch.dict(os.environ,self.env,clear=True),patch.object(final,'context',return_value=(SHA,self.safe)),patch.object(cutover,'gh',side_effect=[self.run,self.jobs]),patch.object(cutover,'local_admin',return_value=admin),patch.object(final,'existing_images',return_value=self.images),patch.object(release_phase,'remove_success_job') as cleanup,patch.object(cutover,'rollback',side_effect=RuntimeError('restore failed') if rollback_error else None) as rollback:
            rc=final.finalize()
            return rc,rollback.call_count,cleanup.call_count
    def test_full_uat_then_admin_and_image_verification_accepts(self):
        rc,rollbacks,cleanups=self.call()
        self.assertEqual((rc,rollbacks,cleanups),(0,0,1));self.assertFalse(self.private.exists())
        receipt=json.loads((self.safe/'activation-receipt.json').read_text())
        self.assertEqual(receipt['status'],'passed');self.assertTrue(receipt['applicationUatPassed'])
    def test_failed_application_test_rolls_back_and_never_calls_acceptance_cleanup(self):
        next(s for s in self.jobs['jobs'][0]['steps'] if s['name']==gate.APPLICATION_UAT_STEPS[0])['conclusion']='failure'
        rc,rollbacks,cleanups=self.call()
        self.assertEqual((rc,rollbacks,cleanups),(1,1,0));self.assertFalse(self.private.exists())
        receipt=json.loads((self.safe/'activation-receipt.json').read_text())
        self.assertFalse(receipt['applicationUatPassed']);self.assertFalse(receipt['servicesActivated']);self.assertTrue(receipt['rollbackCompleted'])
    def test_changed_local_superadmin_identity_is_never_accepted(self):
        self.assertEqual(self.call(admin='other-user'),(1,1,0))
    def test_rollback_failure_is_reported_not_passed(self):
        self.jobs['jobs'][0]['steps'][0]['conclusion']='failure'
        self.assertEqual(self.call(rollback_error=True),(1,1,0))
        self.assertFalse(json.loads((self.safe/'activation-receipt.json').read_text())['rollbackCompleted'])
    def test_existing_services_not_deleted_when_app_acceptance_fails(self):
        self.receipt['mode']='existing_verified';(self.safe/'activation-receipt.json').write_text(json.dumps(self.receipt))
        next(s for s in self.jobs['jobs'][0]['steps'] if s['name']==gate.APPLICATION_UAT_STEPS[1])['conclusion']='failure'
        self.assertEqual(self.call(),(1,0,0))
    def test_unknown_origin_fails_before_account_or_rollback_access(self):
        self.run['path']='untrusted.yml'
        with self.assertRaises(cutover.CutoverError):self.call()
        self.assertTrue(self.private.exists())
    def test_missing_provisional_state_fails_without_new_resource_mutations(self):
        self.private.rmdir() if not list(self.private.iterdir()) else (self.private/'preflight.json').unlink()
        if self.private.exists():self.private.rmdir()
        self.assertEqual(self.call(),(1,0,0))

if __name__=='__main__':unittest.main(verbosity=2)
